<#
.SYNOPSIS
    XML Documentation to Styled HTML Converter

.DESCRIPTION
    Converts Visual Studio XML documentation files to styled HTML format
    for better readability and presentation.

.PARAMETER InputXmlPath
    Path to the input XML documentation file

.PARAMETER OutputHtmlPath
    Path for the output HTML file

.PARAMETER Title
    Title for the HTML document

.EXAMPLE
    .\Convert-XmlDocToStyledHtml.ps1

.EXAMPLE
    .\Convert-XmlDocToStyledHtml.ps1 -InputXmlPath "MyApp.xml" -OutputHtmlPath "MyApp_Docs.html" -Title "My Application Documentation"

.NOTES
    Requires PowerShell 5.0 or later
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$InputXmlPath = "C:\Users\rofli\iCloudDrive\Source\NuGetInspectorApp\NuGetInspectorApp\bin\Debug\net9.0\NuGetInspectorApp.xml",

    [Parameter(Mandatory = $false)]
    [string]$OutputHtmlPath = "NuGetInspectorApp_Doc_Styled.html",

    [Parameter(Mandatory = $false)]
    [string]$Title = "NuGetInspectorApp Documentation"
)

# Load required assemblies
Add-Type -AssemblyName System.Web

function Get-CssStyles {
    <#
    .SYNOPSIS
        Returns the CSS styles for the HTML documentation.
    #>
    return @"
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            margin: 2em;
            background-color: #f8f9fa;
            color: #333;
            line-height: 1.6;
        }

        h1 {
            color: #0d47a1;
            border-bottom: 2px solid #1976d2;
            padding-bottom: 0.3em;
            margin-bottom: 1em;
        }

        h2 {
            color: #1565c0;
            margin-top: 1.5em;
            margin-bottom: 0.5em;
        }

        p {
            margin: 0.5em 0 1em;
            line-height: 1.6;
        }

        .member-block {
            background-color: #ffffff;
            border-left: 4px solid #42a5f5;
            padding: 1em;
            margin: 1em 0;
            box-shadow: 0 2px 4px rgba(0,0,0,0.05);
            border-radius: 4px;
        }

        code {
            background-color: #eceff1;
            padding: 0.2em 0.4em;
            border-radius: 3px;
            font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
            font-size: 0.9em;
        }

        .member-name {
            font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
            font-size: 0.9em;
            color: #424242;
        }

        .no-summary {
            color: #757575;
            font-style: italic;
        }
"@
}

function Get-MemberInfo {
    <#
    .SYNOPSIS
        Extracts member name and summary from XML member element.

    .PARAMETER MemberElement
        XML element containing member documentation

    .OUTPUTS
        PSCustomObject with Name and Summary properties
    #>
    param (
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlElement]$MemberElement
    )

    # Get name attribute - matches Python: member_element.attrib.get("name", "Unknown")
    $name = if ($MemberElement.GetAttribute("name")) {
        $MemberElement.GetAttribute("name")
    } else {
        "Unknown"
    }

    # Find summary element - matches Python: member_element.find("summary")
    $summaryElement = $MemberElement.SelectSingleNode("summary")

    # Extract summary text - matches Python logic exactly
    if ($summaryElement -and $summaryElement.'#text' -and $summaryElement.'#text'.Trim()) {
        $summaryText = $summaryElement.'#text'.Trim()
        $summary = [System.Web.HttpUtility]::HtmlEncode($summaryText)
    } else {
        $summary = '<span class="no-summary">No summary available.</span>'
    }

    return [PSCustomObject]@{
        Name = $name
        Summary = $summary
    }
}

