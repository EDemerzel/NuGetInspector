using Microsoft.Extensions.Logging;
using Moq;
using NuGetInspectorApp.Models;
using NuGetInspectorApp.Services;
using NUnit.Framework;
using FluentAssertions;
using System.Collections.Generic;
using System.Linq;
using System; // Required for ArgumentNullException and ArgumentException

namespace NuGetInspectorApp.Tests.Services
{
    [TestFixture]
    public class PackageAnalyzerTests
    {
        private Mock<ILogger<PackageAnalyzer>> _mockLogger = null!;
        private PackageAnalyzer _analyzer = null!;

        private const string TestProjectPath = "TestProject.csproj";
        private const string TestFramework = "net9.0";

        [SetUp]
        public void SetUp()
        {
            _mockLogger = new Mock<ILogger<PackageAnalyzer>>();
            _analyzer = new PackageAnalyzer(_mockLogger.Object);
        }

        #region Argument Validation Tests

        [Test]
        public void MergePackages_WithNullOutdatedProjects_ThrowsArgumentNullException()
        {
            FluentActions.Invoking(() => _analyzer.MergePackages(null!, new List<ProjectInfo>(), new List<ProjectInfo>(), TestProjectPath, TestFramework))
                .Should().Throw<ArgumentNullException>().WithMessage("*outdatedProjects*");
        }

        [Test]
        public void MergePackages_WithNullDeprecatedProjects_ThrowsArgumentNullException()
        {
            FluentActions.Invoking(() => _analyzer.MergePackages(new List<ProjectInfo>(), null!, new List<ProjectInfo>(), TestProjectPath, TestFramework))
                .Should().Throw<ArgumentNullException>().WithMessage("*deprecatedProjects*");
        }

        [Test]
        public void MergePackages_WithNullVulnerableProjects_ThrowsArgumentNullException()
        {
            FluentActions.Invoking(() => _analyzer.MergePackages(new List<ProjectInfo>(), new List<ProjectInfo>(), null!, TestProjectPath, TestFramework))
                .Should().Throw<ArgumentNullException>().WithMessage("*vulnerableProjects*");
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void MergePackages_WithInvalidProjectPath_ThrowsArgumentException(string? projectPath)
        {
            FluentActions.Invoking(() => _analyzer.MergePackages(new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), projectPath!, TestFramework))
                .Should().Throw<ArgumentException>().WithMessage("*projectPath*");
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void MergePackages_WithInvalidTargetFramework_ThrowsArgumentException(string? targetFramework)
        {
            FluentActions.Invoking(() => _analyzer.MergePackages(new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), TestProjectPath, targetFramework!))
                .Should().Throw<ArgumentException>().WithMessage("*framework*");
        }

        #endregion

        #region Core Merging Logic Tests

        [Test]
        public void MergePackages_NoMatchingProject_ReturnsEmptyDictionary()
        {
            var outdated = CreateProjectList("OtherProject.csproj", TestFramework, CreateTestPackageReference("PkgA", "1.0", "1.1"));
            var result = _analyzer.MergePackages(outdated, new List<ProjectInfo>(), new List<ProjectInfo>(), TestProjectPath, TestFramework);
            result.Should().BeEmpty();
        }

        [Test]
        public void MergePackages_NoMatchingFramework_ReturnsEmptyDictionary()
        {
            var outdated = CreateProjectList(TestProjectPath, "net8.0", CreateTestPackageReference("PkgA", "1.0", "1.1"));
            var result = _analyzer.MergePackages(outdated, new List<ProjectInfo>(), new List<ProjectInfo>(), TestProjectPath, TestFramework);
            result.Should().BeEmpty();
        }

        [Test]
        public void MergePackages_ProjectAndFrameworkMatchButNoPackages_ReturnsEmptyDictionary()
        {
            var outdated = CreateProjectList(TestProjectPath, TestFramework); // No packages
            var result = _analyzer.MergePackages(outdated, new List<ProjectInfo>(), new List<ProjectInfo>(), TestProjectPath, TestFramework);
            result.Should().BeEmpty();
        }

