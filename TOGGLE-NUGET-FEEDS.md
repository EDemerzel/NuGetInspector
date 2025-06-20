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

---

## 🧼 Best Practices

- Keep the `.build/` folder for automation-safe scripts.
- Add execution policies if using in CI:

  - PowerShell: `Set-ExecutionPolicy RemoteSigned -Scope Process`
  - Bash: Ensure `chmod +x` on `.sh` script

- Always run the toggle script **before** restore or build steps if you're changing feed states dynamically.

---

## 📎 Related

- [NuGet Package Sources](https://learn.microsoft.com/en-us/nuget/consume-packages/configuring-nuget-behavior)
- [Microsoft dotnet-experimental feed](https://github.com/dotnet)
