using System.Text.Json;
using System.Text.Json.Serialization;

namespace NuGetInspectorApp.Configuration;

/// <summary>
/// Configuration settings for the NuGet Inspector application.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Gets or sets the base URL for NuGet API operations.
    /// </summary>
    /// <value>
    /// A string containing the base URL for NuGet API requests. Default is the compressed SemVer 2.0 endpoint.
    /// </value>
    /// <remarks>
    /// <para>The NuGet API supports multiple endpoint versions with different capabilities:</para>
    /// <list type="number">
    /// <item><description><c>https://api.nuget.org/v3/registration5-semver1/</c> - Basic registration (SemVer 1.0)</description></item>
    /// <item><description><c>https://api.nuget.org/v3/registration5-gz-semver2/</c> - <strong>Current working</strong> (compressed, SemVer 2.0)</description></item>
    /// <item><description><c>https://api.nuget.org/v3/registration5-semver2/</c> - SemVer 2.0 without compression</description></item>
    /// </list>
    /// <para>The compressed endpoint (gz-semver2) provides better performance for large-scale operations and is the recommended default.</para>
    /// <para>This setting can be overridden using the environment variable:</para>
    /// <c>NUGET_API_BASE_URL</c>
    /// </remarks>
    /// <example>
    /// <code>
    /// var settings = new AppSettings
    /// {
    ///     NuGetApiBaseUrl = "https://api.nuget.org/v3/registration5-gz-semver2"
    /// };
    /// // Or in configuration file:
    /// // "apiSettings": { "baseUrl": "https://api.nuget.org/v3/registration5-gz-semver2" }
    /// // Or via environment variable:
    /// // export NUGET_API_BASE_URL="https://api.nuget.org/v3/registration5-gz-semver2"
    /// </code>
    /// </example>
    public string NuGetApiBaseUrl { get; set; } = "https://api.nuget.org/v3/registration5-gz-semver2";

    /// <summary>
    /// Base URL for NuGet Gallery links.
    /// </summary>
    /// <remarks>
    /// <para>This setting can be overridden using the environment variable:</para>
    /// <c>NUGET_GALLERY_BASE_URL</c>
    /// </remarks>
    public string NuGetGalleryBaseUrl { get; set; } = "https://www.nuget.org/packages";

    /// <summary>
    /// Gets or sets the maximum number of concurrent HTTP requests allowed.
    /// </summary>
    /// <value>The maximum concurrent requests. Default is 5.</value>
    /// <remarks>
    /// <para>This setting can be overridden using the environment variable:</para>
    /// <c>NUGET_API_MAX_CONCURRENT_REQUESTS</c>
    /// </remarks>
    public int MaxConcurrentRequests { get; set; } = 5;

    /// <summary>
    /// Gets or sets the HTTP request timeout in seconds.
    /// </summary>
    /// <value>The HTTP timeout in seconds. Default is 30.</value>
    /// <remarks>
    /// <para>This setting can be overridden using the environment variable:</para>
    /// <c>HTTP_TIMEOUT_SECONDS</c>
    /// </remarks>
    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for failed HTTP requests.
    /// </summary>
    /// <value>The maximum retry attempts. Default is 3.</value>
    /// <remarks>
    /// <para>This setting can be overridden using the environment variable:</para>
    /// <c>MAX_RETRY_ATTEMPTS</c>
    /// </remarks>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the delay in seconds between retry attempts for failed HTTP requests.
    /// </summary>
    /// <value>The retry delay in seconds. Default is 2.</value>
    /// <remarks>
    /// <para>This setting can be overridden using the environment variable:</para>
    /// <c>RETRY_DELAY_SECONDS</c>
    /// </remarks>
    public double RetryDelaySeconds { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets the exponential backoff factor for retry delays.
    /// </summary>
    /// <value>The backoff multiplier applied to retry delays. Default is 2.0.</value>
    /// <remarks>
    /// <para>This setting can be overridden using the environment variable:</para>
    /// <c>RETRY_BACKOFF_FACTOR</c>
    /// </remarks>
    public double RetryBackoffFactor { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets the maximum retry delay in seconds.
    /// </summary>
    /// <value>The maximum delay between retries. Default is 30 seconds.</value>
    /// <remarks>
    /// <para>This setting can be overridden using the environment variable:</para>
    /// <c>MAX_RETRY_DELAY_SECONDS</c>
    /// </remarks>
    public int MaxRetryDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets a value indicating whether to use jitter in retry delays.
    /// </summary>
    /// <value><c>true</c> to add random jitter to retry delays; otherwise, <c>false</c>. Default is <c>true</c>.</value>
    /// <remarks>
    /// <para>This setting can be overridden using the environment variable:</para>
    /// <c>USE_RETRY_JITTER</c>
    /// </remarks>
    public bool UseRetryJitter { get; set; } = true;

    /// <summary>
    /// Loads settings from a .nugetinspector configuration file if it exists.
    /// Merges file-based configuration with default values.
    /// </summary>
    /// <param name="configPath">Optional path to a specific config file. If null, searches in standard locations.</param>
    /// <returns>An AppSettings instance with merged configuration values.</returns>
    public static AppSettings LoadFromConfigFile(string? configPath = null)
    {
        var settings = new AppSettings(); // Start with defaults

        // Default config file locations to check (in order of preference)
        var configPaths = new[]
        {
            configPath, // User-specified path
            ".nugetinspector", // Current directory
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nugetinspector"), // User home
            Path.Combine(Environment.CurrentDirectory, ".nugetinspector") // Current working directory
        }.Where(p => !string.IsNullOrEmpty(p) && File.Exists(p));

        var configFile = configPaths.FirstOrDefault();
        if (configFile == null)
        {
            // No config file found, Apply Environment Variables
            Console.WriteLine("No configuration file found. Using default and environment settings.");
            // Apply environment variables to defaults
            ApplyEnvironmentVariables(settings);
            return settings;
        }

        try
        {
            var json = File.ReadAllText(configFile);

            // Configure JSON options for robust parsing
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            var config = JsonSerializer.Deserialize<NuGetInspectorConfig>(json, options);

            if (config != null)
            {
                ApplyConfigurationSettings(settings, config);
                Console.WriteLine($"Loaded configuration from: {configFile}");
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Warning: Invalid JSON in config file '{configFile}': {ex.Message}");
            Console.WriteLine("Using default configuration values.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not load config file '{configFile}': {ex.Message}");
            Console.WriteLine("Using default configuration values.");
        }

        // Override with environment variables if present
        ApplyEnvironmentVariables(settings);

        return settings;
    }

    /// <summary>
    /// Applies environment variable overrides to the settings.
    /// </summary>
    private static void ApplyEnvironmentVariables(AppSettings settings)
    {
        // ✅ Complete all existing environment variables
        if (Environment.GetEnvironmentVariable("NUGET_API_BASE_URL") is string apiBaseUrl && !string.IsNullOrEmpty(apiBaseUrl))
            settings.NuGetApiBaseUrl = apiBaseUrl;

        if (Environment.GetEnvironmentVariable("NUGET_API_MAX_CONCURRENT_REQUESTS") is string maxConcurrent && int.TryParse(maxConcurrent, out var maxConcurrentValue))
            settings.MaxConcurrentRequests = maxConcurrentValue;

        if (Environment.GetEnvironmentVariable("HTTP_TIMEOUT_SECONDS") is string timeout && int.TryParse(timeout, out var timeoutValue))
            settings.HttpTimeoutSeconds = timeoutValue;

        if (Environment.GetEnvironmentVariable("MAX_RETRY_ATTEMPTS") is string retryAttempts && int.TryParse(retryAttempts, out var retryValue))
            settings.MaxRetryAttempts = retryValue;

        if (Environment.GetEnvironmentVariable("VERBOSE_LOGGING") is string verbose && bool.TryParse(verbose, out var verboseValue))
            settings.VerboseLogging = verboseValue;

        if (Environment.GetEnvironmentVariable("NUGET_GALLERY_BASE_URL") is string galleryUrl && !string.IsNullOrEmpty(galleryUrl))
            settings.NuGetGalleryBaseUrl = galleryUrl;

        if (Environment.GetEnvironmentVariable("DEFAULT_OUTPUT_FORMAT") is string format && !string.IsNullOrEmpty(format))
            settings.DefaultFormat = format;

        if (Environment.GetEnvironmentVariable("INCLUDE_TRANSITIVE") is string includeTransitive && bool.TryParse(includeTransitive, out var includeTransitiveValue))
            settings.IncludeTransitive = includeTransitiveValue;

        if (Environment.GetEnvironmentVariable("SHOW_DEPENDENCIES") is string showDeps && bool.TryParse(showDeps, out var showDepsValue))
            settings.ShowDependencies = showDepsValue;

        if (Environment.GetEnvironmentVariable("INCLUDE_PRERELEASE") is string includePrerelease && bool.TryParse(includePrerelease, out var prereleaseValue))
            settings.IncludePrerelease = prereleaseValue;

        if (Environment.GetEnvironmentVariable("MIN_SEVERITY") is string minSeverity && !string.IsNullOrEmpty(minSeverity))
            settings.MinSeverity = minSeverity;

        // ✅ Add missing retry configuration environment variables
        if (Environment.GetEnvironmentVariable("RETRY_DELAY_SECONDS") is string retryDelay && double.TryParse(retryDelay, out var retryDelayValue))
            settings.RetryDelaySeconds = retryDelayValue;

        if (Environment.GetEnvironmentVariable("RETRY_BACKOFF_FACTOR") is string backoffFactor && double.TryParse(backoffFactor, out var backoffValue))
            settings.RetryBackoffFactor = backoffValue;

        if (Environment.GetEnvironmentVariable("MAX_RETRY_DELAY_SECONDS") is string maxRetryDelay && int.TryParse(maxRetryDelay, out var maxRetryDelayValue))
            settings.MaxRetryDelaySeconds = maxRetryDelayValue;

        if (Environment.GetEnvironmentVariable("USE_RETRY_JITTER") is string useJitter && bool.TryParse(useJitter, out var jitterValue))
            settings.UseRetryJitter = jitterValue;

        // ✅ Add package exclusions from environment (comma-separated)
        if (Environment.GetEnvironmentVariable("EXCLUDE_PACKAGES") is string excludePackages && !string.IsNullOrEmpty(excludePackages))
        {
            var packages = excludePackages.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                        .Select(p => p.Trim())
                                        .Where(p => !string.IsNullOrEmpty(p))
                                        .ToList();
            if (packages.Any())
                settings.ExcludePackages = packages;
        }

        // Gallery URL
        if (Environment.GetEnvironmentVariable("NUGET_GALLERY_BASE_URL") is string nugetGalleryUrl && !string.IsNullOrEmpty(nugetGalleryUrl))
            settings.NuGetGalleryBaseUrl = nugetGalleryUrl;

        // Retry configuration
        if (Environment.GetEnvironmentVariable("RETRY_DELAY_SECONDS") is string retryDelaySeconds && double.TryParse(retryDelaySeconds, out var retryDelaySecondsValue))
            settings.RetryDelaySeconds = retryDelaySecondsValue;

        if (Environment.GetEnvironmentVariable("RETRY_BACKOFF_FACTOR") is string retryBackoffFactor && double.TryParse(retryBackoffFactor, out var retryBackoffFactorValue))
            settings.RetryBackoffFactor = retryBackoffFactorValue;

        if (Environment.GetEnvironmentVariable("MAX_RETRY_DELAY_SECONDS") is string maxRetryDelaySeconds && int.TryParse(maxRetryDelaySeconds, out var maxRetryDelaySecondsValue))
            settings.MaxRetryDelaySeconds = maxRetryDelaySecondsValue;

        if (Environment.GetEnvironmentVariable("USE_RETRY_JITTER") is string useRetryJitter && bool.TryParse(useRetryJitter, out var retryJitterValue))
            settings.UseRetryJitter = retryJitterValue;

        // Compression setting
        if (Environment.GetEnvironmentVariable("USE_COMPRESSION") is string useCompression && bool.TryParse(useCompression, out var compressionValue))
            settings.UseCompression = compressionValue;

        // Report settings
        if (Environment.GetEnvironmentVariable("GROUP_BY_FRAMEWORK") is string groupByFramework && bool.TryParse(groupByFramework, out var groupValue))
            settings.GroupByFramework = groupValue;

        if (Environment.GetEnvironmentVariable("SORT_BY_NAME") is string sortByName && bool.TryParse(sortByName, out var sortValue))
            settings.SortByName = sortValue;

        if (Environment.GetEnvironmentVariable("SHOW_OUTDATED_ONLY") is string showOutdated && bool.TryParse(showOutdated, out var outdatedValue))
            settings.ShowOutdatedOnly = outdatedValue;

        if (Environment.GetEnvironmentVariable("SHOW_VULNERABLE_ONLY") is string showVuln && bool.TryParse(showVuln, out var vulnValue))
            settings.ShowVulnerableOnly = vulnValue;

        if (Environment.GetEnvironmentVariable("SHOW_DEPRECATED_ONLY") is string showDeprecated && bool.TryParse(showDeprecated, out var deprecatedValue))
            settings.ShowDeprecatedOnly = deprecatedValue;
    }

    /// <summary>
    /// Applies configuration from the parsed config file to the AppSettings instance.
    /// </summary>
    private static void ApplyConfigurationSettings(AppSettings settings, NuGetInspectorConfig config)
    {
        // Apply API settings
        if (config.ApiSettings is { } apiSettings)
        {
            if (!string.IsNullOrEmpty(apiSettings.BaseUrl))
                settings.NuGetApiBaseUrl = apiSettings.BaseUrl;

            if (!string.IsNullOrEmpty(apiSettings.GalleryUrl))
                settings.NuGetGalleryBaseUrl = apiSettings.GalleryUrl;

            if (apiSettings.Timeout > 0)
                settings.HttpTimeoutSeconds = apiSettings.Timeout;

            if (apiSettings.MaxConcurrentRequests > 0)
                settings.MaxConcurrentRequests = apiSettings.MaxConcurrentRequests;

            if (apiSettings.RetryAttempts >= 0)
                settings.MaxRetryAttempts = apiSettings.RetryAttempts;

            // ✅ Add missing retry configuration mappings
            if (apiSettings.RetryDelaySeconds > 0)
                settings.RetryDelaySeconds = apiSettings.RetryDelaySeconds;

            if (apiSettings.RetryBackoffFactor > 0)
                settings.RetryBackoffFactor = apiSettings.RetryBackoffFactor;

            if (apiSettings.MaxRetryDelaySeconds > 0)
                settings.MaxRetryDelaySeconds = apiSettings.MaxRetryDelaySeconds;

            settings.UseRetryJitter = apiSettings.UseRetryJitter;
            settings.UseCompression = apiSettings.UseCompression;
        }

        // Apply output settings
        if (config.OutputSettings is { } outputSettings)
        {
            if (!string.IsNullOrEmpty(outputSettings.DefaultFormat))
                settings.DefaultFormat = outputSettings.DefaultFormat;

            settings.VerboseLogging = outputSettings.VerboseLogging;
            settings.IncludeTransitive = outputSettings.IncludeTransitive;
            settings.ShowDependencies = outputSettings.ShowDependencies;
        }

        // Apply filter settings
        if (config.FilterSettings is { } filterSettings)
        {
            if (filterSettings.ExcludePackages?.Count > 0)
                settings.ExcludePackages = new List<string>(filterSettings.ExcludePackages);

            settings.IncludePrerelease = filterSettings.IncludePrerelease;

            if (!string.IsNullOrEmpty(filterSettings.MinSeverity))
                settings.MinSeverity = filterSettings.MinSeverity;
        }

        // Apply report settings mapping
        if (config.ReportSettings is { } reportSettings)
        {
            settings.ShowOutdatedOnly = reportSettings.ShowOutdatedOnly;
            settings.ShowVulnerableOnly = reportSettings.ShowVulnerableOnly;
            settings.ShowDeprecatedOnly = reportSettings.ShowDeprecatedOnly;
            settings.GroupByFramework = reportSettings.GroupByFramework;
            settings.SortByName = reportSettings.SortByName;
        }
    }

    /// <summary>
    /// Validates the configuration settings and throws exceptions if any values are out of range.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a value is out of the allowed range.</exception>
    /// <exception cref="ArgumentException">Thrown when a URL is invalid.</exception>
    public void Validate()
    {
        if (HttpTimeoutSeconds <= 0 || HttpTimeoutSeconds > 300)
            throw new ArgumentOutOfRangeException(nameof(HttpTimeoutSeconds), "Must be between 1 and 300 seconds");

        if (MaxConcurrentRequests <= 0 || MaxConcurrentRequests > 20)
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrentRequests), "Must be between 1 and 20");

        if (MaxRetryAttempts < 0 || MaxRetryAttempts > 10)
            throw new ArgumentOutOfRangeException(nameof(MaxRetryAttempts), "Must be between 0 and 10");

        if (RetryDelaySeconds <= 0 || RetryDelaySeconds > 60)
            throw new ArgumentOutOfRangeException(nameof(RetryDelaySeconds), "Must be between 1 and 60 seconds");

        if (RetryBackoffFactor < 1.0 || RetryBackoffFactor > 5.0)
            throw new ArgumentOutOfRangeException(nameof(RetryBackoffFactor), "Must be between 1.0 and 5.0");

        if (MaxRetryDelaySeconds <= 0 || MaxRetryDelaySeconds > 300)
            throw new ArgumentOutOfRangeException(nameof(MaxRetryDelaySeconds), "Must be between 1 and 300 seconds");

        // ✅ Validation for MinSeverity
        var validSeverities = new[] { "low", "medium", "high", "critical" };
        if (!validSeverities.Contains(MinSeverity.ToLowerInvariant()))
            throw new ArgumentException($"MinSeverity must be one of: {string.Join(", ", validSeverities)}", nameof(MinSeverity));

        // ✅ Validation for DefaultFormat
        var validFormats = new[] { "console", "html", "markdown", "json" };
        if (!validFormats.Contains(DefaultFormat.ToLowerInvariant()))
            throw new ArgumentException($"DefaultFormat must be one of: {string.Join(", ", validFormats)}", nameof(DefaultFormat));

        // Validate URLs
        if (!Uri.TryCreate(NuGetApiBaseUrl, UriKind.Absolute, out var apiUri) ||
            (apiUri.Scheme != "https" && apiUri.Scheme != "http"))
            throw new ArgumentException("NuGetApiBaseUrl must be a valid HTTP/HTTPS URL", nameof(NuGetApiBaseUrl));

        if (!Uri.TryCreate(NuGetGalleryBaseUrl, UriKind.Absolute, out var galleryUri) ||
            (galleryUri.Scheme != "https" && galleryUri.Scheme != "http"))
            throw new ArgumentException("NuGetGalleryBaseUrl must be a valid HTTP/HTTPS URL", nameof(NuGetGalleryBaseUrl));
    }

    /// <summary>
    /// Gets or sets a value indicating whether verbose logging is enabled.
    /// </summary>
    /// <value><c>true</c> if verbose logging is enabled; otherwise, <c>false</c>. Default is <c>false</c>.</value>
    /// <remarks>
    /// <para>When enabled, the application will output detailed logging information including:</para>
    /// <list type="bullet">
    /// <item><description>HTTP request and response details</description></item>
    /// <item><description>Package merging and analysis operations</description></item>
    /// <item><description>Performance timing information</description></item>
    /// <item><description>Error details and stack traces</description></item>
    /// <item><description>Configuration values being used</description></item>
    /// </list>
    /// <para>Verbose logging is useful for:</para>
    /// <list type="bullet">
    /// <item><description>Troubleshooting issues</description></item>
    /// <item><description>Understanding application behavior</description></item>
    /// <item><description>Performance analysis</description></item>
    /// <item><description>Development and debugging</description></item>
    /// </list>
    /// <para>Note: Verbose logging can significantly increase output volume and may impact performance.</para>
    /// <para>This setting can be overridden using the environment variable:</para>
    /// <c>VERBOSE_LOGGING</c>
    /// </remarks>
    /// <example>
    /// <code>
    /// var settings = new AppSettings { VerboseLogging = true };
    /// // Or in configuration file:
    /// // "outputSettings": { "verboseLogging": true }
    /// // Or via environment variable:
    /// // export VERBOSE_LOGGING=true
    /// </code>
    /// </example>
    public bool VerboseLogging { get; set; } = false;

    /// <summary>
    /// Gets or sets the default output format for reports.
    /// </summary>
    /// <value>
    /// A string specifying the output format. Default is "console".
    /// Valid values are: "console", "html", "markdown", "json".
    /// </value>
    /// <remarks>
    /// <para>This setting determines the default format for generated reports. Users can override
    /// this setting using command-line options for individual runs.</para>
    /// <para>Available formats:</para>
    /// <list type="bullet">
    /// <item><description><c>console</c> - Human-readable text output suitable for terminal display</description></item>
    /// <item><description><c>html</c> - Rich HTML format with styling and interactive elements (planned)</description></item>
    /// <item><description><c>markdown</c> - Markdown format suitable for documentation (planned)</description></item>
    /// <item><description><c>json</c> - Machine-readable JSON format for automation (planned)</description></item>
    /// </list>
    /// <para>Currently, only the "console" format is implemented.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var settings = new AppSettings { DefaultFormat = "console" };
    /// // Or in configuration file:
    /// // "outputSettings": { "defaultFormat": "console" }
    /// </code>
    /// </example>
    public string DefaultFormat { get; set; } = "console";

    /// <summary>
    /// Gets or sets a value indicating whether to include transitive packages in reports.
    /// </summary>
    /// <value>
    /// <c>true</c> to include transitive packages; otherwise, <c>false</c>. Default is <c>true</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, the application will include transitive (indirect) package dependencies
    /// in the analysis report. Transitive packages are dependencies of your direct dependencies
    /// that are automatically resolved by the package manager.</para>
    /// <para>Including transitive packages provides a complete view of your dependency tree but
    /// can make reports significantly longer for solutions with many dependencies.</para>
    /// <para>Disable this setting when:</para>
    /// <list type="bullet">
    /// <item><description>You only want to focus on directly referenced packages</description></item>
    /// <item><description>Reports are too verbose for practical use</description></item>
    /// <item><description>Performance is a concern for very large solutions</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var settings = new AppSettings { IncludeTransitive = false };
    /// // Or in configuration file:
    /// // "outputSettings": { "includeTransitive": false }
    /// </code>
    /// </example>
    public bool IncludeTransitive { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to show package dependency information.
    /// </summary>
    /// <value>
    /// <c>true</c> to show dependency information; otherwise, <c>false</c>. Default is <c>true</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, the application will display detailed dependency information for each package,
    /// including the specific dependencies required for each target framework.</para>
    /// <para>This information is useful for:</para>
    /// <list type="bullet">
    /// <item><description>Understanding the complete dependency chain</description></item>
    /// <item><description>Identifying framework-specific dependencies</description></item>
    /// <item><description>Troubleshooting dependency conflicts</description></item>
    /// <item><description>Planning package updates and migrations</description></item>
    /// </list>
    /// <para>Disable this setting to reduce report verbosity when dependency details are not needed.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var settings = new AppSettings { ShowDependencies = true };
    /// // Or in configuration file:
    /// // "outputSettings": { "showDependencies": true }
    /// </code>
    /// </example>
    public bool ShowDependencies { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to show only outdated packages in reports.
    /// </summary>
    /// <value>
    /// <c>true</c> to show only outdated packages; otherwise, <c>false</c>. Default is <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, the report will only include packages that have newer versions available.
    /// This filtering option is equivalent to using the <c>--only-outdated</c> command-line flag
    /// and helps focus on packages that may need updates.</para>
    /// <para>This filter is useful for:</para>
    /// <list type="bullet">
    /// <item><description>Maintenance planning and update prioritization</description></item>
    /// <item><description>Identifying packages that may have security fixes</description></item>
    /// <item><description>Keeping dependencies current with latest features</description></item>
    /// <item><description>Reducing report size when only updates are of interest</description></item>
    /// </list>
    /// <para>Note: This setting cannot be combined with <see cref="ShowVulnerableOnly"/> or
    /// <see cref="ShowDeprecatedOnly"/> - only one filter type can be active at a time.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var settings = new AppSettings { ShowOutdatedOnly = true };
    /// // Or in configuration file:
    /// // "reportSettings": { "showOutdatedOnly": true }
    /// </code>
    /// </example>
    public bool ShowOutdatedOnly { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to show only vulnerable packages in reports.
    /// </summary>
    /// <value>
    /// <c>true</c> to show only vulnerable packages; otherwise, <c>false</c>. Default is <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, the report will only include packages that have known security vulnerabilities.
    /// This filtering option is equivalent to using the <c>--only-vulnerable</c> command-line flag
    /// and helps focus on packages that pose security risks.</para>
    /// <para>This filter is essential for:</para>
    /// <list type="bullet">
    /// <item><description>Security audits and compliance reporting</description></item>
    /// <item><description>Prioritizing critical security updates</description></item>
    /// <item><description>Identifying immediate security risks</description></item>
    /// <item><description>Meeting security policy requirements</description></item>
    /// </list>
    /// <para>Use in combination with <see cref="MinSeverity"/> to focus on
    /// vulnerabilities of specific severity levels.</para>
    /// <para>Note: This setting cannot be combined with <see cref="ShowOutdatedOnly"/> or
    /// <see cref="ShowDeprecatedOnly"/> - only one filter type can be active at a time.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var settings = new AppSettings { ShowVulnerableOnly = true };
    /// // Or in configuration file:
    /// // "reportSettings": { "showVulnerableOnly": true }
    /// </code>
    /// </example>
    public bool ShowVulnerableOnly { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to show only deprecated packages in reports.
    /// </summary>
    /// <value>
    /// <c>true</c> to show only deprecated packages; otherwise, <c>false</c>. Default is <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, the report will only include packages that have been marked as deprecated
    /// by their maintainers. This filtering option is equivalent to using the <c>--only-deprecated</c>
    /// command-line flag and helps identify packages that should be replaced.</para>
    /// <para>This filter is important for:</para>
    /// <list type="bullet">
    /// <item><description>Migration planning and technical debt management</description></item>
    /// <item><description>Ensuring long-term maintainability of projects</description></item>
    /// <item><description>Identifying packages that may stop receiving updates</description></item>
    /// <item><description>Planning transitions to recommended alternatives</description></item>
    /// </list>
    /// <para>Deprecated packages often include information about recommended alternatives,
    /// which will be displayed in the report to guide migration efforts.</para>
    /// <para>Note: This setting cannot be combined with <see cref="ShowOutdatedOnly"/> or
    /// <see cref="ShowVulnerableOnly"/> - only one filter type can be active at a time.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var settings = new AppSettings { ShowDeprecatedOnly = true };
    /// // Or in configuration file:
    /// // "reportSettings": { "showDeprecatedOnly": true }
    /// </code>
    /// </example>
    public bool ShowDeprecatedOnly { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to include pre-release packages in analysis.
    /// </summary>
    /// <value>
    /// <c>true</c> to include pre-release packages; otherwise, <c>false</c>. Default is <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, the application will consider pre-release package versions when checking
    /// for updates and analyzing package status. Pre-release versions include alpha, beta, release
    /// candidate, and other non-stable versions.</para>
    /// <para>Enable this setting when:</para>
    /// <list type="bullet">
    /// <item><description>Your project uses pre-release packages</description></item>
    /// <item><description>You want to see the latest features and fixes</description></item>
    /// <item><description>You're working on experimental or development projects</description></item>
    /// </list>
    /// <para>Disable this setting for production environments where stability is prioritized over
    /// having the latest features.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var settings = new AppSettings { IncludePrerelease = false };
    /// // Or in configuration file:
    /// // "filterSettings": { "includePrerelease": false }
    /// </code>
    /// </example>
    public bool IncludePrerelease { get; set; } = false;

    /// <summary>
    /// Gets or sets the list of package IDs to exclude from analysis.
    /// </summary>
    /// <value>
    /// A list of package ID strings to exclude. Default is an empty list.
    /// </value>
    /// <remarks>
    /// <para>This setting allows you to specify packages that should be completely excluded from
    /// analysis and reporting. This is useful for packages that:</para>
    /// <list type="bullet">
    /// <item><description>Are development-only dependencies (e.g., test frameworks, analyzers)</description></item>
    /// <item><description>Have known issues that cannot be addressed immediately</description></item>
    /// <item><description>Are internal company packages not relevant for security analysis</description></item>
    /// <item><description>Generate noise in reports due to their nature</description></item>
    /// </list>
    /// <para>Package IDs should be specified exactly as they appear in project files (case-sensitive).
    /// The exclusion applies to both direct and transitive references.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var settings = new AppSettings
    /// {
    ///     ExcludePackages = new List&lt;string&gt;
    ///     {
    ///         "Microsoft.NET.Test.Sdk",
    ///         "coverlet.collector",
    ///         "NUnit",
    ///         "CompanyName.InternalLibrary"
    ///     }
    /// };
    /// // Or in configuration file:
    /// // "filterSettings": {
    /// //   "excludePackages": [
    /// //     "Microsoft.NET.Test.Sdk",
    /// //     "coverlet.collector"
    /// //   ]
    /// // }
    /// </code>
    /// </example>
    public List<string> ExcludePackages { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the minimum vulnerability severity level to report.
    /// </summary>
    /// <value>
    /// A string representing the minimum severity level. Default is "low".
    /// Valid values are: "low", "medium", "high", "critical" (case-insensitive).
    /// </value>
    /// <remarks>
    /// <para>This setting filters vulnerability reports to only show vulnerabilities at or above
    /// the specified severity level. This helps focus attention on the most critical security issues.</para>
    /// <para>Severity levels (in ascending order of severity):</para>
    /// <list type="bullet">
    /// <item><description><c>low</c> - Minor security issues with limited impact</description></item>
    /// <item><description><c>medium</c> - Moderate security issues that should be addressed</description></item>
    /// <item><description><c>high</c> - Serious security issues requiring prompt attention</description></item>
    /// <item><description><c>critical</c> - Severe security issues requiring immediate action</description></item>
    /// </list>
    /// <para>Setting this to "high" will only show High and Critical vulnerabilities, effectively
    /// filtering out Low and Medium severity issues.</para>
    /// <para>The comparison is case-insensitive, so "Low", "LOW", and "low" are all equivalent.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var settings = new AppSettings { MinSeverity = "medium" };
    /// // Or in configuration file:
    /// // "filterSettings": { "minSeverity": "Medium" }
    /// </code>
    /// </example>
    public string MinSeverity { get; set; } = "low";

    /// <summary>
    /// Gets or sets a value indicating whether to use compression for HTTP requests.
    /// </summary>
    public bool UseCompression { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to group packages by framework in reports.
    /// </summary>
    public bool GroupByFramework { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to sort packages alphabetically by name.
    /// </summary>
    public bool SortByName { get; set; } = false;
}

/// <summary>
/// Represents command line options for the NuGet Inspector application.
/// </summary>
public class CommandLineOptions
{
    /// <summary>
    /// Gets or sets the path to the solution file to analyze.
    /// </summary>
    /// <value>The solution file path.</value>
    public string SolutionPath { get; set; } = "";

    /// <summary>
    /// Gets or sets the output format for the report.
    /// </summary>
    /// <value>The output format. Supported values: console, html, markdown, json. Default is "console".</value>
    public string OutputFormat { get; set; } = "console";

    /// <summary>
    /// Gets or sets the output file path for saving the report.
    /// </summary>
    /// <value>The output file path, or <c>null</c> to output to console.</value>
    public string? OutputFile { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether verbose output is enabled.
    /// </summary>
    /// <value><c>true</c> if verbose output is enabled; otherwise, <c>false</c>. Default is <c>false</c>.</value>
    public bool VerboseOutput { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to show only outdated packages.
    /// </summary>
    /// <value><c>true</c> to show only outdated packages; otherwise, <c>false</c>. Default is <c>false</c>.</value>
    public bool OnlyOutdated { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to show only vulnerable packages.
    /// </summary>
    /// <value><c>true</c> to show only vulnerable packages; otherwise, <c>false</c>. Default is <c>false</c>.</value>
    public bool OnlyVulnerable { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to show only deprecated packages.
    /// </summary>
    /// <value><c>true</c> to show only deprecated packages; otherwise, <c>false</c>. Default is <c>false</c>.</value>
    public bool OnlyDeprecated { get; set; } = false;

    /// <summary>
    /// Validates the command line options.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    public void Validate()
    {
        // Enhanced validation with security checks
        if (string.IsNullOrWhiteSpace(SolutionPath))
            throw new ArgumentException("Solution path cannot be null or empty", nameof(SolutionPath));

        // Check for path traversal attempts
        if (SolutionPath.Contains("..") || SolutionPath.Contains("~"))
            throw new ArgumentException("Solution path cannot contain path traversal sequences", nameof(SolutionPath));

        // Validate file extension
        if (!SolutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Solution path must point to a .sln file", nameof(SolutionPath));

        // Check for invalid characters
        if (Path.GetInvalidPathChars().Any(SolutionPath.Contains))
            throw new ArgumentException("Solution path contains invalid characters", nameof(SolutionPath));

        // Validate output format
        var validFormats = new[] { "console", "html", "markdown", "json" };
        if (!validFormats.Contains(OutputFormat.ToLowerInvariant()))
            throw new ArgumentException($"Output format must be one of: {string.Join(", ", validFormats)}", nameof(OutputFormat));

        // Validate output file if specified
        if (!string.IsNullOrEmpty(OutputFile))
        {
            if (Path.GetInvalidPathChars().Any(OutputFile.Contains))
                throw new ArgumentException("Output file path contains invalid characters", nameof(OutputFile));

            if (OutputFile.Contains("..") || OutputFile.Contains("~"))
                throw new ArgumentException("Output file path cannot contain path traversal sequences", nameof(OutputFile));

            var outputDir = Path.GetDirectoryName(OutputFile);
            if (outputDir != null && !Directory.Exists(outputDir))
                throw new ArgumentException($"Output directory does not exist: {outputDir}", nameof(OutputFile));
        }
    }
}

/// <summary>
/// Root configuration object for .nugetinspector files.
/// </summary>
/// <remarks>
/// This class represents the top-level structure of the NuGet Inspector configuration file format.
/// Configuration files should reference the schema for validation:
/// <c>"$schema": "./nugetinspector-schema.json"</c>
///
/// For complete schema documentation, see: nugetinspector-schema.json
/// </remarks>
/// <example>
/// Example configuration file structure:
/// <code>
/// {
///   "apiSettings": {
///     "baseUrl": "https://api.nuget.org/v3/registration5-gz-semver2",
///     "timeout": 30
///   },
///   "outputSettings": {
///     "verboseLogging": true
///   }
/// }
/// </code>
/// </example>
public class NuGetInspectorConfig
{
    /// <summary>
    /// Gets or sets the API-related configuration settings.
    /// </summary>
    /// <value>
    /// An <see cref="ApiSettings"/> object containing NuGet API connection parameters,
    /// or <c>null</c> if no API settings are specified.
    /// </value>
    /// <remarks>
    /// These settings control how the application connects to and interacts with the NuGet API,
    /// including timeout values, retry logic, and concurrency limits.
    /// </remarks>
    [JsonPropertyName("apiSettings")]
    public ApiSettings? ApiSettings { get; set; }

    /// <summary>
    /// Gets or sets the output formatting configuration settings.
    /// </summary>
    /// <value>
    /// An <see cref="OutputSettings"/> object containing output format preferences,
    /// or <c>null</c> if no output settings are specified.
    /// </value>
    /// <remarks>
    /// These settings determine how analysis results are formatted and displayed,
    /// including logging verbosity and report format options.
    /// </remarks>
    [JsonPropertyName("outputSettings")]
    public OutputSettings? OutputSettings { get; set; }

    /// <summary>
    /// Gets or sets the package filtering configuration settings.
    /// </summary>
    /// <value>
    /// A <see cref="FilterSettings"/> object containing package filtering criteria,
    /// or <c>null</c> if no filter settings are specified.
    /// </value>
    /// <remarks>
    /// These settings control which packages are included or excluded from analysis,
    /// such as excluding test packages or setting minimum vulnerability severity levels.
    /// </remarks>
    [JsonPropertyName("filterSettings")]
    public FilterSettings? FilterSettings { get; set; }

    /// <summary>
    /// Gets or sets the report generation configuration settings.
    /// </summary>
    /// <value>
    /// A <see cref="ReportSettings"/> object containing report generation preferences,
    /// or <c>null</c> if no report settings are specified.
    /// </value>
    /// <remarks>
    /// These settings control the structure and content of generated reports,
    /// including grouping options and filtering for specific package types.
    /// </remarks>
    [JsonPropertyName("reportSettings")]
    public ReportSettings? ReportSettings { get; set; }
}

/// <summary>
/// Represents API-related configuration settings for NuGet API interactions.
/// </summary>
/// <remarks>
/// This class contains settings that control how the application communicates with the NuGet API,
/// including connection parameters, retry logic, and performance optimizations. These settings
/// directly affect the reliability and performance of package metadata retrieval operations.
/// </remarks>
public class ApiSettings
{
    /// <summary>
    /// Gets or sets the base URL for NuGet API operations.
    /// </summary>
    /// <value>
    /// A string containing the base URL for NuGet API requests, or <c>null</c> to use the default.
    /// </value>
    /// <remarks>
    /// <para>The NuGet API supports multiple endpoint versions with different capabilities:</para>
    /// <list type="bullet">
    /// <item><description><c>https://api.nuget.org/v3/registration5-gz-semver2</c> - Recommended (compressed, SemVer 2.0)</description></item>
    /// <item><description><c>https://api.nuget.org/v3/registration5-semver2</c> - Uncompressed SemVer 2.0</description></item>
    /// <item><description><c>https://api.nuget.org/v3/registration5-semver1</c> - Legacy SemVer 1.0</description></item>
    /// </list>
    /// <para>The compressed endpoint (gz-semver2) provides better performance for large-scale operations.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// "apiSettings": {
    ///   "baseUrl": "https://api.nuget.org/v3/registration5-gz-semver2"
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the base URL for NuGet Gallery package links.
    /// </summary>
    /// <value>
    /// A string containing the base URL for generating package gallery links, or <c>null</c> to use the default.
    /// </value>
    /// <remarks>
    /// This URL is used to construct direct links to package pages on the NuGet Gallery website.
    /// The application appends the package ID and version to this base URL to create complete package URLs.
    /// </remarks>
    /// <example>
    /// <code>
    /// "apiSettings": {
    ///   "galleryUrl": "https://www.nuget.org/packages"
    /// }
    /// </code>
    /// Results in URLs like: <c>https://www.nuget.org/packages/Microsoft.Extensions.Logging/8.0.0</c>
    /// </example>
    [JsonPropertyName("galleryUrl")]
    public string? GalleryUrl { get; set; }

    /// <summary>
    /// Gets or sets the HTTP request timeout in seconds.
    /// </summary>
    /// <value>
    /// An integer representing the timeout duration in seconds. Must be between 5 and 300 seconds.
    /// </value>
    /// <remarks>
    /// <para>This setting controls how long the application waits for individual HTTP requests to complete
    /// before timing out. Longer timeouts may be necessary for slow network connections or when
    /// the NuGet API is under heavy load.</para>
    /// <para>Recommended values:</para>
    /// <list type="bullet">
    /// <item><description>Fast networks: 15-30 seconds</description></item>
    /// <item><description>Slow networks: 45-60 seconds</description></item>
    /// <item><description>CI/CD environments: 60-120 seconds</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// "apiSettings": {
    ///   "timeout": 45
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("timeout")]
    public int Timeout { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent HTTP requests.
    /// </summary>
    /// <value>
    /// An integer representing the maximum concurrent requests. Must be between 1 and 20.
    /// </value>
    /// <remarks>
    /// <para>This setting controls the level of parallelism when fetching package metadata from the NuGet API.
    /// Higher values can improve performance for large solutions but may trigger rate limiting or
    /// overwhelm slower network connections.</para>
    /// <para>Recommended values based on context:</para>
    /// <list type="bullet">
    /// <item><description>Interactive use: 3-5 requests</description></item>
    /// <item><description>CI/CD pipelines: 5-10 requests</description></item>
    /// <item><description>Batch processing: 8-15 requests</description></item>
    /// </list>
    /// <para>Note: The NuGet API may implement rate limiting that could affect performance with higher values.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// "apiSettings": {
    ///   "maxConcurrentRequests": 8
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("maxConcurrentRequests")]
    public int MaxConcurrentRequests { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for failed HTTP requests.
    /// </summary>
    /// <value>
    /// An integer representing the maximum retry attempts. Must be between 0 and 10.
    /// </value>
    /// <remarks>
    /// <para>This setting determines how many times the application will retry failed HTTP requests
    /// before giving up. The retry logic uses exponential backoff with jitter to avoid overwhelming
    /// the server and to handle temporary network issues gracefully.</para>
    /// <para>Retryable conditions include:</para>
    /// <list type="bullet">
    /// <item><description>Network timeouts</description></item>
    /// <item><description>HTTP 429 (Too Many Requests)</description></item>
    /// <item><description>HTTP 500-series server errors</description></item>
    /// <item><description>Connection failures</description></item>
    /// </list>
    /// <para>Setting this to 0 disables retry logic entirely.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// "apiSettings": {
    ///   "retryAttempts": 5
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("retryAttempts")]

    public int RetryAttempts { get; set; }

    /// <summary>
    /// Gets or sets the delay in seconds between retry attempts.
    /// </summary>
    [JsonPropertyName("retryDelaySeconds")]
    public double RetryDelaySeconds { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets the exponential backoff factor for retry delays.
    /// </summary>
    [JsonPropertyName("retryBackoffFactor")]
    public double RetryBackoffFactor { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets the maximum retry delay in seconds.
    /// </summary>
    [JsonPropertyName("maxRetryDelaySeconds")]
    public int MaxRetryDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets whether to use jitter in retry delays.
    /// </summary>
    [JsonPropertyName("useRetryJitter")]
    public bool UseRetryJitter { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to use HTTP compression for requests.
    /// </summary>
    /// <value>
    /// <c>true</c> to enable HTTP compression (gzip/deflate); otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, this setting requests compressed responses from the NuGet API,
    /// which can significantly reduce bandwidth usage and improve performance, especially
    /// for large package metadata responses.</para>
    /// <para>This setting should generally be enabled unless:</para>
    /// <list type="bullet">
    /// <item><description>Working with very limited CPU resources</description></item>
    /// <item><description>Debugging HTTP traffic and need uncompressed responses</description></item>
    /// <item><description>Using a proxy that doesn't handle compression correctly</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// "apiSettings": {
    ///   "useCompression": true
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("useCompression")]
    public bool UseCompression { get; set; } = true;
}

/// <summary>
/// Represents output formatting configuration settings.
/// </summary>
/// <remarks>
/// This class contains settings that control how analysis results are formatted and displayed
/// to the user, including output format selection, logging verbosity, and content inclusion options.
/// These settings affect the user experience and the amount of information presented in reports.
/// </remarks>
public class OutputSettings
{
    /// <summary>
    /// Gets or sets the default output format for reports.
    /// </summary>
    /// <value>
    /// A string specifying the output format, or <c>null</c> to use the application default.
    /// Valid values are: "console", "html", "markdown", "json".
    /// </value>
    /// <remarks>
    /// <para>This setting determines the default format for generated reports. Users can override
    /// this setting using command-line options for individual runs.</para>
    /// <para>Available formats:</para>
    /// <list type="bullet">
    /// <item><description><c>console</c> - Human-readable text output suitable for terminal display</description></item>
    /// <item><description><c>html</c> - Rich HTML format with styling and interactive elements (planned)</description></item>
    /// <item><description><c>markdown</c> - Markdown format suitable for documentation (planned)</description></item>
    /// <item><description><c>json</c> - Machine-readable JSON format for automation (planned)</description></item>
    /// </list>
    /// <para>Currently, only the "console" format is implemented.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// "outputSettings": {
    ///   "defaultFormat": "console"
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("defaultFormat")]
    public string? DefaultFormat { get; set; } = "console";

    /// <summary>
    /// Gets or sets a value indicating whether to include transitive packages in reports.
    /// </summary>
    /// <value>
    /// <c>true</c> to include transitive packages; otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, the application will include transitive (indirect) package dependencies
    /// in the analysis report. Transitive packages are dependencies of your direct dependencies
    /// that are automatically resolved by the package manager.</para>
    /// <para>Including transitive packages provides a complete view of your dependency tree but
    /// can make reports significantly longer for solutions with many dependencies.</para>
    /// <para>Disable this setting when:</para>
    /// <list type="bullet">
    /// <item><description>You only want to focus on directly referenced packages</description></item>
    /// <item><description>Reports are too verbose for practical use</description></item>
    /// <item><description>Performance is a concern for very large solutions</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// "outputSettings": {
    ///   "includeTransitive": false
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("includeTransitive")]
    public bool IncludeTransitive { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to show package dependency information.
    /// </summary>
    /// <value>
    /// <c>true</c> to show dependency information; otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, the application will display detailed dependency information for each package,
    /// including the specific dependencies required for each target framework.</para>
    /// <para>This information is useful for:</para>
    /// <list type="bullet">
    /// <item><description>Understanding the complete dependency chain</description></item>
    /// <item><description>Identifying framework-specific dependencies</description></item>
    /// <item><description>Troubleshooting dependency conflicts</description></item>
    /// <item><description>Planning package updates and migrations</description></item>
    /// </list>
    /// <para>Disable this setting to reduce report verbosity when dependency details are not needed.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// "outputSettings": {
    ///   "showDependencies": true
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("showDependencies")]
    public bool ShowDependencies { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable verbose logging output.
    /// </summary>
    /// <value>
    /// <c>true</c> to enable verbose logging; otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, the application will output detailed logging information including:</para>
    /// <list type="bullet">
    /// <item><description>HTTP request and response details</description></item>
    /// <item><description>Package merging and analysis operations</description></item>
    /// <item><description>Performance timing information</description></item>
    /// <item><description>Error details and stack traces</description></item>
    /// <item><description>Configuration values being used</description></item>
    /// </list>
    /// <para>Verbose logging is useful for:</para>
    /// <list type="bullet">
    /// <item><description>Troubleshooting issues</description></item>
    /// <item><description>Understanding application behavior</description></item>
    /// <item><description>Performance analysis</description></item>
    /// <item><description>Development and debugging</description></item>
    /// </list>
    /// <para>Note: Verbose logging can significantly increase output volume and may impact performance.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// "outputSettings": {
    ///   "verboseLogging": true
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("verboseLogging")]
    public bool VerboseLogging { get; set; } = false;
}

/// <summary>
/// Represents package filtering configuration settings.
/// </summary>
/// <remarks>
/// This class contains settings that control which packages are included or excluded from analysis.
/// Filtering can help focus the analysis on relevant packages and reduce noise from packages
/// that are not of interest for security or maintenance purposes.
/// </remarks>
public class FilterSettings
{
    /// <summary>
    /// Gets or sets the list of package IDs to exclude from analysis.
    /// </summary>
    /// <value>
    /// A list of package ID strings to exclude, or <c>null</c> if no packages should be excluded.
    /// </value>
    /// <remarks>
    /// <para>This setting allows you to specify packages that should be completely excluded from
    /// analysis and reporting. This is useful for packages that:</para>
    /// <list type="bullet">
    /// <item><description>Are development-only dependencies (e.g., test frameworks, analyzers)</description></item>
    /// <item><description>Have known issues that cannot be addressed immediately</description></item>
    /// <item><description>Are internal company packages not relevant for security analysis</description></item>
    /// <item><description>Generate noise in reports due to their nature</description></item>
    /// </list>
    /// <para>Package IDs should be specified exactly as they appear in project files (case-sensitive).
    /// The exclusion applies to both direct and transitive references.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// "filterSettings": {
    ///   "excludePackages": [
    ///     "Microsoft.NET.Test.Sdk",
    ///     "coverlet.collector",
    ///     "NUnit",
    ///     "CompanyName.InternalLibrary"
    ///   ]
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("excludePackages")]
    public List<string>? ExcludePackages { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include pre-release packages in analysis.
    /// </summary>
    /// <value>
    /// <c>true</c> to include pre-release packages; otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, the application will consider pre-release package versions when checking
    /// for updates and analyzing package status. Pre-release versions include alpha, beta, release
    /// candidate, and other non-stable versions.</para>
    /// <para>Enable this setting when:</para>
    /// <list type="bullet">
    /// <item><description>Your project uses pre-release packages</description></item>
    /// <item><description>You want to see the latest features and fixes</description></item>
    /// <item><description>You're working on experimental or development projects</description></item>
    /// </list>
    /// <para>Disable this setting for production environments where stability is prioritized over
    /// having the latest features.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// "filterSettings": {
    ///   "includePrerelease": false
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("includePrerelease")]
    public bool IncludePrerelease { get; set; } = false;

    /// <summary>
    /// Gets or sets the minimum vulnerability severity level to report.
    /// </summary>
    /// <value>
    /// A string representing the minimum severity level. Valid values are: "Low", "Medium", "High", "Critical".
    /// </value>
    /// <remarks>
    /// <para>This setting filters vulnerability reports to only show vulnerabilities at or above
    /// the specified severity level. This helps focus attention on the most critical security issues.</para>
    /// <para>Severity levels (in ascending order of severity):</para>
    /// <list type="bullet">
    /// <item><description><c>Low</c> - Minor security issues with limited impact</description></item>
    /// <item><description><c>Medium</c> - Moderate security issues that should be addressed</description></item>
    /// <item><description><c>High</c> - Serious security issues requiring prompt attention</description></item>
    /// <item><description><c>Critical</c> - Severe security issues requiring immediate action</description></item>
    /// </list>
    /// <para>Setting this to "High" will only show High and Critical vulnerabilities, effectively
    /// filtering out Low and Medium severity issues.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// "filterSettings": {
    ///   "minSeverity": "Medium"
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("minSeverity")]
    public string? MinSeverity { get; set; } = "low";
}

/// <summary>
/// Represents report generation configuration settings.
/// </summary>
/// <remarks>
/// This class contains settings that control the structure, organization, and content filtering
/// of generated reports. These settings affect how information is presented and can be used
/// to focus reports on specific types of package issues.
/// </remarks>
public class ReportSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether to group packages by target framework.
    /// </summary>
    /// <value>
    /// <c>true</c> to group packages by framework; otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, the report will organize packages by their target framework (e.g., net9.0, netstandard2.0).
    /// This is particularly useful for multi-targeting projects where different frameworks may have
    /// different package dependencies or versions.</para>
    /// <para>Framework grouping helps with:</para>
    /// <list type="bullet">
    /// <item><description>Understanding framework-specific dependencies</description></item>
    /// <item><description>Identifying framework compatibility issues</description></item>
    /// <item><description>Planning framework migration strategies</description></item>
    /// <item><description>Troubleshooting build issues related to specific frameworks</description></item>
    /// </list>
    /// <para>Disable this setting for single-framework projects or when a flat package list is preferred.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// "reportSettings": {
    ///   "groupByFramework": true
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("groupByFramework")]
    public bool GroupByFramework { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to sort packages alphabetically by name.
    /// </summary>
    /// <value>
    /// <c>true</c> to sort packages alphabetically; otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, packages within each section of the report will be sorted alphabetically
    /// by package ID. This makes it easier to locate specific packages in large reports and
    /// provides consistent ordering across multiple report runs.</para>
    /// <para>Alphabetical sorting is recommended for:</para>
    /// <list type="bullet">
    /// <item><description>Large solutions with many packages</description></item>
    /// <item><description>Reports that will be reviewed by multiple people</description></item>
    /// <item><description>Comparing reports across different time periods</description></item>
    /// <item><description>Automated processing where consistent ordering is important</description></item>
    /// </list>
    /// <para>Disable this setting if you prefer packages to appear in the order they were discovered
    /// or if there's a specific custom ordering requirement.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// "reportSettings": {
    ///   "sortByName": true
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("sortByName")]
    public bool SortByName { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to show only outdated packages in reports.
    /// </summary>
    /// <value>
    /// <c>true</c> to show only outdated packages; otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, the report will only include packages that have newer versions available.
    /// This filtering option is equivalent to using the <c>--only-outdated</c> command-line flag
    /// and helps focus on packages that may need updates.</para>
    /// <para>This filter is useful for:</para>
    /// <list type="bullet">
    /// <item><description>Maintenance planning and update prioritization</description></item>
    /// <item><description>Identifying packages that may have security fixes</description></item>
    /// <item><description>Keeping dependencies current with latest features</description></item>
    /// <item><description>Reducing report size when only updates are of interest</description></item>
    /// </list>
    /// <para>Note: This setting cannot be combined with <see cref="ShowVulnerableOnly"/> or
    /// <see cref="ShowDeprecatedOnly"/> - only one filter type can be active at a time.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// "reportSettings": {
    ///   "showOutdatedOnly": true
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("showOutdatedOnly")]
    public bool ShowOutdatedOnly { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to show only vulnerable packages in reports.
    /// </summary>
    /// <value>
    /// <c>true</c> to show only vulnerable packages; otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, the report will only include packages that have known security vulnerabilities.
    /// This filtering option is equivalent to using the <c>--only-vulnerable</c> command-line flag
    /// and helps focus on packages that pose security risks.</para>
    /// <para>This filter is essential for:</para>
    /// <list type="bullet">
    /// <item><description>Security audits and compliance reporting</description></item>
    /// <item><description>Prioritizing critical security updates</description></item>
    /// <item><description>Identifying immediate security risks</description></item>
    /// <item><description>Meeting security policy requirements</description></item>
    /// </list>
    /// <para>Use in combination with <see cref="FilterSettings.MinSeverity"/> to focus on
    /// vulnerabilities of specific severity levels.</para>
    /// <para>Note: This setting cannot be combined with <see cref="ShowOutdatedOnly"/> or
    /// <see cref="ShowDeprecatedOnly"/> - only one filter type can be active at a time.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// "reportSettings": {
    ///   "showVulnerableOnly": true
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("showVulnerableOnly")]
    public bool ShowVulnerableOnly { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to show only deprecated packages in reports.
    /// </summary>
    /// <value>
    /// <c>true</c> to show only deprecated packages; otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>When enabled, the report will only include packages that have been marked as deprecated
    /// by their maintainers. This filtering option is equivalent to using the <c>--only-deprecated</c>
    /// command-line flag and helps identify packages that should be replaced.</para>
    /// <para>This filter is important for:</para>
    /// <list type="bullet">
    /// <item><description>Migration planning and technical debt management</description></item>
    /// <item><description>Ensuring long-term maintainability of projects</description></item>
    /// <item><description>Identifying packages that may stop receiving updates</description></item>
    /// <item><description>Planning transitions to recommended alternatives</description></item>
    /// </list>
    /// <para>Deprecated packages often include information about recommended alternatives,
    /// which will be displayed in the report to guide migration efforts.</para>
    /// <para>Note: This setting cannot be combined with <see cref="ShowOutdatedOnly"/> or
    /// <see cref="ShowVulnerableOnly"/> - only one filter type can be active at a time.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// "reportSettings": {
    ///   "showDeprecatedOnly": true
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("showDeprecatedOnly")]
    public bool ShowDeprecatedOnly { get; set; } = false;
}