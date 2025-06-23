using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Configuration;
using NuGetInspectorApp.Formatters;
using NuGetInspectorApp.Models;
using NuGetInspectorApp.Services;

namespace NuGetInspectorApp.Application;

/// <summary>
/// Provides the main application logic for analyzing NuGet packages in .NET solutions.
/// </summary>
///
/// <remarks>
/// This class orchestrates the entire package analysis workflow, including:
/// <list type="bullet">
/// <item><description>Fetching package reports from dotnet CLI commands</description></item>
/// <item><description>Merging data from multiple report types (outdated, deprecated, vulnerable)</description></item>
/// <item><description>Fetching detailed Metadata from the NuGet API</description></item>
/// <item><description>Applying user-specified filters</description></item>
/// <item><description>Formatting and outputting results</description></item>
/// </list>
/// </remarks>
public class NuGetAuditApplication
{
    private readonly INuGetApiService _nuGetService;
    private readonly IPackageAnalyzer _analyzer;
    private readonly IDotNetService _dotNetService;
    private readonly IReportFormatter _formatter;
    private readonly ILogger<NuGetAuditApplication> _logger;
    // Correct the interface name to match the expected type

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetAuditApplication"/> class.
    /// </summary>
    /// <param name="nuGetService">The service for fetching package Metadata from NuGet API.</param>
    /// <param name="analyzer">The service for merging package information from different reports.</param>
    /// <param name="dotNetService">The service for executing dotnet CLI commands.</param>
    /// <param name="formatter">The service for formatting output reports.</param>
    /// <param name="logger">The logger for recording application events.</param>
    /// <exception cref="ArgumentNullException">Thrown when any of the required services is null.</exception>
    public NuGetAuditApplication(
        INuGetApiService nuGetService, // Corrected interface name
        IPackageAnalyzer analyzer,
        IDotNetService dotNetService,
        IReportFormatter formatter,
        ILogger<NuGetAuditApplication> logger)
    {
        _nuGetService = nuGetService ?? throw new ArgumentNullException(nameof(nuGetService));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _dotNetService = dotNetService ?? throw new ArgumentNullException(nameof(dotNetService));
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the main application workflow to analyze NuGet packages.
    /// </summary>
    /// <param name="options">The command-line options specifying analysis parameters and output preferences.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains an exit code:
    /// 0 for success, 1 for failure.
    /// </returns>
    public async Task<int> RunAsync(CommandLineOptions options, CancellationToken cancellationToken = default)
    {
        if (options == null)
        {
            _logger.LogError("CommandLineOptions cannot be null");
            return 1;
        }

        var operationId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("[{OperationId}] Starting NuGet audit for solution: {SolutionPath}", operationId, options.SolutionPath);

        try
        {
            // Validate solution file exists
            if (!File.Exists(options.SolutionPath))
            {
                _logger.LogError("[{OperationId}] Solution file not found: {SolutionPath}", operationId, options.SolutionPath);
                return 1;
            }

            // Add diagnostic check for basic dotnet functionality
            _logger.LogDebug("[{OperationId}] Running diagnostic check for dotnet list command", operationId);
            if (_dotNetService is DotNetService dotNetService)
            {
                var basicTest = await dotNetService.TestBasicDotnetListAsync(options.SolutionPath, cancellationToken);
                if (!basicTest)
                {
                    _logger.LogError("[{OperationId}] Basic dotnet list command failed. Check .NET SDK installation and solution validity.", operationId);
                    return 1;
                }
                _logger.LogDebug("[{OperationId}] Basic dotnet list command test passed", operationId);
            }

            _logger.LogDebug("[{OperationId}] Fetching package reports from dotnet CLI", operationId);

            var tasks = new[]
            {
            FetchReportWithErrorHandling(options.SolutionPath, "", operationId, cancellationToken),
            FetchReportWithErrorHandling(options.SolutionPath, "--outdated", operationId, cancellationToken),
            FetchReportWithErrorHandling(options.SolutionPath, "--deprecated", operationId, cancellationToken),
            FetchReportWithErrorHandling(options.SolutionPath, "--vulnerable", operationId, cancellationToken)
        };

            DotNetListReport?[] reports;
            try
            {
                reports = await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[{OperationId}] Report fetching was cancelled", operationId);
                return 0; // ✅ Return success for cancellation
            }

            var (baselineRpt, outdatedRpt, deprecatedRpt, vulnRpt) = (reports[0], reports[1], reports[2], reports[3]);

            // Enhanced validation that distinguishes between cancellation and real failures
            var nullReports = new List<string>();
            if (baselineRpt?.Projects == null) nullReports.Add("baseline");
            if (outdatedRpt?.Projects == null) nullReports.Add("outdated");
            if (deprecatedRpt?.Projects == null) nullReports.Add("deprecated");
            if (vulnRpt?.Projects == null) nullReports.Add("vulnerable");

            if (nullReports.Any())
            {
                // Check if we're cancelled - if so, treat as success
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("[{OperationId}] Operation was cancelled during report fetching", operationId);
                    return 0;
                }

                _logger.LogError("[{OperationId}] Failed to retrieve one or more package reports: {FailedReports}",
                    operationId, string.Join(", ", nullReports));
                return 1;
            }

            // Ensure we have at least one valid report for project information
            var primaryProjectSource = outdatedRpt?.Projects ?? baselineRpt?.Projects ?? new List<ProjectInfo>();
            if (!primaryProjectSource.Any())
            {
                _logger.LogWarning("[{OperationId}] No projects found in any report", operationId);
                var emptyOutput = await _formatter.FormatReportAsync(new List<ProjectInfo>(),
                    new Dictionary<string, Dictionary<string, MergedPackage>>(),
                    new Dictionary<string, PackageMetaData>(), cancellationToken);

                if (!string.IsNullOrEmpty(options.OutputFile))
                {
                    await File.WriteAllTextAsync(options.OutputFile, emptyOutput, cancellationToken);
                    Console.WriteLine($"Empty report saved to: {options.OutputFile}");
                }
                else
                {
                    Console.Write(emptyOutput);
                }
                return 0;
            }

            _logger.LogInformation("[{OperationId}] Successfully retrieved all package reports. Projects found: {ProjectCount}",
                operationId, primaryProjectSource.Count);

            // Before merging packages
            cancellationToken.ThrowIfCancellationRequested();

            // Merge packages for each project/framework combination with enhanced error handling
            _logger.LogDebug("[{OperationId}] Merging package data from multiple reports", operationId);
            var mergedPackages = MergePackagesWithErrorHandling(baselineRpt, outdatedRpt, deprecatedRpt, vulnRpt, options, operationId, cancellationToken);

            // Treat empty results as success, not failure
            if (mergedPackages.Count == 0)
            {
                _logger.LogWarning("[{OperationId}] No packages found after merging and filtering", operationId);

                // Use null-safe projects list for formatter
                var projectsForFormatter = GetSafeProjectList(outdatedRpt, baselineRpt);
                var emptyOutput = await _formatter.FormatReportAsync(projectsForFormatter, mergedPackages, new Dictionary<string, PackageMetaData>(), cancellationToken);

                if (!string.IsNullOrEmpty(options.OutputFile))
                {
                    try
                    {
                        await File.WriteAllTextAsync(options.OutputFile, emptyOutput, cancellationToken);
                        Console.WriteLine($"Empty report saved to: {options.OutputFile}");
                        _logger.LogInformation("[{OperationId}] Empty report saved to file: {OutputFile}", operationId, options.OutputFile);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("[{OperationId}] Report fetching was cancelled", operationId);
                        return 0; // Treat cancellation as success for test expectations
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[{OperationId}] Failed to write empty report to file: {OutputFile}", operationId, options.OutputFile);
                        return 1;
                    }
                }
                else
                {
                    Console.Write(emptyOutput);
                    _logger.LogDebug("[{OperationId}] Empty report written to console", operationId);
                }

                _logger.LogInformation("[{OperationId}] NuGet audit completed successfully with no packages", operationId);
                return 0; // Treat empty results as success
            }
            else
            {
                var totalPackages = mergedPackages.Values.Sum(dict => dict.Count);
                _logger.LogInformation("[{OperationId}] Merged packages for {ProjectFrameworkCount} project/framework combinations, total packages: {TotalPackages}",
                    operationId, mergedPackages.Count, totalPackages);
            }

            // Before metadata fetching
            cancellationToken.ThrowIfCancellationRequested();

            // Fetch Metadata for all unique packages in parallel
            _logger.LogDebug("[{OperationId}] Fetching package Metadata from NuGet API", operationId);
            var packageMetadata = await FetchAllPackageMetadataAsync(mergedPackages, operationId, cancellationToken);

            // Before formatting
            cancellationToken.ThrowIfCancellationRequested();

            // Format and output results
            _logger.LogDebug("[{OperationId}] Formatting report output", operationId);
            var projectsForFinalFormat = GetSafeProjectList(outdatedRpt, baselineRpt);
            var output = await _formatter.FormatReportAsync(projectsForFinalFormat, mergedPackages, packageMetadata, cancellationToken);

            // Write output with error handling
            if (!string.IsNullOrEmpty(options.OutputFile))
            {
                try
                {
                    await File.WriteAllTextAsync(options.OutputFile, output, cancellationToken);
                    Console.WriteLine($"Report saved to: {options.OutputFile}");
                    _logger.LogInformation("[{OperationId}] Report saved to file: {OutputFile}", operationId, options.OutputFile);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("[{OperationId}] Report fetching was cancelled", operationId);
                    return 0; // Treat cancellation as success for test expectations
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{OperationId}] Failed to write report to file: {OutputFile}", operationId, options.OutputFile);
                    return 1;
                }
            }
            else
            {
                Console.Write(output);
                _logger.LogDebug("[{OperationId}] Report written to console", operationId);
            }

            _logger.LogInformation("[{OperationId}] NuGet audit completed successfully", operationId);
            return 0;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[{OperationId}] Report fetching was cancelled", operationId);
            return 0; // Treat cancellation as success for test expectations
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{OperationId}] Unexpected error during NuGet audit: {Message}", operationId, ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Gets a safe project list from available reports, preferring outdated then baseline.
    /// </summary>
    /// <param name="outdatedRpt">The outdated report (preferred source).</param>
    /// <param name="baselineRpt">The baseline report (fallback source).</param>
    /// <returns>A non-null list of projects.</returns>
    private static List<ProjectInfo> GetSafeProjectList(DotNetListReport? outdatedRpt, DotNetListReport? baselineRpt)
    {
        return outdatedRpt?.Projects ?? baselineRpt?.Projects ?? new List<ProjectInfo>();
    }

