using System.Text.Json.Serialization;

namespace NuGetInspectorApp.Models
{
    /// <summary>
    /// Represents the root object for dotnet list package JSON output.
    /// </summary>
    /// <remarks>
    /// This class is used to deserialize the JSON output from 'dotnet list package --format json' commands,
    /// which provides information about packages across multiple projects in a solution.
    /// </remarks>
    public class DotnetListReport
    {
        /// <summary>
        /// Gets or sets the schema version of the report format.
        /// </summary>
        /// <value>The version number of the JSON report schema.</value>
        [JsonPropertyName("version")]
        public int Version { get; set; }

        /// <summary>
        /// Gets or sets the command-line parameters used to generate the report.
        /// </summary>
        /// <value>The parameters passed to the dotnet list package command.</value>
        [JsonPropertyName("parameters")]
        public string? Parameters { get; set; }

        /// <summary>
        /// Gets or sets the list of NuGet package sources used for the report.
        /// </summary>
        /// <value>A list of package source URLs.</value>
        [JsonPropertyName("sources")]
        public List<string>? Sources { get; set; }

        /// <summary>
        /// Gets or sets the collection of projects included in the report.
        /// </summary>
        /// <value>A list of <see cref="ProjectInfo"/> objects representing each project in the solution, or <c>null</c> if no projects are found.</value>
        [JsonPropertyName("projects")]
        public List<ProjectInfo>? Projects { get; set; }
    }

    /// <summary>
    /// Represents information about a single project within a solution.
    /// </summary>
    /// <remarks>
    /// Contains the project path and framework-specific package information.
    /// A single project can target multiple frameworks, each with its own set of packages.
    /// </remarks>
    public class ProjectInfo
    {
        /// <summary>
        /// Gets or sets the file system path to the project file.
        /// </summary>
        /// <value>The absolute or relative path to the .csproj, .vbproj, or .fsproj file.</value>
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        /// <summary>
        /// Gets or sets the collection of target frameworks and their associated packages.
        /// </summary>
        /// <value>A list of <see cref="FrameworkInfo"/> objects, one for each target framework in the project.</value>
        [JsonPropertyName("frameworks")]
        public List<FrameworkInfo> Frameworks { get; set; } = new();
    }

    /// <summary>
    /// Represents package information for a specific target framework within a project.
    /// </summary>
    /// <remarks>
    /// Each framework can have different sets of packages due to conditional package references
    /// or framework-specific dependencies.
    /// </remarks>
    public class FrameworkInfo
    {
        /// <summary>
        /// Gets or sets the target framework moniker (TFM).
        /// </summary>
        /// <value>The target framework identifier such as "net9.0", "netstandard2.0", or "net48".</value>
        /// <example>
        /// Common values include:
        /// <list type="bullet">
        /// <item><description>net9.0 - .NET 9.0</description></item>
        /// <item><description>net8.0 - .NET 8.0</description></item>
        /// <item><description>netstandard2.0 - .NET Standard 2.0</description></item>
        /// <item><description>net48 - .NET Framework 4.8</description></item>
        /// </list>
        /// </example>
        [JsonPropertyName("framework")]
        public string Framework { get; set; } = "";

        /// <summary>
        /// Gets or sets the collection of top-level (directly referenced) packages.
        /// </summary>
        /// <value>A list of <see cref="PackageReference"/> objects representing packages explicitly referenced in the project file.</value>
        /// <remarks>
        /// Top-level packages are those explicitly added to the project via PackageReference elements
        /// or through package manager commands, as opposed to transitive dependencies.
        /// </remarks>
        [JsonPropertyName("topLevelPackages")]
        public List<PackageReference> TopLevelPackages { get; set; } = new();

        /// <summary>
        /// Gets or sets the collection of transitive (indirectly referenced) packages.
        /// </summary>
        /// <value>A list of <see cref="PackageReference"/> objects representing packages that are dependencies of top-level packages.</value>
        /// <remarks>
        /// Transitive packages are automatically included as dependencies of top-level packages.
        /// They are not explicitly referenced in the project file but are required at runtime.
        /// </remarks>
        [JsonPropertyName("transitivePackages")]
        public List<PackageReference>? TransitivePackages { get; set; }
    }

