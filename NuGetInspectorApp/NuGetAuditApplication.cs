using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Configuration;
using NuGetInspectorApp.Formatters;
using NuGetInspectorApp.Models;
using NuGetInspectorApp.Services;
namespace NuGetInspectorApp.Application;

public class NuGetAuditApplication
{
    private readonly INuGetApiService _nugetService;
    private readonly IPackageAnalyzer _analyzer;
    private readonly IDotNetService _dotnetService;
    private readonly IReportFormatter _formatter;
    private readonly ILogger<NuGetAuditApplication> _logger;

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