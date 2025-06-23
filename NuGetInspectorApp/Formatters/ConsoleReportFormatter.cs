using NuGetInspectorApp.Models;
using System.Text;

namespace NuGetInspectorApp.Formatters;

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
        Dictionary<string, PackageMetaData> packageMetadata,
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
    /// <param name="packageMetaData">The package Metadata dictionary for additional details.</param>
    /// <summary>
    /// Formats direct package dependencies for the console output.
    /// </summary>
    private static void FormatDirectPackages(
        StringBuilder sb,
        Dictionary<string, MergedPackage> merged,
        Dictionary<string, PackageMetaData> packageMetaData)
    {
        foreach (var pkg in merged.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"• {pkg.Id} ({pkg.ResolvedVersion})");

            var metaKey = $"{pkg.Id}|{pkg.ResolvedVersion}";
            PackageMetaData? meta = null;
            packageMetaData.TryGetValue(metaKey, out meta);

            if (meta != null)
            {
                sb.AppendLine($"    Gallery URL: {meta.PackageUrl}");

                if (!string.IsNullOrEmpty(meta.ProjectUrl))
                    sb.AppendLine($"    Project URL: {meta.ProjectUrl}");

                if (!string.IsNullOrEmpty(meta.CatalogUrl))
                    sb.AppendLine($"    Catalog URL: {meta.CatalogUrl}");

                if (!string.IsNullOrEmpty(meta.Description))
                {
                    // Clean and truncate description for console readability
                    var cleanDescription = CleanDescriptionForConsole(meta.Description);
                    var description = cleanDescription.Length > 100
                        ? cleanDescription.Substring(0, 97) + "..."
                        : cleanDescription;
                    sb.AppendLine($"    Description: {description}");
                }
            }

            FormatVersionInformation(sb, pkg);
            FormatDeprecationInformation(sb, pkg, meta);
            FormatVulnerabilityInformation(sb, pkg);
            FormatDependencyInformation(sb, pkg, packageMetaData, metaKey);

            sb.AppendLine();
        }
    }

    /// <summary>
    /// Cleans a package description for console output by removing problematic characters.
    /// </summary>
    /// <param name="description">The raw description text.</param>
    /// <returns>A cleaned description suitable for single-line console output.</returns>
    private static string CleanDescriptionForConsole(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        // Remove all types of line breaks and normalize whitespace
        var cleaned = description
            .Replace("\r\n", " ")      // Windows line endings
            .Replace("\r", " ")        // Mac line endings
            .Replace("\n", " ")        // Unix line endings
            .Replace("\t", " ")        // Tabs
            .Replace("  ", " ");       // Double spaces

        // Continue removing multiple spaces until none remain
        while (cleaned.Contains("  "))
        {
            cleaned = cleaned.Replace("  ", " ");
        }

        return cleaned.Trim();
    }

    /// <summary>
    /// Formats version information for a package.
    /// </summary>
    /// <param name="sb">The StringBuilder to append formatted output to.</param>
    /// <param name="pkg">The package to format version information for.</param>
    private static void FormatVersionInformation(StringBuilder sb, MergedPackage pkg)
    {
        sb.AppendLine($"    Requested: {pkg.RequestedVersion}");

        // Check if we have latest version information
        if (!string.IsNullOrEmpty(pkg.LatestVersion))
        {
            // We have latest version information - use IsOutdated flag
            if (pkg.IsOutdated)
            {
                sb.AppendLine($"    Latest:    {pkg.LatestVersion}  (Outdated)");
            }
            else
            {
                sb.AppendLine($"    Latest:    {pkg.LatestVersion}  (Current)");
            }
        }
        else
        {
            // No latest version found - provide helpful messaging
            if (pkg.ResolvedVersion?.Contains("-") == true)
            {
                // Looks like a pre-release version
                sb.AppendLine($"    Latest:    Unknown (Pre-release version - stable version may not be available)");
            }
            else
            {
                // Regular version but no latest info available
                sb.AppendLine($"    Latest:    Unknown (Unable to determine latest version)");
            }
        }
    }


    /// <summary>
    /// Formats deprecation information for a package.
    /// </summary>
    /// <param name="sb">The StringBuilder to append formatted output to.</param>
    /// <param name="pkg">The package to format deprecation information for.</param>
    /// <param name="meta">Optional package metadata from NuGet API.</param>
    private static void FormatDeprecationInformation(StringBuilder sb, MergedPackage pkg, PackageMetaData? meta = null)
    {
        // Check both sources: dotnet CLI report and NuGet API metadata
        var isDeprecated = pkg.IsDeprecated || (meta?.IsDeprecated == true);

        if (isDeprecated)
        {
            // Combine reasons from both sources
            var allReasons = new List<string>();

            if (pkg.DeprecationReasons?.Any() == true)
                allReasons.AddRange(pkg.DeprecationReasons);

            if (meta?.DeprecationReasons?.Any() == true)
                allReasons.AddRange(meta.DeprecationReasons.Where(r => !allReasons.Contains(r)));

            if (allReasons.Any())
            {
                sb.AppendLine($"    Deprecated: Yes ({string.Join(", ", allReasons)})");
            }
            else
            {
                sb.AppendLine($"    Deprecated: Yes");
            }

            // Show deprecation message if available from API
            if (!string.IsNullOrEmpty(meta?.DeprecationMessage))
            {
                sb.AppendLine($"      Message: {meta.DeprecationMessage}");
            }

            // Show alternatives from both sources, preferring API catalog data
            var alternativeShown = false;

            // First, show alternative from API catalog (more authoritative)
            if (meta?.AlternativePackage != null)
            {
                sb.AppendLine($"      Alternative: {meta.AlternativePackage.Id} {meta.AlternativePackage.VersionRange ?? "*"}");
                alternativeShown = true;
            }

            // Then show CLI alternative if different from API alternative
            if (pkg.Alternative != null)
            {
                var cliAlternative = $"{pkg.Alternative.Id} {pkg.Alternative.VersionRange}";
                var apiAlternative = meta?.AlternativePackage != null ?
                    $"{meta.AlternativePackage.Id} {meta.AlternativePackage.VersionRange ?? "*"}" : null;

                // Only show CLI alternative if it's different from API alternative
                if (!alternativeShown || !string.Equals(cliAlternative, apiAlternative, StringComparison.OrdinalIgnoreCase))
                {
                    var prefix = alternativeShown ? "      CLI Alternative: " : "      Alternative: ";
                    sb.AppendLine($"{prefix}{pkg.Alternative.Id} {pkg.Alternative.VersionRange}");
                }
            }
        }
        else
        {
            sb.AppendLine($"    Deprecated: No");
        }
    }

    /// <summary>
    /// Formats vulnerability information for a package.
    /// </summary>
    /// <param name="sb">The StringBuilder to append formatted output to.</param>
    /// <param name="pkg">The package to format vulnerability information for.</param>
    private static void FormatVulnerabilityInformation(StringBuilder sb, MergedPackage pkg)
    {
        if (pkg.Vulnerabilities?.Count > 0)
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
    /// <param name="packageMetaData">The package Metadata dictionary.</param>
    /// <param name="metaKey">The Metadata key for the current package.</param>
    private static void FormatDependencyInformation(
        StringBuilder sb,
        MergedPackage pkg,
        Dictionary<string, PackageMetaData> packageMetaData,
        string metaKey)
    {
        if (packageMetaData.TryGetValue(metaKey, out var meta))
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
        {
            foreach (var tp in fw.TransitivePackages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($" • {tp.Id} ({tp.ResolvedVersion})");
            }
            sb.AppendLine($" ({fw.TransitivePackages.Count} transitive dependencies found)");
        }
        else
        {
            sb.AppendLine(" (none)");
        }
    }
}