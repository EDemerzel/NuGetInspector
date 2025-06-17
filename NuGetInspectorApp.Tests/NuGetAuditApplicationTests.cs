using Microsoft.Extensions.Logging;
using Moq;
using NuGetInspectorApp.Models;
using NuGetInspectorApp.Services;
using NuGetInspectorApp.Formatters;

namespace NuGetInspectorApp.Tests;

/// <summary>
/// Tests for the NuGetAuditApplication class.
/// </summary>
[TestFixture]
public class NuGetAuditApplicationTests
{
    private Mock<INuGetApiService> _mockNuGetService = null!;
    private Mock<IPackageAnalyzer> _mockAnalyzer = null!;
    private Mock<IDotNetService> _mockDotNetService = null!;
    private Mock<IReportFormatter> _mockFormatter = null!;
    private Mock<ILogger<NuGetAuditApplication>> _mockLogger = null!;
    private NuGetAuditApplication _application = null!;

    [SetUp]
    public void SetUp()
    {
        _mockNuGetService = new Mock<INuGetApiService>();
        _mockAnalyzer = new Mock<IPackageAnalyzer>();
        _mockDotNetService = new Mock<IDotNetService>();
        _mockFormatter = new Mock<IReportFormatter>();
        _mockLogger = new Mock<ILogger<NuGetAuditApplication>>();

        _application = new NuGetAuditApplication(
            _mockNuGetService.Object,
            _mockAnalyzer.Object,
            _mockDotNetService.Object,
            _mockFormatter.Object,
            _mockLogger.Object);
    }

    [Test]
    public async Task RunAsync_WithValidOptions_ReturnsSuccess()
    {
        // Arrange
        var options = new CommandLineOptions
        {
            SolutionPath = "test.sln",
            OutputFormat = "console"
        };

        var projects = CreateTestProjects();
        var report = new DotnetListReport { Projects = projects };
        var mergedPackages = CreateMergedPackages();
        var packageMetadata = CreatePackageMetadata();

        SetupMockServices(report, mergedPackages, packageMetadata);

        // Act
        var result = await _application.RunAsync(options);

        // Assert
        result.Should().Be(0);
        VerifyServiceCalls(options);
    }

