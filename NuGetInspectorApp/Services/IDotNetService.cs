using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Services;

public interface IDotNetService
{
    Task<DotnetListReport?> GetPackageReportAsync(string solutionPath, string reportType, CancellationToken cancellationToken = default);
}