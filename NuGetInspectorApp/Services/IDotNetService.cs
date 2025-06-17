using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Services;

/// <summary>
/// Defines the contract for executing dotnet CLI commands to retrieve package information.
/// </summary>
/// <remarks>
/// This service interface abstracts the execution of 'dotnet list package' commands with various flags
/// to gather comprehensive package information including outdated, deprecated, and vulnerable packages.
/// Implementations should handle process execution, output capture, and JSON deserialization.
/// </remarks>
public interface IDotNetService
{
    /// <summary>
    /// Executes a dotnet list package command and returns the parsed report.
    /// </summary>
    /// <param name="solutionPath">The path to the solution file to analyze.</param>
    /// <param name="reportType">The type of report to generate (e.g., "--outdated", "--deprecated", "--vulnerable").</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a <see cref="DotNetListReport"/>
    /// object with the parsed package information, or <c>null</c> if the operation fails.
    /// </returns>
    /// <remarks>
    /// This method should execute the dotnet CLI command with the specified report type and deserialize
    /// the JSON output into a strongly-typed object. Common report types include:
    /// <list type="bullet">
    /// <item><description>--outdated: Lists packages with newer versions available</description></item>
    /// <item><description>--deprecated: Lists packages marked as deprecated</description></item>
    /// <item><description>--vulnerable: Lists packages with known security vulnerabilities</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="solutionPath"/> or <paramref name="reportType"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the dotnet CLI is not available or returns an unexpected format.</exception>
    Task<DotNetListReport?> GetPackageReportAsync(string solutionPath, string reportType, CancellationToken cancellationToken = default);
}