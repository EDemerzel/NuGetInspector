param (
    [Parameter(Mandatory = $true)]
    [ValidateSet("enable", "disable")]
    [string]$Action,

    [Parameter(Mandatory = $true)]
    [string[]]$Sources,

    [string]$ConfigPath = ".\nuget.config"
)

if (-not (Test-Path $ConfigPath)) {
    Write-Error "nuget.config not found at '$ConfigPath'."
    exit 1
}

[xml]$nugetXml = Get-Content $ConfigPath

# Ensure <disabledPackageSources> exists
$disabledNode = $nugetXml.configuration.disabledPackageSources
if (-not $disabledNode) {
    $disabledNode = $nugetXml.CreateElement("disabledPackageSources")
    $nugetXml.configuration.AppendChild($disabledNode) | Out-Null
}

foreach ($source in $Sources) {
    $existing = $disabledNode.add | Where-Object { $_.key -eq $source }

    if ($Action -eq "disable") {
        if ($existing) {
            $existing.value = "true"
        }
        else {
            $add = $nugetXml.CreateElement("add")
            $add.SetAttribute("key", $source)
            $add.SetAttribute("value", "true")
            $disabledNode.AppendChild($add) | Out-Null
        }
        Write-Host "Disabled source: $source"
    }
    elseif ($Action -eq "enable") {
        if ($existing) {
            $disabledNode.RemoveChild($existing) | Out-Null
            Write-Host "Enabled source: $source"
        }
        else {
            Write-Host "Source '$source' is already enabled or not listed."
        }
    }
}

# Save changes
$nugetXml.Save($ConfigPath)
Write-Host "`n✔ nuget.config updated successfully at '$ConfigPath'"