        [Test]
        public void MergePackages_PackageOnlyInOutdated_CorrectlyMerged()
        {
            var pkgA = CreateTestPackageReference("PkgA", "1.0.0", latestVersion: "1.1.0");
            var outdated = CreateProjectList(TestProjectPath, TestFramework, pkgA);
            var result = _analyzer.MergePackages(outdated, new List<ProjectInfo>(), new List<ProjectInfo>(), TestProjectPath, TestFramework);

            result.Should().HaveCount(1);
            result.Should().ContainKey("PkgA");
            var merged = result["PkgA"];
            merged.Id.Should().Be("PkgA");
            merged.RequestedVersion.Should().Be("1.0.0");
            merged.ResolvedVersion.Should().Be("1.0.0");
            merged.IsOutdated.Should().BeTrue(); // This line should now compile and pass
            merged.LatestVersion.Should().Be("1.1.0");
            merged.IsDeprecated.Should().BeFalse();
            (merged.Vulnerabilities == null || !merged.Vulnerabilities.Any()).Should().BeTrue();
        }

        [Test]
        public void MergePackages_PackageOnlyInDeprecated_CorrectlyMerged()
        {
            var pkgB = CreateTestPackageReference("PkgB", "2.0.0", isDeprecated: true, deprecationReason: "Old", altPkgId: "PkgBNew");
            var deprecated = CreateProjectList(TestProjectPath, TestFramework, pkgB);
            var result = _analyzer.MergePackages(new List<ProjectInfo>(), deprecated, new List<ProjectInfo>(), TestProjectPath, TestFramework);

            result.Should().HaveCount(1);
            result.Should().ContainKey("PkgB");
            var merged = result["PkgB"];
            merged.Id.Should().Be("PkgB");
            merged.IsOutdated.Should().BeFalse();
            merged.IsDeprecated.Should().BeTrue();
            merged.DeprecationReasons.Should().NotBeNull().And.Contain("Old");
            merged.Alternative.Should().NotBeNull();
            merged.Alternative!.Id.Should().Be("PkgBNew");
            (merged.Vulnerabilities == null || !merged.Vulnerabilities.Any()).Should().BeTrue();
        }

        [Test]
        public void MergePackages_PackageOnlyInVulnerable_CorrectlyMerged()
        {
            var pkgC = CreateTestPackageReference("PkgC", "3.0.0", hasVulnerabilities: true, vulnSeverity: "High");
            var vulnerable = CreateProjectList(TestProjectPath, TestFramework, pkgC);
            var result = _analyzer.MergePackages(new List<ProjectInfo>(), new List<ProjectInfo>(), vulnerable, TestProjectPath, TestFramework);

            result.Should().HaveCount(1);
            result.Should().ContainKey("PkgC");
            var merged = result["PkgC"];
            merged.Id.Should().Be("PkgC");
            merged.IsOutdated.Should().BeFalse();
            merged.IsDeprecated.Should().BeFalse();
            merged.Vulnerabilities.Should().NotBeNull().And.HaveCount(1);
            merged.Vulnerabilities![0].Severity.Should().Be("High");
            merged.HasVulnerabilities.Should().BeTrue();
        }

