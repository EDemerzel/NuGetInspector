using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Services
{
    public interface IPackageAnalyzer
    {
        Dictionary<string, MergedPackage> MergePackages(
            List<ProjectInfo> outdatedProjects,
            List<ProjectInfo> deprecatedProjects,
            List<ProjectInfo> vulnerableProjects,
            string projectPath,
            string framework);
    }
}