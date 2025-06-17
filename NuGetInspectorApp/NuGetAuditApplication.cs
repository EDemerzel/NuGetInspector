using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Configuration;
using NuGetInspectorApp.Formatters;
using NuGetInspectorApp.Models;
using NuGetInspectorApp.Services;

namespace NuGetInspectorApp.Application;

/// <summary>
/// Provides the main application logic for analyzing NuGet packages in .NET solutions.
/// </summary>
/// <remarks>
/// This class orchestrates the entire package analysis workflow, including:
/// <list type="bullet">
/// <item><description>Fetching package reports from dotnet CLI commands</description></item>
/// <item><description>Merging data from multiple report types (outdated, deprecated, vulnerable)</description></item>
/// <item><description>Fetching detailed metadata from the NuGet API</description></item>
/// <item><description>Applying user-specified filters</description></item>
/// <item><description>Formatting and outputting results</description></item>
/// </list>
/// </remarks>
public class NuGetAuditApplication
{
    private readonly INuGetApiService _nugetService;
    private readonly IPackageAnalyzer _analyzer;
    private readonly IDotNetService _dotnetService;
    private readonly IReportFormatter _formatter;
    private readonly ILogger<NuGetAuditApplication> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetAuditApplication"/> class.
    /// </summary>
    /// <param name="nugetService">The service for fetching package metadata from NuGet API.</param>
    /// <param name="analyzer">The service for merging package information from different reports.</param>
    /// <param name="dotnetService">The service for executing dotnet CLI commands.</param>
    /// <param name="formatter">The service for formatting output reports.</param>
    /// <param name="logger">The logger for recording application events.</param>
    /// <exception cref="ArgumentNullException">Thrown when any of the required services is null.</exception>
    public NuGetAuditApplication(
        INuGetApiService nugetService,
        IPackageAnalyzer analyzer,
        IDotNetService dotnetService,
        IReportFormatter formatter,
        ILogger<NuGetAuditApplication> logger)
    {
        _nugetService = nugetService ?? throw new ArgumentNullException(nameof(nugetService));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _dotnetService = dotnetService ?? throw new ArgumentNullException(nameof(dotnetService));
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

            // Fetch all reports in parallel with enhanced error handling
            _logger.LogDebug("[{OperationId}] Fetching package reports from dotnet CLI", operationId);

            var tasks = new[]
            {
                FetchReportWithErrorHandling(options.SolutionPath, "--outdated", operationId, cancellationToken),
                FetchReportWithErrorHandling(options.SolutionPath, "--deprecated", operationId, cancellationToken),
                FetchReportWithErrorHandling(options.SolutionPath, "--vulnerable", operationId, cancellationToken)
            };

            var reports = await Task.WhenAll(tasks);
            var (outdatedRpt, deprecatedRpt, vulnRpt) = (reports[0], reports[1], reports[2]);

            // Validate all reports were retrieved successfully
            if (outdatedRpt?.Projects == null || deprecatedRpt?.Projects == null || vulnRpt?.Projects == null)
            {
                _logger.LogError("[{OperationId}] Failed to retrieve one or more package reports. Outdated: {HasOutdated}, Deprecated: {HasDeprecated}, Vulnerable: {HasVulnerable}",
                    operationId, outdatedRpt?.Projects != null, deprecatedRpt?.Projects != null, vulnRpt?.Projects != null);
                return 1;
            }

            _logger.LogInformation("[{OperationId}] Successfully retrieved all package reports. Projects found: {ProjectCount}",
                operationId, outdatedRpt.Projects.Count);

            // Merge packages for each project/framework combination with enhanced error handling
            _logger.LogDebug("[{OperationId}] Merging package data from multiple reports", operationId);
            var mergedPackages = MergePackagesWithErrorHandling(outdatedRpt, deprecatedRpt, vulnRpt, options, operationId, cancellationToken);

            if (mergedPackages.Count == 0)
            {
                _logger.LogWarning("[{OperationId}] No packages found after merging and filtering", operationId);
            }
            else
            {
                var totalPackages = mergedPackages.Values.Sum(dict => dict.Count);
                _logger.LogInformation("[{OperationId}] Merged packages for {ProjectFrameworkCount} project/framework combinations, total packages: {TotalPackages}",
                    operationId, mergedPackages.Count, totalPackages);
            }

            // Fetch metadata for all unique packages in parallel
            _logger.LogDebug("[{OperationId}] Fetching package metadata from NuGet API", operationId);
            var packageMetadata = await FetchAllPackageMetadataAsync(mergedPackages, operationId, cancellationToken);

            // Format and output results
            _logger.LogDebug("[{OperationId}] Formatting report output", operationId);
            var output = await _formatter.FormatReportAsync(outdatedRpt.Projects, mergedPackages, packageMetadata, cancellationToken);

            // Write output with error handling
            if (!string.IsNullOrEmpty(options.OutputFile))
            {
                try
                {
                    await File.WriteAllTextAsync(options.OutputFile, output, cancellationToken);
                    Console.WriteLine($"Report saved to: {options.OutputFile}");
                    _logger.LogInformation("[{OperationId}] Report saved to file: {OutputFile}", operationId, options.OutputFile);
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
            _logger.LogWarning("[{OperationId}] Operation was cancelled", operationId);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{OperationId}] Error processing NuGet audit", operationId);
            return 1;
        }
    }

