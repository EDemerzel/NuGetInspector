using System.Text.Json.Serialization;

namespace NuGetInspectorApp.Models
{

    public class DotnetListReport
    {
        [JsonPropertyName("projects")]
        public List<ProjectInfo>? Projects { get; set; }
    }

    public class ProjectInfo
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("frameworks")]
        public List<FrameworkInfo> Frameworks { get; set; } = new();
    }

    public class FrameworkInfo
    {
        [JsonPropertyName("framework")]
        public string Framework { get; set; } = "";

        [JsonPropertyName("topLevelPackages")]
        public List<PackageInfo> TopLevelPackages { get; set; } = new();

        [JsonPropertyName("transitivePackages")]
        public List<TransitiveInfo>? TransitivePackages { get; set; }
    }

    public class PackageInfo
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("requestedVersion")] public string RequestedVersion { get; set; } = "";
        [JsonPropertyName("resolvedVersion")] public string ResolvedVersion { get; set; } = "";
        [JsonPropertyName("latestVersion")] public string? LatestVersion { get; set; }
        [JsonPropertyName("deprecationReasons")] public List<string>? DeprecationReasons { get; set; }
        [JsonPropertyName("alternativePackage")] public AlternativePackageInfo? AlternativePackage { get; set; }
        [JsonPropertyName("vulnerabilities")] public List<VulnerabilityInfo>? Vulnerabilities { get; set; }
    }

    public class AlternativePackageInfo
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("versionRange")] public string VersionRange { get; set; } = "";
    }

    public class VulnerabilityInfo
    {
        [JsonPropertyName("severity")] public string Severity { get; set; } = "";
        [JsonPropertyName("advisoryUrl")] public string AdvisoryUrl { get; set; } = "";
    }

    public class TransitiveInfo
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("resolvedVersion")] public string ResolvedVersion { get; set; } = "";
    }

    public class PackageMetadata
    {
        public string PackageUrl { get; set; } = "";
        public string? ProjectUrl { get; set; }
        public List<DependencyGroup> DependencyGroups { get; set; } = new();
    }

    public class DependencyGroup
    {
        public string TargetFramework { get; set; } = "";
        public List<PackageDependency> Dependencies { get; set; } = new();
    }

    public class PackageDependency
    {
        public string Id { get; set; } = "";
        public string Range { get; set; } = "";
    }

    public class MergedPackage
    {
        public string Id { get; init; } = "";
        public string RequestedVersion { get; set; } = "";
        public string ResolvedVersion { get; set; } = "";
        public string? LatestVersion { get; set; }
        public bool IsOutdated { get; set; }
        public bool IsDeprecated { get; set; }
        public List<string> DeprecationReasons { get; set; } = new();
        public AlternativePackageInfo? Alternative { get; set; }
        public List<VulnerabilityInfo> Vulnerabilities { get; set; } = new();
    }

    public enum ReportType { Outdated, Deprecated, Vulnerable }

}
