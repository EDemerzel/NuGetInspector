using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Configuration;
using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Services
{
    /// <summary>
    /// Implements the NuGet API service for fetching package metadata.
    /// </summary>
    public class NuGetApiService : INuGetApiService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly AppConfiguration _configuration;
        private readonly ILogger<NuGetApiService> _logger;
        private readonly SemaphoreSlim _semaphore;

        /// <summary>
        /// Initializes a new instance of the <see cref="NuGetApiService"/> class.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="logger">The logger instance.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> or <paramref name="logger"/> is null.</exception>
        public NuGetApiService(AppConfiguration configuration, ILogger<NuGetApiService> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _semaphore = new SemaphoreSlim(configuration.MaxConcurrentRequests);

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(configuration.HttpTimeoutSeconds)
            };

            // Add user agent for better API usage tracking
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "NuGetInspector/1.0");
        }

        /// <inheritdoc />
        public async Task<PackageMetadata> FetchPackageMetadataAsync(string id, string version, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Package ID cannot be null or empty", nameof(id));
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("Package version cannot be null or empty", nameof(version));

            var meta = new PackageMetadata
            {
                PackageUrl = $"{_configuration.NuGetGalleryBaseUrl}/{id}/{version}"
            };

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                string regUrl = $"{_configuration.NuGetApiBaseUrl}/{id.ToLowerInvariant()}/{version}.json";
                _logger.LogDebug("Fetching registration from {Url}", regUrl);

                using var regRes = await _httpClient.GetAsync(regUrl, cancellationToken);
                
                if (!regRes.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch metadata for {PackageId} {Version}. Status: {StatusCode}", 
                        id, version, regRes.StatusCode);
                    return meta;
                }

                using var regStream = await regRes.Content.ReadAsStreamAsync(cancellationToken);
                using var regDoc = await JsonDocument.ParseAsync(regStream, cancellationToken: cancellationToken);
                var root = regDoc.RootElement;

                JsonElement details = root;
                if (root.TryGetProperty("catalogEntry", out var ce))
                {
                    if (ce.ValueKind == JsonValueKind.String)
                    {
                        var detailsUrl = ce.GetString()!;
                        _logger.LogDebug("Fetching catalogEntry from {Url}", detailsUrl);
                        using var detRes = await _httpClient.GetAsync(detailsUrl, cancellationToken);
                        
                        if (detRes.IsSuccessStatusCode)
                        {
                            using var detStream = await detRes.Content.ReadAsStreamAsync(cancellationToken);
                            using var detDoc = await JsonDocument.ParseAsync(detStream, cancellationToken: cancellationToken);
                            details = detDoc.RootElement.Clone();
                        }
                    }
                    else if (ce.ValueKind == JsonValueKind.Object)
                    {
                        details = ce.Clone();
                    }
                }

                ExtractProjectUrl(meta, details, root);
                ExtractDependencyGroups(meta, details);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken == cancellationToken)
            {
                _logger.LogWarning("Request cancelled for {PackageId} {Version}", id, version);
                throw;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("Network error fetching metadata for {PackageId} {Version}: {Error}", id, version, ex.Message);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("JSON parsing error for {PackageId} {Version}: {Error}", id, version, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error fetching metadata for {PackageId} {Version}", id, version);
            }
            finally
            {
                _semaphore.Release();
            }

            return meta;
        }

        /// <summary>
        /// Extracts the project URL from the package metadata.
        /// </summary>
        /// <param name="meta">The package metadata to update.</param>
        /// <param name="details">The details JSON element.</param>
        /// <param name="root">The root JSON element.</param>
        private static void ExtractProjectUrl(PackageMetadata meta, JsonElement details, JsonElement root)
        {
            if (details.TryGetProperty("projectUrl", out var pu) && pu.ValueKind == JsonValueKind.String)
                meta.ProjectUrl = pu.GetString();
            else if (root.TryGetProperty("projectUrl", out pu) && pu.ValueKind == JsonValueKind.String)
                meta.ProjectUrl = pu.GetString();
        }

        /// <summary>
        /// Extracts dependency groups from the package metadata.
        /// </summary>
        /// <param name="meta">The package metadata to update.</param>
        /// <param name="details">The details JSON element containing dependency information.</param>
        private static void ExtractDependencyGroups(PackageMetadata meta, JsonElement details)
        {
            if (!details.TryGetProperty("dependencyGroups", out var dgArr) || dgArr.ValueKind != JsonValueKind.Array)
                return;

            foreach (var g in dgArr.EnumerateArray())
            {
                var tf = g.TryGetProperty("targetFramework", out var tfProp) ? tfProp.GetString() ?? "" : "";
                var deps = new List<PackageDependency>();

                if (g.TryGetProperty("dependencies", out var depArr) && depArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in depArr.EnumerateArray())
                    {
                        if (d.TryGetProperty("id", out var idProp) && d.TryGetProperty("range", out var rangeProp))
                        {
                            deps.Add(new PackageDependency
                            {
                                Id = idProp.GetString() ?? "",
                                Range = rangeProp.GetString() ?? ""
                            });
                        }
                    }
                }

                meta.DependencyGroups.Add(new DependencyGroup
                {
                    TargetFramework = tf,
                    Dependencies = deps
                });
            }
        }

        /// <summary>
        /// Releases all resources used by the <see cref="NuGetApiService"/>.
        /// </summary>
        public void Dispose()
        {
            _httpClient?.Dispose();
            _semaphore?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}