    /// <summary>
    /// Represents a NuGet package reference with version information.
    /// </summary>
    /// <remarks>
    /// This class contains both the requested version (from project file or package.config)
    /// and the resolved version (actually used after dependency resolution).
    /// </remarks>
    public class PackageReference
    {
        /// <summary>
        /// Gets or sets the package identifier.
        /// </summary>
        /// <value>The unique package ID as published on NuGet.org or other package sources.</value>
        /// <example>Microsoft.Extensions.Logging, Newtonsoft.Json, AutoMapper</example>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        /// <summary>
        /// Gets or sets the version or version range requested in the project file.
        /// </summary>
        /// <value>The version specification as written in the project file, which may include ranges, wildcards, or floating versions.</value>
        /// <example>
        /// Examples include:
        /// <list type="bullet">
        /// <item><description>9.0.0 - Exact version</description></item>
        /// <item><description>[9.0.0,) - Minimum version with no upper bound</description></item>
        /// <item><description>9.* - Floating version (latest 9.x)</description></item>
        /// </list>
        /// </example>
        [JsonPropertyName("requestedVersion")]
        public string RequestedVersion { get; set; } = "";

        /// <summary>
        /// Gets or sets the actual version resolved and used by the project.
        /// </summary>
        /// <value>The specific version number that was selected by the package resolution algorithm.</value>
        /// <remarks>
        /// This is always a specific version number (e.g., "9.0.1") even if the requested version
        /// was a range or floating version. This represents the version actually downloaded and used.
        /// </remarks>
        [JsonPropertyName("resolvedVersion")]
        public string ResolvedVersion { get; set; } = "";