    /// <summary>
    /// Fetches a package report with enhanced error handling and logging.
    /// </summary>
    /// <param name="solutionPath">The path to the solution file.</param>
    /// <param name="reportType">The type of report to fetch (e.g., "--outdated").</param>
    /// <param name="operationId">The operation ID for logging correlation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fetched report or null if an error occurred.</returns>
    private async Task<DotnetListReport?> FetchReportWithErrorHandling(string solutionPath, string reportType, string operationId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("[{OperationId}] Fetching {ReportType} report for solution {SolutionPath}", operationId, reportType, solutionPath);
            var report = await _dotnetService.GetPackageReportAsync(solutionPath, reportType, cancellationToken);

            if (report?.Projects == null)
            {
                _logger.LogWarning("[{OperationId}] {ReportType} report returned null or has no projects", operationId, reportType);
                return null;
            }

            _logger.LogDebug("[{OperationId}] Successfully fetched {ReportType} report with {ProjectCount} projects",
                operationId, reportType, report.Projects.Count);
            return report;
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
        DotnetListReport outdatedRpt,
        DotnetListReport deprecatedRpt,
        DotnetListReport vulnRpt,
        CommandLineOptions options,
        string operationId,
        CancellationToken cancellationToken)
    {
        var mergedPackages = new Dictionary<string, Dictionary<string, MergedPackage>>();
        var processedCount = 0;
        var skippedCount = 0;

        // Additional null safety check
        if (outdatedRpt?.Projects == null)
        {
            _logger.LogError("[{OperationId}] Outdated report or its projects collection is null", operationId);
            return mergedPackages;
        }

        foreach (var project in outdatedRpt.Projects)
        {
            // First check if project itself is null
            if (project == null)
            {
                _logger.LogWarning("[{OperationId}] Encountered null project in outdated report", operationId);
                skippedCount++;
                continue;
            }

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
                    var outdatedProjects = outdatedRpt.Projects ?? new List<ProjectInfo>();
                    var deprecatedProjects = deprecatedRpt?.Projects ?? new List<ProjectInfo>();
                    var vulnerableProjects = vulnRpt?.Projects ?? new List<ProjectInfo>();

                    var merged = _analyzer.MergePackages(
                        outdatedProjects, deprecatedProjects, vulnerableProjects,
                        project.Path, fw.Framework);

                    // Apply filters with null-safe operations
                    var filteredMerged = ApplyFiltersWithErrorHandling(merged, options, operationId, key);

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
        string projectFrameworkKey)
    {
        try
        {
            var originalCount = merged.Count;

            if (options.OnlyOutdated)
            {
                merged = merged.Where(kvp => kvp.Value.IsOutdated).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                _logger.LogTrace("[{OperationId}] Applied outdated filter to {ProjectFramework}: {OriginalCount} -> {FilteredCount}",
                    operationId, projectFrameworkKey, originalCount, merged.Count);
            }

            if (options.OnlyDeprecated)
            {
                merged = merged.Where(kvp => kvp.Value.IsDeprecated).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                _logger.LogTrace("[{OperationId}] Applied deprecated filter to {ProjectFramework}: {FilteredCount} packages remain",
                    operationId, projectFrameworkKey, merged.Count);
            }

            if (options.OnlyVulnerable)
            {
                // Enhanced null-safe vulnerability check
                merged = merged.Where(kvp => kvp.Value.Vulnerabilities?.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                _logger.LogTrace("[{OperationId}] Applied vulnerable filter to {ProjectFramework}: {FilteredCount} packages remain",
                    operationId, projectFrameworkKey, merged.Count);
            }

            if (originalCount != merged.Count)
            {
                _logger.LogDebug("[{OperationId}] Filters applied to {ProjectFramework}: {OriginalCount} -> {FinalCount} packages",
                    operationId, projectFrameworkKey, originalCount, merged.Count);
            }

            return merged;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{OperationId}] Error applying filters to {ProjectFramework}", operationId, projectFrameworkKey);
            return new Dictionary<string, MergedPackage>(); // Return empty dictionary on error
        }
    }

    /// <summary>
    /// Fetches detailed metadata from the NuGet API for all unique packages in the merged package collection.
    /// </summary>
    /// <param name="mergedPackages">
    /// A dictionary containing merged package information keyed by "{ProjectPath}|{Framework}",
    /// with values being dictionaries of packages keyed by package ID.
    /// </param>
    /// <param name="operationId">The operation ID for logging correlation.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a dictionary
    /// of package metadata keyed by "{PackageId}|{Version}" for efficient lookup.
    /// </returns>
    private async Task<Dictionary<string, PackageMetadata>> FetchAllPackageMetadataAsync(
        Dictionary<string, Dictionary<string, MergedPackage>> mergedPackages,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var uniquePackages = mergedPackages.Values
            .SelectMany(dict => dict.Values)
            .Where(pkg => !string.IsNullOrWhiteSpace(pkg.Id) && !string.IsNullOrWhiteSpace(pkg.ResolvedVersion))
            .DistinctBy(pkg => $"{pkg.Id}|{pkg.ResolvedVersion}")
            .ToList();

        _logger.LogDebug("[{OperationId}] Fetching metadata for {UniquePackageCount} unique packages", operationId, uniquePackages.Count);

        if (uniquePackages.Count == 0)
        {
            _logger.LogWarning("[{OperationId}] No valid packages found for metadata fetching", operationId);
            return new Dictionary<string, PackageMetadata>();
        }

        var semaphore = new SemaphoreSlim(5); // Limit concurrent requests
        var successCount = 0;
        var failureCount = 0;

        var tasks = uniquePackages.Select(async pkg =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var meta = await _nugetService.FetchPackageMetadataAsync(pkg.Id, pkg.ResolvedVersion!, cancellationToken);
                Interlocked.Increment(ref successCount);
                return new KeyValuePair<string, PackageMetadata>($"{pkg.Id}|{pkg.ResolvedVersion}", meta);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failureCount);
                _logger.LogWarning(ex, "[{OperationId}] Failed to fetch metadata for package {PackageId} {Version}",
                    operationId, pkg.Id, pkg.ResolvedVersion);

                // Return minimal metadata on failure
                return new KeyValuePair<string, PackageMetadata>($"{pkg.Id}|{pkg.ResolvedVersion}",
                    new PackageMetadata
                    {
                        PackageUrl = $"https://www.nuget.org/packages/{pkg.Id}/{pkg.ResolvedVersion}",
                        DependencyGroups = new List<DependencyGroup>()
                    });
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        semaphore.Dispose();

        _logger.LogInformation("[{OperationId}] Package metadata fetch completed. Success: {SuccessCount}, Failures: {FailureCount}",
            operationId, successCount, failureCount);

        return results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}