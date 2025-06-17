using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using NuGetInspectorApp.Configuration;
using NuGetInspectorApp.Services;
using System.Net;
using System.Text.Json;

// using NuGetInspectorApp.Tests.Formatters; // Not used

namespace NuGetInspectorApp.Tests.Services
{
    /// <summary>
    /// Tests for the NuGetApiService class.
    /// </summary>
    [TestFixture]
    public class NuGetApiServiceTests
    {
        private Mock<ILogger<NuGetApiService>> _mockLogger = null!;
        private Mock<HttpMessageHandler> _mockHttpMessageHandler = null!;
        private HttpClient _httpClient = null!;
        private AppSettings _settings = null!;
        private NuGetApiService _service = null!;

        [SetUp]
        public void SetUp()
        {
            _mockLogger = new Mock<ILogger<NuGetApiService>>();
            _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
            _settings = new AppSettings
            {
                NuGetApiBaseUrl = "https://api.nuget.org/v3/registration5-semver1", // Example base URL
                NuGetGalleryBaseUrl = "https://www.nuget.org/packages",
                MaxConcurrentRequests = 5,
                HttpTimeoutSeconds = 30,
                MaxRetryAttempts = 3,
                RetryDelaySeconds = 2
            };

            _service = new NuGetApiService(_httpClient, _settings, _mockLogger.Object);
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
            var packageId = "Microsoft.Data.SqlClient";
            var version = "5.0.0";

            SetupHttpResponse(HttpStatusCode.OK, CreateValidRegistrationResponse());

            // Act
            var result = await _service.FetchPackageMetaDataAsync(packageId, version);

            Console.WriteLine($"Package ID: {packageId}, Version: {version}");
            Console.WriteLine("Result:");
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

            // Assert
            result.Should().NotBeNull();
            result.PackageUrl.Should().Be($"{_settings.NuGetGalleryBaseUrl}/{packageId}/{version}");
            result.ProjectUrl.Should().Be("https://aka.ms/sqlclientproject");
            result.DependencyGroups.Should().NotBeEmpty();
            result.DependencyGroups![0].TargetFramework.Should().Be("netcoreapp3.1");
        }

        [Test]
        public async Task FetchPackageMetadataAsync_WithInvalidPackageId_ThrowsArgumentException()
        {
            await FluentActions.Invoking(() => _service.FetchPackageMetaDataAsync("", "1.0.0"))
                .Should().ThrowAsync<ArgumentException>().WithMessage("*Package ID*");
        }

        [Test]
        public async Task FetchPackageMetadataAsync_WithInvalidVersion_ThrowsArgumentException()
        {
            await FluentActions.Invoking(() => _service.FetchPackageMetaDataAsync("TestPackage", ""))
                .Should().ThrowAsync<ArgumentException>().WithMessage("*Package version*");
        }

        [Test]
        public async Task FetchPackageMetadataAsync_WithSuspiciousCharactersInId_ThrowsArgumentException()
        {
            await FluentActions.Invoking(() => _service.FetchPackageMetaDataAsync("Test<script>", "1.0.0"))
                .Should().ThrowAsync<ArgumentException>().WithMessage("*Package ID contains invalid characters*");
        }

        [Test]
        public async Task FetchPackageMetadataAsync_WithSuspiciousCharactersInVersion_ThrowsArgumentException()
        {
            await FluentActions.Invoking(() => _service.FetchPackageMetaDataAsync("TestPackage", "1.0.<0>"))
                .Should().ThrowAsync<ArgumentException>().WithMessage("*Package version contains invalid characters*");
        }


        [Test]
        public async Task FetchPackageMetaDataAsync_WithHttpError_ReturnsDefaultMetaDataWithPackageUrl()
        {
            // Arrange
            var packageId = "TestPackage";
            var version = "1.0.0";
            SetupHttpResponse(HttpStatusCode.NotFound, "");

            // Act
            var result = await _service.FetchPackageMetaDataAsync(packageId, version);

            // Assert
            result.Should().NotBeNull();
            result.PackageUrl.Should().Be($"{_settings.NuGetGalleryBaseUrl}/{packageId}/{version}"); // PackageUrl is always constructed
            result.ProjectUrl.Should().BeNull();
            result.DependencyGroups.Should().BeEmpty(); // Or null depending on PackageMetaData initialization
        }

        [Test]
        public async Task FetchPackageMetadataAsync_WithNetworkException_ReturnsDefaultMetaDataWithPackageUrl()
        {
            // Arrange
            var packageId = "TestPackage";
            var version = "1.0.0";
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Network error"));

            // Act
            var result = await _service.FetchPackageMetaDataAsync(packageId, version);

            // Assert
            result.Should().NotBeNull();
            result.PackageUrl.Should().Be($"{_settings.NuGetGalleryBaseUrl}/{packageId}/{version}");
            result.ProjectUrl.Should().BeNull();
        }

