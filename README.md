# NuGet Inspector

A comprehensive command-line tool for analyzing NuGet packages in .NET solutions. NuGet Inspector helps you identify outdated, deprecated, and vulnerable packages across your entire solution, providing detailed metadata and dependency information.

## Features

- 🔍 **Package Analysis**: Detect outdated, deprecated, and vulnerable packages
- 📊 **Comprehensive Reports**: Detailed information including dependencies, URLs, and security advisories
- 🚀 **High Performance**: Parallel processing with configurable concurrency limits
- 📋 **Multiple Output Formats**: Console output (HTML, Markdown, and JSON planned)
- 🎯 **Flexible Filtering**: Focus on specific types of issues with `--only-outdated`, `--only-vulnerable`, `--only-deprecated`
- 🐳 **Docker Support**: Run in containerized environments
- 📝 **Detailed Logging**: Configurable verbosity levels with structured logging
- 🔄 **Retry Logic**: Robust HTTP retry mechanism with exponential backoff and jitter
- 🛡️ **Security**: Input validation and path traversal protection

## Installation

### Prerequisites

- .NET 9.0 SDK or later
- Access to the internet (for NuGet API calls)

### Build from Source

```bash
git clone https://github.com/yourusername/nuget-inspector.git
cd nuget-inspector/NuGetInspectorApp
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
dotnet run -- path/to/your/solution.sln

# Show only outdated packages
dotnet run -- solution.sln --only-outdated

# Show only vulnerable packages
dotnet run -- solution.sln --only-vulnerable

# Show only deprecated packages
dotnet run -- solution.sln --only-deprecated

# Enable verbose output
dotnet run -- solution.sln --verbose

# Save output to file
dotnet run -- solution.sln --output report.txt

# Configure concurrency and timeouts
dotnet run -- solution.sln --max-concurrent 10 --timeout 60 --retry-attempts 5
```

### Command Line Options

| Option              | Description                                  | Default        | Range                    |
| ------------------- | -------------------------------------------- | -------------- | ------------------------ |
| `<solution-path>`   | Path to the solution (.sln) file             | Required       | Must be valid .sln file  |
| `--format <format>` | Output format: console, html, markdown, json | console        | console only (currently) |
| `--output <file>`   | Output file path (optional)                  | Console output | Valid file path          |
| `--verbose`         | Enable verbose logging                       | false          | -                        |
| `--only-outdated`   | Show only outdated packages                  | false          | -                        |
| `--only-vulnerable` | Show only vulnerable packages                | false          | -                        |
| `--only-deprecated` | Show only deprecated packages                | false          | -                        |
| `--max-concurrent`  | Maximum concurrent HTTP requests             | 5              | 1-20                     |
| `--timeout`         | HTTP request timeout in seconds              | 30             | 5-300                    |
| `--retry-attempts`  | Maximum retry attempts                       | 3              | 0-10                     |

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

• Newtonsoft.Json (12.0.3)
    Gallery URL: https://www.nuget.org/packages/Newtonsoft.Json/12.0.3
    Project URL: https://www.newtonsoft.com/json
    Requested: 12.0.3
    Latest:    13.0.3  (Outdated)
    Deprecated: Yes (Legacy package)
      Alternative: System.Text.Json >=6.0.0
    Vulnerabilities:
      - High: https://github.com/advisories/GHSA-5crp-9r3c-p9vr

Transitive packages:
 • Microsoft.Extensions.Primitives (9.0.0)
 • System.Text.Json (9.0.0)

------------------------------------------------------------
```

## Configuration

The application uses two main configuration classes:

### AppSettings

```csharp
public class AppSettings
{
    public string NuGetApiBaseUrl { get; set; } = "https://api.nuget.org/v3/registration5-gz-semver2";
    public string NuGetGalleryBaseUrl { get; set; } = "https://www.nuget.org/packages";
    public int MaxConcurrentRequests { get; set; } = 5;
    public int HttpTimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 3;
    public double RetryDelaySeconds { get; set; } = 2.0;
    public double RetryBackoffFactor { get; set; } = 2.0;
    public int MaxRetryDelaySeconds { get; set; } = 30;
    public bool UseRetryJitter { get; set; } = true;
    public bool VerboseLogging { get; set; } = false;
}
```

### Configuration File Usage

Create a `.nugetinspector` file in your project directory or home directory:

```bash
# Use default configuration file in current directory
dotnet run -- solution.sln

# Use custom configuration file
dotnet run -- solution.sln --config ./custom-config.json

