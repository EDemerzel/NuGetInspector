using System.Text.Json.Serialization;
using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Configuration;

/// <summary>
/// JSON serialization context for AOT and trimming compatibility.
/// </summary>
[JsonSerializable(typeof(DotNetListReport))]
[JsonSerializable(typeof(ProjectInfo))]
[JsonSerializable(typeof(FrameworkInfo))]
[JsonSerializable(typeof(PackageReference))]
[JsonSerializable(typeof(VulnerabilityInfo))]
[JsonSerializable(typeof(PackageAlternative))]
[JsonSerializable(typeof(NuGetInspectorConfig))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ApiSettings))]
[JsonSerializable(typeof(OutputSettings))]
[JsonSerializable(typeof(FilterSettings))]
[JsonSerializable(typeof(ReportSettings))]
[JsonSerializable(typeof(MergedPackage))]
[JsonSerializable(typeof(PackageMetaData))]
[JsonSerializable(typeof(DependencyGroup))]
[JsonSerializable(typeof(CommandLineOptions))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<PackageReference>))]
[JsonSerializable(typeof(List<ProjectInfo>))]
[JsonSerializable(typeof(List<FrameworkInfo>))]
[JsonSerializable(typeof(List<VulnerabilityInfo>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Default)]
public partial class NuGetInspectorJsonContext : JsonSerializerContext
{
}