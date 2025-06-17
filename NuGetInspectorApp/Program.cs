using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System.CommandLine.Invocation;
using NuGetInspectorApp.Configuration;
using NuGetInspectorApp.Formatters;
using NuGetInspectorApp.Services;
using NuGetInspectorApp.Application;

namespace NuGetInspectorApp;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = CreateRootCommand();
        return await rootCommand.InvokeAsync(args);
    }

    static RootCommand CreateRootCommand()
    {
        var solutionPathArgument = new Argument<FileInfo>(
            name: "solution-path",
            description: "Path to the solution (.sln) file to analyze")
        {
            Arity = ArgumentArity.ExactlyOne
        };
        solutionPathArgument.AddValidator(result =>
        {
            var file = result.GetValueForArgument(solutionPathArgument);
            if (file == null)
            {
                result.ErrorMessage = "Solution path is required";
                return;
            }

            // Enhanced security validation
            var path = file.FullName;

            // Check for path traversal
            if (path.Contains("..") || path.Contains("~"))
            {
                result.ErrorMessage = "Solution path cannot contain path traversal sequences";
                return;
            }

            // Validate file extension
            if (!path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                result.ErrorMessage = "Solution path must point to a .sln file";
                return;
            }

            // Check if file exists
            if (!file.Exists)
            {
                result.ErrorMessage = $"Solution file not found: {path}";
                return;
            }

            // Check file size (prevent abuse)
            if (file.Length > 50 * 1024 * 1024) // 50MB limit
            {
                result.ErrorMessage = "Solution file is too large (>50MB)";
                return;
            }
        });

        var formatOption = new Option<string>(
            aliases: new[] { "--format", "-f" },
            description: "Output format")
        {
            ArgumentHelpName = "FORMAT",
            Arity = ArgumentArity.ZeroOrOne
        };
        formatOption.SetDefaultValue("console");
        formatOption.AddValidator(result =>
        {
            var format = result.GetValueForOption(formatOption);
            var validFormats = new[] { "console", "html", "markdown", "json" };
            if (format != null && !validFormats.Contains(format.ToLowerInvariant()))
            {
                result.ErrorMessage = $"Invalid format. Valid options: {string.Join(", ", validFormats)}";
            }
        });

        var outputOption = new Option<FileInfo?>(
            aliases: new[] { "--output", "-o" },
            description: "Output file path (optional, defaults to console)")
        {
            ArgumentHelpName = "FILE",
            Arity = ArgumentArity.ZeroOrOne
        };
        outputOption.AddValidator(result =>
        {
            var file = result.GetValueForOption(outputOption);
            if (file != null)
            {
                var path = file.FullName;

                // Security checks
                if (path.Contains("..") || path.Contains("~"))
                {
                    result.ErrorMessage = "Output path cannot contain path traversal sequences";
                    return;
                }

                // Check directory exists
                var directory = file.Directory;
                if (directory != null && !directory.Exists)
                {
                    result.ErrorMessage = $"Output directory does not exist: {directory.FullName}";
                    return;
                }

                // Check write permissions
                try
                {
                    var testFile = Path.Combine(directory?.FullName ?? "", $".nuget-inspector-test-{Guid.NewGuid():N}.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = $"Cannot write to output directory: {ex.Message}";
                }
            }
        });

        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose output");

        var onlyOutdatedOption = new Option<bool>(
            name: "--only-outdated",
            description: "Show only outdated packages");

        var onlyVulnerableOption = new Option<bool>(
            name: "--only-vulnerable",
            description: "Show only vulnerable packages");

        var onlyDeprecatedOption = new Option<bool>(
            name: "--only-deprecated",
            description: "Show only deprecated packages");

        var maxConcurrentOption = new Option<int>(
            name: "--max-concurrent",
            description: "Maximum number of concurrent HTTP requests")
        {
            ArgumentHelpName = "COUNT"
        };
        maxConcurrentOption.SetDefaultValue(5);
        maxConcurrentOption.AddValidator(result =>
        {
            var value = result.GetValueForOption(maxConcurrentOption);
            if (value < 1 || value > 20)
            {
                result.ErrorMessage = "Max concurrent requests must be between 1 and 20";
            }
        });

        var timeoutOption = new Option<int>(
            name: "--timeout",
            description: "HTTP request timeout in seconds")
        {
            ArgumentHelpName = "SECONDS"
        };
        timeoutOption.SetDefaultValue(30);
        timeoutOption.AddValidator(result =>
        {
            var value = result.GetValueForOption(timeoutOption);
            if (value < 5 || value > 300)
            {
                result.ErrorMessage = "Timeout must be between 5 and 300 seconds";
            }
        });

        var retryAttemptsOption = new Option<int>(
            name: "--retry-attempts",
            description: "Maximum number of retry attempts for failed requests")
        {
            ArgumentHelpName = "COUNT"
        };
        retryAttemptsOption.SetDefaultValue(3);
        retryAttemptsOption.AddValidator(result =>
        {
            var value = result.GetValueForOption(retryAttemptsOption);
            if (value < 0 || value > 10)
            {
                result.ErrorMessage = "Retry attempts must be between 0 and 10";
            }
        });

        var rootCommand = new RootCommand("A comprehensive tool for analyzing NuGet packages in .NET solutions")
        {
            solutionPathArgument,
            formatOption,
            outputOption,
            verboseOption,
            onlyOutdatedOption,
            onlyVulnerableOption,
            onlyDeprecatedOption,
            maxConcurrentOption,
            timeoutOption,
            retryAttemptsOption
        };

        // Use the context-based handler to avoid parameter limit
        rootCommand.SetHandler(async (InvocationContext context) =>
        {
            var solutionPath = context.ParseResult.GetValueForArgument(solutionPathArgument);
            var format = context.ParseResult.GetValueForOption(formatOption) ?? "console";
            var output = context.ParseResult.GetValueForOption(outputOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var onlyOutdated = context.ParseResult.GetValueForOption(onlyOutdatedOption);
            var onlyVulnerable = context.ParseResult.GetValueForOption(onlyVulnerableOption);
            var onlyDeprecated = context.ParseResult.GetValueForOption(onlyDeprecatedOption);
            var maxConcurrent = context.ParseResult.GetValueForOption(maxConcurrentOption);
            var timeout = context.ParseResult.GetValueForOption(timeoutOption);
            var retryAttempts = context.ParseResult.GetValueForOption(retryAttemptsOption);

            var options = new CommandLineOptions
            {
                SolutionPath = solutionPath.FullName,
                OutputFormat = format,
                OutputFile = output?.FullName,
                VerboseOutput = verbose,
                OnlyOutdated = onlyOutdated,
                OnlyVulnerable = onlyVulnerable,
                OnlyDeprecated = onlyDeprecated
            };

            var config = new AppConfiguration
            {
                VerboseLogging = verbose,
                MaxConcurrentRequests = maxConcurrent,
                HttpTimeoutSeconds = timeout,
                MaxRetryAttempts = retryAttempts
            };

            // Validate all configurations
            try
            {
                options.Validate();
                config.Validate();
            }
            catch (ArgumentException ex)
            {
                await Console.Error.WriteLineAsync($"Configuration error: {ex.Message}");
                context.ExitCode = 1;
                return;
            }

            var host = CreateHostBuilder(options, config).Build();

            try
            {
                using var scope = host.Services.CreateScope();
                var app = scope.ServiceProvider.GetRequiredService<NuGetAuditApplication>();
                var exitCode = await app.RunAsync(options, context.GetCancellationToken());
                context.ExitCode = exitCode;
            }
            catch (OperationCanceledException)
            {
                await Console.Error.WriteLineAsync("Operation was cancelled");
                context.ExitCode = 1;
            }
            catch (Exception ex)
            {
                var logger = host.Services.GetService<ILogger<Program>>();
                logger?.LogError(ex, "Unhandled exception occurred");
                await Console.Error.WriteLineAsync($"Error: {ex.Message}");
                if (verbose)
                {
                    await Console.Error.WriteLineAsync($"Details: {ex}");
                }
                context.ExitCode = 1;
            }
            finally
            {
                await host.StopAsync();
                host.Dispose();
            }
        });

        return rootCommand;
    }

static IHostBuilder CreateHostBuilder(CommandLineOptions options, AppConfiguration config) =>
    Host.CreateDefaultBuilder()
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();

            // Use simple console logging configuration
            logging.AddConsole(configure =>
            {
                configure.FormatterName = ConsoleFormatterNames.Simple;
            });

            if (options.VerboseOutput)
                logging.SetMinimumLevel(LogLevel.Debug);
            else
                logging.SetMinimumLevel(LogLevel.Warning);
        })
        .ConfigureServices((context, services) =>
        {
            services.AddSingleton(config);
            services.AddSingleton<INuGetApiService, NuGetApiService>();
            services.AddSingleton<IPackageAnalyzer, PackageAnalyzer>();
            services.AddSingleton<IDotNetService, DotNetService>();
            services.AddSingleton<IReportFormatter, ConsoleReportFormatter>();
            services.AddTransient<NuGetAuditApplication>();

            // Configure console formatter options through the service collection
            services.Configure<ConsoleFormatterOptions>(options =>
            {
                options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                options.UseUtcTimestamp = false;
            });

            // Add HttpClient with proper configuration
            services.AddHttpClient<NuGetApiService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(config.HttpTimeoutSeconds);
                client.DefaultRequestHeaders.Add("User-Agent", "NuGetInspector/1.0");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                MaxConnectionsPerServer = config.MaxConcurrentRequests
            });
        });
}