# Command line options override config file settings
dotnet run -- solution.sln --verbose --max-concurrent 10
```

#### Configuration File Locations (checked in order)

1. File specified with `--config` option
2. `.nugetinspector` in current directory
3. `.nugetinspector` in user home directory
4. `.nugetinspector` in current working directory

#### Example Configuration

```json
{
    "apiSettings": {
        "baseUrl": "https://api.nuget.org/v3/registration5-gz-semver2",
        "timeout": 45,
        "maxConcurrentRequests": 3
    },
    "outputSettings": {
        "verboseLogging": true
    },
    "filterSettings": {
        "excludePackages": ["Microsoft.NET.Test.Sdk"],
        "minSeverity": "Medium"
    }
}
```

### API Endpoints

The application uses the NuGet v3 API with support for multiple endpoints:

- **Production**: `https://api.nuget.org/v3/registration5-gz-semver2` (current default)
- **Alternative**: `https://api.nuget.org/v3/registration5-semver2` (without compression)
- **Legacy**: `https://api.nuget.org/v3/registration5-semver1` (basic registration)

## Architecture

### Core Components

- **Program.cs**: Entry point with System.CommandLine integration and dependency injection setup
- **NuGetAuditApplication**: Main application orchestrator with comprehensive error handling
- **Services**: Business logic components
  - `INuGetApiService` / `NuGetApiService`: Fetches package metadata from NuGet API with retry logic
  - `IDotNetService` / `DotNetService`: Executes `dotnet list package` commands
  - `IPackageAnalyzer` / `PackageAnalyzer`: Merges and analyzes package data with deep cloning
- **Formatters**: Output formatting
  - `IReportFormatter` / `ConsoleReportFormatter`: Console output implementation
- **Models**: Comprehensive data models with JSON serialization support
- **Configuration**: Application settings and command-line options with validation

### Data Flow

1. **Input Validation**: Parse and validate command-line arguments with security checks
2. **Parallel Report Generation**: Execute multiple `dotnet list package` commands concurrently:
    - `--outdated`: Packages with newer versions available
    - `--deprecated`: Packages marked as deprecated
    - `--vulnerable`: Packages with known security vulnerabilities
3. **Package Merging**: Combine data from all report types using `PackageAnalyzer`
4. **Metadata Enrichment**: Fetch detailed information from NuGet API with retry logic
5. **Filtering**: Apply user-specified filters (outdated, deprecated, vulnerable)
6. **Output Generation**: Format and display results using `ConsoleReportFormatter`

### Key Features

- **Robust Error Handling**: Comprehensive exception handling with operation correlation IDs
- **Performance Optimization**: Semaphore-based concurrency control and HTTP connection pooling
- **Security**: Input validation, path traversal protection, and URL validation
- **Observability**: Structured logging with configurable verbosity levels

## Development

### Project Structure

```shell
NuGetInspectorApp/
├── Configuration/
│   └── Configuration.cs          # AppSettings and CommandLineOptions
├── Formatters/
│   ├── IReportFormatter.cs       # Formatter interface
│   └── ConsoleReportFormatter.cs # Console output implementation
├── Models/
│   └── Models.cs                 # Complete data models with JSON attributes
├── Services/
│   ├── INuGetApiService.cs       # NuGet API service interface
│   ├── NuGetApiService.cs        # NuGet API service with retry logic
│   ├── IDotNetService.cs         # .NET CLI service interface
│   ├── DotNetService.cs          # .NET CLI service implementation
│   ├── IPackageAnalyzer.cs       # Package analyzer interface
│   └── PackageAnalyzer.cs        # Package merging logic
├── Application/
│   └── NuGetAuditApplication.cs  # Main application logic
├── Program.cs                    # Entry point with System.CommandLine
├── NuGetInspectorApp.csproj      # Project file
└── Dockerfile                    # Docker container support

NuGetInspectorApp.Tests/
├── Services/
│   ├── NuGetApiServiceTests.cs   # API service tests with HTTP mocking
│   ├── DotNetServiceTests.cs     # CLI service tests with JSON deserialization
│   └── PackageAnalyzerTests.cs   # Package merging logic tests
├── Formatters/
│   └── ConsoleReportFormatterTests.cs # Output formatting tests
├── NuGetAuditApplicationTests.cs # Integration tests
└── NuGetInspectorApp.Tests.csproj # Test project file
```

### Dependencies

