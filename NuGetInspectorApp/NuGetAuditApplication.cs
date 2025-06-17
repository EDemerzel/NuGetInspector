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
        _nugetService = nugetService;
        _analyzer = analyzer;
        _dotnetService = dotnetService;
        _formatter = formatter;
        _logger = logger;
    }

    /// <summary>
    /// Executes the main application workflow to analyze NuGet packages.
    /// </summary>
    /// <param name="options">The command-line options specifying analysis parameters and output preferences.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains an exit code:
    /// 0 for success, 1 for failure.
    /// </returns>
    /// <remarks>
    /// This method performs the following steps:
    /// <list type="number">
    /// <item><description>Executes dotnet list package commands in parallel for outdated, deprecated, and vulnerable packages</description></item>
    /// <item><description>Merges package information across different report types for each project and framework</description></item>
    /// <item><description>Applies user-specified filters (only outdated, only deprecated, only vulnerable)</description></item>
    /// <item><description>Fetches detailed metadata from NuGet API for all unique packages</description></item>
    /// <item><description>Formats the results and outputs to console or file</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public async Task<int> RunAsync(CommandLineOptions options)
    {
        try
        {
            // Fetch all reports in parallel
            var tasks = new[]
            {
                _dotnetService.GetPackageReportAsync(options.SolutionPath, "--outdated"),
                _dotnetService.GetPackageReportAsync(options.SolutionPath, "--deprecated"),
                _dotnetService.GetPackageReportAsync(options.SolutionPath, "--vulnerable")
            };

            var reports = await Task.WhenAll(tasks);
            var (outdatedRpt, deprecatedRpt, vulnRpt) = (reports[0], reports[1], reports[2]);

            if (outdatedRpt?.Projects == null || deprecatedRpt?.Projects == null || vulnRpt?.Projects == null)
            {
                _logger.LogError("Failed to retrieve package reports");
                return 1;
            }

            // Merge packages for each project/framework combination
            var mergedPackages = new Dictionary<string, Dictionary<string, MergedPackage>>();
            foreach (var project in outdatedRpt.Projects)
            {
                foreach (var fw in project.Frameworks)
                {
                    var key = $"{project.Path}|{fw.Framework}";
                    var merged = _analyzer.MergePackages(
                        outdatedRpt.Projects, deprecatedRpt.Projects, vulnRpt.Projects,
                        project.Path, fw.Framework);

                    // Apply filters
                    if (options.OnlyOutdated)
                        merged = merged.Where(kvp => kvp.Value.IsOutdated).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    if (options.OnlyDeprecated)
                        merged = merged.Where(kvp => kvp.Value.IsDeprecated).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    if (options.OnlyVulnerable)
                        merged = merged.Where(kvp => kvp.Value.Vulnerabilities.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                    mergedPackages[key] = merged;
                }
            }

            // Fetch metadata for all unique packages in parallel
            var packageMetadata = await FetchAllPackageMetadataAsync(mergedPackages);

            // Format and output results
            var output = await _formatter.FormatReportAsync(outdatedRpt.Projects, mergedPackages, packageMetadata);

            if (!string.IsNullOrEmpty(options.OutputFile))
            {
                await File.WriteAllTextAsync(options.OutputFile, output);
                Console.WriteLine($"Report saved to: {options.OutputFile}");
            }
            else
            {
                Console.Write(output);
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing NuGet audit");
            return 1;
        }
    }

    /// <summary>
    /// Fetches detailed metadata from the NuGet API for all unique packages in the merged package collection.
    /// </summary>
    /// <param name="mergedPackages">
    /// A dictionary containing merged package information keyed by "{ProjectPath}|{Framework}",
    /// with values being dictionaries of packages keyed by package ID.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a dictionary
    /// of package metadata keyed by "{PackageId}|{Version}" for efficient lookup.
    /// </returns>
    /// <remarks>
    /// This method:
    /// <list type="bullet">
    /// <item><description>Identifies unique packages across all projects and frameworks to avoid duplicate API calls</description></item>
    /// <item><description>Uses a semaphore to limit concurrent HTTP requests to the NuGet API</description></item>
    /// <item><description>Handles failures gracefully by logging errors but continuing with other packages</description></item>
    /// <item><description>Properly disposes of the semaphore to prevent resource leaks</description></item>
    /// </list>
    /// </remarks>
    private async Task<Dictionary<string, PackageMetadata>> FetchAllPackageMetadataAsync(
        Dictionary<string, Dictionary<string, MergedPackage>> mergedPackages)
    {
        var uniquePackages = mergedPackages.Values
            .SelectMany(dict => dict.Values)
            .DistinctBy(pkg => $"{pkg.Id}|{pkg.ResolvedVersion}")
            .ToList();

        var semaphore = new SemaphoreSlim(5); // Limit concurrent requests
        var tasks = uniquePackages.Select(async pkg =>
        {
            await semaphore.WaitAsync();
            try
            {
                var meta = await _nugetService.FetchPackageMetadataAsync(pkg.Id, pkg.ResolvedVersion);
                return new KeyValuePair<string, PackageMetadata>($"{pkg.Id}|{pkg.ResolvedVersion}", meta);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        semaphore.Dispose();
        return results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}