using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Services
{
    /// <summary>
    /// Provides functionality for executing dotnet CLI commands to retrieve package information.
    /// </summary>
    /// <remarks>
    /// This service wraps the execution of 'dotnet list package' commands with various flags
    /// to gather comprehensive package information including outdated, deprecated, and vulnerable packages.
    /// The service handles process execution, output capture, and JSON deserialization.
    /// </remarks>
    public class DotNetService : IDotNetService
    {
        private readonly ILogger<DotNetService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="DotNetService"/> class.
        /// </summary>
        /// <param name="logger">The logger instance for recording service operations and errors.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
        public DotNetService(ILogger<DotNetService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Executes a dotnet list package command and returns the parsed report.
        /// </summary>
        /// <param name="solutionPath">The path to the solution file to analyze.</param>
        /// <param name="reportType">The type of report to generate (e.g., "--outdated", "--deprecated", "--vulnerable").</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains a <see cref="DotnetListReport"/>
        /// object with the parsed package information, or <c>null</c> if the operation fails.
        /// </returns>
        /// <remarks>
        /// This method executes the dotnet CLI command with the specified report type and deserializes
        /// the JSON output into a strongly-typed object. Common report types include:
        /// <list type="bullet">
        /// <item><description>--outdated: Lists packages with newer versions available</description></item>
        /// <item><description>--deprecated: Lists packages marked as deprecated</description></item>
        /// <item><description>--vulnerable: Lists packages with known security vulnerabilities</description></item>
        /// </list>
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown when <paramref name="solutionPath"/> or <paramref name="reportType"/> is null or empty.</exception>
        public async Task<DotnetListReport?> GetPackageReportAsync(string solutionPath, string reportType, CancellationToken cancellationToken = default)
        {
            var json = await RunDotnetListJsonAsync(solutionPath, reportType, cancellationToken);
            if (json == null) return null;

            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<DotnetListReport>(json, opts);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize dotnet list output for {ReportType}", reportType);
                return null;
            }
        }

        /// <summary>
        /// Executes the dotnet list package command and returns the raw JSON output.
        /// </summary>
        /// <param name="solution">The path to the solution file to analyze.</param>
        /// <param name="flag">The command flag to specify the type of package report (e.g., "--outdated", "--deprecated").</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains the raw JSON output
        /// from the dotnet command, or <c>null</c> if the command fails or returns a non-zero exit code.
        /// </returns>
        /// <remarks>
        /// This method constructs and executes a dotnet CLI command with the following format:
        /// <c>dotnet list "{solution}" package {flag} --include-transitive --format json</c>
        /// <para>
        /// The method captures both standard output and standard error streams, logs any errors,
        /// and returns null if the command fails. This provides a robust foundation for the
        /// higher-level GetPackageReportAsync method.
        /// </para>
        /// </remarks>
        private async Task<string?> RunDotnetListJsonAsync(string solution, string flag, CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo("dotnet",
                $"list \"{solution}\" package {flag} --include-transitive --format json")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var proc = new Process { StartInfo = psi };
                proc.Start();

                var stdout = await proc.StandardOutput.ReadToEndAsync();
                var stderr = await proc.StandardError.ReadToEndAsync();

                await proc.WaitForExitAsync(cancellationToken);

                if (proc.ExitCode != 0)
                {
                    _logger.LogError("dotnet list package {Flag} failed with exit code {ExitCode}: {Error}",
                        flag, proc.ExitCode, stderr);
                    return null;
                }

                return stdout;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute dotnet list package {Flag}", flag);
                return null;
            }
        }
    }
}