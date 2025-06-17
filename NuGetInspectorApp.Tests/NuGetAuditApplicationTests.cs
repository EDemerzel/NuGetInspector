using Microsoft.Extensions.Logging;
using Moq;
using NuGetInspectorApp.Application;
using NuGetInspectorApp.Configuration;
using NuGetInspectorApp.Models;
using NuGetInspectorApp.Services;
using NuGetInspectorApp.Formatters;
using NUnit.Framework;
using FluentAssertions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NuGetInspectorApp.Tests
{
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
                _mockNuGetService.Object,    // INuGetApiService first
                _mockAnalyzer.Object,        // IPackageAnalyzer second
                _mockDotNetService.Object,   // IDotNetService third
                _mockFormatter.Object,       // IReportFormatter fourth
                _mockLogger.Object);         // ILogger fifth
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
            // Assuming DotnetListReport can hold Projects. If it also holds Version, Parameters, Sources, they should be initialized if needed by the code under test.
            // Based on previous feedback, DotnetListReport might only have Projects.
            var report = new DotnetListReport { Projects = projectInfos };
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

            _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--outdated", It.IsAny<CancellationToken>()))
                .ReturnsAsync((DotnetListReport?)null);
            _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--deprecated", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DotnetListReport { Projects = new List<ProjectInfo>() });
            _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--vulnerable", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DotnetListReport { Projects = new List<ProjectInfo>() });

            // Act
            var result = await _application.RunAsync(options, CancellationToken.None);

            // Assert
            result.Should().Be(1);
            VerifyErrorLogged("Failed to retrieve package reports");
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
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error processing NuGet audit") && v.ToString()!.Contains(exceptionMessage)),
                    It.IsAny<InvalidOperationException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
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
            var report = new DotnetListReport { Projects = projectInfos };
            var mergedPackagesInput = CreateTestMergedPackagesForFiltering();
            var packageMetadata = CreateTestPackageMetadata();

            SetupMockServices(report, mergedPackagesInput, packageMetadata); // Ensure this doesn't also try to set up _mockFormatter if you're doing it below

            Dictionary<string, Dictionary<string, MergedPackage>>? capturedMergedPackages = null;

            _mockFormatter.Setup(x => x.FormatReportAsync(
                    It.IsAny<List<ProjectInfo>>(),
                    It.IsAny<Dictionary<string, Dictionary<string, MergedPackage>>>(), // Use It.IsAny for matching this argument
                    It.IsAny<Dictionary<string, PackageMetadata>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<List<ProjectInfo>, Dictionary<string, Dictionary<string, MergedPackage>>, Dictionary<string, PackageMetadata>, CancellationToken>(
                    (projs, mp, meta, ct) => { capturedMergedPackages = mp; }) // Capture the second argument (mp)
                .ReturnsAsync("Filtered report output");

            // Act
            var result = await _application.RunAsync(options, CancellationToken.None);

            // Assert
            result.Should().Be(0);
            capturedMergedPackages.Should().NotBeNull();

            var packagesForFramework = capturedMergedPackages![$"{TestProjectPath}|{TestFramework}"];

            if (onlyOutdated)
                packagesForFramework.Values.Should().AllSatisfy(p =>
                    (p.IsOutdated || (!p.IsDeprecated && !p.Vulnerabilities.Any()))
                    .Should().BeTrue("when 'onlyOutdated', packages must be outdated or (not deprecated and not vulnerable)"));
            else
                packagesForFramework.Values.Should().Contain(p => !p.IsOutdated && p.Id == "CurrentPackage");

            if (onlyDeprecated)
                packagesForFramework.Values.Should().AllSatisfy(p =>
                    (p.IsDeprecated || (!p.IsOutdated && !p.Vulnerabilities.Any()))
                    .Should().BeTrue("when 'onlyDeprecated', packages must be deprecated or (not outdated and not vulnerable)"));
            else
                packagesForFramework.Values.Should().Contain(p => !p.IsDeprecated && p.Id == "CurrentPackage");

            if (onlyVulnerable)
                packagesForFramework.Values.Should().AllSatisfy(p =>
                    (p.Vulnerabilities.Any() || (!p.IsOutdated && !p.IsDeprecated))
                    .Should().BeTrue("when 'onlyVulnerable', packages must be vulnerable or (not outdated and not deprecated)"));
            else
                packagesForFramework.Values.Should().Contain(p => !p.Vulnerabilities.Any() && p.Id == "CurrentPackage");

            if (onlyOutdated && packagesForFramework.Any()) packagesForFramework.Values.Should().Contain(p => p.IsOutdated);
            if (onlyDeprecated && packagesForFramework.Any()) packagesForFramework.Values.Should().Contain(p => p.IsDeprecated);
            if (onlyVulnerable && packagesForFramework.Any()) packagesForFramework.Values.Should().Contain(p => p.Vulnerabilities.Any());
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
                var report = new DotnetListReport { Projects = projectInfos };
                var mergedPackages = CreateTestMergedPackages();
                var packageMetadata = CreateTestPackageMetadata();
                const string expectedOutput = "Test report content for file";

                SetupMockServices(report, mergedPackages, packageMetadata);
                _mockFormatter.Setup(x => x.FormatReportAsync(It.IsAny<List<ProjectInfo>>(), It.IsAny<Dictionary<string, Dictionary<string, MergedPackage>>>(), It.IsAny<Dictionary<string, PackageMetadata>>(), It.IsAny<CancellationToken>()))
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
        public async Task RunAsync_OperationCanceled_ReturnsFailureAndLogsCancellation()
        {
            // Arrange
            var options = new CommandLineOptions { SolutionPath = "test.sln" };
            var cts = new CancellationTokenSource();


            _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string s, string r, CancellationToken ct) =>
                {
                    await Task.Delay(100, ct); // Simulate some work
                    ct.ThrowIfCancellationRequested(); // Check for cancellation
                    return new DotnetListReport { Projects = new List<ProjectInfo>() };
                });

            cts.Cancel(); // Pre-cancel the token

            // Act
            var result = await _application.RunAsync(options, cts.Token);

            // Assert
            result.Should().Be(1);
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Operation was cancelled")),
                    It.IsAny<OperationCanceledException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }


        #region Helper Methods

        private const string TestProjectPath = "TestProject.csproj";
        private const string TestFramework = "net9.0";

        private static List<ProjectInfo> CreateTestProjectInfos()
        {
            return new List<ProjectInfo>
            {
                new ProjectInfo
                {
                    Path = TestProjectPath,
                    Frameworks = new List<FrameworkInfo>
                    {
                        new FrameworkInfo
                        {
                            Framework = TestFramework,
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
                ["OutdatedPackage"] = new MergedPackage { Id = "OutdatedPackage", ResolvedVersion = "1.0.0", IsOutdated = true, LatestVersion = "1.1.0" },
                ["DeprecatedPackage"] = new MergedPackage { Id = "DeprecatedPackage", ResolvedVersion = "1.0.0", IsDeprecated = true },
                ["VulnerablePackage"] = new MergedPackage { Id = "VulnerablePackage", ResolvedVersion = "1.0.0", Vulnerabilities = new List<VulnerabilityInfo> { new VulnerabilityInfo { Severity = "High" } } },
                ["CurrentPackage"] = new MergedPackage { Id = "CurrentPackage", ResolvedVersion = "1.0.0" }
            };
            return new Dictionary<string, Dictionary<string, MergedPackage>>
            {
                [$"{TestProjectPath}|{TestFramework}"] = packages
            };
        }

        private static Dictionary<string, Dictionary<string, MergedPackage>> CreateTestMergedPackagesForFiltering()
        {
            var packages = new Dictionary<string, MergedPackage>
            {
                ["OutdatedOnly"] = new MergedPackage { Id = "OutdatedOnly", ResolvedVersion = "1.0.0", IsOutdated = true, LatestVersion = "1.1.0" },
                ["DeprecatedOnly"] = new MergedPackage { Id = "DeprecatedOnly", ResolvedVersion = "2.0.0", IsDeprecated = true },
                ["VulnerableOnly"] = new MergedPackage { Id = "VulnerableOnly", ResolvedVersion = "3.0.0", Vulnerabilities = new List<VulnerabilityInfo> { new VulnerabilityInfo { Severity = "High" } } },
                ["OutdatedAndVulnerable"] = new MergedPackage { Id = "OutdatedAndVulnerable", ResolvedVersion = "4.0.0", IsOutdated = true, LatestVersion = "4.1.0", Vulnerabilities = new List<VulnerabilityInfo> { new VulnerabilityInfo { Severity = "Medium" } } },
                ["CurrentPackage"] = new MergedPackage { Id = "CurrentPackage", ResolvedVersion = "5.0.0" }
            };
            return new Dictionary<string, Dictionary<string, MergedPackage>>
            {
                [$"{TestProjectPath}|{TestFramework}"] = packages
            };
        }


        private static Dictionary<string, PackageMetadata> CreateTestPackageMetadata()
        {
            return new Dictionary<string, PackageMetadata>
            {
                ["OutdatedPackage|1.0.0"] = new PackageMetadata { PackageUrl = "url1" },
                ["DeprecatedPackage|1.0.0"] = new PackageMetadata { PackageUrl = "url2" },
                ["VulnerablePackage|1.0.0"] = new PackageMetadata { PackageUrl = "url3" },
                ["CurrentPackage|1.0.0"] = new PackageMetadata { PackageUrl = "url4" },
                ["OutdatedOnly|1.0.0"] = new PackageMetadata { PackageUrl = "url_filter1" },
                ["DeprecatedOnly|2.0.0"] = new PackageMetadata { PackageUrl = "url_filter2" },
                ["VulnerableOnly|3.0.0"] = new PackageMetadata { PackageUrl = "url_filter3" },
                ["OutdatedAndVulnerable|4.0.0"] = new PackageMetadata { PackageUrl = "url_filter4" },
                ["CurrentPackage|5.0.0"] = new PackageMetadata { PackageUrl = "url_filter5" }
            };
        }

        private void SetupMockServices(DotnetListReport report, Dictionary<string, Dictionary<string, MergedPackage>> mergedPackages, Dictionary<string, PackageMetadata> packageMetadata)
        {
            _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--outdated", It.IsAny<CancellationToken>())).ReturnsAsync(report);
            _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--deprecated", It.IsAny<CancellationToken>())).ReturnsAsync(report);
            _mockDotNetService.Setup(x => x.GetPackageReportAsync(It.IsAny<string>(), "--vulnerable", It.IsAny<CancellationToken>())).ReturnsAsync(report);

            // Setup Analyzer to return the specific merged packages for the test project and framework
            // This setup assumes that MergePackages is called for each project/framework combination.
            // If the report.Projects is null or empty, this specific setup might not be hit.
            if (report.Projects != null)
            {
                foreach (var projectInfo in report.Projects)
                {
                    if (projectInfo.Frameworks != null)
                    {
                        foreach (var frameworkInfo in projectInfo.Frameworks)
                        {
                            _mockAnalyzer.Setup(x => x.MergePackages(
                               It.IsAny<List<ProjectInfo>>(),
                               It.IsAny<List<ProjectInfo>>(),
                               It.IsAny<List<ProjectInfo>>(),
                               projectInfo.Path, // Match specific project path
                               frameworkInfo.Framework)) // Match specific framework
                           .Returns(mergedPackages.TryGetValue($"{projectInfo.Path}|{frameworkInfo.Framework}", out var pkgs) ? pkgs : new Dictionary<string, MergedPackage>());
                        }
                    }
                }
            }


            _mockNuGetService.Setup(x => x.FetchPackageMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string pkgId, string version, CancellationToken ct) =>
                    packageMetadata.TryGetValue($"{pkgId}|{version}", out var meta) ? meta : new PackageMetadata { PackageUrl = $"fallback_url/{pkgId}/{version}" });


            _mockFormatter.Setup(x => x.FormatReportAsync(
                It.IsAny<List<ProjectInfo>>(),
                It.IsAny<Dictionary<string, Dictionary<string, MergedPackage>>>(),
                It.IsAny<Dictionary<string, PackageMetadata>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test report output");
        }

        private void VerifyServiceCalls(CommandLineOptions options, DotnetListReport report)
        {
            _mockDotNetService.Verify(x => x.GetPackageReportAsync(options.SolutionPath, "--outdated", It.IsAny<CancellationToken>()), Times.Once);
            _mockDotNetService.Verify(x => x.GetPackageReportAsync(options.SolutionPath, "--deprecated", It.IsAny<CancellationToken>()), Times.Once);
            _mockDotNetService.Verify(x => x.GetPackageReportAsync(options.SolutionPath, "--vulnerable", It.IsAny<CancellationToken>()), Times.Once);

            if (report.Projects != null)
            {
                foreach (var projectInfo in report.Projects)
                {
                    if (projectInfo.Frameworks != null)
                    {
                        foreach (var frameworkInfo in projectInfo.Frameworks)
                        {
                            _mockAnalyzer.Verify(x => x.MergePackages(
                                It.IsAny<List<ProjectInfo>>(), It.IsAny<List<ProjectInfo>>(), It.IsAny<List<ProjectInfo>>(),
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
                Times.Once);
        }
        #endregion
    }

    public static class Capture
    {
        public static T With<T>(Action<T> callback)
        {
            return Match.Create<T>(value => { callback(value); return true; });
        }
    }
}