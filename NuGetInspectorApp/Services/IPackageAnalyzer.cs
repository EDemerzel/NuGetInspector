using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Services
{
    /// <summary>
    /// Defines the contract for merging package information from multiple dotnet list reports.
    /// </summary>
    /// <remarks>
    /// This service interface abstracts the process of combining data from outdated, deprecated,
    /// and vulnerable package reports to create a unified view of package status across different
    /// analysis types. Implementations should handle the complexity of merging overlapping package
    /// information while preserving all relevant metadata from each report type.
    /// </remarks>
    public interface IPackageAnalyzer
    {
        /// <summary>
        /// Merges package information from multiple dotnet list package reports for a specific project and framework.
        /// </summary>
        /// <param name="outdatedProjects">The collection of projects from the outdated packages report.</param>
        /// <param name="deprecatedProjects">The collection of projects from the deprecated packages report.</param>
        /// <param name="vulnerableProjects">The collection of projects from the vulnerable packages report.</param>
        /// <param name="projectPath">The file system path to the specific project to analyze.</param>
        /// <param name="framework">The target framework moniker to analyze within the project.</param>
        /// <returns>
        /// A dictionary of merged package information keyed by package ID, where each value contains
        /// consolidated information from all applicable report types.
        /// </returns>
        /// <remarks>
        /// This method performs the following operations:
        /// <list type="bullet">
        /// <item><description>Locates the specified project and framework in each report type</description></item>
        /// <item><description>Merges package version information from the outdated report</description></item>
        /// <item><description>Incorporates deprecation status and alternatives from the deprecated report</description></item>
        /// <item><description>Adds vulnerability information from the vulnerable report</description></item>
        /// <item><description>Returns a unified view of all packages for the specified project/framework combination</description></item>
        /// </list>
        /// If a project or framework is not found in any of the reports, appropriate warnings should be logged
        /// and processing should continue with available data.
        /// </remarks>
        /// <exception cref="System.ArgumentNullException">
        /// Thrown when any of the project collections (<paramref name="outdatedProjects"/>,
        /// <paramref name="deprecatedProjects"/>, or <paramref name="vulnerableProjects"/>) is null.
        /// </exception>
        /// <exception cref="System.ArgumentException">
        /// Thrown when <paramref name="projectPath"/> or <paramref name="framework"/> is null or empty.
        /// </exception>
        /// <example>
        /// <code>
        /// var analyzer = new PackageAnalyzer(logger);
        /// var mergedPackages = analyzer.MergePackages(
        ///     outdatedReport.Projects,
        ///     deprecatedReport.Projects,
        ///     vulnerableReport.Projects,
        ///     @"C:\MyProject\MyProject.csproj",
        ///     "net9.0");
        ///
        /// foreach (var package in mergedPackages.Values)
        /// {
        ///     Console.WriteLine($"{package.Id}: Outdated={package.IsOutdated}, Deprecated={package.IsDeprecated}");
        /// }
        /// </code>
        /// </example>
        Dictionary<string, MergedPackage> MergePackages(
            List<ProjectInfo> outdatedProjects,
            List<ProjectInfo> deprecatedProjects,
            List<ProjectInfo> vulnerableProjects,
            string projectPath,
            string framework);
    }
}