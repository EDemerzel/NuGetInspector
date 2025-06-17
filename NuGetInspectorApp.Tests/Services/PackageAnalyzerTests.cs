using Microsoft.Extensions.Logging;
using Moq;
using NuGetInspectorApp.Models;
using NuGetInspectorApp.Services;

namespace NuGetInspectorApp.Tests.Services;

/// <summary>
/// Tests for the PackageAnalyzer class.
/// </summary>
[TestFixture]
public class PackageAnalyzerTests
{
    private Mock<ILogger<PackageAnalyzer>> _mockLogger = null!;
    private PackageAnalyzer _analyzer = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<PackageAnalyzer>>();
        _analyzer = new PackageAnalyzer(_mockLogger.Object);
    }

    [Test]
    public void MergePackages_WithValidInputs_ReturnsCorrectMerge()
    {
        // Arrange
        var outdatedProjects = CreateMockProjects("outdated");
        var deprecatedProjects = CreateMockProjects("deprecated");
        var vulnerableProjects = CreateMockProjects("vulnerable");

        // Act
        var result = _analyzer.MergePackages(
            outdatedProjects,
            deprecatedProjects,
            vulnerableProjects,
            "TestProject.csproj",
            "net9.0");

        // Assert
        result.Should().NotBeEmpty();
        result.Should().ContainKey("TestPackage");

        var mergedPackage = result["TestPackage"];
        mergedPackage.Id.Should().Be("TestPackage");
        mergedPackage.RequestedVersion.Should().Be("1.0.0");
        mergedPackage.ResolvedVersion.Should().Be("1.0.0");
    }

    [Test]
    public void MergePackages_WithOutdatedPackage_SetsLatestVersionAndOutdatedFlag()
    {
        // Arrange
        var outdatedProjects = CreateProjectsWithOutdatedPackage();
        var deprecatedProjects = CreateMockProjects("deprecated");
        var vulnerableProjects = CreateMockProjects("vulnerable");

        // Act
        var result = _analyzer.MergePackages(
            outdatedProjects,
            deprecatedProjects,
            vulnerableProjects,
            "TestProject.csproj",
            "net9.0");

        // Assert
        result["TestPackage"].LatestVersion.Should().Be("2.0.0");
        result["TestPackage"].IsOutdated.Should().BeTrue();
    }

    [Test]
    public void MergePackages_WithDeprecatedPackage_SetsDeprecationInfo()
    {
        // Arrange
        var outdatedProjects = CreateMockProjects("outdated");
        var deprecatedProjects = CreateProjectsWithDeprecatedPackage();
        var vulnerableProjects = CreateMockProjects("vulnerable");

        // Act
        var result = _analyzer.MergePackages(
            outdatedProjects,
            deprecatedProjects,
            vulnerableProjects,
            "TestProject.csproj",
            "net9.0");

        // Assert
        result["TestPackage"].IsDeprecated.Should().BeTrue();
        result["TestPackage"].DeprecationReasons.Should().Contain("Legacy");
        result["TestPackage"].Alternative.Should().NotBeNull();
        result["TestPackage"].Alternative!.Id.Should().Be("NewPackage");
    }

    [Test]
    public void MergePackages_WithVulnerablePackage_SetsVulnerabilityInfo()
    {
        // Arrange
        var outdatedProjects = CreateMockProjects("outdated");
        var deprecatedProjects = CreateMockProjects("deprecated");
        var vulnerableProjects = CreateProjectsWithVulnerablePackage();

        // Act
        var result = _analyzer.MergePackages(
            outdatedProjects,
            deprecatedProjects,
            vulnerableProjects,
            "TestProject.csproj",
            "net9.0");

        // Assert
        result["TestPackage"].Vulnerabilities.Should().NotBeEmpty();
        result["TestPackage"].Vulnerabilities[0].Severity.Should().Be("High");
        result["TestPackage"].Vulnerabilities[0].AdvisoryUrl.Should().Be("https://github.com/advisories/GHSA-test");
    }

    [Test]
    public void MergePackages_WithMissingProject_LogsWarningAndReturnsEmpty()
    {
        // Arrange
        var emptyProjects = new List<ProjectInfo>();

        // Act
        var result = _analyzer.MergePackages(
            emptyProjects,
            emptyProjects,
            emptyProjects,
            "NonExistentProject.csproj",
            "net9.0");

        // Assert
        result.Should().BeEmpty();
        VerifyWarningLogged("Project not found in outdated report");
    }

    [Test]
    public void MergePackages_WithNullInputs_ThrowsArgumentNullException()
    {
        // Act & Assert
        FluentActions.Invoking(() =>
            _analyzer.MergePackages(null!, new List<ProjectInfo>(), new List<ProjectInfo>(), "test.csproj", "net9.0"))
            .Should().Throw<ArgumentNullException>();

        FluentActions.Invoking(() =>
            _analyzer.MergePackages(new List<ProjectInfo>(), null!, new List<ProjectInfo>(), "test.csproj", "net9.0"))
            .Should().Throw<ArgumentNullException>();

        FluentActions.Invoking(() =>
            _analyzer.MergePackages(new List<ProjectInfo>(), new List<ProjectInfo>(), null!, "test.csproj", "net9.0"))
            .Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void MergePackages_WithEmptyProjectPath_ThrowsArgumentException()
    {
        // Arrange
        var projects = new List<ProjectInfo>();

        // Act & Assert
        FluentActions.Invoking(() =>
            _analyzer.MergePackages(projects, projects, projects, "", "net9.0"))
            .Should().Throw<ArgumentException>();

        FluentActions.Invoking(() =>
            _analyzer.MergePackages(projects, projects, projects, "test.csproj", ""))
            .Should().Throw<ArgumentException>();
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t")]
    public void MergePackages_WithWhitespaceProjectPath_ThrowsArgumentException(string projectPath)
    {
        // Arrange
        var projects = new List<ProjectInfo>();

        // Act & Assert
        FluentActions.Invoking(() =>
            _analyzer.MergePackages(projects, projects, projects, projectPath, "net9.0"))
            .Should().Throw<ArgumentException>();
    }

    [Test]
    public void MergePackages_WithMultiplePackages_MergesAllCorrectly()
    {
        // Arrange
        var outdatedProjects = CreateProjectsWithMultiplePackages();
        var deprecatedProjects = CreateMockProjects("deprecated");
        var vulnerableProjects = CreateMockProjects("vulnerable");

        // Act
        var result = _analyzer.MergePackages(
            outdatedProjects,
            deprecatedProjects,
            vulnerableProjects,
            "TestProject.csproj",
            "net9.0");

        // Assert
        result.Should().HaveCount(2);
        result.Should().ContainKeys("TestPackage1", "TestPackage2");
    }

    #region Helper Methods

    private static List<ProjectInfo> CreateMockProjects(string type)
    {
        return new List<ProjectInfo>
        {
            new ProjectInfo
            {
                Path = "TestProject.csproj",
                Frameworks = new List<FrameworkInfo>
                {
                    new FrameworkInfo
                    {
                        Framework = "net9.0",
                        TopLevelPackages = new List<PackageReference>
                        {
                            new PackageReference
                            {
                                Id = "TestPackage",
                                RequestedVersion = "1.0.0",
                                ResolvedVersion = "1.0.0"
                            }
                        }
                    }
                }
            }
        };
    }

    private static List<ProjectInfo> CreateProjectsWithOutdatedPackage()
    {
        return new List<ProjectInfo>
        {
            new ProjectInfo
            {
                Path = "TestProject.csproj",
                Frameworks = new List<FrameworkInfo>
                {
                    new FrameworkInfo
                    {
                        Framework = "net9.0",
                        TopLevelPackages = new List<PackageReference>
                        {
                            new PackageReference
                            {
                                Id = "TestPackage",
                                RequestedVersion = "1.0.0",
                                ResolvedVersion = "1.0.0",
                                LatestVersion = "2.0.0"
                            }
                        }
                    }
                }
            }
        };
    }

    private static List<ProjectInfo> CreateProjectsWithDeprecatedPackage()
    {
        return new List<ProjectInfo>
        {
            new ProjectInfo
            {
                Path = "TestProject.csproj",
                Frameworks = new List<FrameworkInfo>
                {
                    new FrameworkInfo
                    {
                        Framework = "net9.0",
                        TopLevelPackages = new List<PackageReference>
                        {
                            new PackageReference
                            {
                                Id = "TestPackage",
                                RequestedVersion = "1.0.0",
                                ResolvedVersion = "1.0.0",
                                IsDeprecated = true,
                                DeprecationReasons = new List<string> { "Legacy" },
                                Alternative = new PackageAlternative
                                {
                                    Id = "NewPackage",
                                    VersionRange = ">=2.0.0"
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private static List<ProjectInfo> CreateProjectsWithVulnerablePackage()
    {
        return new List<ProjectInfo>
        {
            new ProjectInfo
            {
                Path = "TestProject.csproj",
                Frameworks = new List<FrameworkInfo>
                {
                    new FrameworkInfo
                    {
                        Framework = "net9.0",
                        TopLevelPackages = new List<PackageReference>
                        {
                            new PackageReference
                            {
                                Id = "TestPackage",
                                RequestedVersion = "1.0.0",
                                ResolvedVersion = "1.0.0",
                                HasVulnerabilities = true,
                                Vulnerabilities = new List<VulnerabilityInfo>
                                {
                                    new VulnerabilityInfo
                                    {
                                        Severity = "High",
                                        AdvisoryUrl = "https://github.com/advisories/GHSA-test"
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private static List<ProjectInfo> CreateProjectsWithMultiplePackages()
    {
        return new List<ProjectInfo>
        {
            new ProjectInfo
            {
                Path = "TestProject.csproj",
                Frameworks = new List<FrameworkInfo>
                {
                    new FrameworkInfo
                    {
                        Framework = "net9.0",
                        TopLevelPackages = new List<PackageReference>
                        {
                            new PackageReference
                            {
                                Id = "TestPackage1",
                                RequestedVersion = "1.0.0",
                                ResolvedVersion = "1.0.0"
                            },
                            new PackageReference
                            {
                                Id = "TestPackage2",
                                RequestedVersion = "2.0.0",
                                ResolvedVersion = "2.0.0"
                            }
                        }
                    }
                }
            }
        };
    }

    private void VerifyWarningLogged(string expectedMessage)
    {
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessage)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}