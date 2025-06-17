# NuGet Inspector

A comprehensive command-line tool for analyzing NuGet packages in .NET solutions. NuGet Inspector helps you identify outdated, deprecated, and vulnerable packages across your entire solution, providing detailed Metadata and dependency information.

## Features

- 🔍 **Package Analysis**: Detect outdated, deprecated, and vulnerable packages
- 📊 **Comprehensive Reports**: Detailed information including dependencies, URLs, and security advisories
- 🚀 **High Performance**: Parallel processing with configurable concurrency limits
- 📋 **Multiple Output Formats**: Console, HTML, Markdown, and JSON (planned)
- 🎯 **Flexible Filtering**: Focus on specific types of issues
- 🐳 **Docker Support**: Run in containerized environments
- 📝 **Detailed Logging**: Configurable verbosity levels

## Installation

### Prerequisites

- .NET 9.0 SDK or later
- Access to the internet (for NuGet API calls)

### Build from Source

```bash
git clone https://github.com/yourusername/nuget-inspector.git
cd nuget-inspector
dotnet build --configuration Release
```

### Docker

```bash
docker build -t nuget-inspector .
```

## Usage

### Basic Usage

```bash
# Analyze a solution
NuGetInspector path/to/your/solution.sln

# Show only outdated packages
NuGetInspector solution.sln --only-outdated

# Show only vulnerable packages
NuGetInspector solution.sln --only-vulnerable

# Show only deprecated packages
NuGetInspector solution.sln --only-deprecated

# Enable verbose output
NuGetInspector solution.sln --verbose

# Save output to file
NuGetInspector solution.sln --output report.txt
```

### Command Line Options

| Option | Description | Default |
|--------|-------------|---------|
| `<solution-path>` | Path to the solution (.sln) file | Required |
| `--format <format>` | Output format: console, html, markdown, json | console |
| `--output <file>` | Output file path (optional) | Console output |
| `--verbose` | Enable verbose logging | false |
| `--only-outdated` | Show only outdated packages | false |
| `--only-vulnerable` | Show only vulnerable packages | false |
| `--only-deprecated` | Show only deprecated packages | false |

### Docker Usage

```bash
# Mount your solution directory and run analysis
docker run -v /path/to/your/solution:/app/solution nuget-inspector /app/solution/YourSolution.sln

# With output file
docker run -v /path/to/your/solution:/app/solution nuget-inspector /app/solution/YourSolution.sln --output /app/solution/report.txt
```

## Sample Output

```shell
=== Project: MyWebApp.csproj ===

Framework: net9.0

• Microsoft.AspNetCore.App (9.0.0)
    Gallery URL: https://www.nuget.org/packages/Microsoft.AspNetCore.App/9.0.0
    Project URL: https://asp.net
    Requested: 9.0.0
    Latest:    9.0.1  (Outdated)
    Deprecated: No
    Vulnerabilities: None
    Supported frameworks & their dependencies:
      • net9.0
          - Microsoft.Extensions.Logging 9.0.0
          - Microsoft.Extensions.DependencyInjection 9.0.0

• Newtonsoft.JSON (12.0.3)
    Gallery URL: https://www.nuget.org/packages/Newtonsoft.JSON/12.0.3
    Project URL: https://www.newtonsoft.com/json
    Requested: 12.0.3
    Latest:    13.0.3  (Outdated)
    Deprecated: Yes (Legacy package)
      Alternative: System.Text.JSON >=6.0.0
    Vulnerabilities:
      - High: https://github.com/advisories/GHSA-5crp-9r3c-p9vr

Transitive packages:
 • Microsoft.Extensions.Primitives (9.0.0)
 • System.Text.JSON (9.0.0)

------------------------------------------------------------
```

## Configuration

The application can be configured through the `AppConfiguration` class:

```csharp
public class AppConfiguration
{
    public string NuGetApiBaseUrl { get; set; } = "https://api.nuget.org/v3/registration5-gz-semver2";
    public string NuGetGalleryBaseUrl { get; set; } = "https://www.nuget.org/packages";
    public int MaxConcurrentRequests { get; set; } = 5;
    public int HttpTimeoutSeconds { get; set; } = 30;
    public bool VerboseLogging { get; set; } = false;
}
```

## Architecture

### Core Components

- **Program.cs**: Entry point and dependency injection setup
- **NuGetAuditApplication**: Main application orchestrator
- **Services**: Business logic components
  - `INuGetAPIService`: Fetches package Metadata from NuGet API
  - `IDotNetService`: Executes `dotnet list package` commands
  - `IPackageAnalyzer`: Merges and analyzes package data
