using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NuGetInspectorApp.Services;

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
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        if (string.IsNullOrWhiteSpace(json))
        {
            _logger.LogWarning($"dotnet list package {reportType} returned empty output");
            return null;
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<DotNetListReport>(json, options);
        }
        catch (JsonException)
        {
            _logger.LogError($"Failed to deserialize dotnet list output for {reportType}");
            return null;
        }
    }

    /// <summary>
    /// Executes the dotnet list package command and returns the raw JSON output.
    /// </summary>
    /// <param name="solutionPath">The path to the solution file to analyze.</param>
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
    private async Task<string?> RunDotnetListJSONAsync(string solutionPath, string flag, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            _logger.LogError("Solution path is null or empty");
            return null;
        }

        if (!Path.IsPathRooted(solutionPath))
        {
            solutionPath = Path.GetFullPath(solutionPath);
            _logger.LogDebug($"Using absolute solution path: {solutionPath}");
        }

        if (!File.Exists(solutionPath))
        {
            _logger.LogError($"Solution file does not exist: {solutionPath}");
            return null;
        }

        // Always include --include-transitive flag
        var arguments = string.IsNullOrWhiteSpace(flag)
            ? $"list \"{solutionPath}\" package --include-transitive --format json"
            : $"list \"{solutionPath}\" package {flag} --include-transitive --format json";

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogDebug("Executing dotnet command: {Command} {Arguments}", psi.FileName, psi.Arguments);

        using var process = new Process { StartInfo = psi };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("dotnet list package command was cancelled.");
            return null;
        }

        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();

        if (process.ExitCode != 0)
        {
            _logger.LogError("dotnet list package command failed with exit code {ExitCode}. Error: {Error}",
                process.ExitCode, error);

            if (!string.IsNullOrWhiteSpace(error))
            {
                if (error.Contains("MSBUILD", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("MSBuild error detected. Ensure .NET SDK is properly installed and solution can be restored.");
                    return null;
                }
                if (error.Contains("could not be found", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("File not found error. Check solution path and ensure all projects exist.");
                    return null;
                }
            }
            return null;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            _logger.LogWarning($"dotnet list package {flag} returned empty output");
            return null;
        }

        if (string.IsNullOrWhiteSpace(flag)) // This is the baseline report
        {
            var transitiveCount = output.Split("transitivePackages").Length - 1;
            _logger.LogDebug("Baseline report contains {TransitiveCount} 'transitivePackages' sections", transitiveCount);
        }

        return output;
    }

    /// <summary>
    /// Tests if dotnet list command works without any flags - for diagnostic purposes.
    /// </summary>
    public async Task<bool> TestBasicDotnetListAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"list \"{solutionPath}\" package --include-transitive",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("dotnet list package command was cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute basic dotnet list test: {Message}", ex.Message);
                return false;
            }

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            _logger.LogInformation($"Basic dotnet list test - Exit code: {process.ExitCode}");

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute basic dotnet list test: {Message}", ex.Message);
            return false;
        }
    }
}