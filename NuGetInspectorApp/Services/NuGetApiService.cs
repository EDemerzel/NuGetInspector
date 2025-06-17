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
        /// <inheritdoc />
        public async Task<PackageMetadata> FetchPackageMetadataAsync(string id, string version, CancellationToken cancellationToken = default)
        {
            // Enhanced input validation with detailed logging
            try
            {
                ValidatePackageInput(id, version);
                _logger.LogTrace("Validated input for package {PackageId} version {Version}", id, version);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError("Invalid input for package fetch: {Error}", ex.Message);
                throw;
            }

            var meta = new PackageMetadata
            {
                PackageUrl = $"{_configuration.NuGetGalleryBaseUrl}/{id}/{version}"
            };

            var operationId = Guid.NewGuid().ToString("N")[..8];
            _logger.LogDebug("Starting metadata fetch operation {OperationId} for {PackageId} {Version}", operationId, id, version);

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                string regUrl = $"{_configuration.NuGetApiBaseUrl}/{id.ToLowerInvariant()}/{version}.json";
                _logger.LogDebug("[{OperationId}] Fetching registration from {Url}", operationId, regUrl);

                using var regRes = await ExecuteWithRetryAsync(
                    () => _httpClient.GetAsync(regUrl, cancellationToken),
                    $"fetch registration for {id} {version}",
                    cancellationToken,
                    operationId);

                if (!regRes.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[{OperationId}] Failed to fetch metadata for {PackageId} {Version}. Status: {StatusCode}. Returning minimal metadata.",
                        operationId, id, version, regRes.StatusCode);
                    return meta;
                }

                // Read as string for better lifecycle management
                var regContent = await regRes.Content.ReadAsStringAsync(cancellationToken);
                using var regDoc = JsonDocument.Parse(regContent);
                var root = regDoc.RootElement;

                _logger.LogTrace("[{OperationId}] Successfully parsed registration JSON for {PackageId} {Version}", operationId, id, version);

                // Diagnostic logging for structure analysis
                _logger.LogTrace("[{OperationId}] Registration structure: HasItems={HasItems}, HasDirectCatalogEntry={HasDirectCatalogEntry}",
                    operationId, root.TryGetProperty("items", out _), root.TryGetProperty("catalogEntry", out _));

                // Enhanced catalog entry detection and processing
                JsonElement details = root; // Default to registration data

                // First, try to find catalogEntry at root level
                if (root.TryGetProperty("catalogEntry", out var catalogEntry))
                {
                    _logger.LogTrace("[{OperationId}] Found catalogEntry at root level for {PackageId} {Version}", operationId, id, version);
                    details = await ProcessCatalogEntry(catalogEntry, operationId, id, version, cancellationToken) ?? root;
                }
                // If not found at root, search in items array
                else if (root.TryGetProperty("items", out var itemsArray) && itemsArray.ValueKind == JsonValueKind.Array)
                {
                    _logger.LogTrace("[{OperationId}] Searching for catalogEntry in items array for {PackageId} {Version}", operationId, id, version);

                    foreach (var item in itemsArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("catalogEntry", out catalogEntry))
                        {
                            _logger.LogTrace("[{OperationId}] Found catalogEntry in items array for {PackageId} {Version}", operationId, id, version);
                            var processedEntry = await ProcessCatalogEntry(catalogEntry, operationId, id, version, cancellationToken);
                            if (processedEntry.HasValue)
                            {
                                details = processedEntry.Value;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogTrace("[{OperationId}] No catalogEntry found, using registration data for {PackageId} {Version}", operationId, id, version);
                }

                // Extract metadata with enhanced error handling
                ExtractMetadataWithErrorHandling(meta, details, root, operationId, id, version);

                _logger.LogDebug("[{OperationId}] Successfully completed metadata fetch for {PackageId} {Version}", operationId, id, version);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken == cancellationToken)
            {
                _logger.LogWarning("[{OperationId}] Request cancelled for {PackageId} {Version}", operationId, id, version);
                throw;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("[{OperationId}] Network error fetching metadata for {PackageId} {Version}: {Error}. Returning minimal metadata.",
                    operationId, id, version, ex.Message);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("[{OperationId}] JSON parsing error for {PackageId} {Version}: {Error}. Returning minimal metadata.",
                    operationId, id, version, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{OperationId}] Unexpected error fetching metadata for {PackageId} {Version}. Returning minimal metadata.",
                    operationId, id, version);
            }
            finally
            {
                _semaphore.Release();
            }

            // Ensure DependencyGroups is never null
            meta.DependencyGroups ??= new List<DependencyGroup>();
            return meta;
        }

        /// <summary>
        /// Processes a catalog entry, handling both URL strings and embedded objects.
        /// </summary>
        /// <param name="catalogEntry">The catalog entry JSON element.</param>
        /// <param name="operationId">Operation ID for logging correlation.</param>
        /// <param name="id">Package ID.</param>
        /// <param name="version">Package version.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The processed catalog entry as JsonElement, or null if processing failed.</returns>
        private async Task<JsonElement?> ProcessCatalogEntry(
            JsonElement catalogEntry,
            string operationId,
            string id,
            string version,
            CancellationToken cancellationToken)
        {
            if (catalogEntry.ValueKind == JsonValueKind.String)
            {
                var catalogUrl = catalogEntry.GetString();
                if (!string.IsNullOrWhiteSpace(catalogUrl))
                {
                    _logger.LogDebug("[{OperationId}] Fetching catalog entry from URL: {CatalogUrl}", operationId, catalogUrl);

                    // Validate the catalog URL for security
                    if (!IsValidCatalogUrl(catalogUrl))
                    {
                        _logger.LogWarning("[{OperationId}] Invalid catalog URL for {PackageId} {Version}: {Url}. Skipping catalog fetch.",
                            operationId, id, version, catalogUrl);
                        return null;
                    }

                    try
                    {
                        using var catalogRes = await ExecuteWithRetryAsync(
                            () => _httpClient.GetAsync(catalogUrl, cancellationToken),
                            $"fetch catalog entry for {id} {version}",
                            cancellationToken,
                            operationId);

                        if (catalogRes.IsSuccessStatusCode)
                        {
                            var catalogContent = await catalogRes.Content.ReadAsStringAsync(cancellationToken);
                            using var catalogDoc = JsonDocument.Parse(catalogContent);
                            var catalogRoot = catalogDoc.RootElement.Clone();

                            _logger.LogTrace("[{OperationId}] Successfully fetched and parsed catalog entry from URL for {PackageId} {Version}",
                                operationId, id, version);

                            return catalogRoot;
                        }
                        else
                        {
                            _logger.LogWarning("[{OperationId}] Failed to fetch catalog entry from URL for {PackageId} {Version}. Status: {StatusCode}",
                                operationId, id, version, catalogRes.StatusCode);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[{OperationId}] Error fetching catalog entry from URL for {PackageId} {Version}: {Error}",
                            operationId, id, version, ex.Message);
                    }
                }
            }
            else if (catalogEntry.ValueKind == JsonValueKind.Object)
            {
                _logger.LogTrace("[{OperationId}] Using embedded catalog entry object for {PackageId} {Version}", operationId, id, version);
                return catalogEntry.Clone();
            }
            else
            {
                _logger.LogWarning("[{OperationId}] Unexpected catalog entry type {ValueKind} for {PackageId} {Version}",
                    operationId, catalogEntry.ValueKind, id, version);
            }

            return null;
        }

        /// <summary>
        /// Extracts metadata from JSON elements with comprehensive error handling.
        /// </summary>
        private void ExtractMetadataWithErrorHandling(
            PackageMetadata meta,
            JsonElement details,
            JsonElement root,
            string operationId,
            string id,
            string version)
        {
            // Extract project URL
            try
            {
                ExtractProjectUrl(meta, details, root);
                _logger.LogTrace("[{OperationId}] Extracted project URL for {PackageId} {Version}: {ProjectUrl}",
                    operationId, id, version, meta.ProjectUrl ?? "none");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[{OperationId}] Error extracting project URL for {PackageId} {Version}: {Error}",
                    operationId, id, version, ex.Message);
            }

            // Extract dependency groups
            try
            {
                ExtractDependencyGroups(meta, details);
                var depCount = meta.DependencyGroups?.Sum(g => g.Dependencies?.Count ?? 0) ?? 0;
                _logger.LogTrace("[{OperationId}] Extracted {DependencyCount} dependencies across {GroupCount} groups for {PackageId} {Version}",
                    operationId, depCount, meta.DependencyGroups?.Count ?? 0, id, version);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[{OperationId}] Error extracting dependency groups for {PackageId} {Version}: {Error}",
                    operationId, id, version, ex.Message);
                // Ensure DependencyGroups is not null even if extraction fails
                meta.DependencyGroups ??= new List<DependencyGroup>();
            }
        }

        /// <summary>
        /// Executes an HTTP operation with exponential backoff retry logic.
        /// </summary>
        /// <param name="operation">The HTTP operation to execute.</param>
        /// <param name="operationName">A descriptive name for logging.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="operationId">Unique operation identifier for logging correlation.</param>
        /// <returns>The HTTP response message.</returns>
        private async Task<HttpResponseMessage> ExecuteWithRetryAsync(
            Func<Task<HttpResponseMessage>> operation,
            string operationName,
            CancellationToken cancellationToken,
            string? operationId = null)
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
                        if (response.IsSuccessStatusCode)
                        {
                            _logger.LogTrace("[{OperationId}] {Operation} succeeded on attempt {Attempt}",
                                operationId, operationName, attempt);
                        }
                        return response;
                    }

                    // Retryable HTTP error
                    if (attempt > _configuration.MaxRetryAttempts)
                    {
                        _logger.LogWarning("[{OperationId}] Failed to {Operation} after {Attempts} attempts. Final status: {StatusCode}",
                            operationId, operationName, attempt, response.StatusCode);
                        return response;
                    }

                    _logger.LogDebug("[{OperationId}] Attempt {Attempt} to {Operation} failed with {StatusCode}, retrying in {Delay}ms",
                        operationId, attempt, operationName, response.StatusCode, delay.TotalMilliseconds);

                    response.Dispose();
                    lastException = new HttpRequestException($"HTTP {response.StatusCode}");
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    if (attempt > _configuration.MaxRetryAttempts)
                    {
                        _logger.LogWarning("[{OperationId}] Failed to {Operation} after {Attempts} attempts due to network error: {Error}",
                            operationId, operationName, attempt, ex.Message);
                        throw;
                    }

                    _logger.LogDebug("[{OperationId}] Attempt {Attempt} to {Operation} failed with network error: {Error}, retrying in {Delay}ms",
                        operationId, attempt, operationName, ex.Message, delay.TotalMilliseconds);
                }
                catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
                {
                    lastException = ex;
                    if (attempt > _configuration.MaxRetryAttempts)
                    {
                        _logger.LogWarning("[{OperationId}] Failed to {Operation} after {Attempts} attempts due to timeout",
                            operationId, operationName, attempt);
                        throw new TimeoutException($"Operation '{operationName}' timed out after {attempt} attempts", ex);
                    }

                    _logger.LogDebug("[{OperationId}] Attempt {Attempt} to {Operation} timed out, retrying in {Delay}ms",
                        operationId, attempt, operationName, delay.TotalMilliseconds);
                }
                catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
                {
                    // User-requested cancellation, don't retry
                    _logger.LogDebug("[{OperationId}] {Operation} cancelled by user on attempt {Attempt}",
                        operationId, operationName, attempt);
                    throw;
                }

                // Wait before retry with jitter
                var actualDelay = CalculateDelayWithJitter(delay);
                await Task.Delay(actualDelay, cancellationToken);

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
            // Try details first (catalog entry)
            if (details.TryGetProperty("projectUrl", out var pu) && pu.ValueKind == JsonValueKind.String)
            {
                var projectUrl = pu.GetString();
                if (!string.IsNullOrEmpty(projectUrl) && Uri.TryCreate(projectUrl, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "https" || uri.Scheme == "http"))
                {
                    meta.ProjectUrl = projectUrl;
                    return;
                }
            }

            // Fallback to root element (registration)
            if (root.TryGetProperty("projectUrl", out pu) && pu.ValueKind == JsonValueKind.String)
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