        [Test]
        public void MergePackages_PackageInAllLists_AllInfoCombined()
        {
            var pkg = "CombinedPkg";
            var version = "1.0.0";
            var outdatedPkg = CreateTestPackageReference(pkg, version, latestVersion: "1.1.0");
            var deprecatedPkg = CreateTestPackageReference(pkg, version, isDeprecated: true, deprecationReason: "Legacy");
            var vulnerablePkg = CreateTestPackageReference(pkg, version, hasVulnerabilities: true, vulnSeverity: "Critical");

            var outdatedList = CreateProjectList(TestProjectPath, TestFramework, outdatedPkg);
            var deprecatedList = CreateProjectList(TestProjectPath, TestFramework, deprecatedPkg);
            var vulnerableList = CreateProjectList(TestProjectPath, TestFramework, vulnerablePkg);

            var result = _analyzer.MergePackages(outdatedList, deprecatedList, vulnerableList, TestProjectPath, TestFramework);

            result.Should().HaveCount(1);
            result.Should().ContainKey(pkg);
            var merged = result[pkg];
            merged.Id.Should().Be(pkg);
            merged.RequestedVersion.Should().Be(version);
            merged.ResolvedVersion.Should().Be(version);
            merged.IsOutdated.Should().BeTrue();
            merged.LatestVersion.Should().Be("1.1.0");
            merged.IsDeprecated.Should().BeTrue();
            merged.DeprecationReasons.Should().NotBeNull().And.Contain("Legacy");
            merged.Vulnerabilities.Should().NotBeNull().And.HaveCount(1);
            merged.Vulnerabilities![0].Severity.Should().Be("Critical");
            merged.HasVulnerabilities.Should().BeTrue();
        }

        [Test]
        public void MergePackages_PackageNotOutdatedButInList_CorrectlyMarked()
        {
            var pkgD = CreateTestPackageReference("PkgD", "1.0.0", latestVersion: "1.0.0"); // Resolved == Latest
            var outdated = CreateProjectList(TestProjectPath, TestFramework, pkgD);
            var result = _analyzer.MergePackages(outdated, new List<ProjectInfo>(), new List<ProjectInfo>(), TestProjectPath, TestFramework);

            result.Should().HaveCount(1);
            result.Should().ContainKey("PkgD");
            var merged = result["PkgD"];
            merged.IsOutdated.Should().BeFalse();
            merged.LatestVersion.Should().Be("1.0.0");
        }

        [Test]
        public void MergePackages_PackageInMultipleLists_TakesResolvedVersionFromFirstAvailable()
        {
            var pkgE_outdated = CreateTestPackageReference("PkgE", "1.0.0", latestVersion: "1.1.0");
            var pkgE_deprecated = CreateTestPackageReference("PkgE", "1.0.1", isDeprecated: true); // Different ResolvedVersion

            var outdatedList = CreateProjectList(TestProjectPath, TestFramework, pkgE_outdated);
            var deprecatedList = CreateProjectList(TestProjectPath, TestFramework, pkgE_deprecated);

            var result = _analyzer.MergePackages(outdatedList, deprecatedList, new List<ProjectInfo>(), TestProjectPath, TestFramework);

            result.Should().ContainKey("PkgE");
            var merged = result["PkgE"];
            // The ClonePackageReference ensures that the first time a package is seen, its base data (like ResolvedVersion) is set.
            // Subsequent merges update specific fields like IsDeprecated, LatestVersion, etc.
            merged.ResolvedVersion.Should().Be("1.0.0"); // From outdated list (processed first)
            merged.IsOutdated.Should().BeTrue();
            merged.LatestVersion.Should().Be("1.1.0");
            merged.IsDeprecated.Should().BeTrue(); // Deprecated status from the second list is merged.
        }


        [Test]
        public void MergePackages_MultiplePackages_AllMergedCorrectly()
        {
            var pkgA_outdated = CreateTestPackageReference("PkgA", "1.0.0", latestVersion: "1.1.0");
            var pkgB_deprecated = CreateTestPackageReference("PkgB", "2.0.0", isDeprecated: true);
            var pkgC_vulnerable = CreateTestPackageReference("PkgC", "3.0.0", hasVulnerabilities: true);
            var pkgD_no_status = CreateTestPackageReference("PkgD", "4.0.0"); // No specific status, just exists

            var outdatedList = CreateProjectList(TestProjectPath, TestFramework, pkgA_outdated, pkgD_no_status);
            var deprecatedList = CreateProjectList(TestProjectPath, TestFramework, pkgB_deprecated);
            var vulnerableList = CreateProjectList(TestProjectPath, TestFramework, pkgC_vulnerable);

            var result = _analyzer.MergePackages(outdatedList, deprecatedList, vulnerableList, TestProjectPath, TestFramework);

            result.Should().HaveCount(4);
            result["PkgA"].IsOutdated.Should().BeTrue();
            result["PkgB"].IsDeprecated.Should().BeTrue();
            result["PkgC"].Vulnerabilities.Should().NotBeNull().And.NotBeEmpty();
            result["PkgC"].HasVulnerabilities.Should().BeTrue();
            result["PkgD"].IsOutdated.Should().BeFalse(); // LatestVersion is null by default in CreateTestPackageReference
            result["PkgD"].IsDeprecated.Should().BeFalse();
            result["PkgD"].Vulnerabilities.Should().BeNullOrEmpty();
            result["PkgD"].HasVulnerabilities.Should().BeFalse();
        }