function New-HtmlContent {
    <#
    .SYNOPSIS
        Generates complete HTML content from XML documentation root.

    .PARAMETER XmlDocument
        XML document containing the documentation

    .PARAMETER Title
        Title for the HTML document

    .OUTPUTS
        Complete HTML content as string
    #>
    param (
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlDocument]$XmlDocument,

        [Parameter(Mandatory = $false)]
        [string]$Title = "NuGetInspectorApp Documentation"
    )

    $encodedTitle = [System.Web.HttpUtility]::HtmlEncode($Title)
    $cssStyles = Get-CssStyles

    # Build HTML structure - matches Python exactly
    $htmlParts = @(
        "<!DOCTYPE html>",
        "<html lang='en'>",
        "<head>",
        "    <meta charset='UTF-8'>",
        "    <meta name='viewport' content='width=device-width, initial-scale=1.0'>",
        "    <title>$encodedTitle</title>",
        "    <style>",
        $cssStyles,
        "    </style>",
        "</head>",
        "<body>",
        "    <h1>$encodedTitle</h1>"
    )

    # Process all member elements - matches Python: xml_root.findall(".//member")
    $members = $XmlDocument.SelectNodes("//member")
    foreach ($member in $members) {
        $memberInfo = Get-MemberInfo -MemberElement $member
        $encodedName = [System.Web.HttpUtility]::HtmlEncode($memberInfo.Name)

        $htmlParts += @(
            "    <div class='member-block'>",
            "        <h2><code class='member-name'>$encodedName</code></h2>",
            "        <p>$($memberInfo.Summary)</p>",
            "    </div>"
        )
    }

    $htmlParts += @(
        "</body>",
        "</html>"
    )

    return $htmlParts -join "`n"
}

function Convert-XmlToHtml {
    <#
    .SYNOPSIS
        Converts XML documentation file to styled HTML.

    .PARAMETER InputPath
        Path to the input XML documentation file

    .PARAMETER OutputPath
        Path for the output HTML file

    .PARAMETER DocumentTitle
        Title for the HTML document
    #>
    param (
        [Parameter(Mandatory = $true)]
        [string]$InputPath,

        [Parameter(Mandatory = $true)]
        [string]$OutputPath,

        [Parameter(Mandatory = $false)]
        [string]$DocumentTitle = "NuGetInspectorApp Documentation"
    )

    # Validate input file exists - matches Python logic
    if (-not (Test-Path -Path $InputPath -PathType Leaf)) {
        throw "Input file not found: $InputPath"
    }

    try {
        # Parse XML documentation
        Write-Verbose "📖 Parsing XML documentation from: $InputPath"
        [xml]$xmlDoc = Get-Content -Path $InputPath -ErrorAction Stop

        # Generate HTML content
        Write-Verbose "🔄 Generating HTML content..."
        $htmlContent = New-HtmlContent -XmlDocument $xmlDoc -Title $DocumentTitle

        # Write to output file
        Write-Verbose "💾 Writing HTML to: $OutputPath"
        $htmlContent | Set-Content -Path $OutputPath -Encoding UTF8 -ErrorAction Stop

        # Get member count for reporting
        $memberCount = $xmlDoc.SelectNodes("//member").Count

        Write-Host "✅ Styled documentation written to $OutputPath" -ForegroundColor Green
        Write-Host "📊 Processed $memberCount documentation members" -ForegroundColor Cyan

    } catch [System.Xml.XmlException] {
        Write-Error "❌ XML Parse Error: $($_.Exception.Message)"
        throw
    } catch {
        Write-Error "❌ Unexpected error: $($_.Exception.Message)"
        throw
    }
}

function Test-Prerequisites {
    <#
    .SYNOPSIS
        Tests if all prerequisites are available.
    #>
    try {
        # Test if System.Web assembly is available
        [System.Web.HttpUtility]::HtmlEncode("test") | Out-Null
        Write-Verbose "✅ System.Web assembly loaded successfully"
        return $true
    } catch {
        Write-Error "❌ Failed to load required assemblies: $($_.Exception.Message)"
        return $false
    }
}

function Main {
    <#
    .SYNOPSIS
        Main function to execute the conversion.
    #>
    Write-Verbose "🚀 Starting XML to HTML conversion..."
    Write-Verbose "📁 Input: $InputXmlPath"
    Write-Verbose "📄 Output: $OutputHtmlPath"
    Write-Verbose "📰 Title: $Title"

    # Test prerequisites
    if (-not (Test-Prerequisites)) {
        exit 1
    }

    try {
        Convert-XmlToHtml -InputPath $InputXmlPath -OutputPath $OutputHtmlPath -DocumentTitle $Title
        exit 0
    } catch {
        Write-Host "❌ Conversion failed: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}

# Execute main function
Main