- **Formatters**: Output formatting
  - `IReportFormatter`: Interface for different output formats
  - `ConsoleReportFormatter`: Console output implementation
- **Models**: Data transfer objects and domain models
- **Configuration**: Application settings and command-line options

### Data Flow

1. Parse command-line arguments
2. Execute `dotnet list package` commands in parallel (outdated, deprecated, vulnerable)
3. Merge package information across different report types
4. Fetch detailed Metadata from NuGet API
5. Apply filters based on command-line options
6. Format and output results

## Development

### Project Structure

```shell
NuGetInspectorApp/
├── Configuration/
│   └── Configuration.cs          # App configuration and command-line options
├── Formatters/
│   ├── IReportFormatter.cs       # Formatter interface
│   └── ConsoleReportFormatter.cs # Console output formatter
├── Models/
│   └── Models.cs                 # Data models and DTOs
├── Services/
│   ├── INuGetAPIService.cs       # NuGet API service interface
│   ├── NuGetAPIService.cs        # NuGet API service implementation
│   ├── IDotNetService.cs         # .NET CLI service interface
│   ├── DotNetService.cs          # .NET CLI service implementation
│   ├── IPackageAnalyzer.cs       # Package analyzer interface
│   └── PackageAnalyzer.cs        # Package analyzer implementation
├── Program.cs                    # Application entry point
├── NuGetAuditApplication.cs      # Main application class
└── NuGetInspectorApp.csproj      # Project file
```

### Dependencies

- **Microsoft.Extensions.Hosting** (9.0.4): Dependency injection and hosting
- **Microsoft.Extensions.Logging** (9.0.4): Logging framework
- **Microsoft.Extensions.Logging.Console** (9.0.4): Console logging provider
- **Microsoft.Extensions.DependencyInjection** (9.0.4): Dependency injection container

### Building

```bash
# Debug build
dotnet build

# Release build
dotnet build --configuration Release

# Run tests (when implemented)
dotnet test

# Create NuGet package
dotnet pack --configuration Release
```

### Testing

Unit tests can be added using your preferred testing framework (xUnit, NUnit, MSTest).

Example test structure:

```shell
NuGetInspectorApp.Tests/
├── Services/
│   ├── NuGetAPIServiceTests.cs
│   ├── DotNetServiceTests.cs
│   └── PackageAnalyzerTests.cs
├── Formatters/
│   └── ConsoleReportFormatterTests.cs
└── NuGetAuditApplicationTests.cs
```

## Error Handling

The application includes comprehensive error handling:

- **Network Issues**: Graceful handling of HTTP timeouts and connection failures
- **Invalid JSON**: Protection against malformed API responses
- **File System**: Validation of solution file paths and permissions
- **Process Execution**: Handling of `dotnet` command failures
- **Cancellation**: Support for operation cancellation

## Performance Considerations

- **Parallel Processing**: Multiple `dotnet list package` commands run concurrently
- **Rate Limiting**: Configurable concurrent request limits for NuGet API calls
- **Memory Management**: Proper disposal of HTTP clients and semaphores
- **Streaming**: JSON parsing uses streaming to handle large responses

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Development Guidelines

- Follow C# coding conventions
- Add XML documentation for public APIs
- Include unit tests for new features
- Update README for significant changes
- Use semantic versioning for releases

## Roadmap

- [ ] **HTML Output Format**: Rich HTML reports with charts and graphs
- [ ] **Markdown Output Format**: GitHub-friendly markdown reports
- [ ] **JSON Output Format**: Machine-readable JSON output
- [ ] **Configuration File Support**: Support for `.nugetinspector.json` configuration
- [ ] **CI/CD Integration**: GitHub Actions and Azure DevOps templates
- [ ] **Package Filtering**: Include/exclude patterns for packages
- [ ] **Historical Tracking**: Track changes over time
- [ ] **Custom Rules Engine**: Define custom package policies
- [ ] **Integration APIs**: REST API for programmatic access
- [ ] **GUI Version**: Desktop application with rich UI

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Built on top of the NuGet API
- Uses Microsoft.Extensions for hosting and dependency injection
- Inspired by `dotnet list package` and `dotnet outdated` tools

## Support

- 📝 [Issues](https://github.com/yourusername/nuget-inspector/issues)
- 💬 [Discussions](https://github.com/yourusername/nuget-inspector/discussions)
- 📧 [Email](mailto:your.email@example.com)

Made with ❤️ for the .NET community