    [Test]
    public async Task RunAsync_WithNullReport_ReturnsFailure()
    {
        // Arrange
        var options = new CommandLineOptions
        {
            SolutionPath = "test.sln",
            OutputFormat = "console"
        };

        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DotnetListReport?)null);

        // Act
        var result = await _application.RunAsync(options);

        // Assert
        result.Should().Be(1);
        VerifyErrorLogged("Failed to retrieve package reports");
    }

    [Test]
    public async Task RunAsync_WithException_ReturnsFailureAndLogsError()
    {
        // Arrange
        var options = new CommandLineOptions
        {
            SolutionPath = "test.sln",
            OutputFormat = "console"
        };

        var expectedException = new InvalidOperationException("Test exception");
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act
        var result = await _application.RunAsync(options);

        // Assert
        result.Should().Be(1);
        VerifyErrorLogged("Error processing NuGet audit", expectedException);
    }

    [Test]
    public async Task RunAsync_WithOutputFile_SavesReportToFile()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var options = new CommandLineOptions
        {
            SolutionPath = "test.sln",
            OutputFormat = "console",
            OutputFile = tempFile
        };

        var report = new DotnetListReport { Projects = new List<ProjectInfo>() };
        var reportContent = "Test report content";

        SetupMinimalMocks(report, reportContent);

        try
        {
            // Act
            var result = await _application.RunAsync(options);

            // Assert
            result.Should().Be(0);
            File.Exists(tempFile).Should().BeTrue();
            File.ReadAllText(tempFile).Should().Be(reportContent);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public async Task RunAsync_WithFilters_AppliesCorrectFilters()
    {
        // Arrange
        var options = new CommandLineOptions
        {
            SolutionPath = "test.sln",
            OutputFormat = "console",
            OnlyOutdated = true,
            OnlyDeprecated = false,
            OnlyVulnerable = false
        };

        var projects = CreateTestProjects();
        var report = new DotnetListReport { Projects = projects };
        var mergedPackages = CreateFilterTestPackages();

        SetupFilterMocks(report, mergedPackages);

        // Act
        var result = await _application.RunAsync(options);

        // Assert
        result.Should().Be(0);
        _mockFormatter.Verify(x => x.FormatReportAsync(
            It.IsAny<List<ProjectInfo>>(),
            It.Is<Dictionary<string, Dictionary<string, MergedPackage>>>(d =>
                d.Values.Any(dict => dict.ContainsKey("OutdatedPackage") && !dict.ContainsKey("CurrentPackage"))),
            It.IsAny<Dictionary<string, PackageMetadata>>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RunAsync_WithInvalidOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        await FluentActions.Invoking(() => _application.RunAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Test]
    public async Task RunAsync_WithCancellation_RespectsCancellationToken()
    {
        // Arrange
        var options = new CommandLineOptions
        {
            SolutionPath = "test.sln",
            OutputFormat = "console"
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await FluentActions.Invoking(() => _application.RunAsync(options, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    #region Helper Methods

    private static List<ProjectInfo> CreateTestProjects()
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

    private static Dictionary<string, MergedPackage> CreateMergedPackages()
    {
        return new Dictionary<string, MergedPackage>
        {
            ["TestPackage"] = new MergedPackage
            {
                Id = "TestPackage",
                RequestedVersion = "1.0.0",
                ResolvedVersion = "1.0.0"
            }
        };
    }

    private static Dictionary<string, PackageMetadata> CreatePackageMetadata()
    {
        return new Dictionary<string, PackageMetadata>
        {
            ["TestPackage|1.0.0"] = new PackageMetadata
            {
                PackageUrl = "https://nuget.org/packages/TestPackage/1.0.0"
            }
        };
    }

    private static Dictionary<string, MergedPackage> CreateFilterTestPackages()
    {
        return new Dictionary<string, MergedPackage>
        {
            ["OutdatedPackage"] = new MergedPackage
            {
                Id = "OutdatedPackage",
                IsOutdated = true,
                IsDeprecated = false,
                Vulnerabilities = new List<VulnerabilityInfo>()
            },
            ["CurrentPackage"] = new MergedPackage
            {
                Id = "CurrentPackage",
                IsOutdated = false,
                IsDeprecated = false,
                Vulnerabilities = new List<VulnerabilityInfo>()
            }
        };
    }

    private void SetupMockServices(DotnetListReport report, Dictionary<string, MergedPackage> mergedPackages, Dictionary<string, PackageMetadata> packageMetadata)
    {
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        _mockAnalyzer.Setup(x => x.MergePackages(It.IsAny<List<ProjectInfo>>(), It.IsAny<List<ProjectInfo>>(),
            It.IsAny<List<ProjectInfo>>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(mergedPackages);

        _mockNuGetService.Setup(x => x.FetchPackageMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(packageMetadata.Values.First());

        _mockFormatter.Setup(x => x.FormatReportAsync(It.IsAny<List<ProjectInfo>>(),
            It.IsAny<Dictionary<string, Dictionary<string, MergedPackage>>>(),
            It.IsAny<Dictionary<string, PackageMetadata>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test report output");
    }

    private void SetupMinimalMocks(DotnetListReport report, string reportContent)
    {
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        _mockAnalyzer.Setup(x => x.MergePackages(It.IsAny<List<ProjectInfo>>(), It.IsAny<List<ProjectInfo>>(),
            It.IsAny<List<ProjectInfo>>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new Dictionary<string, MergedPackage>());

        _mockFormatter.Setup(x => x.FormatReportAsync(It.IsAny<List<ProjectInfo>>(),
            It.IsAny<Dictionary<string, Dictionary<string, MergedPackage>>>(),
            It.IsAny<Dictionary<string, PackageMetadata>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reportContent);
    }

    private void SetupFilterMocks(DotnetListReport report, Dictionary<string, MergedPackage> mergedPackages)
    {
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        _mockAnalyzer.Setup(x => x.MergePackages(It.IsAny<List<ProjectInfo>>(), It.IsAny<List<ProjectInfo>>(),
            It.IsAny<List<ProjectInfo>>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(mergedPackages);

        _mockFormatter.Setup(x => x.FormatReportAsync(It.IsAny<List<ProjectInfo>>(),
            It.Is<Dictionary<string, Dictionary<string, MergedPackage>>>(d =>
                d.Values.Any(dict => dict.ContainsKey("OutdatedPackage") && !dict.ContainsKey("CurrentPackage"))),
            It.IsAny<Dictionary<string, PackageMetadata>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Filtered report");
    }

    private void VerifyServiceCalls(CommandLineOptions options)
    {
        _mockDotNetService.Verify(x => x.GetPackageReportAsync(options.SolutionPath, "--outdated", It.IsAny<CancellationToken>()), Times.Once);
        _mockDotNetService.Verify(x => x.GetPackageReportAsync(options.SolutionPath, "--deprecated", It.IsAny<CancellationToken>()), Times.Once);
        _mockDotNetService.Verify(x => x.GetPackageReportAsync(options.SolutionPath, "--vulnerable", It.IsAny<CancellationToken>()), Times.Once);
    }

    private void VerifyErrorLogged(string expectedMessage, Exception? expectedException = null)
    {
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessage)),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}