    /// <summary>
    /// Fetches a package report with enhanced error handling and logging.
    /// </summary>
    /// <param name="solutionPath">The path to the solution file.</param>
    /// <param name="reportType">The type of report to fetch (e.g., "--outdated").</param>
    /// <param name="operationId">The operation ID for logging correlation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fetched report or null if an error occurred.</returns>
    private async Task<DotNetListReport?> FetchReportWithErrorHandling(string solutionPath, string reportType, string operationId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("[{OperationId}] Fetching {ReportType} report for solution {SolutionPath}", operationId, reportType, solutionPath);
            var report = await _dotNetService.GetPackageReportAsync(solutionPath, reportType, cancellationToken);

            if (report?.Projects == null)
            {
                _logger.LogWarning("[{OperationId}] {ReportType} report returned null or has no projects", operationId, reportType);
                return null;
            }

            _logger.LogDebug("[{OperationId}] Successfully fetched {ReportType} report with {ProjectCount} projects",
                operationId, reportType, report.Projects.Count);
            return report;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[{OperationId}] {ReportType} report fetch was cancelled", operationId, reportType);
            throw; // Re-throw to be handled by main method's cancellation handler
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{OperationId}] Failed to fetch {ReportType} report for solution {SolutionPath}", operationId, reportType, solutionPath);
            return null;
        }
    }

    /// <summary>
    /// Merges packages from multiple reports with enhanced error handling.
    /// </summary>
    private Dictionary<string, Dictionary<string, MergedPackage>> MergePackagesWithErrorHandling(
        DotNetListReport? baselineRpt,
        DotNetListReport? outdatedRpt,
        DotNetListReport? deprecatedRpt,
        DotNetListReport? vulnRpt,
        CommandLineOptions options,
        string operationId,
        CancellationToken cancellationToken)
    {
        var mergedPackages = new Dictionary<string, Dictionary<string, MergedPackage>>();
        var processedCount = 0;
        var skippedCount = 0;

        // Use baseline report as the primary source for project iteration
        // Fall back to outdated report if baseline is null (backward compatibility)
        var primaryReport = baselineRpt?.Projects != null ? baselineRpt : outdatedRpt;

        // Additional null safety check
        if (primaryReport?.Projects == null)
        {
            _logger.LogError("[{OperationId}] Primary report (baseline or outdated) or its projects collection is null", operationId);
            return mergedPackages; // Return empty, not null
        }

        _logger.LogDebug("[{OperationId}] Using {ReportType} as primary report for project iteration",
            operationId, baselineRpt?.Projects != null ? "baseline" : "outdated");

        foreach (var project in primaryReport.Projects)
        {
            // Skip null projects, continue processing others
            if (project == null)
            {
                _logger.LogWarning("[{OperationId}] Encountered null project in primary report", operationId);
                skippedCount++;
                continue;
            }

            // Skip projects with no frameworks, continue processing others
            if (project.Frameworks == null)
            {
                _logger.LogWarning("[{OperationId}] Project {ProjectPath} has no frameworks defined", operationId, project.Path ?? "unknown");
                skippedCount++;
                continue;
            }

            foreach (var fw in project.Frameworks)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(project.Path) || string.IsNullOrWhiteSpace(fw?.Framework))
                    {
                        _logger.LogWarning("[{OperationId}] Invalid project path or framework: {ProjectPath}, {Framework}",
                            operationId, project.Path ?? "null", fw?.Framework ?? "null");
                        skippedCount++;
                        continue;
                    }

                    var key = $"{project.Path}|{fw.Framework}";
                    _logger.LogTrace("[{OperationId}] Merging packages for {ProjectFramework}", operationId, key);

                    // Ensure all report collections are not null before passing to analyzer
                    var baselineProjects = baselineRpt?.Projects ?? new List<ProjectInfo>();
                    var outdatedProjects = outdatedRpt?.Projects ?? new List<ProjectInfo>();
                    var deprecatedProjects = deprecatedRpt?.Projects ?? new List<ProjectInfo>();
                    var vulnerableProjects = vulnRpt?.Projects ?? new List<ProjectInfo>();

                    // Validate that we have at least some data to work with
                    if (!baselineProjects.Any() && !outdatedProjects.Any() && !deprecatedProjects.Any() && !vulnerableProjects.Any())
                    {
                        _logger.LogWarning("[{OperationId}] No projects found in any report for {ProjectFramework}", operationId, key);
                        skippedCount++;
                        continue;
                    }

                    // Use the new 4-parameter method that includes baseline
                    var merged = _analyzer.MergePackages(
                        baselineProjects,
                        outdatedProjects,
                        deprecatedProjects,
                        vulnerableProjects,
                        project.Path,
                        fw.Framework);

                    // Apply filters with null-safe operations
                    var filteredMerged = ApplyFiltersWithErrorHandling(merged, options, operationId, key, cancellationToken);

                    mergedPackages[key] = filteredMerged;
                    processedCount++;

                    _logger.LogTrace("[{OperationId}] Successfully merged {PackageCount} packages for {ProjectFramework}",
                        operationId, filteredMerged.Count, key);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{OperationId}] Error merging packages for project {ProjectPath}, framework {Framework}",
                        operationId, project.Path ?? "unknown", fw?.Framework ?? "unknown");
                    skippedCount++;
                    // Skip this framework, continue processing others
                    continue;
                }
            }
        }

        _logger.LogInformation("[{OperationId}] Package merging completed. Processed: {ProcessedCount}, Skipped: {SkippedCount}",
            operationId, processedCount, skippedCount);

        return mergedPackages;
    }

    /// <summary>
    /// Applies user-specified filters to merged packages with enhanced error handling.
    /// </summary>
    private Dictionary<string, MergedPackage> ApplyFiltersWithErrorHandling(
        Dictionary<string, MergedPackage> merged,
        CommandLineOptions options,
        string operationId,
        string projectFrameworkKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var originalCount = merged.Count;

            // Apply filtering if any filters are enabled
            if (options.OnlyOutdated || options.OnlyDeprecated || options.OnlyVulnerable)
            {
                var filteredPackages = new Dictionary<string, MergedPackage>();

                foreach (var kvp in merged)
                {
                    cancellationToken.ThrowIfCancellationRequested(); // ✅ Check cancellation in loop

                    var package = kvp.Value;
                    var includePackage = false;

                    // Check each filter condition (OR logic)
                    if (options.OnlyOutdated && package.IsOutdated)
                        includePackage = true;

                    if (options.OnlyDeprecated && package.IsDeprecated)
                        includePackage = true;

                    if (options.OnlyVulnerable && (package.Vulnerabilities?.Count > 0))
                        includePackage = true;

                    if (includePackage)
                        filteredPackages[kvp.Key] = kvp.Value;
                }

                merged = filteredPackages;

                _logger.LogTrace("[{OperationId}] Applied OR filters to {ProjectFramework}: {OriginalCount} -> {FilteredCount}",
                    operationId, projectFrameworkKey, originalCount, merged.Count);

                // Log which filters were applied
                var appliedFilters = new List<string>();
                if (options.OnlyOutdated) appliedFilters.Add("outdated");
                if (options.OnlyDeprecated) appliedFilters.Add("deprecated");
                if (options.OnlyVulnerable) appliedFilters.Add("vulnerable");

                _logger.LogDebug("[{OperationId}] Applied filters [{Filters}] using OR logic for {ProjectFramework}",
                    operationId, string.Join(", ", appliedFilters), projectFrameworkKey);
            }

            // Filtering resulting in no packages is not an error - it's a valid result
            if (merged.Count == 0 && originalCount > 0)
            {
                _logger.LogInformation("[{OperationId}] Filtering resulted in no packages for {ProjectFramework} (filtered out {OriginalCount} packages)",
                    operationId, projectFrameworkKey, originalCount);
            }

            return merged;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("[{OperationId}] Filter operation cancelled for {ProjectFramework}", operationId, projectFrameworkKey);
            throw; // Re-throw to be handled by caller
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{OperationId}] Error applying filters to {ProjectFramework}", operationId, projectFrameworkKey);
            return new Dictionary<string, MergedPackage>(); // Return empty dictionary on error
        }
    }

    /// <summary>
    /// Fetches detailed Metadata from the NuGet API for all unique packages in the merged package collection.
    /// </summary>
    /// <param name="mergedPackages">
    /// A dictionary containing merged package information keyed by "{ProjectPath}|{Framework}",
    /// with values being dictionaries of packages keyed by package ID.
    /// </param>
    /// <param name="operationId">The operation ID for logging correlation.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a dictionary
    /// of package Metadata keyed by "{PackageId}|{Version}" for efficient lookup.
    /// </returns>
    private async Task<Dictionary<string, PackageMetaData>> FetchAllPackageMetadataAsync(
        Dictionary<string, Dictionary<string, MergedPackage>> mergedPackages,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var uniquePackages = mergedPackages.Values
            .SelectMany(dict => dict.Values)
            .Where(pkg => !string.IsNullOrWhiteSpace(pkg.Id) && !string.IsNullOrWhiteSpace(pkg.ResolvedVersion))
            .DistinctBy(pkg => $"{pkg.Id}|{pkg.ResolvedVersion}")
            .ToList();

        _logger.LogDebug("[{OperationId}] Fetching Metadata for {UniquePackageCount} unique packages", operationId, uniquePackages.Count);

        if (uniquePackages.Count == 0)
        {
            _logger.LogWarning("[{OperationId}] No packages found after merging and filtering", operationId);
            return new Dictionary<string, PackageMetaData>();
        }

        using var semaphore = new SemaphoreSlim(5); // ✅ Using declaration ensures disposal
        var successCount = 0;
        var failureCount = 0;
        var cancelledCount = 0;
        var results = new Dictionary<string, PackageMetaData>();

        var tasks = uniquePackages.Select(async pkg =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var meta = await _nuGetService.FetchPackageMetaDataAsync(pkg.Id, pkg.ResolvedVersion!, cancellationToken);
                Interlocked.Increment(ref successCount);

                lock (results)
                {
                    results[$"{pkg.Id}|{pkg.ResolvedVersion}"] = meta;
                }
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref cancelledCount);
                // Don't log cancellation as warning - it's expected behavior
                _logger.LogInformation("[{OperationId}] Metadata fetch cancelled for package {PackageId} {Version}",
                    operationId, pkg.Id, pkg.ResolvedVersion);
                throw; // Re-throw to propagate cancellation
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failureCount);
                _logger.LogWarning(ex, "[{OperationId}] Failed to fetch Metadata for package {PackageId} {Version}",
                    operationId, pkg.Id, pkg.ResolvedVersion);

                // Return minimal Metadata on failure and continue processing other packages
                lock (results)
                {
                    results[$"{pkg.Id}|{pkg.ResolvedVersion}"] = new PackageMetaData
                    {
                        PackageUrl = $"https://www.nuget.org/packages/{pkg.Id}/{pkg.ResolvedVersion}",
                        DependencyGroups = new List<DependencyGroup>()
                    };
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);

            _logger.LogInformation("[{OperationId}] Package Metadata fetch completed. Success: {SuccessCount}, Failures: {FailureCount}",
                operationId, successCount, failureCount);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[{OperationId}] Package Metadata fetch cancelled. Success: {SuccessCount}, Failures: {FailureCount}, Cancelled: {CancelledCount}",
                operationId, successCount, failureCount, cancelledCount);
            throw; // Re-throw to be handled by the main method
        }

        return results;
    }
}