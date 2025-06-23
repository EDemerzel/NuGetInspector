# 🔄 NuGet Feed Toggle Scripts

This project includes platform-specific scripts that allow you to **enable or disable preview NuGet sources**, such as `dotnet-experimental` or `dotnet10`, which are commonly used for accessing early .NET SDK and runtime packages.

---

## 📁 Folder Structure

| Folder     | Purpose                                      |
| ---------- | -------------------------------------------- |
| `.build/`  | CI/CD-oriented scripts (safe for automation) |
| `scripts/` | Developer tooling (optional, not required)   |

> The toggle scripts are stored in `.build/` because they are intended to be used both manually and in CI pipelines.

---

## ⚙️ Scripts

### Windows PowerShell (🪟)

- **Path:** `.build/Toggle-NuGetFeed.ps1`
- **Requirements:** PowerShell 5.1+ or PowerShell Core

**Usage:**

```powershell
# Enable preview feeds
.\.build\Toggle-NuGetFeed.ps1 -Action enable -Sources "dotnet-experimental", "dotnet10"

# Disable a feed
.\.build\Toggle-NuGetFeed.ps1 -Action disable -Sources "dotnet-experimental"
```

---

### Bash / Linux / macOS (🐧🍎)

- **Path:** `.build/toggle-nuget-feed.sh`
- **Requirements:** Bash + `xmlstarlet`

**Install xmlstarlet:**

```bash
# Ubuntu/Debian
sudo apt install xmlstarlet

# macOS with Homebrew
brew install xmlstarlet
```

**Usage:**

```bash
# Disable preview feeds
./.build/toggle-nuget-feed.sh disable dotnet-experimental dotnet10

# Enable a specific feed
./.build/toggle-nuget-feed.sh enable dotnet-experimental
```

---

## 🧪 Troubleshooting

| Issue                         | Solution                                                                                                                    |
| ----------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| ❌ `nuget.config` not found   | Make sure you run the script from the project root or pass `-ConfigPath` (PowerShell) or modify the path in the Bash script |
| ❌ xmlstarlet not found       | Install `xmlstarlet` as shown above                                                                                         |
| ✔ Feed doesn't change         | Verify the `key` name in `nuget.config` matches exactly (e.g., `dotnet-experimental`, not `dotnet-preview`)                 |
| ⚠️ Script has no effect in CI | Ensure the correct `nuget.config` is committed and that you run the script **before** any `dotnet restore` commands         |
| 🔧 NuGet Inspector conflicts  | Run feed toggle scripts before analyzing solutions to avoid package resolution conflicts                                    |

---

## 🧼 Best Practices

- Keep the `.build/` folder for automation-safe scripts.
- Add execution policies if using in CI:

  - PowerShell: `Set-ExecutionPolicy RemoteSigned -Scope Process`
  - Bash: Ensure `chmod +x` on `.sh` script

- Always run the toggle script **before** restore or build steps if you're changing feed states dynamically.
- **For NuGet Inspector**: Toggle feeds before running package analysis to ensure consistent results across environments.

### 🎯 NuGet Inspector Integration

When using these scripts with NuGet Inspector for package analysis:

```bash
# Disable experimental feeds for stable analysis
./.build/toggle-nuget-feed.sh disable dotnet-experimental dotnet10

# Run NuGet Inspector analysis with enhanced metadata
dotnet run -- YourSolution.sln --only-vulnerable --only-deprecated --verbose

# Re-enable feeds if needed for development
./.build/toggle-nuget-feed.sh enable dotnet-experimental
```

This ensures your package analysis reports are consistent and don't include experimental or preview packages that might skew vulnerability or deprecation assessments.

### 🔍 Package Analysis Workflow

For comprehensive package security and maintenance analysis:

```bash
# 1. Standardize feeds for analysis
./.build/toggle-nuget-feed.sh disable dotnet-experimental dotnet10

# 2. Security-first analysis with enhanced deprecation information
dotnet run -- solution.sln --only-vulnerable --only-deprecated --output security-report.txt

# 3. Full analysis with catalog URLs and descriptions
dotnet run -- solution.sln --verbose --output full-analysis.txt

# 4. Re-enable development feeds
./.build/toggle-nuget-feed.sh enable dotnet-experimental
```

This workflow leverages NuGet Inspector's enhanced capabilities including:

- **Enhanced Deprecation Information** from both CLI and API sources
- **Package Descriptions** with cleaned formatting for better readability
- **Catalog URLs** for detailed metadata inspection
- **Comprehensive Security Analysis** with vulnerability detection

---

## 📎 Related

- [NuGet Package Sources](https://learn.microsoft.com/en-us/nuget/consume-packages/configuring-nuget-behavior)
- [Microsoft dotnet-experimental feed](https://github.com/dotnet)
- [NuGet Inspector Documentation](HowToReadInspectionReport.md)
