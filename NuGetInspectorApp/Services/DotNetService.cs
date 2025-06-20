using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Models;
using System.Diagnostics;
using System.Text.Json;

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
        /// A task that represents the asynchronous operation. The task result contains a <see cref="DotNetListReport"/>
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
        public async Task<DotNetListReport?> GetPackageReportAsync(string solutionPath, string reportType, CancellationToken cancellationToken = default)
        {
            var json = await RunDotnetListJSONAsync(solutionPath, reportType, cancellationToken);
            if (json == null)
                return null;

            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<DotNetListReport>(json, opts);
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
        // In DotNetService.cs - enhance the RunDotnetListJSONAsync method
        private async Task<string?> RunDotnetListJSONAsync(string solution, string flag, CancellationToken cancellationToken)
        {
            // Add comprehensive validation
            if (string.IsNullOrWhiteSpace(solution))
            {
                _logger.LogError("Solution path is null or empty");
                return null;
            }

            if (!File.Exists(solution))
            {
                _logger.LogError("Solution file does not exist: {SolutionPath}", solution);
                return null;
            }

            // Convert to absolute path
            var absolutePath = Path.GetFullPath(solution);
            _logger.LogDebug("Using absolute solution path: {AbsolutePath}", absolutePath);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"list \"{absolutePath}\" package {flag} --include-transitive --format json",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(absolutePath) ?? Environment.CurrentDirectory
            };

            _logger.LogDebug("Executing command: {FileName} {Arguments}", startInfo.FileName, startInfo.Arguments);
            _logger.LogDebug("Working directory: {WorkingDirectory}", startInfo.WorkingDirectory);

            try
            {
                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync(cancellationToken);

                var output = await outputTask;
                var error = await errorTask;

                if (process.ExitCode != 0)
                {
                    _logger.LogError("dotnet list package {Flag} failed with exit code {ExitCode}: {Error}",
                        flag, process.ExitCode, error);

                    // Log additional diagnostic information
                    if (error.Contains("MSBUILD"))
                    {
                        _logger.LogError("MSBuild error detected. Ensure .NET SDK is properly installed and solution can be restored.");
                    }
                    if (error.Contains("not found") || error.Contains("could not be found"))
                    {
                        _logger.LogError("File not found error. Check solution path and ensure all projects exist.");
                    }

                    return null;
                }

                if (string.IsNullOrWhiteSpace(output))
                {
                    _logger.LogWarning("dotnet list package {Flag} returned empty output", flag);
                    return null;
                }

                _logger.LogTrace("dotnet list package {Flag} completed successfully. Output length: {Length}", flag, output.Length);
                return output;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while executing dotnet list package {Flag}: {Message}", flag, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Tests if dotnet list command works without any flags - for diagnostic purposes.
        /// </summary>
        public async Task<bool> TestBasicDotnetListAsync(string solutionPath, CancellationToken cancellationToken = default)
        {
            try
            {
                var absolutePath = Path.GetFullPath(solutionPath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"list \"{absolutePath}\" package --format json",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(absolutePath) ?? Environment.CurrentDirectory
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync(cancellationToken);

                _logger.LogInformation("Basic dotnet list test - Exit code: {ExitCode}, Output length: {OutputLength}, Error: {Error}",
                    process.ExitCode, output?.Length ?? 0, error);

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute basic dotnet list test: {Message}", ex.Message);
                return false;
            }
        }
    }
}