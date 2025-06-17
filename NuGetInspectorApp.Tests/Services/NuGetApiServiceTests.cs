// filepath: c:\Users\rofli\iCloudDrive\Source\NuGetInspectorApp\NuGetInspectorApp.Tests\Services\NuGetApiServiceTests.cs
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using NuGetInspectorApp.Configuration;
using NuGetInspectorApp.Models;
using NuGetInspectorApp.Services;

namespace NuGetInspectorApp.Tests.Services;

/// <summary>
/// Tests for the NuGetApiService class.
/// </summary>
[TestFixture]
public class NuGetApiServiceTests
{
    private Mock<ILogger<NuGetApiService>> _mockLogger = null!;
    private Mock<HttpMessageHandler> _mockHttpMessageHandler = null!;
    private HttpClient _httpClient = null!;
    private AppConfiguration _config = null!;
    private NuGetApiService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<NuGetApiService>>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);

        _config = new AppConfiguration
        {
            NuGetApiBaseUrl = "https://api.nuget.org/v3/registration5-gz-semver2",
            NuGetGalleryBaseUrl = "https://www.nuget.org/packages",
            MaxConcurrentRequests = 5
        };

        _service = new NuGetApiService(_httpClient, _config, _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _httpClient?.Dispose();
        _service?.Dispose();
    }

    [Test]
    public async Task FetchPackageMetadataAsync_WithValidPackage_ReturnsMetadata()
    {
        // Arrange
        var packageId = "TestPackage";
        var version = "1.0.0";
        var expectedUrl = "https://api.nuget.org/v3/registration5-gz-semver2/testpackage/1.0.0.json";

        var responseContent = CreateMockNuGetResponse(packageId, version);
        SetupHttpResponse(expectedUrl, HttpStatusCode.OK, responseContent);

        // Act
        var result = await _service.FetchPackageMetadataAsync(packageId, version);

        // Assert
        result.Should().NotBeNull();
        result.PackageUrl.Should().Be($"https://www.nuget.org/packages/{packageId}/{version}");
        result.ProjectUrl.Should().Be("https://github.com/test/project");
        result.DependencyGroups.Should().HaveCount(1);
        result.DependencyGroups[0].TargetFramework.Should().Be("net9.0");
        result.DependencyGroups[0].Dependencies.Should().HaveCount(1);
        result.DependencyGroups[0].Dependencies[0].Id.Should().Be("DependencyPackage");
        result.DependencyGroups[0].Dependencies[0].Range.Should().Be("[1.0.0, )");
    }

    [Test]
    public async Task FetchPackageMetadataAsync_WithHttpError_ReturnsDefaultMetadata()
    {
        // Arrange
        var packageId = "TestPackage";
        var version = "1.0.0";

        SetupHttpResponse(It.IsAny<string>(), HttpStatusCode.NotFound);

        // Act
        var result = await _service.FetchPackageMetadataAsync(packageId, version);

        // Assert
        result.Should().NotBeNull();
        result.PackageUrl.Should().Be($"https://www.nuget.org/packages/{packageId}/{version}");
        result.ProjectUrl.Should().BeNullOrEmpty();
        result.DependencyGroups.Should().BeEmpty();

        VerifyWarningLogged("Failed to fetch metadata", "404");
    }

    [Test]
    public async Task FetchPackageMetadataAsync_WithNetworkError_ReturnsDefaultMetadataAndLogsWarning()
    {
        // Arrange
        var packageId = "TestPackage";
        var version = "1.0.0";

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _service.FetchPackageMetadataAsync(packageId, version);

        // Assert
        result.Should().NotBeNull();
        result.PackageUrl.Should().Be($"https://www.nuget.org/packages/{packageId}/{version}");
        result.ProjectUrl.Should().BeNullOrEmpty();
        result.DependencyGroups.Should().BeEmpty();

        VerifyWarningLogged("Network error fetching metadata");
    }

    [Test]
    public async Task FetchPackageMetadataAsync_WithInvalidJson_ReturnsDefaultMetadataAndLogsWarning()
    {
        // Arrange
        var packageId = "TestPackage";
        var version = "1.0.0";

        SetupHttpResponse(It.IsAny<string>(), HttpStatusCode.OK, "Invalid JSON content");

        // Act
        var result = await _service.FetchPackageMetadataAsync(packageId, version);

        // Assert
        result.Should().NotBeNull();
        result.PackageUrl.Should().Be($"https://www.nuget.org/packages/{packageId}/{version}");
        result.ProjectUrl.Should().BeNullOrEmpty();
        result.DependencyGroups.Should().BeEmpty();

        VerifyWarningLogged("JSON parsing error");
    }

    [Test]
    public async Task FetchPackageMetadataAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var packageId = "TestPackage";
        var version = "1.0.0";
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        // Act & Assert
        await FluentActions.Invoking(() =>
            _service.FetchPackageMetadataAsync(packageId, version, cancellationTokenSource.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t")]
    public void FetchPackageMetadataAsync_WithInvalidPackageId_ThrowsArgumentException(string packageId)
    {
        // Act & Assert
        FluentActions.Invoking(() => _service.FetchPackageMetadataAsync(packageId, "1.0.0"))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public void FetchPackageMetadataAsync_WithNullPackageId_ThrowsArgumentException()
    {
        // Act & Assert
        FluentActions.Invoking(() => _service.FetchPackageMetadataAsync(null!, "1.0.0"))
            .Should().ThrowAsync<ArgumentException>();
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t")]
    public void FetchPackageMetadataAsync_WithInvalidVersion_ThrowsArgumentException(string version)
    {
        // Act & Assert
        FluentActions.Invoking(() => _service.FetchPackageMetadataAsync("TestPackage", version))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public void FetchPackageMetadataAsync_WithNullVersion_ThrowsArgumentException()
    {
        // Act & Assert
        FluentActions.Invoking(() => _service.FetchPackageMetadataAsync("TestPackage", null!))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public async Task FetchPackageMetadataAsync_WithSpecialCharactersInPackageId_UrlEncodesCorrectly()
    {
        // Arrange
        var packageId = "Test.Package-Name";
        var version = "1.0.0";
        var expectedUrl = "https://api.nuget.org/v3/registration5-gz-semver2/test.package-name/1.0.0.json";

        var responseContent = CreateMockNuGetResponse(packageId, version);
        SetupHttpResponse(expectedUrl, HttpStatusCode.OK, responseContent);

        // Act
        var result = await _service.FetchPackageMetadataAsync(packageId, version);

        // Assert
        result.Should().NotBeNull();
        result.PackageUrl.Should().Be($"https://www.nuget.org/packages/{packageId}/{version}");
    }

    [Test]
    public async Task FetchPackageMetadataAsync_WithCatalogEntryUrl_FetchesDetailedMetadata()
    {
        // Arrange
        var packageId = "TestPackage";
        var version = "1.0.0";
        var catalogEntryUrl = "https://api.nuget.org/v3/catalog0/data/2024.01.01.01.01.01/testpackage.1.0.0.json";

        var registrationResponse = new { catalogEntry = catalogEntryUrl };
        var catalogResponse = CreateCatalogResponse();

        SetupSequentialHttpResponses(registrationResponse, catalogResponse, catalogEntryUrl);

        // Act
        var result = await _service.FetchPackageMetadataAsync(packageId, version);

        // Assert
        result.Should().NotBeNull();
        result.ProjectUrl.Should().Be("https://github.com/test/detailed-project");
        result.DependencyGroups.Should().HaveCount(1);
        result.DependencyGroups[0].Dependencies[0].Id.Should().Be("DetailedDependency");
    }

    [Test]
    public async Task FetchPackageMetadataAsync_WithEmbeddedCatalogEntry_ParsesInlineMetadata()
    {
        // Arrange
        var packageId = "TestPackage";
        var version = "1.0.0";

        var responseContent = CreateMockNuGetResponseWithEmbeddedCatalog(packageId, version);
        SetupHttpResponse(It.IsAny<string>(), HttpStatusCode.OK, responseContent);

        // Act
        var result = await _service.FetchPackageMetadataAsync(packageId, version);

        // Assert
        result.Should().NotBeNull();
        result.ProjectUrl.Should().Be("https://github.com/test/embedded-project");
        result.DependencyGroups.Should().HaveCount(1);
        result.DependencyGroups[0].Dependencies[0].Id.Should().Be("EmbeddedDependency");
    }

    #region Helper Methods

    private void SetupHttpResponse(string expectedUrl, HttpStatusCode statusCode, string? content = null)
    {
        var httpResponse = new HttpResponseMessage(statusCode);
        if (content != null)
        {
            httpResponse.Content = new StringContent(content);
        }

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => expectedUrl == It.IsAny<string>() || req.RequestUri!.ToString() == expectedUrl),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);
    }

    private void SetupSequentialHttpResponses(object registrationResponse, object catalogResponse, string catalogEntryUrl)
    {
        var registrationJson = JsonSerializer.Serialize(registrationResponse, JsonOptions);
        var catalogJson = JsonSerializer.Serialize(catalogResponse, JsonOptions);

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("registration5-gz-semver2")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(registrationJson)
            });

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString() == catalogEntryUrl),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(catalogJson)
            });
    }

    private static object CreateCatalogResponse()
    {
        return new
        {
            projectUrl = "https://github.com/test/detailed-project",
            dependencyGroups = new[]
            {
                new
                {
                    targetFramework = "net9.0",
                    dependencies = new[]
                    {
                        new
                        {
                            id = "DetailedDependency",
                            range = "[2.0.0, )"
                        }
                    }
                }
            }
        };
    }

    private static string CreateMockNuGetResponse(string packageId, string version)
    {
        var response = new
        {
            catalogEntry = new
            {
                projectUrl = "https://github.com/test/project",
                dependencyGroups = new[]
                {
                    new
                    {
                        targetFramework = "net9.0",
                        dependencies = new[]
                        {
                            new
                            {
                                id = "DependencyPackage",
                                range = "[1.0.0, )"
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private static string CreateMockNuGetResponseWithEmbeddedCatalog(string packageId, string version)
    {
        var response = new
        {
            catalogEntry = new
            {
                projectUrl = "https://github.com/test/embedded-project",
                dependencyGroups = new[]
                {
                    new
                    {
                        targetFramework = "net9.0",
                        dependencies = new[]
                        {
                            new
                            {
                                id = "EmbeddedDependency",
                                range = "[1.5.0, )"
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private static JsonSerializerOptions JsonOptions => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private void VerifyWarningLogged(string expectedMessage, string? additionalText = null)
    {
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains(expectedMessage) &&
                    (additionalText == null || v.ToString()!.Contains(additionalText))),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}