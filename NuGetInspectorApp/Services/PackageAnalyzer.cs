using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Services
{
    public class PackageAnalyzer : IPackageAnalyzer
    {
        private readonly ILogger<PackageAnalyzer> _logger;

        public PackageAnalyzer(ILogger<PackageAnalyzer> logger)
        {
            _logger = logger;
        }

        public Dictionary<string, MergedPackage> MergePackages(
            List<ProjectInfo> outdatedProjects,
            List<ProjectInfo> deprecatedProjects,
            List<ProjectInfo> vulnerableProjects,
            string projectPath,
            string framework)
        {
            var map = new Dictionary<string, MergedPackage>(StringComparer.OrdinalIgnoreCase);

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

            var outFw = outProject.Frameworks.FirstOrDefault(f => f.Framework == framework);
            var depFw = depProject.Frameworks.FirstOrDefault(f => f.Framework == framework);
            var vulFw = vulProject.Frameworks.FirstOrDefault(f => f.Framework == framework);

            if (outFw != null) UpsertPackages(map, outFw.TopLevelPackages, ReportType.Outdated);
            if (depFw != null) UpsertPackages(map, depFw.TopLevelPackages, ReportType.Deprecated);
            if (vulFw != null) UpsertPackages(map, vulFw.TopLevelPackages, ReportType.Vulnerable);

            return map;
        }

        private static void UpsertPackages(Dictionary<string, MergedPackage> map, List<PackageInfo> packages, ReportType type)
        {
            foreach (var p in packages)
            {
                if (!map.TryGetValue(p.Id, out var m))
                    map[p.Id] = m = new MergedPackage { Id = p.Id };

                m.RequestedVersion = p.RequestedVersion;
                m.ResolvedVersion = p.ResolvedVersion;

                switch (type)
                {
                    case ReportType.Outdated:
                        m.LatestVersion = p.LatestVersion;
                        m.IsOutdated = p.LatestVersion != null && p.ResolvedVersion != p.LatestVersion;
                        break;
                    case ReportType.Deprecated:
                        m.DeprecationReasons = p.DeprecationReasons ?? new();
                        m.IsDeprecated = m.DeprecationReasons.Count > 0;
                        m.Alternative = p.AlternativePackage;
                        break;
                    case ReportType.Vulnerable:
                        m.Vulnerabilities = p.Vulnerabilities ?? new();
                        break;
                }
            }
        }
    }
}