using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using NuGetInspectorApp.Models;
using NuGetInspectorApp.Services;

namespace NuGetInspectorApp.Tests.Services
{
    /// <summary>
    /// Tests for the DotNetService class.
    /// </summary>
    [TestFixture]
    public class DotNetServiceTests
    {
        private Mock<ILogger<DotNetService>> _mockLogger = null!;
        // private DotNetService _service = null!; // _service is not used in these specific tests, they test JsonSerializer directly.

        [SetUp]
        public void SetUp()
        {
            _mockLogger = new Mock<ILogger<DotNetService>>();
            // _service = new DotNetService(_mockLogger.Object); // Only needed if testing _service methods
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
            result!.Projects.Should().HaveCount(1);
            result.Projects![0].Path.Should().Be("TestProject.csproj");
            result.Projects![0].Frameworks.Should().HaveCount(1);
            result.Projects![0].Frameworks![0].Framework.Should().Be("net9.0");
            result.Projects![0].Frameworks![0].TopLevelPackages.Should().HaveCount(1);
            result.Projects![0].Frameworks![0].TopLevelPackages![0].Id.Should().Be("TestPackage");
            result.Projects![0].Frameworks![0].TopLevelPackages![0].LatestVersion.Should().Be("2.0.0");
            result.Version.Should().Be(1);
            result.Parameters.Should().Be("--outdated");
            result.Sources.Should().Contain("https://api.nuget.org/v3/index.json");
        }

        [Test]
        public void JsonDeserialization_WithInvalidJson_ThrowsJsonException()
        {
            // Arrange
#pragma warning disable JSON001 // Invalid JSON pattern
            var invalidJson = "{ invalid json }";
#pragma warning restore JSON001 // Invalid JSON pattern
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Act & Assert
            FluentActions.Invoking(() => JsonSerializer.Deserialize<DotnetListReport>(invalidJson, options))
                .Should().Throw<JsonException>();
        }

