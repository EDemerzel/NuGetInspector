using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NuGetInspectorApp.Services;

/// <summary>
/// Provides functionality for merging package information from multiple dotnet list reports.
/// </summary>
/// <remarks>
/// This service combines data from baseline, outdated, deprecated, and vulnerable package reports
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

    private static PackageReference ClonePackageReference(PackageReference original)
    {
        if (original == null)
            throw new ArgumentNullException(nameof(original));

        return new PackageReference
        {
            Id = original.Id,
            RequestedVersion = original.RequestedVersion,
            ResolvedVersion = original.ResolvedVersion,
            LatestVersion = original.LatestVersion,
            IsOutdated = false, // Will be calculated later
            IsDeprecated = original.IsDeprecated,
            DeprecationReasons = original.DeprecationReasons?.ToList() ?? new List<string>(),
            Alternative = original.Alternative != null ? new PackageAlternative
            {
                Id = original.Alternative.Id,
                VersionRange = original.Alternative.VersionRange
            } : null,
            HasVulnerabilities = original.HasVulnerabilities,
            Vulnerabilities = original.Vulnerabilities?.Select(v => new VulnerabilityInfo
            {
                Severity = v.Severity ?? "Unknown",
                AdvisoryUrl = v.AdvisoryUrl ?? string.Empty
            }).ToList() ?? new List<VulnerabilityInfo>()
        };
    }

    /// <summary>
    /// Merges package information from baseline and issue-specific reports.
    /// </summary>
    /// <param name="baselineProjects">The baseline report containing all packages (from dotnet list package).</param>
    /// <param name="outdatedProjects">Projects containing packages with newer versions available.</param>
    /// <param name="deprecatedProjects">Projects containing deprecated packages.</param>
    /// <param name="vulnerableProjects">Projects containing packages with security vulnerabilities.</param>
    /// <param name="projectPath">The path of the project to analyze.</param>
    /// <param name="framework">The target framework to analyze.</param>
    /// <returns>A dictionary of merged package information keyed by package ID.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any required parameter is null.</exception>
    /// <exception cref="ArgumentException">Thrown when projectPath or framework is null or whitespace.</exception>
    public Dictionary<string, PackageReference> MergePackages(
        List<ProjectInfo> baselineProjects,
        List<ProjectInfo> outdatedProjects,
        List<ProjectInfo> deprecatedProjects,
        List<ProjectInfo> vulnerableProjects,
        string projectPath,
        string framework)
    {
        if (baselineProjects == null) throw new ArgumentNullException(nameof(baselineProjects));
        if (outdatedProjects == null) throw new ArgumentNullException(nameof(outdatedProjects));
        if (deprecatedProjects == null) throw new ArgumentNullException(nameof(deprecatedProjects));
        if (vulnerableProjects == null) throw new ArgumentNullException(nameof(vulnerableProjects));
        if (string.IsNullOrWhiteSpace(projectPath)) throw new ArgumentException("projectPath cannot be null or whitespace", nameof(projectPath));
        if (string.IsNullOrWhiteSpace(framework)) throw new ArgumentException("framework cannot be null or whitespace", nameof(framework));

        var result = new Dictionary<string, PackageReference>(StringComparer.OrdinalIgnoreCase);

        // Helper to upsert package info from a list
        void UpsertFrom(List<ProjectInfo> projects, string reportType, Action<PackageReference, PackageReference> mergeAction)
        {
            var project = projects.FirstOrDefault(p => string.Equals(p.Path, projectPath, StringComparison.OrdinalIgnoreCase));
            if (project == null)
            {
                _logger.LogDebug("No project found with path {ProjectPath} in {ReportType} report.", projectPath, reportType);
                return;
            }

            var fw = project.Frameworks?.FirstOrDefault(f => string.Equals(f.Framework, framework, StringComparison.OrdinalIgnoreCase));
            if (fw == null)
            {
                _logger.LogDebug("No framework matching {Framework} found in project {ProjectPath} for {ReportType} report.", framework, projectPath, reportType);
                return;
            }
            if (fw.TopLevelPackages == null)
            {
                _logger.LogDebug("TopLevelPackages list is null for project {ProjectPath}, framework {Framework} in {ReportType} report.", projectPath, framework, reportType);
                return;
            }

            foreach (var pkg in fw.TopLevelPackages)
            {
                if (pkg == null || string.IsNullOrEmpty(pkg.Id))
                {
                    _logger.LogWarning("Skipping a null package or package with null/empty ID in project {ProjectPath}, framework {Framework}, {ReportType} report.", projectPath, framework, reportType);
                    continue;
                }

                if (!result.TryGetValue(pkg.Id, out var existing))
                {
                    existing = ClonePackageReference(pkg);
                    result[pkg.Id] = existing;
                    _logger.LogTrace("Added new package {PackageId} to results from {ReportType} report.", pkg.Id, reportType);
                }
                else
                {
                    _logger.LogTrace("Merging data for existing package {PackageId} from {ReportType} report.", pkg.Id, reportType);
                }
                mergeAction(existing, pkg); // Apply specific merge logic
            }
        }

        // Step 1: Process Baseline - This establishes all packages in the project
        _logger.LogDebug("Processing baseline packages for project {ProjectPath}, framework {Framework}.", projectPath, framework);
        UpsertFrom(baselineProjects, "baseline", (existing, incoming) =>
        {
            // For baseline packages, we take all basic information as-is
            // Status flags will be updated by subsequent reports
            existing.RequestedVersion = incoming.RequestedVersion ?? existing.RequestedVersion;
            existing.ResolvedVersion = incoming.ResolvedVersion ?? existing.ResolvedVersion;

            // Initialize status flags to safe defaults
            existing.IsOutdated = false;
            existing.IsDeprecated = false;
            existing.HasVulnerabilities = false;
            existing.DeprecationReasons = new List<string>();
            existing.Vulnerabilities = new List<VulnerabilityInfo>();
            existing.Alternative = null;
            existing.LatestVersion = incoming.LatestVersion; // May be null for baseline
        });

        // Step 2: Process Outdated - Updates LatestVersion and will set IsOutdated flag
        _logger.LogDebug("Processing outdated packages for project {ProjectPath}, framework {Framework}.", projectPath, framework);
        UpsertFrom(outdatedProjects, "outdated", (existing, incoming) =>
        {
            // Update latest version information from outdated report
            if (!string.IsNullOrEmpty(incoming.LatestVersion))
            {
                // Check if this is an error message rather than a version
                var errorMessages = new[]
                {
                    "Not found at the sources",
                    "Unable to load",
                    "Not found",
                    "Error",
                    "Failed"
                };

                if (errorMessages.Any(error => incoming.LatestVersion.Contains(error, StringComparison.OrdinalIgnoreCase)))
                {
                    // Don't set LatestVersion for error messages - leave it null
                    _logger.LogDebug("Ignoring error message in LatestVersion field for package {PackageId}: {ErrorMessage}",
                        incoming.Id, incoming.LatestVersion);
                }
                else
                {
                    existing.LatestVersion = incoming.LatestVersion;
                }
            }
            // IsOutdated will be calculated in the final pass for consistency
        });

        // Step 3: Process Deprecated - Updates deprecation status and alternatives
        _logger.LogDebug("Processing deprecated packages for project {ProjectPath}, framework {Framework}.", projectPath, framework);
        UpsertFrom(deprecatedProjects, "deprecated", (existing, incoming) =>
        {
            existing.IsDeprecated = incoming.IsDeprecated;
            if (incoming.DeprecationReasons != null && incoming.DeprecationReasons.Any())
                existing.DeprecationReasons = incoming.DeprecationReasons.ToList(); // Ensure it's a new list
            if (incoming.Alternative != null)
                existing.Alternative = new PackageAlternative // Clone alternative
                {
                    Id = incoming.Alternative.Id,
                    VersionRange = incoming.Alternative.VersionRange
                };
        });

        // Step 4: Process Vulnerable - Updates vulnerability status and details
        _logger.LogDebug("Processing vulnerable packages for project {ProjectPath}, framework {Framework}.", projectPath, framework);
        UpsertFrom(vulnerableProjects, "vulnerable", (existing, incoming) =>
        {
            // Always sync the flag first
            existing.HasVulnerabilities = incoming.HasVulnerabilities;

            if (incoming.Vulnerabilities?.Any() == true)
            {
                // Clone and merge vulnerabilities, avoiding duplicates
                existing.Vulnerabilities = incoming.Vulnerabilities
                    .Where(v => v != null)
                    .Select(v => new VulnerabilityInfo
                    {
                        Severity = v.Severity ?? "Unknown",
                        AdvisoryUrl = v.AdvisoryUrl ?? string.Empty
                    })
                    .ToList();
            }
            else if (incoming.HasVulnerabilities)
            {
                // HasVulnerabilities is true but no specific vulnerabilities listed
                existing.Vulnerabilities = existing.Vulnerabilities ?? new List<VulnerabilityInfo>();
                _logger.LogWarning("Package {PackageId} marked as vulnerable but no specific vulnerabilities provided.", incoming.Id);
            }
            else
            {
                // Ensure consistency: if not vulnerable, clear any existing vulnerabilities
                existing.Vulnerabilities = new List<VulnerabilityInfo>();
            }
        });

        // Step 5: Final pass to set IsOutdated consistently and ensure flag consistency
        _logger.LogDebug("Finalizing package status flags for all merged packages.");
        foreach (var pkg in result.Values)
        {
            // FOR PACKAGES NOT IN THE OUTDATED REPORT: Set LatestVersion to ResolvedVersion (they're current)
            if (string.IsNullOrEmpty(pkg.LatestVersion))
            {
                // If we don't have latest version info, assume it's current
                // This happens for packages that don't appear in the --outdated report
                pkg.LatestVersion = pkg.ResolvedVersion;
                _logger.LogTrace("Package {PackageId} not in outdated report, assuming current version {Version}",
                    pkg.Id, pkg.ResolvedVersion);
            }

            // Calculate IsOutdated based on version comparison
            pkg.IsOutdated = IsPackageOutdated(pkg.ResolvedVersion, pkg.LatestVersion);

            // Ensure HasVulnerabilities reflects the Vulnerabilities list
            pkg.HasVulnerabilities = pkg.Vulnerabilities?.Any() == true;

            // Log inconsistencies for debugging
            if (pkg.HasVulnerabilities != (pkg.Vulnerabilities?.Any() == true))
            {
                _logger.LogWarning("Vulnerability flag inconsistency for package {PackageId}: Flag={HasVulnerabilities}, ActualCount={Count}",
                    pkg.Id, pkg.HasVulnerabilities, pkg.Vulnerabilities?.Count ?? 0);
            }
        }

        _logger.LogInformation("Merged {PackageCount} unique packages for project {ProjectPath}, framework {Framework}.", result.Count, projectPath, framework);
        return result;
    }

    /// <summary>
    /// Determines if a package is outdated by comparing resolved and latest versions.
    /// </summary>
    /// <param name="resolvedVersion">The currently resolved version of the package.</param>
    /// <param name="latestVersion">The latest available version of the package.</param>
    /// <returns>True if the package is outdated, false otherwise.</returns>
    private static bool IsPackageOutdated(string? resolvedVersion, string? latestVersion)
    {
        // If we don't have both versions, we cannot determine if it's outdated
        if (string.IsNullOrWhiteSpace(resolvedVersion) || string.IsNullOrWhiteSpace(latestVersion))
            return false;

        // Handle error messages from dotnet CLI that aren't actually version numbers
        var errorMessages = new[]
        {
            "Not found at the sources",
            "Unable to load",
            "Not found",
            "Error",
            "Failed"
        };

        if (errorMessages.Any(error => latestVersion.Contains(error, StringComparison.OrdinalIgnoreCase)))
        {
            return false; // Don't consider it outdated if we can't determine the latest version
        }

        // Simple string comparison (could be enhanced with SemVer parsing)
        return !string.Equals(resolvedVersion.Trim(), latestVersion.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Merges package information from baseline and issue-specific reports, returning MergedPackage objects.
    /// </summary>
    /// <param name="baselineProjects">The baseline report containing all packages.</param>
    /// <param name="outdatedProjects">Projects containing packages with newer versions available.</param>
    /// <param name="deprecatedProjects">Projects containing deprecated packages.</param>
    /// <param name="vulnerableProjects">Projects containing packages with security vulnerabilities.</param>
    /// <param name="projectPath">The path of the project to analyze.</param>
    /// <param name="framework">The target framework to analyze.</param>
    /// <returns>A dictionary of merged package information keyed by package ID.</returns>
    Dictionary<string, MergedPackage> IPackageAnalyzer.MergePackages(
        List<ProjectInfo> baselineProjects,
        List<ProjectInfo> outdatedProjects,
        List<ProjectInfo> deprecatedProjects,
        List<ProjectInfo> vulnerableProjects,
        string projectPath,
        string framework)
    {
        var packageReferenceResult = MergePackages(baselineProjects, outdatedProjects, deprecatedProjects, vulnerableProjects, projectPath, framework);
        var mergedPackageResult = new Dictionary<string, MergedPackage>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in packageReferenceResult)
        {
            var pr = entry.Value;
            mergedPackageResult[entry.Key] = new MergedPackage
            {
                Id = pr.Id,
                RequestedVersion = pr.RequestedVersion,
                ResolvedVersion = pr.ResolvedVersion,
                LatestVersion = pr.LatestVersion,
                IsOutdated = pr.IsOutdated,
                IsDeprecated = pr.IsDeprecated,
                DeprecationReasons = pr.DeprecationReasons?.ToList() ?? new List<string>(),
                Alternative = pr.Alternative != null ? new PackageAlternative
                {
                    Id = pr.Alternative.Id,
                    VersionRange = pr.Alternative.VersionRange
                } : null,
                Vulnerabilities = pr.Vulnerabilities?.Select(v => new VulnerabilityInfo
                {
                    Severity = v.Severity,
                    AdvisoryUrl = v.AdvisoryUrl
                }).ToList() ?? new List<VulnerabilityInfo>()
            };
        }
        return mergedPackageResult;
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
    /// Represents the baseline report containing all packages (no flags).
    /// </summary>
    Baseline,

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