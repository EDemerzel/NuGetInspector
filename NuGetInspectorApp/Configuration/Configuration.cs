namespace NuGetInspectorApp.Configuration
{
    /// <summary>
    /// Configuration settings for the NuGet Inspector application.
    /// </summary>
    public class AppConfiguration
    {
        /// <summary>
        /// Gets or sets the base URL for the NuGet API registration endpoint.
        /// </summary>
        /// <value>The NuGet API base URL. Default is "https://api.nuget.org/v3/registration5-gz-semver2".</value>
        public string NuGetApiBaseUrl { get; set; } = "https://api.nuget.org/v3/registration5-gz-semver2";

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
        /// Validates the configuration settings and throws exceptions if any values are out of range.
        /// </summary>
        public void Validate()
        {
            if (HttpTimeoutSeconds <= 0 || HttpTimeoutSeconds > 300)
                throw new ArgumentOutOfRangeException(nameof(HttpTimeoutSeconds), "Must be between 1 and 300 seconds");

            if (MaxConcurrentRequests <= 0 || MaxConcurrentRequests > 20)
                throw new ArgumentOutOfRangeException(nameof(MaxConcurrentRequests), "Must be between 1 and 20");
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
    }
}