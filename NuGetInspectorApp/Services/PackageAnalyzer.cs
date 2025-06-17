using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Services
{
    /// <summary>
    /// Provides functionality for merging package information from multiple dotnet list reports.
    /// </summary>
    /// <remarks>
    /// This service combines data from outdated, deprecated, and vulnerable package reports
    /// to create a unified view of package status across different analysis types.
    /// </remarks>
    public class PackageAnalyzer : IPackageAnalyzer
    {
        private readonly ILogger<PackageAnalyzer> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PackageAnalyzer"/> class.
        /// </summary>
        /// <param name="logger">The logger instance for recording analysis operations.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
        public PackageAnalyzer(ILogger<PackageAnalyzer> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public Dictionary<string, MergedPackage> MergePackages(
            List<ProjectInfo> outdatedProjects,
            List<ProjectInfo> deprecatedProjects,
            List<ProjectInfo> vulnerableProjects,
            string projectPath,
            string framework)
        {
            ArgumentNullException.ThrowIfNull(outdatedProjects);
            ArgumentNullException.ThrowIfNull(deprecatedProjects);
            ArgumentNullException.ThrowIfNull(vulnerableProjects);
            ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(framework);

            var map = new Dictionary<string, MergedPackage>(StringComparer.OrdinalIgnoreCase);

            // Find the project in each report type
            var outProject = outdatedProjects.FirstOrDefault(pr => pr.Path == projectPath);
            if (outProject == null)
            {
                _logger.LogWarning("Project not found in outdated report: {ProjectPath}", projectPath);
                return map;
            }

            var depProject = deprecatedProjects.FirstOrDefault(pr => pr.Path == projectPath);
            if (depProject == null)
            {
                _logger.LogWarning("Project not found in deprecated report: {ProjectPath}", projectPath);
                return map;
            }

            var vulProject = vulnerableProjects.FirstOrDefault(pr => pr.Path == projectPath);
            if (vulProject == null)
            {
                _logger.LogWarning("Project not found in vulnerable report: {ProjectPath}", projectPath);
                return map;
            }

            // Find the framework in each project
            var outFw = outProject.Frameworks.FirstOrDefault(f => f.Framework == framework);
            var depFw = depProject.Frameworks.FirstOrDefault(f => f.Framework == framework);
            var vulFw = vulProject.Frameworks.FirstOrDefault(f => f.Framework == framework);

            // Merge packages from each report type
            if (outFw != null) 
            {
                UpsertPackages(map, outFw.TopLevelPackages, ReportType.Outdated);
                _logger.LogDebug("Merged {Count} outdated packages for {Project}:{Framework}", 
                    outFw.TopLevelPackages.Count, projectPath, framework);
            }

            if (depFw != null) 
            {
                UpsertPackages(map, depFw.TopLevelPackages, ReportType.Deprecated);
                _logger.LogDebug("Merged {Count} deprecated packages for {Project}:{Framework}", 
                    depFw.TopLevelPackages.Count, projectPath, framework);
            }

            if (vulFw != null) 
            {
                UpsertPackages(map, vulFw.TopLevelPackages, ReportType.Vulnerable);
                _logger.LogDebug("Merged {Count} vulnerable packages for {Project}:{Framework}", 
                    vulFw.TopLevelPackages.Count, projectPath, framework);
            }

            _logger.LogInformation("Successfully merged {TotalPackages} packages for {Project}:{Framework}", 
                map.Count, projectPath, framework);

            return map;
        }

        /// <summary>
        /// Updates or inserts packages into the merged package dictionary based on the report type.
        /// </summary>
        /// <param name="map">The dictionary of merged packages to update.</param>
        /// <param name="packages">The list of package references to process.</param>
        /// <param name="type">The type of report being processed.</param>
        /// <remarks>
        /// This method consolidates package information from different report types:
        /// <list type="bullet">
        /// <item><description>Outdated reports provide latest version information</description></item>
        /// <item><description>Deprecated reports provide deprecation status and alternatives</description></item>
        /// <item><description>Vulnerable reports provide security vulnerability information</description></item>
        /// </list>
        /// </remarks>
        private static void UpsertPackages(Dictionary<string, MergedPackage> map, List<PackageReference> packages, ReportType type)
        {
            foreach (var p in packages)
            {
                // Create or get existing merged package
                if (!map.TryGetValue(p.Id, out var m))
                    map[p.Id] = m = new MergedPackage { Id = p.Id };

                // Update common properties (these should be consistent across report types)
                m.RequestedVersion = p.RequestedVersion;
                m.ResolvedVersion = p.ResolvedVersion;

                // Update type-specific properties
                switch (type)
                {
                    case ReportType.Outdated:
                        m.LatestVersion = p.LatestVersion;
                        m.IsOutdated = !string.IsNullOrEmpty(p.LatestVersion) && 
                                      p.ResolvedVersion != p.LatestVersion;
                        break;

                    case ReportType.Deprecated:
                        m.DeprecationReasons = p.DeprecationReasons ?? new List<string>();
                        m.IsDeprecated = p.IsDeprecated || m.DeprecationReasons.Count > 0;
                        m.Alternative = p.Alternative;
                        break;

                    case ReportType.Vulnerable:
                        m.Vulnerabilities = p.Vulnerabilities ?? new List<VulnerabilityInfo>();
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown report type");
                }
            }
        }
    }

    /// <summary>
    /// Represents the different types of package analysis reports.
    /// </summary>
    /// <remarks>
    /// These report types correspond to different dotnet list package command flags
    /// and determine how package information is processed and merged.
    /// </remarks>
    public enum ReportType
    {
        /// <summary>
        /// Represents packages that have newer versions available (--outdated flag).
        /// </summary>
        Outdated,

        /// <summary>
        /// Represents packages that have been marked as deprecated (--deprecated flag).
        /// </summary>
        Deprecated,

        /// <summary>
        /// Represents packages that have known security vulnerabilities (--vulnerable flag).
        /// </summary>
        Vulnerable
    }
}