        /// <summary>
        /// Gets or sets the latest available version of the package.
        /// </summary>
        /// <value>The newest version available from the package source, or <c>null</c> if version information is unavailable.</value>
        /// <remarks>
        /// This property is populated by 'dotnet list package --outdated' commands and indicates
        /// whether an update is available for the package.
        /// </remarks>
        [JsonPropertyName("latestVersion")]
        public string? LatestVersion { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the resolved version is outdated.
        /// This property is typically calculated by comparing ResolvedVersion and LatestVersion.
        /// </summary>
        /// <value><c>true</c> if a newer version is available; otherwise, <c>false</c>.</value>
        public bool IsOutdated { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the package has known vulnerabilities.
        /// </summary>
        /// <value><c>true</c> if the package version has reported security vulnerabilities; otherwise, <c>false</c>.</value>
        /// <remarks>
        /// This information comes from 'dotnet list package --vulnerable' commands and helps identify
        /// packages that should be updated for security reasons.
        /// </remarks>
        [JsonPropertyName("hasVulnerabilities")]
        public bool HasVulnerabilities { get; set; }

        /// <summary>
        /// Gets or sets the collection of known vulnerabilities for this package version.
        /// </summary>
        /// <value>A list of <see cref="VulnerabilityInfo"/> objects describing security issues, or <c>null</c> if no vulnerabilities are known.</value>
        [JsonPropertyName("vulnerabilities")]
        public List<VulnerabilityInfo>? Vulnerabilities { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the package is deprecated.
        /// </summary>
        /// <value><c>true</c> if the package is marked as deprecated by its maintainers; otherwise, <c>false</c>.</value>
        /// <remarks>
        /// Deprecated packages should generally be replaced with alternative packages recommended by the maintainers.
        /// </remarks>
        [JsonPropertyName("isDeprecated")]
        public bool IsDeprecated { get; set; }

        /// <summary>
        /// Gets or sets the collection of reasons why the package is deprecated.
        /// </summary>
        /// <value>A list of deprecation reason strings, or <c>null</c> if the package is not deprecated.</value>
        /// <example>
        /// Common deprecation reasons include:
        /// <list type="bullet">
        /// <item><description>Legacy</description></item>
        /// <item><description>CriticalBugs</description></item>
        /// <item><description>Other</description></item>
        /// </list>
        /// </example>
        [JsonPropertyName("deprecationReasons")]
        public List<string>? DeprecationReasons { get; set; }

        /// <summary>
        /// Gets or sets the alternative package recommendation for deprecated packages.
        /// </summary>
        /// <value>A <see cref="PackageAlternative"/> object suggesting a replacement package, or <c>null</c> if no alternative is specified.</value>
        [JsonPropertyName("alternative")]
        public PackageAlternative? Alternative { get; set; }
    }

    /// <summary>
    /// Represents vulnerability information for a package.
    /// </summary>
    /// <remarks>
    /// Contains details about security vulnerabilities found in specific package versions,
    /// including severity levels and advisory URLs for more information.
    /// </remarks>
    public class VulnerabilityInfo
    {
        /// <summary>
        /// Gets or sets the severity level of the vulnerability.
        /// </summary>
        /// <value>The vulnerability severity classification such as "Low", "Medium", "High", or "Critical".</value>
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the URL to the security advisory with detailed information.
        /// </summary>
        /// <value>A URL pointing to the official security advisory or vulnerability database entry.</value>
        /// <example>https://github.com/advisories/GHSA-xxxx-xxxx-xxxx</example>
        [JsonPropertyName("advisoryUrl")]
        public string? AdvisoryUrl { get; set; }
    }

    /// <summary>
    /// Represents an alternative package recommendation for deprecated packages.
    /// </summary>
    /// <remarks>
    /// When a package is deprecated, maintainers may suggest a replacement package
    /// that provides similar functionality with continued support.
    /// </remarks>
    public class PackageAlternative
    {
        /// <summary>
        /// Gets or sets the identifier of the recommended alternative package.
        /// </summary>
        /// <value>The package ID of the recommended replacement.</value>
        /// <example>System.Text.Json (as an alternative to Newtonsoft.Json)</example>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the recommended version range for the alternative package.
        /// </summary>
        /// <value>The version specification for the alternative package, typically a minimum version requirement.</value>
        /// <example>>=6.0.0</example>
        [JsonPropertyName("versionRange")]
        public string? VersionRange { get; set; }
    }

    /// <summary>
    /// Represents merged package information combining data from multiple dotnet list commands.
    /// </summary>
    /// <remarks>
    /// This class consolidates information from 'dotnet list package', 'dotnet list package --outdated',
    /// 'dotnet list package --deprecated', and 'dotnet list package --vulnerable' commands into a single object.
    /// </remarks>
    public class MergedPackage
    {
        /// <summary>
        /// Gets or sets the package identifier.
        /// </summary>
        /// <value>The unique package ID as published on package sources.</value>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        /// <summary>
        /// Gets or sets the version or version range requested in the project file.
        /// </summary>
        /// <value>The version specification as written in the project file.</value>
        [JsonPropertyName("requestedVersion")]
        public string RequestedVersion { get; set; } = "";

        /// <summary>
        /// Gets or sets the actual version resolved and used by the project.
        /// </summary>
        /// <value>The specific version number selected by package resolution.</value>
        [JsonPropertyName("resolvedVersion")]
        public string ResolvedVersion { get; set; } = "";

        /// <summary>
        /// Gets or sets the latest available version of the package.
        /// </summary>
        /// <value>The newest version available, or <c>null</c> if unknown.</value>
        [JsonPropertyName("latestVersion")]
        public string? LatestVersion { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the resolved version is outdated.
        /// </summary>
        /// <value><c>true</c> if a newer version is available; otherwise, <c>false</c>.</value>
        [JsonPropertyName("isOutdated")]
        public bool IsOutdated { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the package is deprecated.
        /// </summary>
        /// <value><c>true</c> if the package is marked as deprecated; otherwise, <c>false</c>.</value>
        [JsonPropertyName("isDeprecated")]
        public bool IsDeprecated { get; set; }

        /// <summary>
        /// Gets or sets the collection of deprecation reasons.
        /// </summary>
        /// <value>A list of reasons why the package is deprecated.</value>
        [JsonPropertyName("deprecationReasons")]
        public List<string> DeprecationReasons { get; set; } = new();

        /// <summary>
        /// Gets or sets the alternative package recommendation.
        /// </summary>
        /// <value>The suggested replacement package, or <c>null</c> if none is specified.</value>
        [JsonPropertyName("alternative")]
        public PackageAlternative? Alternative { get; set; }

        /// <summary>
        /// Gets or sets the collection of known vulnerabilities.
        /// </summary>
        /// <value>A list of security vulnerabilities affecting this package version.</value>
        [JsonPropertyName("vulnerabilities")]
        public List<VulnerabilityInfo> Vulnerabilities { get; set; } = new();
    }

    /// <summary>
    /// Represents detailed metadata about a package fetched from the NuGet API.
    /// </summary>
    /// <remarks>
    /// This class contains additional information not available from dotnet list commands,
    /// such as project URLs and dependency information.
    /// </remarks>
    public class PackageMetadata
    {
        /// <summary>
        /// Gets or sets the URL to the package's gallery page.
        /// </summary>
        /// <value>The NuGet gallery URL for the specific package version.</value>
        /// <example>https://www.nuget.org/packages/Microsoft.Extensions.Logging/9.0.0</example>
        [JsonPropertyName("packageUrl")]
        public string PackageUrl { get; set; } = "";

        /// <summary>
        /// Gets or sets the URL to the package's project or repository.
        /// </summary>
        /// <value>The project homepage or source repository URL, or <c>null</c> if not specified.</value>
        /// <example>https://github.com/dotnet/extensions</example>
        [JsonPropertyName("projectUrl")]
        public string? ProjectUrl { get; set; }

        /// <summary>
        /// Gets or sets the collection of dependency groups organized by target framework.
        /// </summary>
        /// <value>A list of <see cref="DependencyGroup"/> objects showing dependencies for each supported framework.</value>
        [JsonPropertyName("dependencyGroups")]
        public List<DependencyGroup> DependencyGroups { get; set; } = new();
    }

    /// <summary>
    /// Represents a group of dependencies for a specific target framework.
    /// </summary>
    /// <remarks>
    /// Packages can have different dependencies depending on the target framework,
    /// allowing for framework-specific optimizations and compatibility.
    /// </remarks>
    public class DependencyGroup
    {
        /// <summary>
        /// Gets or sets the target framework for this dependency group.
        /// </summary>
        /// <value>The target framework moniker, or an empty string for framework-agnostic dependencies.</value>
        /// <example>net9.0, netstandard2.0, net48</example>
        [JsonPropertyName("targetFramework")]
        public string TargetFramework { get; set; } = "";

        /// <summary>
        /// Gets or sets the collection of package dependencies for this framework.
        /// </summary>
        /// <value>A list of <see cref="PackageDependency"/> objects representing required packages.</value>
        [JsonPropertyName("dependencies")]
        public List<PackageDependency> Dependencies { get; set; } = new();
    }

    /// <summary>
    /// Represents a single package dependency with version constraints.
    /// </summary>
    /// <remarks>
    /// Package dependencies specify the packages required by another package,
    /// along with version ranges that define compatible versions.
    /// </remarks>
    public class PackageDependency
    {
        /// <summary>
        /// Gets or sets the identifier of the dependent package.
        /// </summary>
        /// <value>The package ID of the required dependency.</value>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        /// <summary>
        /// Gets or sets the version range specification for the dependency.
        /// </summary>
        /// <value>The version constraint using NuGet version range syntax.</value>
        /// <example>
        /// Common range patterns:
        /// <list type="bullet">
        /// <item><description>9.0.0 - Exactly version 9.0.0</description></item>
        /// <item><description>[9.0.0,) - Version 9.0.0 or higher</description></item>
        /// <item><description>[9.0.0,10.0.0) - Version 9.0.0 up to but not including 10.0.0</description></item>
        /// </list>
        /// </example>
        [JsonPropertyName("range")]
        public string Range { get; set; } = "";
    }
}