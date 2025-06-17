using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using NuGetInspectorApp.Models;
using NuGetInspectorApp.Services;

namespace NuGetInspectorApp.Tests.Services;

/// <summary>
/// Tests for the DotNetService class.
/// </summary>
[TestFixture]
public class DotNetServiceTests
{
    private Mock<ILogger<DotNetService>> _mockLogger = null!;
    private DotNetService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<DotNetService>>();
        _service = new DotNetService(_mockLogger.Object);
    }

    [Test]
    public void JsonDeserialization_WithValidJson_ReturnsDeserializedReport()
    {
        // Arrange
        var validJsonReport = CreateValidJsonReport();

        // Act
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<DotnetListReport>(validJsonReport, options);

        // Assert
        result.Should().NotBeNull();
        result.Projects.Should().HaveCount(1);
        result.Projects![0].Path.Should().Be("TestProject.csproj");
        result.Projects![0].Frameworks.Should().HaveCount(1);
        result.Projects![0].Frameworks[0].Framework.Should().Be("net9.0");
        result.Projects![0].Frameworks[0].TopLevelPackages.Should().HaveCount(1);
        result.Projects![0].Frameworks[0].TopLevelPackages[0].Id.Should().Be("TestPackage");
        result.Projects![0].Frameworks[0].TopLevelPackages[0].LatestVersion.Should().Be("2.0.0");
    }

    [Test]
    public void JsonDeserialization_WithInvalidJson_ThrowsJsonException()
    {
        // Arrange
        var invalidJson = "{ invalid json }";
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Act & Assert
        FluentActions.Invoking(() => JsonSerializer.Deserialize<DotnetListReport>(invalidJson, options))
            .Should().Throw<JsonException>();
    }

    [Test]
    public void JsonDeserialization_WithEmptyJson_ReturnsEmptyReport()
    {
        // Arrange
        var emptyJson = "{}";
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Act
        var result = JsonSerializer.Deserialize<DotnetListReport>(emptyJson, options);

        // Assert
        result.Should().NotBeNull();
        result.Projects.Should().BeNull();
    }

    [Test]
    public void JsonDeserialization_WithComplexPackageData_ParsesAllProperties()
    {
        // Arrange
        var complexJsonReport = CreateComplexJsonReport();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Act
        var result = JsonSerializer.Deserialize<DotnetListReport>(complexJsonReport, options);

        // Assert
        result.Should().NotBeNull();
        result.Projects.Should().HaveCount(1);

        var project = result.Projects![0];
        project.Frameworks.Should().HaveCount(1);

        var framework = project.Frameworks[0];
        framework.TopLevelPackages.Should().HaveCount(2);
        framework.TransitivePackages.Should().HaveCount(1);

        // Test vulnerable package
        var vulnerablePackage = framework.TopLevelPackages.First(p => p.Id == "VulnerablePackage");
        vulnerablePackage.HasVulnerabilities.Should().BeTrue();
        vulnerablePackage.Vulnerabilities.Should().HaveCount(1);
        vulnerablePackage.Vulnerabilities![0].Severity.Should().Be("High");

        // Test deprecated package
        var deprecatedPackage = framework.TopLevelPackages.First(p => p.Id == "DeprecatedPackage");
        deprecatedPackage.IsDeprecated.Should().BeTrue();
        deprecatedPackage.DeprecationReasons.Should().HaveCount(2);
        deprecatedPackage.Alternative.Should().NotBeNull();
        deprecatedPackage.Alternative!.Id.Should().Be("NewPackage");

        // Test transitive package
        var transitivePackage = framework.TransitivePackages![0];
        transitivePackage.Id.Should().Be("TransitivePackage");
        transitivePackage.ResolvedVersion.Should().Be("1.5.0");
    }

    [Test]
    public void JsonDeserialization_WithMultipleFrameworks_ParsesAllFrameworks()
    {
        // Arrange
        var multiFrameworkJson = CreateMultiFrameworkJsonReport();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Act
        var result = JsonSerializer.Deserialize<DotnetListReport>(multiFrameworkJson, options);

        // Assert
        result.Should().NotBeNull();
        result.Projects![0].Frameworks.Should().HaveCount(2);

        var net9Framework = result.Projects![0].Frameworks.First(f => f.Framework == "net9.0");
        var net48Framework = result.Projects![0].Frameworks.First(f => f.Framework == "net48");

        net9Framework.TopLevelPackages[0].Id.Should().Be("ModernPackage");
        net48Framework.TopLevelPackages[0].Id.Should().Be("LegacyPackage");
    }

    [Test]
    public async Task GetPackageReportAsync_WithCancellationToken_RespectsCancellation()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        var solutionPath = "TestSolution.sln";
        var reportType = "--outdated";

        cancellationTokenSource.Cancel();

        // Act & Assert
        await FluentActions.Invoking(() =>
            _service.GetPackageReportAsync(solutionPath, reportType, cancellationTokenSource.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void GetPackageReportAsync_WithInvalidSolutionPath_ThrowsArgumentException(string? solutionPath)
    {
        // Act & Assert
        FluentActions.Invoking(() => _service.GetPackageReportAsync(solutionPath!, "--outdated"))
            .Should().ThrowAsync<ArgumentException>();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void GetPackageReportAsync_WithInvalidReportType_ThrowsArgumentException(string? reportType)
    {
        // Act & Assert
        FluentActions.Invoking(() => _service.GetPackageReportAsync("test.sln", reportType!))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public void JsonDeserialization_HandlesNullValues()
    {
        // Arrange
        var jsonWithNulls = """
        {
            "projects": [
                {
                    "path": "TestProject.csproj",
                    "frameworks": [
                        {
                            "framework": "net9.0",
                            "topLevelPackages": [
                                {
                                    "id": "TestPackage",
                                    "requestedVersion": "1.0.0",
                                    "resolvedVersion": "1.0.0",
                                    "latestVersion": null,
                                    "deprecationReasons": null,
                                    "vulnerabilities": null
                                }
                            ],
                            "transitivePackages": null
                        }
                    ]
                }
            ]
        }
        """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Act
        var result = JsonSerializer.Deserialize<DotnetListReport>(jsonWithNulls, options);

        // Assert
        result.Should().NotBeNull();
        result.Projects.Should().HaveCount(1);

        var package = result.Projects![0].Frameworks[0].TopLevelPackages[0];
        package.Id.Should().Be("TestPackage");
        package.LatestVersion.Should().BeNull();
        package.DeprecationReasons.Should().BeNull();
        package.Vulnerabilities.Should().BeNull();
        result.Projects![0].Frameworks[0].TransitivePackages.Should().BeNull();
    }

    [Test]
    public void JsonDeserialization_WithMalformedPackageStructure_HandlesGracefully()
    {
        // Arrange
        var malformedJson = """
        {
            "projects": [
                {
                    "path": "TestProject.csproj",
                    "frameworks": [
                        {
                            "framework": "net9.0",
                            "topLevelPackages": [
                                {
                                    "id": "IncompletePackage"
                                }
                            ]
                        }
                    ]
                }
            ]
        }
        """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Act
        var result = JsonSerializer.Deserialize<DotnetListReport>(malformedJson, options);

        // Assert
        result.Should().NotBeNull();
        result.Projects.Should().HaveCount(1);
        var package = result.Projects![0].Frameworks[0].TopLevelPackages[0];
        package.Id.Should().Be("IncompletePackage");
        package.RequestedVersion.Should().BeNull();
        package.ResolvedVersion.Should().BeNull();
    }

    #region Helper Methods

    private static string CreateValidJsonReport()
    {
        var report = new
        {
            projects = new[]
            {
                new
                {
                    path = "TestProject.csproj",
                    frameworks = new[]
                    {
                        new
                        {
                            framework = "net9.0",
                            topLevelPackages = new[]
                            {
                                new
                                {
                                    id = "TestPackage",
                                    requestedVersion = "1.0.0",
                                    resolvedVersion = "1.0.0",
                                    latestVersion = "2.0.0"
                                }
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(report, JsonOptions);
    }

    private static string CreateComplexJsonReport()
    {
        var report = new
        {
            projects = new[]
            {
                new
                {
                    path = "ComplexProject.csproj",
                    frameworks = new[]
                    {
                        new
                        {
                            framework = "net9.0",
                            topLevelPackages = new object[]
                            {
                                new
                                {
                                    id = "VulnerablePackage",
                                    requestedVersion = "1.0.0",
                                    resolvedVersion = "1.0.0",
                                    latestVersion = (string?)null,
                                    hasVulnerabilities = true,
                                    vulnerabilities = new[]
                                    {
                                        new
                                        {
                                            severity = "High",
                                            advisoryUrl = "https://github.com/advisories/GHSA-test"
                                        }
                                    },
                                    isDeprecated = false,
                                    deprecationReasons = (string[]?)null,
                                    alternative = (object?)null
                                },
                                new
                                {
                                    id = "DeprecatedPackage",
                                    requestedVersion = "2.0.0",
                                    resolvedVersion = "2.0.0",
                                    latestVersion = (string?)null,
                                    hasVulnerabilities = false,
                                    vulnerabilities = (object[]?)null,
                                    isDeprecated = true,
                                    deprecationReasons = new[] { "Legacy", "CriticalBugs" },
                                    alternative = new
                                    {
                                        id = "NewPackage",
                                        versionRange = ">=3.0.0"
                                    }
                                }
                            },
                            transitivePackages = new[]
                            {
                                new
                                {
                                    id = "TransitivePackage",
                                    requestedVersion = "1.5.0",
                                    resolvedVersion = "1.5.0"
                                }
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(report, JsonOptions);
    }

    private static string CreateMultiFrameworkJsonReport()
    {
        var report = new
        {
            projects = new[]
            {
                new
                {
                    path = "MultiTargetProject.csproj",
                    frameworks = new[]
                    {
                        new
                        {
                            framework = "net9.0",
                            topLevelPackages = new[]
                            {
                                new
                                {
                                    id = "ModernPackage",
                                    requestedVersion = "3.0.0",
                                    resolvedVersion = "3.0.0"
                                }
                            }
                        },
                        new
                        {
                            framework = "net48",
                            topLevelPackages = new[]
                            {
                                new
                                {
                                    id = "LegacyPackage",
                                    requestedVersion = "2.0.0",
                                    resolvedVersion = "2.0.0"
                                }
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(report, JsonOptions);
    }

    private static JsonSerializerOptions JsonOptions => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    #endregion
}