        [Test]
        public async Task FetchPackageMetaDataAsync_WithCatalogEntryUrl_FetchesAdditionalData()
        {
            // Arrange
            var packageId = "TestPackage";
            var version = "1.0.0";
            var catalogUrl = "https://api.nuget.org/v3/catalog0/data/2023.01.01.01.01.01/testpackage.1.0.0.json";

            // First response for the registration index
            _mockHttpMessageHandler.Protected()
                .SetupSequence<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CreateRegistrationWithCatalogUrlResponse(catalogUrl)) })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CreateValidCatalogEntryResponse()) }); // Second response for the catalog entry URL

            // Act
            var result = await _service.FetchPackageMetaDataAsync(packageId, version);

            // Assert
            result.Should().NotBeNull();
            result.ProjectUrl.Should().Be("https://project.example.com"); // From catalog entry
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync", Times.Exactly(2), // Expect two calls
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains(packageId.ToLowerInvariant()) || req.RequestUri!.ToString() == catalogUrl), // Check both URIs
                ItExpr.IsAny<CancellationToken>());
        }


        [Test]
        public async Task FetchPackageMetaDataAsync_WithCancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var packageId = "TestPackage";
            var version = "1.0.0";

            using var cts = new CancellationTokenSource(); // Ensure cts is disposed
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns(async (HttpRequestMessage request, CancellationToken token) =>
                {
                    await Task.Delay(100, token); // Simulate work
                    token.ThrowIfCancellationRequested();
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                });
            await cts.CancelAsync(); // Await CancelAsync instead of calling Cancel()

            // Act & Assert
            await FluentActions.Invoking(() => _service.FetchPackageMetaDataAsync(packageId, version, cts.Token))
                .Should().ThrowAsync<OperationCanceledException>();
        }

        [Test]
        public async Task FetchPackageMetaDataAsync_WithInvalidJSON_ReturnsDefaultMetaDataWithPackageUrl()
        {
            // Arrange
            var packageId = "TestPackage";
            var version = "1.0.0";
            SetupHttpResponse(HttpStatusCode.OK, "{ invalid json }");

            // Act
            var result = await _service.FetchPackageMetaDataAsync(packageId, version);

            // Assert
            result.Should().NotBeNull();
            result.PackageUrl.Should().Be($"{_settings.NuGetGalleryBaseUrl}/{packageId}/{version}");
            result.ProjectUrl.Should().BeNull();
        }

        [Test]
        public async Task FetchPackageMetaDataAsync_WithTimeout_ReturnsDefaultMetaDataWithPackageUrl()
        {
            // Arrange
            var packageId = "TestPackage";
            var version = "1.0.0";
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new TaskCanceledException("Request timed out due to HttpClient timeout.", new TimeoutException())); // Simulate HttpClient timeout

            // Act
            var result = await _service.FetchPackageMetaDataAsync(packageId, version);

            // Assert
            result.Should().NotBeNull();
            result.PackageUrl.Should().Be($"{_settings.NuGetGalleryBaseUrl}/{packageId}/{version}");
            result.ProjectUrl.Should().BeNull();
        }

        #region Helper Methods

        private void SetupHttpResponse(HttpStatusCode statusCode, string content)
        {
            var response = new HttpResponseMessage(statusCode) { Content = new StringContent(content) };
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>())
                .ReturnsAsync(response);
        }

        private static string CreateValidRegistrationResponse()
        {
            // This JSON structure now matches the Microsoft.Data.SqlClient package and includes the correct projectUrl.
            return """
            {
                "items": [
                    {
                        "catalogEntry": {
                            "id": "Microsoft.Data.SqlClient",
                            "version": "5.0.0",
                            "projectUrl": "https://aka.ms/sqlclientproject",
                            "licenseUrl": "https://licenses.nuget.org/MIT",
                            "description": "Microsoft.Data.SqlClient is a data provider for SQL Server.",
                            "authors": "Microsoft",
                            "tags": ["sql", "sqlclient", "microsoft"],
                            "published": "2023-01-01T00:00:00Z",
                            "dependencyGroups": [
                                {
                                    "targetFramework": "netcoreapp3.1",
                                    "dependencies": [
                                        { "id": "System.Data.Common", "range": "[4.3.0, )" }
                                    ]
                                }
                            ]
                        }
                    }
                ]
            }
            """;
        }

        private static string CreateRegistrationWithCatalogUrlResponse(string catalogEntryUrl)
        {
            // This JSON structure represents an item within the "items" array of a registration page,
            // where the catalogEntry is a URL to the full catalog entry.
            return $$"""
            {
                "items": [
                    {
                        "catalogEntry": "{{catalogEntryUrl}}"
                    }
                ]
            }
            """;
        }

        private static string CreateValidCatalogEntryResponse() // This is the full catalog entry
        {
            return """
            {
                "id": "TestPackage",
                "version": "1.0.0",
                "projectUrl": "https://project.example.com",
                "licenseUrl": "https://license.example.com",
                "description": "A test package.",
                "authors": "Test Author",
                "tags": ["test", "example"],
                "published": "2023-01-01T12:00:00Z",
                "dependencyGroups": [
                    {
                        "targetFramework": ".NETStandard2.0",
                        "dependencies": [ { "id": "Dependency1", "range": "[1.0.0, )" } ]
                    }
                ]
            }
            """;
        }

        #endregion
    }
}