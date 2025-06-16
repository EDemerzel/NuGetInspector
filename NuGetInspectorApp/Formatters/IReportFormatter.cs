using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Formatters
{
    /// <summary>
    /// Defines the contract for formatting package analysis reports into different output formats.
    /// </summary>
    /// <remarks>
    /// Implementations of this interface are responsible for taking the raw package analysis data
    /// and converting it into a human-readable or machine-readable format. The formatter receives
    /// project information, merged package data, and detailed package metadata to create comprehensive reports.
    /// </remarks>
    public interface IReportFormatter
    {
        /// <summary>
        /// Asynchronously formats a package analysis report based on the provided data.
        /// </summary>
        /// <param name="projects">The collection of projects analyzed, containing framework and package information.</param>
        /// <param name="mergedPackages">
        /// A dictionary of merged package information keyed by "{ProjectPath}|{Framework}",
        /// with values being dictionaries of packages keyed by package ID.
        /// </param>
        /// <param name="packageMetadata">
        /// A dictionary of detailed package metadata keyed by "{PackageId}|{Version}",
        /// containing additional information fetched from the NuGet API.
        /// </param>
        /// <param name="cancellationToken">A cancellation token to cancel the formatting operation.</param>
        /// <returns>
        /// A task that represents the asynchronous formatting operation.
        /// The task result contains the formatted report as a string.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="projects"/>, <paramref name="mergedPackages"/>, 
        /// or <paramref name="packageMetadata"/> is null.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Thrown when the operation is cancelled via the <paramref name="cancellationToken"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// var formatter = new ConsoleReportFormatter();
        /// var report = await formatter.FormatReportAsync(projects, mergedPackages, metadata);
        /// Console.WriteLine(report);
        /// </code>
        /// </example>
        Task<string> FormatReportAsync(
            List<ProjectInfo> projects,
            Dictionary<string, Dictionary<string, MergedPackage>> mergedPackages,
            Dictionary<string, PackageMetadata> packageMetadata,
            CancellationToken cancellationToken = default);
    }
}