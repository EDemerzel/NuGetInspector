using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;

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

        /// <inheritdoc />
        public Dictionary<string, PackageReference> MergePackages(
            List<ProjectInfo> outdatedProjects,
            List<ProjectInfo> deprecatedProjects,
            List<ProjectInfo> vulnerableProjects,
            string projectPath,
            string framework)
        {
            if (outdatedProjects == null) throw new ArgumentNullException(nameof(outdatedProjects));
            if (deprecatedProjects == null) throw new ArgumentNullException(nameof(deprecatedProjects));
            if (vulnerableProjects == null) throw new ArgumentNullException(nameof(vulnerableProjects));
            if (string.IsNullOrWhiteSpace(projectPath)) throw new ArgumentException("projectPath cannot be null or whitespace", nameof(projectPath));
            if (string.IsNullOrWhiteSpace(framework)) throw new ArgumentException("framework cannot be null or whitespace", nameof(framework));

            var result = new Dictionary<string, PackageReference>(StringComparer.OrdinalIgnoreCase);

            // Helper to upsert package info from a list
            void UpsertFrom(List<ProjectInfo> projects, Action<PackageReference, PackageReference> mergeAction)
            {
                var project = projects.FirstOrDefault(p => string.Equals(p.Path, projectPath, StringComparison.OrdinalIgnoreCase));
                if (project == null)
                {
                    _logger.LogDebug("No project found with path {ProjectPath} in the current list being processed.", projectPath);
                    return;
                }

                var fw = project.Frameworks?.FirstOrDefault(f => string.Equals(f.Framework, framework, StringComparison.OrdinalIgnoreCase));
                if (fw == null)
                {
                    _logger.LogDebug("No framework matching {Framework} found in project {ProjectPath}.", framework, projectPath);
                    return;
                }
                if (fw.TopLevelPackages == null)
                {
                    _logger.LogDebug("TopLevelPackages list is null for project {ProjectPath}, framework {Framework}.", projectPath, framework);
                    return;
                }


                foreach (var pkg in fw.TopLevelPackages)
                {
                    if (pkg == null || string.IsNullOrEmpty(pkg.Id))
                    {
                        _logger.LogWarning("Skipping a null package or package with null/empty ID in project {ProjectPath}, framework {Framework}.", projectPath, framework);
                        continue;
                    }

                    if (!result.TryGetValue(pkg.Id, out var existing))
                    {
                        existing = ClonePackageReference(pkg);
                        result[pkg.Id] = existing;
                        _logger.LogTrace("Added new package {PackageId} to results from current list.", pkg.Id);
                    }
                    else
                    {
                        _logger.LogTrace("Merging data for existing package {PackageId} from current list.", pkg.Id);
                    }
                    mergeAction(existing, pkg); // Apply specific merge logic
                }
            }

            // Process Outdated
            _logger.LogDebug("Processing outdated packages for project {ProjectPath}, framework {Framework}.", projectPath, framework);
            UpsertFrom(outdatedProjects, (existing, incoming) =>
            {
                existing.LatestVersion = incoming.LatestVersion;
                // IsOutdated will be finalized at the end for consistency
            });

            // Process Deprecated
            _logger.LogDebug("Processing deprecated packages for project {ProjectPath}, framework {Framework}.", projectPath, framework);
            UpsertFrom(deprecatedProjects, (existing, incoming) =>
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

            // Process Vulnerable
            _logger.LogDebug("Processing vulnerable packages for project {ProjectPath}, framework {Framework}.", projectPath, framework);
            UpsertFrom(vulnerableProjects, (existing, incoming) =>
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

            // Final pass to set IsOutdated consistently and ensure HasVulnerabilities aligns with Vulnerabilities list
            _logger.LogDebug("Finalizing IsOutdated and HasVulnerabilities flags for all merged packages.");
            // Final pass to set IsOutdated consistently
            foreach (var pkg in result.Values)
            {
                // Enhanced version comparison
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

        private static bool IsPackageOutdated(string? resolvedVersion, string? latestVersion)
        {
            if (string.IsNullOrWhiteSpace(resolvedVersion) || string.IsNullOrWhiteSpace(latestVersion))
                return false;

            // Simple string comparison (could be enhanced with SemVer parsing)
            return !string.Equals(resolvedVersion.Trim(), latestVersion.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // This explicit interface implementation was throwing NotImplementedException.
        // It should either be implemented or removed if the public method is the intended one.
        // Assuming the public method is the one being used and tested.
        // If MergedPackage is a different target type, this needs separate logic.
Dictionary<string, MergedPackage> IPackageAnalyzer.MergePackages(
    List<ProjectInfo> outdatedProjects,
    List<ProjectInfo> deprecatedProjects,
    List<ProjectInfo> vulnerableProjects,
    string projectPath,
    string framework)
{
    var packageReferenceResult = MergePackages(outdatedProjects, deprecatedProjects, vulnerableProjects, projectPath, framework);
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

    // Enum ReportType is defined in the same file in the provided context.
    // If it's meant to be used by UpsertPackages (which is currently unused), it should remain.
    // However, the primary MergePackages method does not use it.
    // For clarity, if UpsertPackages and ReportType are not used by the public MergePackages,
    // they could be removed or refactored. Keeping it as per provided context.

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