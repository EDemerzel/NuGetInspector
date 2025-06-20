using Microsoft.Extensions.Logging;
using Moq;
using NuGetInspectorApp.Models;
using NuGetInspectorApp.Services;
using NUnit.Framework;
using FluentAssertions;
using System.Collections.Generic;
using System.Linq;
using System; // Required for ArgumentNullException and ArgumentException

namespace NuGetInspectorApp.Tests.Services;

[TestFixture]
public class PackageAnalyzerTests
{
    private Mock<ILogger<PackageAnalyzer>> _mockLogger = null!;
    private PackageAnalyzer _analyzer = null!;

    private const string _testProjectPath = "TestProject.csproj";
    private const string _testFramework = "net9.0";

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<PackageAnalyzer>>();
        _analyzer = new PackageAnalyzer(_mockLogger.Object);
    }

    #region Argument Validation Tests

    [Test]
    public void MergePackages_WithNullBaselineProjects_ThrowsArgumentNullException()
    {
        FluentActions.Invoking(() => _analyzer.MergePackages(null!, new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), _testProjectPath, _testFramework))
            .Should().Throw<ArgumentNullException>().WithMessage("*baselineProjects*");
    }

    [Test]
    public void MergePackages_WithNullOutdatedProjects_ThrowsArgumentNullException()
    {
        FluentActions.Invoking(() => _analyzer.MergePackages(new List<ProjectInfo>(), null!, new List<ProjectInfo>(), new List<ProjectInfo>(), _testProjectPath, _testFramework))
            .Should().Throw<ArgumentNullException>().WithMessage("*outdatedProjects*");
    }

    [Test]
    public void MergePackages_WithNullDeprecatedProjects_ThrowsArgumentNullException()
    {
        FluentActions.Invoking(() => _analyzer.MergePackages(new List<ProjectInfo>(), new List<ProjectInfo>(), null!, new List<ProjectInfo>(), _testProjectPath, _testFramework))
            .Should().Throw<ArgumentNullException>().WithMessage("*deprecatedProjects*");
    }

    [Test]
    public void MergePackages_WithNullVulnerableProjects_ThrowsArgumentNullException()
    {
        FluentActions.Invoking(() => _analyzer.MergePackages(new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), null!, _testProjectPath, _testFramework))
            .Should().Throw<ArgumentNullException>().WithMessage("*vulnerableProjects*");
    }

    [Test]
    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void MergePackages_WithInvalidProjectPath_ThrowsArgumentException(string? projectPath)
    {
        FluentActions.Invoking(() => _analyzer.MergePackages(new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), projectPath!, _testFramework))
            .Should().Throw<ArgumentException>().WithMessage("*projectPath*");
    }

    [Test]
    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void MergePackages_WithInvalidTargetFramework_ThrowsArgumentException(string? targetFramework)
    {
        FluentActions.Invoking(() => _analyzer.MergePackages(new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), _testProjectPath, targetFramework!))
            .Should().Throw<ArgumentException>().WithMessage("*framework*");
    }

    #endregion

    #region Core Merging Logic Tests

    [Test]
    public void MergePackages_NoMatchingProject_ReturnsEmptyDictionary()
    {
        var baseline = CreateProjectList("OtherProject.csproj", _testFramework, CreateTestPackageReference("PkgA", "1.0"));
        var result = _analyzer.MergePackages(baseline, new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), _testProjectPath, _testFramework);
        result.Should().BeEmpty();
    }

    [Test]
    public void MergePackages_NoMatchingFramework_ReturnsEmptyDictionary()
    {
        var baseline = CreateProjectList(_testProjectPath, "net8.0", CreateTestPackageReference("PkgA", "1.0"));
        var result = _analyzer.MergePackages(baseline, new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), _testProjectPath, _testFramework);
        result.Should().BeEmpty();
    }

    [Test]
    public void MergePackages_ProjectAndFrameworkMatchButNoPackages_ReturnsEmptyDictionary()
    {
        var baseline = CreateProjectList(_testProjectPath, _testFramework); // No packages
        var result = _analyzer.MergePackages(baseline, new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), _testProjectPath, _testFramework);
        result.Should().BeEmpty();
    }

    [Test]
    public void MergePackages_PackageOnlyInBaseline_CorrectlyMerged()
    {
        var pkgA = CreateTestPackageReference("PkgA", "1.0.0");
        var baseline = CreateProjectList(_testProjectPath, _testFramework, pkgA);
        var result = _analyzer.MergePackages(baseline, new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), _testProjectPath, _testFramework);

        result.Should().HaveCount(1);
        result.Should().ContainKey("PkgA");
        var merged = result["PkgA"];
        merged.Id.Should().Be("PkgA");
        merged.RequestedVersion.Should().Be("1.0.0");
        merged.ResolvedVersion.Should().Be("1.0.0");
        merged.IsOutdated.Should().BeFalse(); // No newer version available
        merged.IsDeprecated.Should().BeFalse();
        merged.HasVulnerabilities.Should().BeFalse();
        (merged.Vulnerabilities == null || !merged.Vulnerabilities.Any()).Should().BeTrue();
    }

    [Test]
    public void MergePackages_PackageInBaselineAndOutdated_CorrectlyMerged()
    {
        var baselinePkg = CreateTestPackageReference("PkgA", "1.0.0");
        var outdatedPkg = CreateTestPackageReference("PkgA", "1.0.0", latestVersion: "1.1.0");

        var baseline = CreateProjectList(_testProjectPath, _testFramework, baselinePkg);
        var outdated = CreateProjectList(_testProjectPath, _testFramework, outdatedPkg);
        var result = _analyzer.MergePackages(baseline, outdated, new List<ProjectInfo>(), new List<ProjectInfo>(), _testProjectPath, _testFramework);

        result.Should().HaveCount(1);
        result.Should().ContainKey("PkgA");
        var merged = result["PkgA"];
        merged.Id.Should().Be("PkgA");
        merged.RequestedVersion.Should().Be("1.0.0");
        merged.ResolvedVersion.Should().Be("1.0.0");
        merged.IsOutdated.Should().BeTrue(); // Has newer version
        merged.LatestVersion.Should().Be("1.1.0");
        merged.IsDeprecated.Should().BeFalse();
        (merged.Vulnerabilities == null || !merged.Vulnerabilities.Any()).Should().BeTrue();
    }

    [Test]
    public void MergePackages_PackageOnlyInOutdated_CorrectlyMerged()
    {
        var pkgA = CreateTestPackageReference("PkgA", "1.0.0", latestVersion: "1.1.0");
        var outdated = CreateProjectList(_testProjectPath, _testFramework, pkgA);
        var result = _analyzer.MergePackages(new List<ProjectInfo>(), outdated, new List<ProjectInfo>(), new List<ProjectInfo>(), _testProjectPath, _testFramework);

        result.Should().HaveCount(1);
        result.Should().ContainKey("PkgA");
        var merged = result["PkgA"];
        merged.Id.Should().Be("PkgA");
        merged.RequestedVersion.Should().Be("1.0.0");
        merged.ResolvedVersion.Should().Be("1.0.0");
        merged.IsOutdated.Should().BeTrue();
        merged.LatestVersion.Should().Be("1.1.0");
        merged.IsDeprecated.Should().BeFalse();
        (merged.Vulnerabilities == null || !merged.Vulnerabilities.Any()).Should().BeTrue();
    }

    [Test]
    public void MergePackages_PackageOnlyInDeprecated_CorrectlyMerged()
    {
        var pkgB = CreateTestPackageReference("PkgB", "2.0.0", isDeprecated: true, deprecationReason: "Old", altPkgId: "PkgBNew");
        var deprecated = CreateProjectList(_testProjectPath, _testFramework, pkgB);
        var result = _analyzer.MergePackages(new List<ProjectInfo>(), new List<ProjectInfo>(), deprecated, new List<ProjectInfo>(), _testProjectPath, _testFramework);

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
        var vulnerable = CreateProjectList(_testProjectPath, _testFramework, pkgC);
        var result = _analyzer.MergePackages(new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), vulnerable, _testProjectPath, _testFramework);

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
        var baselinePkg = CreateTestPackageReference(pkg, version);
        var outdatedPkg = CreateTestPackageReference(pkg, version, latestVersion: "1.1.0");
        var deprecatedPkg = CreateTestPackageReference(pkg, version, isDeprecated: true, deprecationReason: "Legacy");
        var vulnerablePkg = CreateTestPackageReference(pkg, version, hasVulnerabilities: true, vulnSeverity: "Critical");

        var baselineList = CreateProjectList(_testProjectPath, _testFramework, baselinePkg);
        var outdatedList = CreateProjectList(_testProjectPath, _testFramework, outdatedPkg);
        var deprecatedList = CreateProjectList(_testProjectPath, _testFramework, deprecatedPkg);
        var vulnerableList = CreateProjectList(_testProjectPath, _testFramework, vulnerablePkg);

        var result = _analyzer.MergePackages(baselineList, outdatedList, deprecatedList, vulnerableList, _testProjectPath, _testFramework);

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
        var baselinePkg = CreateTestPackageReference("PkgD", "1.0.0");
        var outdatedPkg = CreateTestPackageReference("PkgD", "1.0.0", latestVersion: "1.0.0"); // Resolved == Latest

        var baseline = CreateProjectList(_testProjectPath, _testFramework, baselinePkg);
        var outdated = CreateProjectList(_testProjectPath, _testFramework, outdatedPkg);
        var result = _analyzer.MergePackages(baseline, outdated, new List<ProjectInfo>(), new List<ProjectInfo>(), _testProjectPath, _testFramework);

        result.Should().HaveCount(1);
        result.Should().ContainKey("PkgD");
        var merged = result["PkgD"];
        merged.IsOutdated.Should().BeFalse();
        merged.LatestVersion.Should().Be("1.0.0");
    }

    [Test]
    public void MergePackages_PackageInMultipleLists_TakesResolvedVersionFromBaseline()
    {
        var baselinePkg = CreateTestPackageReference("PkgE", "1.0.0");
        var outdatedPkg = CreateTestPackageReference("PkgE", "1.0.1", latestVersion: "1.1.0"); // Different ResolvedVersion
        var deprecatedPkg = CreateTestPackageReference("PkgE", "1.0.2", isDeprecated: true); // Different ResolvedVersion

        var baselineList = CreateProjectList(_testProjectPath, _testFramework, baselinePkg);
        var outdatedList = CreateProjectList(_testProjectPath, _testFramework, outdatedPkg);
        var deprecatedList = CreateProjectList(_testProjectPath, _testFramework, deprecatedPkg);

        var result = _analyzer.MergePackages(baselineList, outdatedList, deprecatedList, new List<ProjectInfo>(), _testProjectPath, _testFramework);

        result.Should().ContainKey("PkgE");
        var merged = result["PkgE"];
        // Baseline takes precedence for version information
        merged.ResolvedVersion.Should().Be("1.0.0"); // From baseline (processed first)
        merged.IsOutdated.Should().BeTrue();
        merged.LatestVersion.Should().Be("1.1.0");
        merged.IsDeprecated.Should().BeTrue(); // Deprecated status is merged
    }

    [Test]
    public void MergePackages_MultiplePackages_AllMergedCorrectly()
    {
        var baselinePkgA = CreateTestPackageReference("PkgA", "1.0.0");
        var baselinePkgD = CreateTestPackageReference("PkgD", "4.0.0");
        var outdatedPkgA = CreateTestPackageReference("PkgA", "1.0.0", latestVersion: "1.1.0");
        var deprecatedPkgB = CreateTestPackageReference("PkgB", "2.0.0", isDeprecated: true);
        var vulnerablePkgC = CreateTestPackageReference("PkgC", "3.0.0", hasVulnerabilities: true);

        var baselineList = CreateProjectList(_testProjectPath, _testFramework, baselinePkgA, baselinePkgD);
        var outdatedList = CreateProjectList(_testProjectPath, _testFramework, outdatedPkgA);
        var deprecatedList = CreateProjectList(_testProjectPath, _testFramework, deprecatedPkgB);
        var vulnerableList = CreateProjectList(_testProjectPath, _testFramework, vulnerablePkgC);

        var result = _analyzer.MergePackages(baselineList, outdatedList, deprecatedList, vulnerableList, _testProjectPath, _testFramework);

        result.Should().HaveCount(4);
        result["PkgA"].IsOutdated.Should().BeTrue();
        result["PkgB"].IsDeprecated.Should().BeTrue();
        result["PkgC"].Vulnerabilities.Should().NotBeNull().And.NotBeEmpty();
        result["PkgC"].HasVulnerabilities.Should().BeTrue();
        result["PkgD"].IsOutdated.Should().BeFalse(); // Not in outdated list
        result["PkgD"].IsDeprecated.Should().BeFalse();
        result["PkgD"].Vulnerabilities.Should().BeNullOrEmpty();
        result["PkgD"].HasVulnerabilities.Should().BeFalse();
    }

    [Test]
    public void MergePackages_HandlesNullTopLevelPackagesListInFrameworkInfo()
    {
        var projectWithNullPackages = new ProjectInfo
        {
            Path = _testProjectPath,
            Frameworks = new List<FrameworkInfo>
            {
                new FrameworkInfo { Framework = _testFramework, TopLevelPackages = null! } // Null list
            }
        };
        var baselineList = new List<ProjectInfo> { projectWithNullPackages };
        var result = _analyzer.MergePackages(baselineList, new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), _testProjectPath, _testFramework);
        result.Should().BeEmpty();
    }

    [Test]
    public void MergePackages_HandlesEmptyTopLevelPackagesListInFrameworkInfo()
    {
        var projectWithEmptyPackages = new ProjectInfo
        {
            Path = _testProjectPath,
            Frameworks = new List<FrameworkInfo>
            {
                new FrameworkInfo { Framework = _testFramework, TopLevelPackages = new List<PackageReference>() } // Empty list
            }
        };
        var baselineList = new List<ProjectInfo> { projectWithEmptyPackages };
        var result = _analyzer.MergePackages(baselineList, new List<ProjectInfo>(), new List<ProjectInfo>(), new List<ProjectInfo>(), _testProjectPath, _testFramework);
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