- **Microsoft.Extensions.Hosting** (9.0.4): Dependency injection and hosting
- **Microsoft.Extensions.Logging** (9.0.4): Structured logging framework
- **Microsoft.Extensions.Logging.Console** (9.0.4): Console logging provider
- **Microsoft.Extensions.DependencyInjection** (9.0.4): Dependency injection container
- **Microsoft.Extensions.Http** (9.0.4): HTTP client factory and configuration
- **System.CommandLine** (2.0.0-beta4.22272.1): Modern command-line parsing
- **System.Text.Json** (9.0.4): High-performance JSON serialization

### Testing Framework

- **NUnit** (4.3.2): Unit testing framework
- **Moq** (4.20.72): Mocking framework for dependencies
- **FluentAssertions** (7.0.0): Expressive assertion library
- **Microsoft.NET.Test.Sdk** (17.14.1): Test platform SDK

### Building

```bash
# Debug build
dotnet build

# Release build
dotnet build --configuration Release

# Run tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Create NuGet package
dotnet pack --configuration Release
```

### Testing

The project includes comprehensive unit tests covering:

- **Service Layer**: HTTP client mocking, JSON deserialization, and error handling
- **Business Logic**: Package merging algorithms and filtering logic
- **Output Formatting**: Report generation and console formatting
- **Integration**: End-to-end application workflow testing

```bash
# Run specific test categories
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"

# Run tests for specific class
dotnet test --filter "ClassName~NuGetApiServiceTests"
```

## Error Handling

The application includes comprehensive error handling:

- **Network Issues**: Graceful handling of HTTP timeouts and connection failures with exponential backoff
- **Invalid JSON**: Protection against malformed API responses with detailed error logging
- **File System**: Validation of solution file paths and permissions with security checks
- **Process Execution**: Robust handling of `dotnet` command failures and output parsing
- **Cancellation**: Support for operation cancellation with proper resource cleanup
- **Input Validation**: Comprehensive validation of all user inputs with security considerations

## Performance Considerations

- **Parallel Processing**: Multiple `dotnet list package` commands run concurrently
- **Rate Limiting**: Configurable concurrent request limits for NuGet API calls (default: 5)
- **Memory Management**: Proper disposal of HTTP clients, semaphores, and JSON documents
- **HTTP Optimization**: Connection pooling, compression support, and keep-alive
- **Retry Logic**: Exponential backoff with jitter to prevent thundering herd problems
- **Streaming**: Efficient JSON parsing using System.Text.Json with lifecycle management

## Security Features

- **Input Validation**: Protection against path traversal and injection attacks
- **URL Validation**: Whitelisted domains for catalog URL fetching
- **File Path Security**: Comprehensive validation of solution and output file paths
- **Resource Limits**: Configurable limits on concurrent requests and timeouts
- **Error Information**: Sanitized error messages to prevent information disclosure

## Troubleshooting

### Common Issues

1. **404 Errors for All Packages**: Check NuGet API URL configuration
2. **Timeout Issues**: Increase `--timeout` and `--retry-attempts` values
3. **Rate Limiting**: Reduce `--max-concurrent` value
4. **Large Solutions**: Use filtering options to reduce scope

### Debug Logging

Enable verbose logging to see detailed operation information:

```bash
dotnet run -- solution.sln --verbose
```

This provides:

- HTTP request/response details
- Package merging operations
- API endpoint URLs
- Error correlation IDs

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Development Guidelines

- Follow C# coding conventions and use provided `.editorconfig`
- Add comprehensive XML documentation for public APIs
- Include unit tests for new features with good coverage
- Update README for significant changes
- Use semantic versioning for releases
- Ensure all tests pass before submitting PR

### Code Quality

- **EditorConfig**: Consistent formatting rules defined in `.editorconfig`
- **Nullable Reference Types**: Enabled throughout the project
- **XML Documentation**: Required for all public APIs
- **Unit Tests**: Comprehensive test coverage with multiple frameworks
- **Static Analysis**: Built-in analyzers and code quality rules

## Roadmap

- [x] **Console Output Format**: Rich console reports with detailed package information
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

- Built on top of the NuGet v3 API
- Uses Microsoft.Extensions ecosystem for hosting and dependency injection
- Inspired by `dotnet list package` and `dotnet outdated` tools
- Leverages System.CommandLine for modern CLI experience

## Support

- 📝 [Issues](https://github.com/yourusername/nuget-inspector/issues)
- 💬 [Discussions](https://github.com/yourusername/nuget-inspector/discussions)
- 📧 [Email](mailto:your.email@example.com)

Made with ❤️ for the .NET community
