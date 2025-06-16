using System.Text;
using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Formatters
{
    /// <summary>
    /// Provides console-friendly formatting for package analysis reports.
    /// </summary>
    /// <remarks>
    /// This formatter generates human-readable text output suitable for console display,
    /// with clear hierarchical structure and detailed package information including
    /// dependencies, vulnerabilities, and deprecation status.
    /// </remarks>
    public class ConsoleReportFormatter : IReportFormatter
    {
        /// <inheritdoc />
        public Task<string> FormatReportAsync(
            List<ProjectInfo> projects,
            Dictionary<string, Dictionary<string, MergedPackage>> mergedPackages,
            Dictionary<string, PackageMetadata> packageMetadata,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(projects);
            ArgumentNullException.ThrowIfNull(mergedPackages);
            ArgumentNullException.ThrowIfNull(packageMetadata);

            var sb = new StringBuilder();

            foreach (var project in projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                sb.AppendLine($"\n=== Project: {Path.GetFileName(project.Path)} ===\n");

                foreach (var fw in project.Frameworks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    sb.AppendLine($"Framework: {fw.Framework}\n");

                    var projectKey = $"{project.Path}|{fw.Framework}";
                    if (!mergedPackages.TryGetValue(projectKey, out var merged))
                        continue;

                    FormatDirectPackages(sb, merged, packageMetadata);
                    FormatTransitivePackages(sb, fw);

                    sb.AppendLine(new string('-', 60));
                }
            }

            return Task.FromResult(sb.ToString());
        }

        /// <summary>
        /// Formats direct package dependencies for the console output.
        /// </summary>
        /// <param name="sb">The StringBuilder to append formatted output to.</param>
        /// <param name="merged">The collection of merged packages to format.</param>
        /// <param name="packageMetadata">The package metadata dictionary for additional details.</param>
        private static void FormatDirectPackages(
            StringBuilder sb, 
            Dictionary<string, MergedPackage> merged, 
            Dictionary<string, PackageMetadata> packageMetadata)
        {
            foreach (var pkg in merged.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"• {pkg.Id} ({pkg.ResolvedVersion})");

                var metaKey = $"{pkg.Id}|{pkg.ResolvedVersion}";
                if (packageMetadata.TryGetValue(metaKey, out var meta))
                {
                    sb.AppendLine($"    Gallery URL: {meta.PackageUrl}");
                    if (!string.IsNullOrEmpty(meta.ProjectUrl))
                        sb.AppendLine($"    Project URL: {meta.ProjectUrl}");
                }

                FormatVersionInformation(sb, pkg);
                FormatDeprecationInformation(sb, pkg);
                FormatVulnerabilityInformation(sb, pkg);
                FormatDependencyInformation(sb, pkg, packageMetadata, metaKey);

                sb.AppendLine();
            }
        }

        /// <summary>
        /// Formats version information for a package.
        /// </summary>
        /// <param name="sb">The StringBuilder to append formatted output to.</param>
        /// <param name="pkg">The package to format version information for.</param>
        private static void FormatVersionInformation(StringBuilder sb, MergedPackage pkg)
        {
            sb.AppendLine($"    Requested: {pkg.RequestedVersion}");
            if (pkg.LatestVersion != null)
                sb.AppendLine($"    Latest:    {pkg.LatestVersion}  " +
                              $"({(pkg.IsOutdated ? "Outdated" : "Up-to-date")})");
        }

        /// <summary>
        /// Formats deprecation information for a package.
        /// </summary>
        /// <param name="sb">The StringBuilder to append formatted output to.</param>
        /// <param name="pkg">The package to format deprecation information for.</param>
        private static void FormatDeprecationInformation(StringBuilder sb, MergedPackage pkg)
        {
            sb.AppendLine($"    Deprecated: {(pkg.IsDeprecated ? $"Yes ({string.Join(", ", pkg.DeprecationReasons)})" : "No")}");
            if (pkg.Alternative != null)
                sb.AppendLine($"      Alternative: {pkg.Alternative.Id} {pkg.Alternative.VersionRange}");
        }

        /// <summary>
        /// Formats vulnerability information for a package.
        /// </summary>
        /// <param name="sb">The StringBuilder to append formatted output to.</param>
        /// <param name="pkg">The package to format vulnerability information for.</param>
        private static void FormatVulnerabilityInformation(StringBuilder sb, MergedPackage pkg)
        {
            if (pkg.Vulnerabilities.Count > 0)
            {
                sb.AppendLine("    Vulnerabilities:");
                foreach (var v in pkg.Vulnerabilities)
                    sb.AppendLine($"      - {v.Severity}: {v.AdvisoryUrl}");
            }
            else
            {
                sb.AppendLine("    Vulnerabilities: None");
            }
        }

        /// <summary>
        /// Formats dependency information for a package.
        /// </summary>
        /// <param name="sb">The StringBuilder to append formatted output to.</param>
        /// <param name="pkg">The package to format dependency information for.</param>
        /// <param name="packageMetadata">The package metadata dictionary.</param>
        /// <param name="metaKey">The metadata key for the current package.</param>
        private static void FormatDependencyInformation(
            StringBuilder sb, 
            MergedPackage pkg, 
            Dictionary<string, PackageMetadata> packageMetadata, 
            string metaKey)
        {
            if (packageMetadata.TryGetValue(metaKey, out var meta))
            {
                sb.AppendLine("    Supported frameworks & their dependencies:");
                if (meta.DependencyGroups.Count == 0)
                {
                    sb.AppendLine("      (none detected)");
                }
                else
                {
                    foreach (var group in meta.DependencyGroups)
                    {
                        var tf = string.IsNullOrEmpty(group.TargetFramework) ? "(none)" : group.TargetFramework;
                        sb.AppendLine($"      • {tf}");
                        if (group.Dependencies.Count == 0)
                            sb.AppendLine("          (none)");
                        else
                            foreach (var d in group.Dependencies)
                                sb.AppendLine($"          - {d.Id} {d.Range}");
                    }
                }
            }
        }

        /// <summary>
        /// Formats transitive package information for a framework.
        /// </summary>
        /// <param name="sb">The StringBuilder to append formatted output to.</param>
        /// <param name="fw">The framework containing transitive packages.</param>
        private static void FormatTransitivePackages(StringBuilder sb, FrameworkInfo fw)
        {
            sb.AppendLine("Transitive packages:");
            if (fw.TransitivePackages?.Count > 0)
                foreach (var tp in fw.TransitivePackages)
                    sb.AppendLine($" • {tp.Id} ({tp.ResolvedVersion})");
            else
                sb.AppendLine(" (none)");
        }
    }
}