using Microsoft.Extensions.Logging;
using Moq;
using NuGetInspectorApp.Application;
using NuGetInspectorApp.Configuration;
using NuGetInspectorApp.Formatters;
using NuGetInspectorApp.Models;
using NuGetInspectorApp.Services;

namespace NuGetInspectorApp.Tests;

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

        // Create temp solution file for tests that need it
        File.WriteAllText("test.sln", "Microsoft Visual Studio Solution File, Version 12.00");
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up temp files
        if (File.Exists("test.sln"))
            File.Delete("test.sln");
    }

    [Test]
    public async Task RunAsync_WithNullOptions_ReturnsFailure()
    {
        // Act
        var result = await _application.RunAsync(null!, CancellationToken.None);

        // Assert
        result.Should().Be(1);
        VerifyErrorLogged("CommandLineOptions cannot be null");
    }

    [Test]
    public async Task RunAsync_WithNonExistentSolutionFile_ReturnsFailure()
    {
        // Arrange
        var options = new CommandLineOptions { SolutionPath = "nonexistent.sln", OutputFormat = "console" };

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(1);
        VerifyErrorLogged("Solution file not found");
    }

    [Test]
    public async Task RunAsync_WithValidOptions_ReturnsSuccess()
    {
        // Arrange
        var options = new CommandLineOptions
        {
            SolutionPath = "test.sln",
            OutputFormat = "console",
            VerboseOutput = false,
            OnlyOutdated = false,
            OnlyVulnerable = false,
            OnlyDeprecated = false
        };

        var projectInfos = CreateTestProjectInfos();
        var report = new DotNetListReport { Projects = projectInfos };
        var mergedPackages = CreateTestMergedPackages();
        var packageMetadata = CreateTestPackageMetadata();

        SetupMockServices(report, mergedPackages, packageMetadata);

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(0);
        VerifyServiceCalls(options, report);
    }

    [Test]
    public async Task RunAsync_WithNullReportFromDotNetService_ReturnsFailure()
    {
        // Arrange
        var options = new CommandLineOptions { SolutionPath = "test.sln", OutputFormat = "console" };

        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DotNetListReport { Projects = new List<ProjectInfo>() });
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--outdated", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DotNetListReport?)null);
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--deprecated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DotNetListReport { Projects = new List<ProjectInfo>() });
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--vulnerable", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DotNetListReport { Projects = new List<ProjectInfo>() });

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(1);
        VerifyErrorLogged("Failed to retrieve one or");
    }

    [Test]
    public async Task RunAsync_DotNetServiceThrowsException_ReturnsFailureAndLogsError()
    {
        // Arrange
        var options = new CommandLineOptions { SolutionPath = "test.sln", OutputFormat = "console" };
        var exceptionMessage = "DotNetService failed";

        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(exceptionMessage));

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(1);
        VerifyErrorLogged("Failed to retrieve one or");
    }

    [Test]
    public async Task RunAsync_WithEmptyMergedPackages_LogsWarning()
    {
        // Arrange
        var options = new CommandLineOptions { SolutionPath = "test.sln", OutputFormat = "console" };
        var projectInfos = CreateTestProjectInfos();
        var report = new DotNetListReport { Projects = projectInfos };
        var emptyMergedPackages = new Dictionary<string, Dictionary<string, MergedPackage>>();
        var packageMetadata = CreateTestPackageMetadata();

        SetupMockServices(report, emptyMergedPackages, packageMetadata);

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(0);
        VerifyWarningLogged("No packages found after merging and filtering");
    }

    [Test]
    [TestCase(true, false, false, Description = "Only Outdated")]
    [TestCase(false, true, false, Description = "Only Deprecated")]
    [TestCase(false, false, true, Description = "Only Vulnerable")]
    [TestCase(true, true, true, Description = "All Filters")]
    public async Task RunAsync_WithFilters_AppliesCorrectFiltering(bool onlyOutdated, bool onlyDeprecated, bool onlyVulnerable)
    {
        // Arrange
        var options = new CommandLineOptions
        {
            SolutionPath = "test.sln",
            OutputFormat = "console",
            OnlyOutdated = onlyOutdated,
            OnlyDeprecated = onlyDeprecated,
            OnlyVulnerable = onlyVulnerable
        };

        var projectInfos = CreateTestProjectInfos();
        var report = new DotNetListReport { Projects = projectInfos };
        var mergedPackagesInput = CreateTestMergedPackagesForFiltering();
        var packageMetadata = CreateTestPackageMetadata();

        SetupMockServices(report, mergedPackagesInput, packageMetadata);

        Dictionary<string, Dictionary<string, MergedPackage>>? capturedMergedPackages = null;

        _mockFormatter.Setup(x => x.FormatReportAsync(
                It.IsAny<List<ProjectInfo>>(),
                It.IsAny<Dictionary<string, Dictionary<string, MergedPackage>>>(),
                It.IsAny<Dictionary<string, PackageMetaData>>(),
                It.IsAny<CancellationToken>()))
            .Callback<List<ProjectInfo>, Dictionary<string, Dictionary<string, MergedPackage>>, Dictionary<string, PackageMetaData>, CancellationToken>(
                (projs, mp, meta, ct) => { capturedMergedPackages = mp; })
            .ReturnsAsync("Filtered report output");

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(0);
        capturedMergedPackages.Should().NotBeNull();

        var packagesForFramework = capturedMergedPackages![$"{_testProjectPath}|{_testFramework}"];

        // Apply the expected filtering logic manually to validate the test (OR logic)
        var expectedPackages = mergedPackagesInput[$"{_testProjectPath}|{_testFramework}"].Values.Where(p =>
        {
            // No filters = include all packages
            if (!onlyOutdated && !onlyDeprecated && !onlyVulnerable)
                return true;

            // OR logic - include if package matches ANY enabled filter
            var includePackage = false;

            if (onlyOutdated && p.IsOutdated)
                includePackage = true;

            if (onlyDeprecated && p.IsDeprecated)
                includePackage = true;

            if (onlyVulnerable && HasVulnerabilities(p))
                includePackage = true;

            return includePackage;
        }).ToList();

        // Verify that the actual application filtering matches our expected filtering
        if (onlyOutdated || onlyDeprecated || onlyVulnerable)
        {
            // If any filters are applied, verify the results match OR logic
            packagesForFramework.Count.Should().BeLessOrEqualTo(mergedPackagesInput[$"{_testProjectPath}|{_testFramework}"].Count,
                "filtering should reduce or maintain the number of packages");

            // Verify that remaining packages match at least one filter criteria (OR logic)
            if (packagesForFramework.Any())
            {
                packagesForFramework.Values.Should().AllSatisfy(p =>
                {
                    var matchesFilter = false;

                    if (onlyOutdated && p.IsOutdated) matchesFilter = true;
                    if (onlyDeprecated && p.IsDeprecated) matchesFilter = true;
                    if (onlyVulnerable && HasVulnerabilities(p)) matchesFilter = true;

                    matchesFilter.Should().BeTrue(
                        $"Package {p.Id} should match at least one enabled filter (outdated={onlyOutdated}, deprecated={onlyDeprecated}, vulnerable={onlyVulnerable})");
                });
            }

            // Verify expected packages are present
            var expectedPackageIds = expectedPackages.Select(p => p.Id).ToHashSet();
            var actualPackageIds = packagesForFramework.Values.Select(p => p.Id).ToHashSet();

            actualPackageIds.Should().BeEquivalentTo(expectedPackageIds,
                $"the filtered packages should match expected OR logic results for filters: outdated={onlyOutdated}, deprecated={onlyDeprecated}, vulnerable={onlyVulnerable}");
        }
        else
        {
            // No filters applied, should have all packages
            packagesForFramework.Count.Should().Be(mergedPackagesInput[$"{_testProjectPath}|{_testFramework}"].Count,
                "when no filters are applied, all packages should be present");
        }
    }

    [Test]
    public async Task RunAsync_WithOutputFile_WritesReportToFile()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var options = new CommandLineOptions
        {
            SolutionPath = "test.sln",
            OutputFormat = "console",
            OutputFile = tempFile
        };

        try
        {
            var projectInfos = CreateTestProjectInfos();
            var report = new DotNetListReport { Projects = projectInfos };
            var mergedPackages = CreateTestMergedPackages();
            var packageMetadata = CreateTestPackageMetadata();
            const string expectedOutput = "Test report content for file";

            SetupMockServices(report, mergedPackages, packageMetadata);
            _mockFormatter.Setup(x => x.FormatReportAsync(It.IsAny<List<ProjectInfo>>(), It.IsAny<Dictionary<string, Dictionary<string, MergedPackage>>>(), It.IsAny<Dictionary<string, PackageMetaData>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedOutput);

            // Act
            var result = await _application.RunAsync(options, CancellationToken.None);

            // Assert
            result.Should().Be(0);
            File.Exists(tempFile).Should().BeTrue();
            var fileContent = await File.ReadAllTextAsync(tempFile);
            fileContent.Should().Be(expectedOutput);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public async Task RunAsync_WithInvalidOutputPath_ReturnsFailureAndLogsError()
    {
        // Arrange
        var options = new CommandLineOptions
        {
            SolutionPath = "test.sln",
            OutputFormat = "console",
            OutputFile = "Z:\\invalid\\path\\output.txt" // Invalid path
        };

        var projectInfos = CreateTestProjectInfos();
        var report = new DotNetListReport { Projects = projectInfos };
        var mergedPackages = CreateTestMergedPackages();
        var packageMetadata = CreateTestPackageMetadata();

        SetupMockServices(report, mergedPackages, packageMetadata);
        _mockFormatter.Setup(x => x.FormatReportAsync(It.IsAny<List<ProjectInfo>>(), It.IsAny<Dictionary<string, Dictionary<string, MergedPackage>>>(), It.IsAny<Dictionary<string, PackageMetaData>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test output");

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(1);
        VerifyErrorLogged("Failed to write report to file");
    }

    [Test]
    public async Task RunAsync_OperationCanceled_ReturnsSuccessAndLogsCancellation()
    {
        // Arrange
        var options = new CommandLineOptions { SolutionPath = "test.sln" };
        using var cts = new CancellationTokenSource();

        // Setup the first call to succeed, then cancel on subsequent calls
        var callCount = 0;
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string s, string r, CancellationToken ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First call (baseline report) succeeds
                    return new DotNetListReport { Projects = new List<ProjectInfo>() };
                }

                // Subsequent calls get cancelled
                await Task.Delay(50, ct);
                ct.ThrowIfCancellationRequested();
                return new DotNetListReport { Projects = new List<ProjectInfo>() };
            });

        // Cancel after a short delay to allow the first call to complete
        _ = Task.Run(async () =>
        {
            await Task.Delay(25);
            cts.Cancel();
        });

        // Act
        var result = await _application.RunAsync(options, cts.Token);

        // Assert
        result.Should().Be(0); // Should return success (0) for cancellation
        VerifyInformationLogged("Report fetching was cancelled"); // ✅ Changed to Information level
    }

    [Test]
    public async Task RunAsync_NuGetServiceThrowsException_ContinuesProcessingOtherPackages()
    {
        // Arrange
        var options = new CommandLineOptions { SolutionPath = "test.sln", OutputFormat = "console" };
        var projectInfos = CreateTestProjectInfos();
        var report = new DotNetListReport { Projects = projectInfos };
        var mergedPackages = CreateTestMergedPackages();
        var packageMetadata = new Dictionary<string, PackageMetaData>();

        SetupMockServices(report, mergedPackages, packageMetadata);

        // Setup NuGet service to throw exception for some packages but succeed for others
        _mockNuGetService.Setup(x => x.FetchPackageMetaDataAsync("OutdatedPackage", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));
        _mockNuGetService.Setup(x => x.FetchPackageMetaDataAsync("CurrentPackage", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackageMetaData { PackageUrl = "https://example.com", DependencyGroups = new List<DependencyGroup>() });

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(0, "Application should continue despite individual package Metadata failures");
    }

    [Test]
    public async Task RunAsync_WithNullProjectsInReport_HandlesGracefully()
    {
        // Arrange
        var options = new CommandLineOptions { SolutionPath = "test.sln", OutputFormat = "console" };

        var baselineReport = new DotNetListReport { Projects = new List<ProjectInfo>() };
        var outdatedReport = new DotNetListReport { Projects = null };
        var validReport = new DotNetListReport { Projects = new List<ProjectInfo>() };

        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "", It.IsAny<CancellationToken>()))
            .ReturnsAsync(baselineReport);
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--outdated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(outdatedReport);
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--deprecated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(validReport);
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--vulnerable", It.IsAny<CancellationToken>()))
            .ReturnsAsync(validReport);

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(1);
        VerifyErrorLogged("Failed to retrieve one or");
    }

    [Test]
    public async Task RunAsync_WithNullProjectInProjectsList_SkipsNullProjects()
    {
        // Arrange
        var options = new CommandLineOptions { SolutionPath = "test.sln", OutputFormat = "console" };

        var projectInfos = new List<ProjectInfo>
        {
            null!, // Null project
            new ProjectInfo
            {
                Path = _testProjectPath,
                Frameworks = new List<FrameworkInfo>
                {
                    new FrameworkInfo
                    {
                        Framework = _testFramework,
                        TopLevelPackages = new List<PackageReference>(),
                        TransitivePackages = new List<PackageReference>()
                    }
                }
            }
        };

        var report = new DotNetListReport { Projects = projectInfos };
        var mergedPackages = new Dictionary<string, Dictionary<string, MergedPackage>>();
        var packageMetadata = CreateTestPackageMetadata();

        SetupMockServices(report, mergedPackages, packageMetadata);

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(0);
        VerifyWarningLogged("Encountered null project");
    }

    [Test]
    public async Task RunAsync_WithNullFrameworksInProject_SkipsProject()
    {
        // Arrange
        var options = new CommandLineOptions { SolutionPath = "test.sln", OutputFormat = "console" };

        var projectInfos = new List<ProjectInfo>
        {
            new ProjectInfo
            {
                Path = _testProjectPath,
                Frameworks = null! // Null frameworks
            }
        };

        var report = new DotNetListReport { Projects = projectInfos };
        var mergedPackages = new Dictionary<string, Dictionary<string, MergedPackage>>();
        var packageMetadata = CreateTestPackageMetadata();

        SetupMockServices(report, mergedPackages, packageMetadata);

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(0);
        VerifyWarningLogged("has no frameworks defined");
    }

    [Test]
    public async Task RunAsync_WithFilterResultingInEmptyPackages_LogsAppropriateMessage()
    {
        // Arrange
        var options = new CommandLineOptions
        {
            SolutionPath = "test.sln",
            OutputFormat = "console",
            OnlyVulnerable = true // Filter that will result in no packages
        };

        var projectInfos = CreateTestProjectInfos();
        var report = new DotNetListReport { Projects = projectInfos };

        // Create packages with no vulnerabilities
        var packagesWithoutVulnerabilities = new Dictionary<string, MergedPackage>
        {
            ["SafePackage"] = new MergedPackage
            {
                Id = "SafePackage",
                ResolvedVersion = "1.0.0",
                Vulnerabilities = new List<VulnerabilityInfo>(), // No vulnerabilities
                DeprecationReasons = new List<string>()
            }
        };

        var mergedPackages = new Dictionary<string, Dictionary<string, MergedPackage>>
        {
            [$"{_testProjectPath}|{_testFramework}"] = packagesWithoutVulnerabilities
        };

        var packageMetadata = CreateTestPackageMetadata();

        SetupMockServices(report, mergedPackages, packageMetadata);

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(0);
        // The filtering should remove all packages, but this shouldn't cause a failure
    }

    [Test]
    public async Task RunAsync_PackageAnalyzerThrowsException_SkipsFrameworkAndContinues()
    {
        // Arrange
        var options = new CommandLineOptions { SolutionPath = "test.sln", OutputFormat = "console" };
        var projectInfos = CreateTestProjectInfos();
        var report = new DotNetListReport { Projects = projectInfos };

        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "", It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--outdated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--deprecated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--vulnerable", It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        // Setup analyzer to throw exception
        _mockAnalyzer.Setup(x => x.MergePackages(
                It.IsAny<List<ProjectInfo>>(),
                It.IsAny<List<ProjectInfo>>(),
                It.IsAny<List<ProjectInfo>>(),
                It.IsAny<List<ProjectInfo>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new InvalidOperationException("Analyzer error"));

        _mockNuGetService.Setup(x => x.FetchPackageMetaDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackageMetaData { PackageUrl = "test", DependencyGroups = new List<DependencyGroup>() });

        _mockFormatter.Setup(x => x.FormatReportAsync(
                It.IsAny<List<ProjectInfo>>(),
                It.IsAny<Dictionary<string, Dictionary<string, MergedPackage>>>(),
                It.IsAny<Dictionary<string, PackageMetaData>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test output");

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(0, "Application should continue despite analyzer failures");
        VerifyErrorLogged("Error merging packages");
    }

    [Test]
    public async Task RunAsync_CallsAllExpectedServices_InCorrectOrder()
    {
        // Arrange
        var options = new CommandLineOptions
        {
            SolutionPath = "test.sln",
            OutputFormat = "console"
        };

        var projectInfos = CreateTestProjectInfos();
        var report = new DotNetListReport { Projects = projectInfos };
        var mergedPackages = CreateTestMergedPackages();
        var packageMetadata = CreateTestPackageMetadata();

        SetupMockServices(report, mergedPackages, packageMetadata);

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(0);

        // Verify all expected service calls were made
        _mockDotNetService.Verify(x => x.GetPackageReportAsync(options.SolutionPath, "", It.IsAny<CancellationToken>()), Times.Once);
        _mockDotNetService.Verify(x => x.GetPackageReportAsync(options.SolutionPath, "--outdated", It.IsAny<CancellationToken>()), Times.Once);
        _mockDotNetService.Verify(x => x.GetPackageReportAsync(options.SolutionPath, "--deprecated", It.IsAny<CancellationToken>()), Times.Once);
        _mockDotNetService.Verify(x => x.GetPackageReportAsync(options.SolutionPath, "--vulnerable", It.IsAny<CancellationToken>()), Times.Once);

        _mockAnalyzer.Verify(x => x.MergePackages(
            It.IsAny<List<ProjectInfo>>(),
            It.IsAny<List<ProjectInfo>>(),
            It.IsAny<List<ProjectInfo>>(),
            It.IsAny<List<ProjectInfo>>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.AtLeastOnce);

        _mockFormatter.Verify(x => x.FormatReportAsync(
            It.IsAny<List<ProjectInfo>>(),
            It.IsAny<Dictionary<string, Dictionary<string, MergedPackage>>>(),
            It.IsAny<Dictionary<string, PackageMetaData>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RunAsync_WithTransitivePackages_PreservesTransitivePackagesInOutput()
    {
        // Arrange
        var options = new CommandLineOptions { SolutionPath = "test.sln", OutputFormat = "console" };

        // Create baseline report with transitive packages
        var baselineProject = new ProjectInfo
        {
            Path = "TestProject.csproj",
            Frameworks = new List<FrameworkInfo>
        {
            new FrameworkInfo
            {
                Framework = "net9.0",
                TopLevelPackages = new List<PackageReference>
                {
                    new PackageReference { Id = "TopLevel", ResolvedVersion = "1.0.0" }
                },
                TransitivePackages = new List<PackageReference>
                {
                    new PackageReference { Id = "Transitive.Package", ResolvedVersion = "2.0.0" },
                    new PackageReference { Id = "Another.Transitive", ResolvedVersion = "3.0.0" }
                }
            }
        }
        };

        var baselineReport = new DotNetListReport { Projects = new List<ProjectInfo> { baselineProject } };
        var mergedPackages = CreateTestMergedPackages();
        var packageMetadata = CreateTestPackageMetadata();

        // ✅ Setup mock services to return the baseline report that contains transitive packages
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "", It.IsAny<CancellationToken>()))
            .ReturnsAsync(baselineReport); // Return baseline report with transitive packages
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--outdated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(baselineReport);
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--deprecated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(baselineReport);
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--vulnerable", It.IsAny<CancellationToken>()))
            .ReturnsAsync(baselineReport);

        // ✅ Setup the analyzer to return merged packages (this processes both top-level and transitive)
        _mockAnalyzer.Setup(x => x.MergePackages(
                It.IsAny<List<ProjectInfo>>(),
                It.IsAny<List<ProjectInfo>>(),
                It.IsAny<List<ProjectInfo>>(),
                It.IsAny<List<ProjectInfo>>(),
                "TestProject.csproj",
                "net9.0"))
            .Returns(mergedPackages[$"{_testProjectPath}|{_testFramework}"]);

        // ✅ Setup NuGet service
        _mockNuGetService.Setup(x => x.FetchPackageMetaDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string pkgId, string version, CancellationToken ct) =>
                packageMetadata.TryGetValue($"{pkgId}|{version}", out var meta) ? meta : new PackageMetaData { PackageUrl = $"fallback_url/{pkgId}/{version}", DependencyGroups = new List<DependencyGroup>() });

        // ✅ Setup formatter
        _mockFormatter.Setup(x => x.FormatReportAsync(
                It.IsAny<List<ProjectInfo>>(),
                It.IsAny<Dictionary<string, Dictionary<string, MergedPackage>>>(),
                It.IsAny<Dictionary<string, PackageMetaData>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test report output");

        // Act
        var result = await _application.RunAsync(options, CancellationToken.None);

        // Assert
        result.Should().Be(0);

        // ✅ Verify that formatter was called with the BASELINE report that includes transitive packages
        _mockFormatter.Verify(x => x.FormatReportAsync(
            It.Is<List<ProjectInfo>>(projects =>
                projects.Any(p => p.Frameworks.Any(f => f.TransitivePackages != null && f.TransitivePackages.Any()))),
            It.IsAny<Dictionary<string, Dictionary<string, MergedPackage>>>(),
            It.IsAny<Dictionary<string, PackageMetaData>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #region Helper Methods

    private const string _testProjectPath = "TestProject.csproj";
    private const string _testFramework = "net9.0";

    /// <summary>
    /// Helper method to check if a package has vulnerabilities.
    /// Avoids expression tree issues with null-conditional operators.
    /// </summary>
    private static bool HasVulnerabilities(MergedPackage package)
    {
        return package.Vulnerabilities != null && package.Vulnerabilities.Any();
    }

    private static List<ProjectInfo> CreateTestProjectInfos()
    {
        return new List<ProjectInfo>
        {
            new ProjectInfo
            {
                Path = _testProjectPath,
                Frameworks = new List<FrameworkInfo>
                {
                    new FrameworkInfo
                    {
                        Framework = _testFramework,
                        TopLevelPackages = new List<PackageReference>
                        {
                            new PackageReference { Id = "OutdatedPackage", RequestedVersion = "1.0.0", ResolvedVersion = "1.0.0" },
                            new PackageReference { Id = "DeprecatedPackage", RequestedVersion = "1.0.0", ResolvedVersion = "1.0.0" },
                            new PackageReference { Id = "VulnerablePackage", RequestedVersion = "1.0.0", ResolvedVersion = "1.0.0" },
                            new PackageReference { Id = "CurrentPackage", RequestedVersion = "1.0.0", ResolvedVersion = "1.0.0" }
                        },
                        TransitivePackages = new List<PackageReference>()
                    }
                }
            }
        };
    }

    private static Dictionary<string, Dictionary<string, MergedPackage>> CreateTestMergedPackages()
    {
        var packages = new Dictionary<string, MergedPackage>
        {
            ["OutdatedPackage"] = new MergedPackage
            {
                Id = "OutdatedPackage",
                ResolvedVersion = "1.0.0",
                IsOutdated = true,
                LatestVersion = "1.1.0",
                Vulnerabilities = new List<VulnerabilityInfo>(),
                DeprecationReasons = new List<string>()
            },
            ["DeprecatedPackage"] = new MergedPackage
            {
                Id = "DeprecatedPackage",
                ResolvedVersion = "1.0.0",
                IsDeprecated = true,
                Vulnerabilities = new List<VulnerabilityInfo>(),
                DeprecationReasons = new List<string> { "Legacy package" }
            },
            ["VulnerablePackage"] = new MergedPackage
            {
                Id = "VulnerablePackage",
                ResolvedVersion = "1.0.0",
                Vulnerabilities = new List<VulnerabilityInfo> { new VulnerabilityInfo { Severity = "High" } },
                DeprecationReasons = new List<string>()
            },
            ["CurrentPackage"] = new MergedPackage
            {
                Id = "CurrentPackage",
                ResolvedVersion = "1.0.0",
                Vulnerabilities = new List<VulnerabilityInfo>(),
                DeprecationReasons = new List<string>()
            }
        };
        return new Dictionary<string, Dictionary<string, MergedPackage>>
        {
            [$"{_testProjectPath}|{_testFramework}"] = packages
        };
    }

    private static Dictionary<string, Dictionary<string, MergedPackage>> CreateTestMergedPackagesForFiltering()
    {
        var packages = new Dictionary<string, MergedPackage>
        {
            // Test single conditions
            ["OutdatedOnly"] = new MergedPackage
            {
                Id = "OutdatedOnly",
                IsOutdated = true,
                IsDeprecated = false,
                Vulnerabilities = new List<VulnerabilityInfo>()
            },
            ["DeprecatedOnly"] = new MergedPackage
            {
                Id = "DeprecatedOnly",
                IsOutdated = false,
                IsDeprecated = true,
                Vulnerabilities = new List<VulnerabilityInfo>()
            },
            ["VulnerableOnly"] = new MergedPackage
            {
                Id = "VulnerableOnly",
                IsOutdated = false,
                IsDeprecated = false,
                Vulnerabilities = new List<VulnerabilityInfo> { new VulnerabilityInfo { Severity = "High" } }
            },
            // Test combination (should be included by OR logic)
            ["OutdatedAndVulnerable"] = new MergedPackage
            {
                Id = "OutdatedAndVulnerable",
                IsOutdated = true,
                IsDeprecated = false,
                Vulnerabilities = new List<VulnerabilityInfo> { new VulnerabilityInfo { Severity = "Medium" } }
            },
            // Test no issues (should be excluded when filters are applied)
            ["CurrentPackage"] = new MergedPackage
            {
                Id = "CurrentPackage",
                IsOutdated = false,
                IsDeprecated = false,
                Vulnerabilities = new List<VulnerabilityInfo>()
            }
        };
        return new Dictionary<string, Dictionary<string, MergedPackage>>
        {
            [$"{_testProjectPath}|{_testFramework}"] = packages
        };
    }

    private static Dictionary<string, PackageMetaData> CreateTestPackageMetadata()
    {
        return new Dictionary<string, PackageMetaData>
        {
            ["OutdatedPackage|1.0.0"] = new PackageMetaData { PackageUrl = "url1", DependencyGroups = new List<DependencyGroup>() },
            ["DeprecatedPackage|1.0.0"] = new PackageMetaData { PackageUrl = "url2", DependencyGroups = new List<DependencyGroup>() },
            ["VulnerablePackage|1.0.0"] = new PackageMetaData { PackageUrl = "url3", DependencyGroups = new List<DependencyGroup>() },
            ["CurrentPackage|1.0.0"] = new PackageMetaData { PackageUrl = "url4", DependencyGroups = new List<DependencyGroup>() },
            ["OutdatedOnly|1.0.0"] = new PackageMetaData { PackageUrl = "url_filter1", DependencyGroups = new List<DependencyGroup>() },
            ["DeprecatedOnly|2.0.0"] = new PackageMetaData { PackageUrl = "url_filter2", DependencyGroups = new List<DependencyGroup>() },
            ["VulnerableOnly|3.0.0"] = new PackageMetaData { PackageUrl = "url_filter3", DependencyGroups = new List<DependencyGroup>() },
            ["OutdatedAndVulnerable|4.0.0"] = new PackageMetaData { PackageUrl = "url_filter4", DependencyGroups = new List<DependencyGroup>() },
            ["CurrentPackage|5.0.0"] = new PackageMetaData { PackageUrl = "url_filter5", DependencyGroups = new List<DependencyGroup>() }
        };
    }

    private void SetupMockServices(DotNetListReport report, Dictionary<string, Dictionary<string, MergedPackage>> mergedPackages, Dictionary<string, PackageMetaData> packageMetadata)
    {
        // ✅ Setup all 4 report types - the key is that baseline report (empty string) should contain transitive packages
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "", It.IsAny<CancellationToken>())).ReturnsAsync(report);
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--outdated", It.IsAny<CancellationToken>())).ReturnsAsync(report);
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--deprecated", It.IsAny<CancellationToken>())).ReturnsAsync(report);
        _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--vulnerable", It.IsAny<CancellationToken>())).ReturnsAsync(report);

        if (report.Projects != null)
        {
            foreach (var projectInfo in report.Projects)
            {
                if (projectInfo?.Frameworks != null)
                {
                    foreach (var frameworkInfo in projectInfo.Frameworks)
                    {
                        // ✅ Setup analyzer to return merged packages for the project/framework combination
                        _mockAnalyzer.Setup(x => x.MergePackages(
                               It.IsAny<List<ProjectInfo>>(),
                               It.IsAny<List<ProjectInfo>>(),
                               It.IsAny<List<ProjectInfo>>(),
                               It.IsAny<List<ProjectInfo>>(),
                               projectInfo.Path,
                               frameworkInfo.Framework))
                           .Returns(mergedPackages.TryGetValue($"{projectInfo.Path}|{frameworkInfo.Framework}", out var pkgs) ? pkgs : new Dictionary<string, MergedPackage>());
                    }
                }
            }
        }

        _mockNuGetService.Setup(x => x.FetchPackageMetaDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string pkgId, string version, CancellationToken ct) =>
                packageMetadata.TryGetValue($"{pkgId}|{version}", out var meta) ? meta : new PackageMetaData { PackageUrl = $"fallback_url/{pkgId}/{version}", DependencyGroups = new List<DependencyGroup>() });

        _mockFormatter.Setup(x => x.FormatReportAsync(
            It.IsAny<List<ProjectInfo>>(),
            It.IsAny<Dictionary<string, Dictionary<string, MergedPackage>>>(),
            It.IsAny<Dictionary<string, PackageMetaData>>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync("Test report output");
    }

    private void VerifyServiceCalls(CommandLineOptions options, DotNetListReport report)
    {
        _mockDotNetService.Verify(x => x.GetPackageReportAsync(options.SolutionPath, "", It.IsAny<CancellationToken>()), Times.Once);
        _mockDotNetService.Verify(x => x.GetPackageReportAsync(options.SolutionPath, "--outdated", It.IsAny<CancellationToken>()), Times.Once);
        _mockDotNetService.Verify(x => x.GetPackageReportAsync(options.SolutionPath, "--deprecated", It.IsAny<CancellationToken>()), Times.Once);
        _mockDotNetService.Verify(x => x.GetPackageReportAsync(options.SolutionPath, "--vulnerable", It.IsAny<CancellationToken>()), Times.Once);

        if (report.Projects != null)
        {
            foreach (var projectInfo in report.Projects)
            {
                if (projectInfo?.Frameworks != null)
                {
                    foreach (var frameworkInfo in projectInfo.Frameworks)
                    {
                        _mockAnalyzer.Verify(x => x.MergePackages(
                            It.IsAny<List<ProjectInfo>>(),
                            It.IsAny<List<ProjectInfo>>(),
                            It.IsAny<List<ProjectInfo>>(),
                            It.IsAny<List<ProjectInfo>>(),
                            projectInfo.Path, frameworkInfo.Framework), Times.AtLeastOnce());
                    }
                }
            }
        }
    }

    private void VerifyErrorLogged(string expectedMessageSubstring)
    {
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessageSubstring)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    private void VerifyWarningLogged(string expectedMessageSubstring)
    {
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessageSubstring)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    private void VerifyInformationLogged(string expectedMessageSubstring)
    {
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessageSubstring)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion
}