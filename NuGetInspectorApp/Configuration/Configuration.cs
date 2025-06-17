namespace NuGetInspectorApp.Configuration
{
    /// <summary>
    /// Configuration settings for the NuGet Inspector application.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Gets or sets the base URL for the NuGet API registration endpoint.
        /// </summary>
        /// <value>The NuGet API base URL. Default is "https://api.nuget.org/v3/registration5-gz-semver2".</value>
        public string NuGetApiBaseUrl { get; set; } = "https://api.nuget.org/v3/registration5-semver1";

        /// <summary>
        /// Gets or sets the base URL for the NuGet Gallery.
        /// </summary>
        /// <value>The NuGet Gallery base URL. Default is "https://www.nuget.org/packages".</value>
        public string NuGetGalleryBaseUrl { get; set; } = "https://www.nuget.org/packages";

        /// <summary>
        /// Gets or sets the maximum number of concurrent HTTP requests allowed.
        /// </summary>
        /// <value>The maximum concurrent requests. Default is 5.</value>
        public int MaxConcurrentRequests { get; set; } = 5;

        /// <summary>
        /// Gets or sets the HTTP request timeout in seconds.
        /// </summary>
        /// <value>The HTTP timeout in seconds. Default is 30.</value>
        public int HttpTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Gets or sets the maximum number of retry attempts for failed HTTP requests.
        /// </summary>
        /// <value>The maximum retry attempts. Default is 3.</value>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Gets or sets the delay in seconds between retry attempts for failed HTTP requests.
        /// </summary>
        /// <value>The retry delay in seconds. Default is 2.</value>
        public int RetryDelaySeconds { get; set; } = 2;

        /// <summary>
        /// Gets or sets the exponential backoff factor for retry delays.
        /// </summary>
        /// <value>The backoff multiplier applied to retry delays. Default is 2.0.</value>
        public double RetryBackoffFactor { get; set; } = 2.0;

        /// <summary>
        /// Gets or sets the maximum retry delay in seconds.
        /// </summary>
        /// <value>The maximum delay between retries. Default is 30 seconds.</value>
        public int MaxRetryDelaySeconds { get; set; } = 30;

        /// <summary>
        /// Gets or sets a value indicating whether to use jitter in retry delays.
        /// </summary>
        /// <value><c>true</c> to add random jitter to retry delays; otherwise, <c>false</c>. Default is <c>true</c>.</value>
        public bool UseRetryJitter { get; set; } = true;

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
        public bool VerboseLogging { get; set; } = false;
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

                var outputDir = Path.GetDirectoryName(Path.GetFullPath(OutputFile));
                if (outputDir != null && !Directory.Exists(outputDir))
                    throw new ArgumentException($"Output directory does not exist: {outputDir}", nameof(OutputFile));
            }
        }
    }
}