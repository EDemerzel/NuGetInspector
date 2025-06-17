using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Configuration;
using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Services
{
    /// <summary>
    /// Implements the NuGet API service for fetching package metadata with retry logic.
    /// </summary>
    public class NuGetApiService : INuGetApiService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly AppConfiguration _configuration;
        private readonly ILogger<NuGetApiService> _logger;
        private readonly SemaphoreSlim _semaphore;
        private readonly Random _jitterRandom;

        /// <summary>
        /// Initializes a new instance of the <see cref="NuGetApiService"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client to use for requests.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="logger">The logger instance.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public NuGetApiService(HttpClient httpClient, AppConfiguration configuration, ILogger<NuGetApiService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _semaphore = new SemaphoreSlim(configuration.MaxConcurrentRequests);
            _jitterRandom = new Random();

            // Validate configuration
            _configuration.Validate();

            // Configure HTTP client
            _httpClient.Timeout = TimeSpan.FromSeconds(configuration.HttpTimeoutSeconds);
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "NuGetInspector/1.0");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        /// <summary>
        /// Alternative constructor that creates its own HttpClient with optimized settings.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="logger">The logger instance.</param>
        public NuGetApiService(AppConfiguration configuration, ILogger<NuGetApiService> logger)
            : this(CreateOptimizedHttpClient(configuration), configuration, logger)
        {
        }

        /// <inheritdoc />
        public async Task<PackageMetadata> FetchPackageMetadataAsync(string id, string version, CancellationToken cancellationToken = default)
        {
            // Enhanced input validation
            ValidatePackageInput(id, version);

            var meta = new PackageMetadata
            {
                PackageUrl = $"{_configuration.NuGetGalleryBaseUrl}/{id}/{version}"
            };

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                string regUrl = $"{_configuration.NuGetApiBaseUrl}/{id.ToLowerInvariant()}/{version}.json";
                _logger.LogDebug("Fetching registration from {Url}", regUrl);

                using var regRes = await ExecuteWithRetryAsync(
                    () => _httpClient.GetAsync(regUrl, cancellationToken),
                    $"fetch registration for {id} {version}",
                    cancellationToken);

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

                        // Validate the catalog URL for security
                        if (!IsValidCatalogUrl(detailsUrl))
                        {
                            _logger.LogWarning("Invalid catalog URL for {PackageId} {Version}: {Url}", id, version, detailsUrl);
                            return meta;
                        }

                        _logger.LogDebug("Fetching catalogEntry from {Url}", detailsUrl);
                        using var detRes = await ExecuteWithRetryAsync(
                            () => _httpClient.GetAsync(detailsUrl, cancellationToken),
                            $"fetch catalog entry for {id} {version}",
                            cancellationToken);

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
        /// Executes an HTTP operation with exponential backoff retry logic.
        /// </summary>
        /// <param name="operation">The HTTP operation to execute.</param>
        /// <param name="operationName">A descriptive name for logging.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The HTTP response message.</returns>
        private async Task<HttpResponseMessage> ExecuteWithRetryAsync(
            Func<Task<HttpResponseMessage>> operation,
            string operationName,
            CancellationToken cancellationToken)
        {
            var attempt = 0;
            var delay = TimeSpan.FromSeconds(_configuration.RetryDelaySeconds);
            Exception? lastException = null;

            while (true)
            {
                attempt++;
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var response = await operation();

                    // Success or non-retryable error
                    if (response.IsSuccessStatusCode || !IsRetryableStatusCode(response.StatusCode))
                    {
                        return response;
                    }

                    // Retryable HTTP error
                    if (attempt > _configuration.MaxRetryAttempts)
                    {
                        _logger.LogWarning("Failed to {Operation} after {Attempts} attempts. Status: {StatusCode}",
                            operationName, attempt, response.StatusCode);
                        return response;
                    }

                    _logger.LogDebug("Attempt {Attempt} to {Operation} failed with {StatusCode}, retrying in {Delay}ms",
                        attempt, operationName, response.StatusCode, delay.TotalMilliseconds);

                    response.Dispose();
                    lastException = new HttpRequestException($"HTTP {response.StatusCode}");
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    if (attempt > _configuration.MaxRetryAttempts)
                    {
                        _logger.LogWarning("Failed to {Operation} after {Attempts} attempts due to network error: {Error}",
                            operationName, attempt, ex.Message);
                        throw;
                    }

                    _logger.LogDebug("Attempt {Attempt} to {Operation} failed with network error: {Error}, retrying in {Delay}ms",
                        attempt, operationName, ex.Message, delay.TotalMilliseconds);
                }
                catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
                {
                    lastException = ex;
                    if (attempt > _configuration.MaxRetryAttempts)
                    {
                        _logger.LogWarning("Failed to {Operation} after {Attempts} attempts due to timeout",
                            operationName, attempt);
                        throw new TimeoutException($"Operation '{operationName}' timed out after {attempt} attempts", ex);
                    }

                    _logger.LogDebug("Attempt {Attempt} to {Operation} timed out, retrying in {Delay}ms",
                        attempt, operationName, delay.TotalMilliseconds);
                }
                catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
                {
                    // User-requested cancellation, don't retry
                    throw;
                }

                // Wait before retry with jitter
                await Task.Delay(CalculateDelayWithJitter(delay), cancellationToken);

                // Exponential backoff
                delay = TimeSpan.FromSeconds(Math.Min(
                    delay.TotalSeconds * _configuration.RetryBackoffFactor,
                    _configuration.MaxRetryDelaySeconds));
            }
        }

        /// <summary>
        /// Determines if an HTTP status code is retryable.
        /// </summary>
        /// <param name="statusCode">The HTTP status code.</param>
        /// <returns>True if the status code indicates a retryable error.</returns>
        private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
        {
            return statusCode switch
            {
                HttpStatusCode.RequestTimeout => true,
                HttpStatusCode.TooManyRequests => true,
                HttpStatusCode.InternalServerError => true,
                HttpStatusCode.BadGateway => true,
                HttpStatusCode.ServiceUnavailable => true,
                HttpStatusCode.GatewayTimeout => true,
                _ => false
            };
        }

        /// <summary>
        /// Calculates retry delay with optional jitter.
        /// </summary>
        /// <param name="baseDelay">The base delay before applying jitter.</param>
        /// <returns>The delay with jitter applied.</returns>
        private TimeSpan CalculateDelayWithJitter(TimeSpan baseDelay)
        {
            if (!_configuration.UseRetryJitter)
                return baseDelay;

            // Add up to 25% jitter
            var jitter = _jitterRandom.NextDouble() * 0.25;
            var jitteredDelay = baseDelay.TotalMilliseconds * (1 + jitter);
            return TimeSpan.FromMilliseconds(jitteredDelay);
        }

        /// <summary>
        /// Validates package ID and version inputs for security.
        /// </summary>
        /// <param name="id">The package ID.</param>
        /// <param name="version">The package version.</param>
        /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
        private static void ValidatePackageInput(string id, string version)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Package ID cannot be null or empty", nameof(id));

            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("Package version cannot be null or empty", nameof(version));

            // Check for suspicious characters that could indicate injection attacks
            var suspiciousChars = new[] { '<', '>', '"', '\'', '&', '\0', '\r', '\n' };
            if (id.IndexOfAny(suspiciousChars) >= 0)
                throw new ArgumentException("Package ID contains invalid characters", nameof(id));

            if (version.IndexOfAny(suspiciousChars) >= 0)
                throw new ArgumentException("Package version contains invalid characters", nameof(version));

            // Validate package ID format (basic NuGet package ID rules)
            if (id.Length > 100 || !char.IsLetter(id[0]))
                throw new ArgumentException("Invalid package ID format", nameof(id));

            // Validate version format (basic semantic version check)
            if (version.Length > 64)
                throw new ArgumentException("Package version is too long", nameof(version));
        }

        /// <summary>
        /// Validates that a catalog URL is safe to fetch.
        /// </summary>
        /// <param name="url">The catalog URL to validate.</param>
        /// <returns>True if the URL is considered safe.</returns>
        private bool IsValidCatalogUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            // Only allow HTTPS for security
            if (uri.Scheme != "https")
                return false;

            // Only allow NuGet API domains
            var allowedHosts = new[] { "api.nuget.org" };
            return allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates an optimized HttpClient for NuGet API requests.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>A configured HttpClient instance.</returns>
        private static HttpClient CreateOptimizedHttpClient(AppConfiguration configuration)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                MaxConnectionsPerServer = configuration.MaxConcurrentRequests
            };

            return new HttpClient(handler, disposeHandler: true);
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
            {
                var projectUrl = pu.GetString();
                if (!string.IsNullOrEmpty(projectUrl) && Uri.TryCreate(projectUrl, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "https" || uri.Scheme == "http"))
                {
                    meta.ProjectUrl = projectUrl;
                }
            }
            else if (root.TryGetProperty("projectUrl", out pu) && pu.ValueKind == JsonValueKind.String)
            {
                var projectUrl = pu.GetString();
                if (!string.IsNullOrEmpty(projectUrl) && Uri.TryCreate(projectUrl, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "https" || uri.Scheme == "http"))
                {
                    meta.ProjectUrl = projectUrl;
                }
            }
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
                            var depId = idProp.GetString() ?? "";
                            var depRange = rangeProp.GetString() ?? "";

                            // Basic validation of dependency data
                            if (!string.IsNullOrEmpty(depId) && depId.Length <= 100 &&
                                !string.IsNullOrEmpty(depRange) && depRange.Length <= 64)
                            {
                                deps.Add(new PackageDependency
                                {
                                    Id = depId,
                                    Range = depRange
                                });
                            }
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