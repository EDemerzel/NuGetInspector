using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NuGetInspectorApp.Configuration;
using NuGetInspectorApp.Formatters;
using NuGetInspectorApp.Models;
using NuGetInspectorApp.Services;
using NuGetInspectorApp.Application;

namespace NuGetInspectorApp;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        var options = ParseCommandLineArguments(args);
        if (options == null) return 1;

        var host = CreateHostBuilder(options).Build();

        try
        {
            using var scope = host.Services.CreateScope();
            var app = scope.ServiceProvider.GetRequiredService<NuGetAuditApplication>();
            return await app.RunAsync(options);
        }
        catch (Exception ex)
        {
            var logger = host.Services.GetService<ILogger<Program>>();
            logger?.LogError(ex, "Unhandled exception occurred");
            return 1;
        }
        finally
        {
            await host.StopAsync();
        }
    }

    static CommandLineOptions? ParseCommandLineArguments(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: NuGetInspector <path-to-solution.sln> [options]");
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --format <console|html|markdown|json>  Output format (default: console)");
            Console.Error.WriteLine("  --output <file>                        Output file path");
            Console.Error.WriteLine("  --verbose                              Verbose output");
            Console.Error.WriteLine("  --only-outdated                       Show only outdated packages");
            Console.Error.WriteLine("  --only-vulnerable                     Show only vulnerable packages");
            Console.Error.WriteLine("  --only-deprecated                     Show only deprecated packages");
            return null;
        }

        var options = new CommandLineOptions { SolutionPath = args[0] };

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--format" when i + 1 < args.Length:
                    options.OutputFormat = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    options.OutputFile = args[++i];
                    break;
                case "--verbose":
                    options.VerboseOutput = true;
                    break;
                case "--only-outdated":
                    options.OnlyOutdated = true;
                    break;
                case "--only-vulnerable":
                    options.OnlyVulnerable = true;
                    break;
                case "--only-deprecated":
                    options.OnlyDeprecated = true;
                    break;
            }
        }

        // Input validation
        if (string.IsNullOrWhiteSpace(options.SolutionPath) ||
            Path.GetInvalidPathChars().Any(options.SolutionPath.Contains))
        {
            Console.Error.WriteLine("Error: Invalid solution path provided.");
            return null;
        }

        if (!File.Exists(options.SolutionPath))
        {
            Console.Error.WriteLine($"Error: Solution file not found at '{options.SolutionPath}'.");
            return null;
        }

        return options;
    }

    static IHostBuilder CreateHostBuilder(CommandLineOptions options) =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                if (options.VerboseOutput)
                    logging.SetMinimumLevel(LogLevel.Debug);
                else
                    logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureServices((context, services) =>
            {
                var config = new AppConfiguration
                {
                    VerboseLogging = options.VerboseOutput
                };

                services.AddSingleton(config);
                services.AddSingleton<INuGetApiService, NuGetApiService>();
                services.AddSingleton<IPackageAnalyzer, PackageAnalyzer>();
                services.AddSingleton<IDotNetService, DotNetService>();
                services.AddSingleton<IReportFormatter, ConsoleReportFormatter>();
                services.AddTransient<NuGetAuditApplication>();
            });
}

