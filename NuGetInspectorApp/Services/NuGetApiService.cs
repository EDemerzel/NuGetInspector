using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Configuration;
using NuGetInspectorApp.Models;
using System.Net;
using System.Text.Json;

namespace NuGetInspectorApp.Services;

/// <summary>
/// Implements the NuGet API service for fetching package Metadata with retry logic.
/// </summary>
public class NuGetApiService : INuGetApiService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;
    private readonly ILogger<NuGetApiService> _logger;
    private readonly SemaphoreSlim _semaphore;
    private readonly Random _jitterRandom;

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetApiService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    /// <param name="settings">The application settings.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public NuGetApiService(HttpClient httpClient, AppSettings settings, ILogger<NuGetApiService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _semaphore = new SemaphoreSlim(settings.MaxConcurrentRequests);
        _jitterRandom = new Random();

        // Validate settings
        _settings.Validate();

        // Configure HTTP client
        _httpClient.Timeout = TimeSpan.FromSeconds(settings.HttpTimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "NuGetInspector/1.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    /// <summary>
    /// Alternative constructor that creates its own HttpClient with optimized settings.
    /// </summary>
    /// <param name="settings">The application settings.</param>
    /// <param name="logger">The logger instance.</param>
    public NuGetApiService(AppSettings settings, ILogger<NuGetApiService> logger)
        : this(CreateOptimizedHttpClient(settings), settings, logger)
    {
    }

    /// <inheritdoc />
    public async Task<PackageMetaData> FetchPackageMetaDataAsync(string id, string version, CancellationToken cancellationToken = default)
    {
        // Enhanced input validation with detailed logging
        try
        {
            ValidatePackageInput(id, version);
            _logger.LogTrace("Validated input for package {PackageId} version {Version}", id, version);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid input for package fetch: {Error}", ex.Message);
            throw;
        }

        var meta = new PackageMetaData
        {
            PackageUrl = $"{_settings.NuGetGalleryBaseUrl}/{id}/{version}",
            DependencyGroups = new List<DependencyGroup>()
        };

        var operationId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogDebug("Starting Metadata fetch operation {OperationId} for {PackageId} {Version}", operationId, id, version);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            string regUrl = $"{_settings.NuGetApiBaseUrl}/{id.ToLowerInvariant()}/{version}.json";
            _logger.LogDebug("[{OperationId}] Fetching registration from {Url}", operationId, regUrl);

            using var regRes = await ExecuteWithRetryAsync(
                () => _httpClient.GetAsync(regUrl, cancellationToken),
                $"fetch registration for {id} {version}",
                cancellationToken,
                operationId);

            if (!regRes.IsSuccessStatusCode)
            {
                _logger.LogWarning("[{OperationId}] Failed to fetch Metadata for {PackageId} {Version}. Status: {StatusCode}. Returning minimal Metadata.",
                    operationId, id, version, regRes.StatusCode);
                return meta;
            }

            // Read content as string for better lifecycle management
            var regContent = await regRes.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogTrace("[{OperationId}] Registration response size: {Size} characters", operationId, regContent.Length);

            using var regDoc = JsonDocument.Parse(regContent);
            var root = regDoc.RootElement;

            _logger.LogTrace("[{OperationId}] Successfully parsed registration JSON for {PackageId} {Version}", operationId, id, version);

            // Enhanced diagnostic logging for structure analysis
            var rootProperties = root.EnumerateObject().Select(p => p.Name).ToList();
            _logger.LogTrace("[{OperationId}] Registration structure: HasItems={HasItems}, HasDirectCatalogEntry={HasDirectCatalogEntry}, RootProperties=[{Properties}]",
                operationId, root.TryGetProperty("items", out _), root.TryGetProperty("catalogEntry", out _), string.Join(", ", rootProperties));

            // Process catalog entry with improved lifecycle management
            string? catalogContent = null;
            JsonElement? catalogElement = null;

            // First, try to find catalogEntry at root level
            if (root.TryGetProperty("catalogEntry", out var catalogEntry))
            {
                _logger.LogTrace("[{OperationId}] Found catalogEntry at root level for {PackageId} {Version}", operationId, id, version);
                catalogContent = await ProcessCatalogEntryToString(catalogEntry, operationId, id, version, cancellationToken);
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
                        catalogContent = await ProcessCatalogEntryToString(catalogEntry, operationId, id, version, cancellationToken);
                        if (!string.IsNullOrEmpty(catalogContent))
                        {
                            break;
                        }
                    }
                }
            }
            else
            {
                _logger.LogTrace("[{OperationId}] No catalogEntry found, using registration data for {PackageId} {Version}", operationId, id, version);
            }

            // Parse catalog content if available
            if (!string.IsNullOrEmpty(catalogContent))
            {
                try
                {
                    using var catalogDoc = JsonDocument.Parse(catalogContent);
                    catalogElement = catalogDoc.RootElement.Clone();
                    _logger.LogTrace("[{OperationId}] Successfully parsed catalog content for {PackageId} {Version}", operationId, id, version);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "[{OperationId}] Failed to parse catalog content for {PackageId} {Version}: {Error}",
                        operationId, id, version, ex.Message);
                }
            }

            // Extract metadata with enhanced error handling
            var detailsElement = catalogElement ?? root;
            ExtractMetadataWithErrorHandling(meta, detailsElement, root, operationId, id, version);

            _logger.LogDebug("[{OperationId}] Successfully completed Metadata fetch for {PackageId} {Version}. ProjectUrl: {ProjectUrl}, Dependencies: {DepCount}",
                operationId, id, version, meta.ProjectUrl ?? "none", meta.DependencyGroups?.Sum(g => g.Dependencies?.Count ?? 0) ?? 0);
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken == cancellationToken)
        {
            _logger.LogWarning(ex, "[{OperationId}] Request cancelled for {PackageId} {Version}", operationId, id, version);
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[{OperationId}] Network error fetching Metadata for {PackageId} {Version}: {Error}. Returning minimal Metadata.",
                operationId, id, version, ex.Message);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[{OperationId}] JSON parsing error for {PackageId} {Version}: {Error}. Returning minimal Metadata.",
                operationId, id, version, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{OperationId}] Unexpected error fetching Metadata for {PackageId} {Version}. Returning minimal Metadata.",
                operationId, id, version);
        }
        finally
        {
            _semaphore.Release();
        }

        return meta;
    }

    /// <summary>
    /// Processes a catalog entry and returns its content as a string for safe parsing.
    /// </summary>
    /// <param name="catalogEntry">The catalog entry JSON element.</param>
    /// <param name="operationId">Operation ID for logging correlation.</param>
    /// <param name="id">Package ID.</param>
    /// <param name="version">Package version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The catalog content as a string, or null if processing failed.</returns>
    private async Task<string?> ProcessCatalogEntryToString(
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
                        _logger.LogTrace("[{OperationId}] Successfully fetched catalog entry from URL for {PackageId} {Version}. Size: {Size} characters",
                            operationId, id, version, catalogContent.Length);

                        // Store the catalog URL that we successfully fetched from
                        // This will be used later in ExtractCatalogUrl
                        return catalogContent;
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
            return catalogEntry.GetRawText();
        }
        else
        {
            _logger.LogWarning("[{OperationId}] Unexpected catalog entry type {ValueKind} for {PackageId} {Version}",
                operationId, catalogEntry.ValueKind, id, version);
        }

        return null;
    }

    /// <summary>
    /// Extracts Metadata from JSON elements with comprehensive error handling.
    /// </summary>
    private void ExtractMetadataWithErrorHandling(
        PackageMetaData meta,
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
            _logger.LogWarning(ex, "[{OperationId}] Error extracting project URL for {PackageId} {Version}: {Error}",
                operationId, id, version, ex.Message);
        }

        // Extract description
        try
        {
            ExtractDescription(meta, details, root);
            _logger.LogTrace("[{OperationId}] Extracted description for {PackageId} {Version}: Length={Length}",
                operationId, id, version, meta.Description?.Length ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{OperationId}] Error extracting description for {PackageId} {Version}: {Error}",
                operationId, id, version, ex.Message);
        }

        // Extract catalog URL (if available from the registration response)
        try
        {
            ExtractCatalogUrl(meta, details, root);
            _logger.LogTrace("[{OperationId}] Extracted catalog URL for {PackageId} {Version}: {CatalogUrl}",
                operationId, id, version, meta.CatalogUrl ?? "none");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{OperationId}] Error extracting catalog URL for {PackageId} {Version}: {Error}",
                operationId, id, version, ex.Message);
        }

        // Extract deprecation information
        try
        {
            ExtractDeprecationInfo(meta, details, root);
            _logger.LogTrace("[{OperationId}] Extracted deprecation info for {PackageId} {Version}: IsDeprecated={IsDeprecated}, Reasons={ReasonCount}",
                operationId, id, version, meta.IsDeprecated, meta.DeprecationReasons?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{OperationId}] Error extracting deprecation info for {PackageId} {Version}: {Error}",
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
            _logger.LogWarning(ex, "[{OperationId}] Error extracting dependency groups for {PackageId} {Version}: {Error}",
                operationId, id, version, ex.Message);
            meta.DependencyGroups ??= new List<DependencyGroup>();
        }
    }

    /// <summary>
    /// Extracts the package description from the package metadata.
    /// </summary>
    /// <param name="meta">The package metadata to update.</param>
    /// <param name="details">The details JSON element (catalog entry).</param>
    /// <param name="root">The root JSON element (registration).</param>
    private static void ExtractDescription(PackageMetaData meta, JsonElement details, JsonElement root)
    {
        // Try details first (catalog entry) - this usually has the most complete information
        if (details.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String)
        {
            var description = desc.GetString();
            if (!string.IsNullOrWhiteSpace(description))
            {
                meta.Description = description.Trim();
                return;
            }
        }

        // Fallback to root element (registration)
        if (root.TryGetProperty("description", out desc) && desc.ValueKind == JsonValueKind.String)
        {
            var description = desc.GetString();
            if (!string.IsNullOrWhiteSpace(description))
            {
                meta.Description = description.Trim();
            }
        }
    }

    /// <summary>
    /// Extracts the catalog URL from the package metadata.
    /// </summary>
    /// <param name="meta">The package metadata to update.</param>
    /// <param name="details">The details JSON element (catalog entry).</param>
    /// <param name="root">The root JSON element (registration).</param>
    private static void ExtractCatalogUrl(PackageMetaData meta, JsonElement details, JsonElement root)
    {
        // Try to get catalog URL from root level first
        if (root.TryGetProperty("catalogEntry", out var catalogEntry))
        {
            if (catalogEntry.ValueKind == JsonValueKind.String)
            {
                var catalogUrl = catalogEntry.GetString();
                if (!string.IsNullOrWhiteSpace(catalogUrl) && IsValidCatalogUrl(catalogUrl))
                {
                    meta.CatalogUrl = catalogUrl;
                    return;
                }
            }
        }

        // Try to find catalog URL in items array
        if (root.TryGetProperty("items", out var itemsArray) && itemsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemsArray.EnumerateArray())
            {
                if (item.TryGetProperty("catalogEntry", out catalogEntry))
                {
                    if (catalogEntry.ValueKind == JsonValueKind.String)
                    {
                        var catalogUrl = catalogEntry.GetString();
                        if (!string.IsNullOrWhiteSpace(catalogUrl) && IsValidCatalogUrl(catalogUrl))
                        {
                            meta.CatalogUrl = catalogUrl;
                            return;
                        }
                    }
                    // If catalogEntry is an object, we might be able to extract an @id
                    else if (catalogEntry.ValueKind == JsonValueKind.Object &&
                             catalogEntry.TryGetProperty("@id", out var idElement) &&
                             idElement.ValueKind == JsonValueKind.String)
                    {
                        var catalogUrl = idElement.GetString();
                        if (!string.IsNullOrWhiteSpace(catalogUrl) && IsValidCatalogUrl(catalogUrl))
                        {
                            meta.CatalogUrl = catalogUrl;
                            return;
                        }
                    }
                }
            }
        }

        // If catalog entry details is from a URL, try to extract that URL
        if (details.TryGetProperty("@id", out var detailsId) && detailsId.ValueKind == JsonValueKind.String)
        {
            var catalogUrl = detailsId.GetString();
            if (!string.IsNullOrWhiteSpace(catalogUrl) && IsValidCatalogUrl(catalogUrl))
            {
                meta.CatalogUrl = catalogUrl;
            }
        }
    }

    /// <summary>
    /// Extracts deprecation information from the package metadata.
    /// </summary>
    /// <param name="meta">The package metadata to update.</param>
    /// <param name="details">The details JSON element (catalog entry).</param>
    /// <param name="root">The root JSON element (registration).</param>
    private static void ExtractDeprecationInfo(PackageMetaData meta, JsonElement details, JsonElement root)
    {
        // Try details first (catalog entry) - this is where deprecation info usually is
        if (TryExtractDeprecationFromElement(meta, details))
        {
            return; // Found in catalog entry
        }

        // Fallback to root element (registration)
        TryExtractDeprecationFromElement(meta, root);
    }

    /// <summary>
    /// Attempts to extract deprecation information from a JSON element.
    /// </summary>
    /// <param name="meta">The package metadata to update.</param>
    /// <param name="element">The JSON element to extract from.</param>
    /// <returns>True if deprecation information was found and extracted.</returns>
    private static bool TryExtractDeprecationFromElement(PackageMetaData meta, JsonElement element)
    {
        if (!element.TryGetProperty("deprecation", out var deprecationElement))
            return false;

        meta.IsDeprecated = true;

        // Extract deprecation message
        if (deprecationElement.TryGetProperty("message", out var messageElement) &&
            messageElement.ValueKind == JsonValueKind.String)
        {
            meta.DeprecationMessage = messageElement.GetString();
        }

        // Extract deprecation reasons
        if (deprecationElement.TryGetProperty("reasons", out var reasonsElement) &&
            reasonsElement.ValueKind == JsonValueKind.Array)
        {
            var reasons = new List<string>();
            foreach (var reasonElement in reasonsElement.EnumerateArray())
            {
                if (reasonElement.ValueKind == JsonValueKind.String)
                {
                    var reason = reasonElement.GetString();
                    if (!string.IsNullOrEmpty(reason))
                    {
                        reasons.Add(reason);
                    }
                }
            }
            meta.DeprecationReasons = reasons;
        }

        // Extract alternate package information from API catalog
        if (deprecationElement.TryGetProperty("alternatePackage", out var alternateElement))
        {
            var alternateId = string.Empty;
            var alternateRange = string.Empty;

            if (alternateElement.TryGetProperty("id", out var idElement) &&
                idElement.ValueKind == JsonValueKind.String)
            {
                alternateId = idElement.GetString() ?? string.Empty;
            }

            if (alternateElement.TryGetProperty("range", out var rangeElement) &&
                rangeElement.ValueKind == JsonValueKind.String)
            {
                alternateRange = rangeElement.GetString() ?? "*";
            }

            // Create the alternative package info if we have a valid ID
            if (!string.IsNullOrEmpty(alternateId))
            {
                meta.AlternativePackage = new PackageAlternative
                {
                    Id = alternateId,
                    VersionRange = alternateRange
                };
            }
        }

        return true;
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
        var delay = TimeSpan.FromSeconds(_settings.RetryDelaySeconds);

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
                if (attempt > _settings.MaxRetryAttempts)
                {
                    _logger.LogWarning("[{OperationId}] Failed to {Operation} after {Attempts} attempts. Final status: {StatusCode}",
                        operationId, operationName, attempt, response.StatusCode);
                    return response;
                }

                _logger.LogDebug("[{OperationId}] Attempt {Attempt} to {Operation} failed with {StatusCode}, retrying in {Delay}ms",
                    operationId, attempt, operationName, response.StatusCode, delay.TotalMilliseconds);

                response.Dispose();
            }
            catch (HttpRequestException ex)
            {
                if (attempt > _settings.MaxRetryAttempts)
                {
                    _logger.LogWarning(ex, "[{OperationId}] Failed to {Operation} after {Attempts} attempts due to network error: {Error}",
                        operationId, operationName, attempt, ex.Message);
                    throw;
                }

                _logger.LogDebug("[{OperationId}] Attempt {Attempt} to {Operation} failed with network error: {Error}, retrying in {Delay}ms",
                    operationId, attempt, operationName, ex.Message, delay.TotalMilliseconds);
            }
            catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
            {
                if (attempt > _settings.MaxRetryAttempts)
                {
                    _logger.LogWarning(ex, "[{OperationId}] Failed to {Operation} after {Attempts} attempts due to timeout",
                        operationId, operationName, attempt);
                    throw new TimeoutException($"Operation '{operationName}' timed out after {attempt} attempts", ex);
                }

                _logger.LogDebug("[{OperationId}] Attempt {Attempt} to {Operation} timed out, retrying in {Delay}ms",
                    operationId, attempt, operationName, delay.TotalMilliseconds);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                // User-requested cancellation, don't retry
                _logger.LogDebug(ex, "[{OperationId}] {Operation} cancelled by user on attempt {Attempt}",
                    operationId, operationName, attempt);
                throw;
            }

            // Wait before retry with jitter
            var actualDelay = CalculateDelayWithJitter(delay);
            await Task.Delay(actualDelay, cancellationToken);

            // Exponential backoff
            delay = TimeSpan.FromSeconds(Math.Min(
                delay.TotalSeconds * _settings.RetryBackoffFactor,
                _settings.MaxRetryDelaySeconds));
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
        if (!_settings.UseRetryJitter)
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
    private static bool IsValidCatalogUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Only allow HTTPS for security
        if (uri.Scheme != "https")
            return false;

        // Allow NuGet API domains and CDN endpoints
        var allowedHosts = new[] {
            "api.nuget.org",
            "api-v2v3search-0.nuget.org",
            "nuget.cdn.azure.cn"
        };

        return allowedHosts.Any(host =>
            uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith($".{host}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates an optimized HttpClient for NuGet API requests.
    /// </summary>
    /// <param name="settings">The application settings.</param>
    /// <returns>A configured HttpClient instance.</returns>
    private static HttpClient CreateOptimizedHttpClient(AppSettings settings)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            MaxConnectionsPerServer = settings.MaxConcurrentRequests
        };

        return new HttpClient(handler, disposeHandler: true);
    }

    /// <summary>
    /// Extracts the project URL from the package Metadata.
    /// </summary>
    /// <param name="meta">The package Metadata to update.</param>
    /// <param name="details">The details JSON element.</param>
    /// <param name="root">The root JSON element.</param>
    private static void ExtractProjectUrl(PackageMetaData meta, JsonElement details, JsonElement root)
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
    /// Extracts dependency groups from the package Metadata.
    /// </summary>
    /// <param name="meta">The package Metadata to update.</param>
    /// <param name="details">The details JSON element containing dependency information.</param>
    private static void ExtractDependencyGroups(PackageMetaData meta, JsonElement details)
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