        [Test]
        public void MergePackages_HandlesNullTopLevelPackagesListInFrameworkInfo()
        {
            var projectWithNullPackages = new ProjectInfo
            {
                Path = TestProjectPath,
                Frameworks = new List<FrameworkInfo>
                {
                    new FrameworkInfo { Framework = TestFramework, TopLevelPackages = null! } // Null list
                }
            };
            var outdatedList = new List<ProjectInfo> { projectWithNullPackages };
            var result = _analyzer.MergePackages(outdatedList, new List<ProjectInfo>(), new List<ProjectInfo>(), TestProjectPath, TestFramework);
            result.Should().BeEmpty();
        }

        [Test]
        public void MergePackages_HandlesEmptyTopLevelPackagesListInFrameworkInfo()
        {
            var projectWithEmptyPackages = new ProjectInfo
            {
                Path = TestProjectPath,
                Frameworks = new List<FrameworkInfo>
                {
                    new FrameworkInfo { Framework = TestFramework, TopLevelPackages = new List<PackageReference>() } // Empty list
                }
            };
            var outdatedList = new List<ProjectInfo> { projectWithEmptyPackages };
            var result = _analyzer.MergePackages(outdatedList, new List<ProjectInfo>(), new List<ProjectInfo>(), TestProjectPath, TestFramework);
            result.Should().BeEmpty();
        }


        #endregion

        #region Helper Methods

        private static List<ProjectInfo> CreateProjectList(string projectPath, string framework, params PackageReference[] packages)
        {
            return new List<ProjectInfo>
            {
                new ProjectInfo
                {
                    Path = projectPath,
                    Frameworks = new List<FrameworkInfo>
                    {
                        new FrameworkInfo
                        {
                            Framework = framework,
                            TopLevelPackages = packages.ToList(),
                            TransitivePackages = new List<PackageReference>()
                        }
                    }
                }
            };
        }

        private static PackageReference CreateTestPackageReference(
            string id,
            string version,
            string? latestVersion = null,
            bool isDeprecated = false,
            string? deprecationReason = null,
            string? altPkgId = null,
            bool hasVulnerabilities = false,
            string? vulnSeverity = null,
            string? vulnAdvisoryUrl = null)
        {
            var reasons = new List<string>();
            if (isDeprecated && deprecationReason != null)
            {
                reasons.Add(deprecationReason);
            }

            var alternative = isDeprecated && altPkgId != null ? new PackageAlternative { Id = altPkgId, VersionRange = ">=0.0.0" } : null;

            var vulnerabilities = new List<VulnerabilityInfo>();
            if (hasVulnerabilities)
            {
                vulnerabilities.Add(new VulnerabilityInfo { Severity = vulnSeverity ?? "Unknown", AdvisoryUrl = vulnAdvisoryUrl ?? string.Empty });
            }

            return new PackageReference
            {
                Id = id,
                RequestedVersion = version,
                ResolvedVersion = version,
                LatestVersion = latestVersion,
                IsDeprecated = isDeprecated,
                DeprecationReasons = reasons.Any() ? reasons : null, // Match how it might be deserialized or set
                Alternative = alternative,
                HasVulnerabilities = hasVulnerabilities,
                Vulnerabilities = vulnerabilities.Any() ? vulnerabilities : null // Match how it might be deserialized or set
                // IsOutdated is not set here; it's calculated by the analyzer
            };
        }
        #endregion
    }
}