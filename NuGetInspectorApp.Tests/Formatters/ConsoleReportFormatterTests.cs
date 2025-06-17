using NuGetInspectorApp.Formatters;
using NuGetInspectorApp.Models;
using NUnit.Framework;
using FluentAssertions;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace NuGetInspectorApp.Tests.Formatters
{
    [TestFixture]
    public class ConsoleReportFormatterTests
    {
        private ConsoleReportFormatter _formatter = null!;

        [SetUp]
        public void SetUp()
        {
            _formatter = new ConsoleReportFormatter();
        }

        [Test]
        public async Task FormatReportAsync_WithValidData_ReturnsFormattedReport()
        {
            // Arrange
            var projects = CreateTestProjects();
            var mergedPackages = CreateMergedPackages();
            var metadata = CreatePackageMetadata();

            // Act
            var result = await _formatter.FormatReportAsync(projects, mergedPackages, metadata, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("because we provided valid data");
            result.Should().Contain("SampleProject"); // Assuming project path or name is in output
        }

        [Test]
        public async Task FormatReportAsync_WithNoProjects_ReturnsEmptyOutput()
        {
            // Arrange
            var projects = new List<ProjectInfo>();
            var mergedPackages = new Dictionary<string, Dictionary<string, MergedPackage>>();
            var metadata = new Dictionary<string, PackageMetadata>();

            // Act
            var result = await _formatter.FormatReportAsync(projects, mergedPackages, metadata, CancellationToken.None);

            // Assert
            result.Should().BeEmpty("because there are no projects to format");
        }

        [Test]
        public async Task FormatReportAsync_WithMissingPackageMetadata_StillReturnsReport()
        {
            // Arrange
            var projects = CreateTestProjects();
            var mergedPackages = CreateMergedPackages();
            var emptyMetadata = new Dictionary<string, PackageMetadata>();

            // Act
            var result = await _formatter.FormatReportAsync(projects, mergedPackages, emptyMetadata, CancellationToken.None);

            // Assert
            result.Should().NotBeNull().And.NotBeEmpty();
            // Depending on implementation, it might show "N/A" or similar for missing URLs,
            // but shouldn't throw or include literal "null" strings in a user-facing way.
            result.Should().NotContain("null");
        }

        [Test]
        public async Task FormatReportAsync_WithMultipleFrameworks_ReturnsCombinedOutput()
        {
            // Arrange
            var projects = CreateMultiFrameworkProjects();
            // Ensure mergedPackages and metadata cover the multi-framework scenario
            var mergedPackages = CreateMultiFrameworkMergedPackages();
            var metadata = CreatePackageMetadata(); // Ensure metadata for "MultiPackage" if needed

            // Act
            var result = await _formatter.FormatReportAsync(projects, mergedPackages, metadata, CancellationToken.None);

            // Assert
            result.Should().Contain("net7.0")
                  .And.Contain("net9.0");
        }

        private static List<ProjectInfo> CreateTestProjects()
        {
            return new List<ProjectInfo>
            {
                new ProjectInfo
                {
                    Path = "c:\\projects\\SampleProject.csproj",
                    Frameworks = new List<FrameworkInfo>
                    {
                        new FrameworkInfo
                        {
                            Framework = "net7.0",
                            TopLevelPackages = new List<PackageReference>
                            {
                                new PackageReference
                                {
                                    Id = "SamplePackage",
                                    RequestedVersion = "1.0.0",
                                    ResolvedVersion = "1.0.0",
                                }
                            }
                        }
                    }
                }
            };
        }

        private static List<ProjectInfo> CreateMultiFrameworkProjects()
        {
            var singleProject = CreateTestProjects()[0]; // Get a copy or new instance
            singleProject.Frameworks.Add(
                new FrameworkInfo
                {
                    Framework = "net9.0",
                    TopLevelPackages = new List<PackageReference>
                    {
                        new PackageReference
                        {
                            Id = "MultiPackage", // Different package for clarity
                            RequestedVersion = "2.0.0",
                            ResolvedVersion = "2.0.0",
                        }
                    }
                });
            return new List<ProjectInfo> { singleProject };
        }

        private static Dictionary<string, Dictionary<string, MergedPackage>> CreateMergedPackages()
        {
            var merged = new Dictionary<string, Dictionary<string, MergedPackage>>();
            merged["c:\\projects\\SampleProject.csproj|net7.0"] = new Dictionary<string, MergedPackage>
            {
                {
                    "SamplePackage",
                    new MergedPackage
                    {
                        Id = "SamplePackage",
                        RequestedVersion = "1.0.0",
                        ResolvedVersion = "1.0.0",
                        IsOutdated = false,
                        IsDeprecated = false,
                        Vulnerabilities = new List<VulnerabilityInfo>() // Initialize lists
                    }
                }
            };
            return merged;
        }

        private static Dictionary<string, Dictionary<string, MergedPackage>> CreateMultiFrameworkMergedPackages()
        {
            var merged = CreateMergedPackages(); // Start with the base
            merged["c:\\projects\\SampleProject.csproj|net9.0"] = new Dictionary<string, MergedPackage>
            {
                {
                    "MultiPackage",
                    new MergedPackage
                    {
                        Id = "MultiPackage",
                        RequestedVersion = "2.0.0",
                        ResolvedVersion = "2.0.0",
                        IsOutdated = false,
                        IsDeprecated = false,
                        Vulnerabilities = new List<VulnerabilityInfo>()
                    }
                }
            };
            return merged;
        }

        private static Dictionary<string, PackageMetadata> CreatePackageMetadata()
        {
            var metadata = new Dictionary<string, PackageMetadata>();
            metadata["SamplePackage|1.0.0"] = new PackageMetadata
            {
                PackageUrl = "https://www.nuget.org/packages/SamplePackage/1.0.0"
            };
            metadata["MultiPackage|2.0.0"] = new PackageMetadata // For multi-framework test
            {
                PackageUrl = "https://www.nuget.org/packages/MultiPackage/2.0.0"
            };
            return metadata;
        }
    }
}