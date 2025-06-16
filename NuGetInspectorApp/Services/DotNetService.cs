using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Services
{
    public class DotNetService : IDotNetService
    {
        private readonly ILogger<DotNetService> _logger;

        public DotNetService(ILogger<DotNetService> logger)
        {
            _logger = logger;
        }

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