        [Test]
        public void JsonDeserialization_WithNullJson_ThrowsArgumentNullException()
        {
            // Arrange
            string? nullJson = null;
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Act & Assert
            FluentActions.Invoking(() => JsonSerializer.Deserialize<DotnetListReport>(nullJson!, options))
                .Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void JsonDeserialization_WithEmptyJsonString_ThrowsJsonException() // "null" string is valid JSON for null object
        {
            // Arrange
            var emptyJson = ""; // Empty string is not valid JSON
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Act & Assert
            FluentActions.Invoking(() => JsonSerializer.Deserialize<DotnetListReport>(emptyJson, options))
                .Should().Throw<JsonException>();
        }

        [Test]
        public void JsonDeserialization_WithJsonNull_ReturnsNull()
        {
            // Arrange
            var jsonNull = "null";
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Act
            var result = JsonSerializer.Deserialize<DotnetListReport>(jsonNull, options);

            // Assert
            result.Should().BeNull();
        }


        [Test]
        public void JsonDeserialization_WithOutdatedPackageJson_DeserializesCorrectly()
        {
            // Arrange
            var outdatedJsonReport = CreateOutdatedJsonReport();

            // Act
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<DotnetListReport>(outdatedJsonReport, options);

            // Assert
            result.Should().NotBeNull();
            result!.Projects![0].Frameworks![0].TopLevelPackages![0].LatestVersion.Should().Be("2.0.0");
            result.Projects![0].Frameworks![0].TopLevelPackages![0].ResolvedVersion.Should().Be("1.0.0");
        }

        [Test]
        public void JsonDeserialization_WithVulnerablePackageJson_DeserializesCorrectly()
        {
            // Arrange
            var vulnerableJsonReport = CreateVulnerableJsonReport();

            // Act
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<DotnetListReport>(vulnerableJsonReport, options);

            // Assert
            result.Should().NotBeNull();
            var package = result!.Projects![0].Frameworks![0].TopLevelPackages![0];
            package.HasVulnerabilities.Should().BeTrue();
            package.Vulnerabilities.Should().HaveCount(1);
            package.Vulnerabilities![0].Severity.Should().Be("High");
            package.Vulnerabilities![0].AdvisoryUrl.Should().Be("https://github.com/advisories/GHSA-test");
        }

        [Test]
        public void JsonDeserialization_WithDeprecatedPackageJson_DeserializesCorrectly()
        {
            // Arrange
            var deprecatedJsonReport = CreateDeprecatedJsonReport();

            // Act
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<DotnetListReport>(deprecatedJsonReport, options);

            // Assert
            result.Should().NotBeNull();
            var package = result!.Projects![0].Frameworks![0].TopLevelPackages![0];
            package.IsDeprecated.Should().BeTrue();
            package.DeprecationReasons.Should().HaveCount(1).And.Contain("Legacy");
            package.Alternative.Should().NotBeNull();
            package.Alternative!.Id.Should().Be("NewPackage");
            package.Alternative!.VersionRange.Should().Be(">=2.0.0");
        }

        [Test]
        public void JsonDeserialization_WithTransitivePackages_DeserializesCorrectly()
        {
            // Arrange
            var transitiveJsonReport = CreateTransitivePackagesJsonReport();

            // Act
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<DotnetListReport>(transitiveJsonReport, options);

            // Assert
            result.Should().NotBeNull();
            result!.Projects![0].Frameworks![0].TransitivePackages.Should().HaveCount(1);
            result.Projects![0].Frameworks![0].TransitivePackages![0].Id.Should().Be("TransitivePackage");
            result.Projects![0].Frameworks![0].TransitivePackages![0].ResolvedVersion.Should().Be("1.5.0");
        }

        [Test]
        public void JsonDeserialization_WithComplexStructure_HandlesAllProperties()
        {
            // Arrange
            var complexJson = CreateComplexJsonReport();

            // Act
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<DotnetListReport>(complexJson, options);

            // Assert
            result.Should().NotBeNull();
            result!.Projects.Should().HaveCount(2);

            var firstProject = result.Projects![0];
            firstProject.Path.Should().Be("WebApp/WebApp.csproj");
            firstProject.Frameworks.Should().HaveCount(1);
            firstProject.Frameworks![0].Framework.Should().Be("net9.0");
            firstProject.Frameworks![0].TopLevelPackages.Should().HaveCount(2);
            firstProject.Frameworks![0].TransitivePackages.Should().HaveCount(1);


            var secondProject = result.Projects![1];
            secondProject.Path.Should().Be("ClassLibrary/ClassLibrary.csproj");
            secondProject.Frameworks.Should().HaveCount(1);
            secondProject.Frameworks![0].TopLevelPackages.Should().HaveCount(1);
        }

        [Test]
        public void JsonDeserialization_WithMissingOptionalProperties_HandlesGracefully()
        {
            // Arrange
            var minimalJson = CreateMinimalJsonReport(); // Assumes DotnetListReport has nullable Version, Parameters, Sources

            // Act
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<DotnetListReport>(minimalJson, options);

            // Assert
            result.Should().NotBeNull();
            result!.Projects.Should().HaveCount(1);
            var package = result.Projects![0].Frameworks![0].TopLevelPackages![0];
            package.LatestVersion.Should().BeNull();
            package.Vulnerabilities.Should().BeNull(); // Or empty list depending on model init
            package.DeprecationReasons.Should().BeNull(); // Or empty list
            package.Alternative.Should().BeNull();

            // Check top-level optional properties
            // result.Version.Should().Be(0); // Default for int if not present and not nullable
            // result.Parameters.Should().BeNull();
            // result.Sources.Should().BeNull(); // Or empty list
        }

        #region Helper Methods

        // These helpers now create JSON that should map to DotnetListReport, ProjectInfo, FrameworkInfo, PackageReference, etc.
        private static string CreateValidJsonReport()
        {
            return """
            {
              "version": 1,
              "parameters": "--outdated",
              "sources": [ "https://api.nuget.org/v3/index.json" ],
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
                          "latestVersion": "2.0.0"
                        }
                      ],
                      "transitivePackages": []
                    }
                  ]
                }
              ]
            }
            """;
        }

        private static string CreateOutdatedJsonReport() => CreateValidJsonReport(); // Same structure for this test

        private static string CreateVulnerableJsonReport()
        {
            return """
            {
              "version": 1, "parameters": "--vulnerable", "sources": [],
              "projects": [ { "path": "TestProject.csproj", "frameworks": [ { "framework": "net9.0",
                  "topLevelPackages": [ {
                      "id": "TestPackage", "requestedVersion": "1.0.0", "resolvedVersion": "1.0.0",
                      "hasVulnerabilities": true,
                      "vulnerabilities": [ { "severity": "High", "advisoryUrl": "https://github.com/advisories/GHSA-test" } ]
                  } ], "transitivePackages": [] } ] } ]
            }
            """;
        }

        private static string CreateDeprecatedJsonReport()
        {
            return """
            {
              "version": 1, "parameters": "--deprecated", "sources": [],
              "projects": [ { "path": "TestProject.csproj", "frameworks": [ { "framework": "net9.0",
                  "topLevelPackages": [ {
                      "id": "TestPackage", "requestedVersion": "1.0.0", "resolvedVersion": "1.0.0",
                      "isDeprecated": true, "deprecationReasons": ["Legacy"],
                      "alternative": { "id": "NewPackage", "versionRange": ">=2.0.0" }
                  } ], "transitivePackages": [] } ] } ]
            }
            """;
        }

        private static string CreateTransitivePackagesJsonReport()
        {
            return """
            {
              "version": 1, "parameters": "", "sources": [],
              "projects": [ { "path": "TestProject.csproj", "frameworks": [ { "framework": "net9.0",
                  "topLevelPackages": [ { "id": "TestPackage", "requestedVersion": "1.0.0", "resolvedVersion": "1.0.0" } ],
                  "transitivePackages": [ { "id": "TransitivePackage", "resolvedVersion": "1.5.0" } ]
              } ] } ]
            }
            """;
        }

        private static string CreateComplexJsonReport()
        {
            return """
            {
              "version": 1, "parameters": "--outdated --deprecated --vulnerable", "sources": ["s1"],
              "projects": [
                { "path": "WebApp/WebApp.csproj", "frameworks": [ { "framework": "net9.0",
                    "topLevelPackages": [
                      { "id": "Microsoft.AspNetCore.App", "requestedVersion": "9.0.0", "resolvedVersion": "9.0.0", "latestVersion": "9.0.1" },
                      { "id": "Newtonsoft.Json", "requestedVersion": "12.0.3", "resolvedVersion": "12.0.3", "latestVersion": "13.0.3",
                        "isDeprecated": true, "deprecationReasons": ["Legacy package"],
                        "alternative": { "id": "System.Text.Json", "versionRange": ">=6.0.0" },
                        "hasVulnerabilities": true,
                        "vulnerabilities": [ { "severity": "High", "advisoryUrl": "https://github.com/advisories/GHSA-5crp-9r3c-p9vr" } ]
                      }
                    ],
                    "transitivePackages": [ { "id": "System.Text.Json", "resolvedVersion": "9.0.0" } ]
                } ] },
                { "path": "ClassLibrary/ClassLibrary.csproj", "frameworks": [ { "framework": "net9.0",
                    "topLevelPackages": [ { "id": "FluentAssertions", "requestedVersion": "7.0.0", "resolvedVersion": "7.0.0" } ],
                    "transitivePackages": []
                } ] }
              ]
            }
            """;
        }

        private static string CreateMinimalJsonReport()
        {
            // This JSON only includes projects, assuming other top-level fields in DotnetListReport are nullable or have defaults.
            return """
            {
              "projects": [ { "path": "MinimalProject.csproj", "frameworks": [ { "framework": "net9.0",
                  "topLevelPackages": [ { "id": "MinimalPackage", "requestedVersion": "1.0.0", "resolvedVersion": "1.0.0" } ],
                  "transitivePackages": []
              } ] } ]
            }
            """;
        }
        #endregion
    }
}