#!/usr/bin/env python3
"""
XML Documentation to Styled HTML Converter

Converts Visual Studio XML documentation files to styled HTML format
for better readability and presentation.
"""

import xml.etree.ElementTree as ET
import html
from pathlib import Path
from typing import Optional


def get_css_styles() -> str:
    """Returns the CSS styles for the HTML documentation."""
    return """
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
    """


def extract_member_info(member_element: ET.Element) -> tuple[str, str]:
    """
    Extracts member name and summary from XML member element.

    Args:
        member_element: XML element containing member documentation

    Returns:
        Tuple of (member_name, summary_text)
    """
    name = member_element.attrib.get("name", "Unknown")
    summary_elem = member_element.find("summary")

    if summary_elem is not None and summary_elem.text:
        summary = html.escape(summary_elem.text.strip())
    else:
        summary = '<span class="no-summary">No summary available.</span>'

    return name, summary


def generate_html_content(xml_root: ET.Element, title: str = "NuGetInspectorApp Documentation") -> str:
    """
    Generates complete HTML content from XML documentation root.

    Args:
        xml_root: Root element of the XML documentation
        title: Title for the HTML document

    Returns:
        Complete HTML content as string
    """
    html_parts = [
        "<!DOCTYPE html>",
        "<html lang='en'>",
        "<head>",
        "    <meta charset='UTF-8'>",
        "    <meta name='viewport' content='width=device-width, initial-scale=1.0'>",
        f"    <title>{html.escape(title)}</title>",
        "    <style>",
        get_css_styles(),
        "    </style>",
        "</head>",
        "<body>",
        f"    <h1>{html.escape(title)}</h1>"
    ]

    # Process all member elements
    members = xml_root.findall(".//member")
    for member in members:
        name, summary = extract_member_info(member)

        html_parts.extend([
            "    <div class='member-block'>",
            f"        <h2><code class='member-name'>{html.escape(name)}</code></h2>",
            f"        <p>{summary}</p>",
            "    </div>"
        ])

    html_parts.extend([
        "</body>",
        "</html>"
    ])

    return "\n".join(html_parts)


def convert_xml_to_html(input_path: str, output_path: str) -> None:
    """
    Converts XML documentation file to styled HTML.

    Args:
        input_path: Path to the input XML documentation file
        output_path: Path for the output HTML file

    Raises:
        FileNotFoundError: If input file doesn't exist
        ET.ParseError: If XML file is malformed
    """
    input_file = Path(input_path)
    output_file = Path(output_path)

    if not input_file.exists():
        raise FileNotFoundError(f"Input file not found: {input_path}")

    # Parse XML documentation
    tree = ET.parse(input_file)
    root = tree.getroot()

    # Generate HTML content
    html_content = generate_html_content(root)

    # Write to output file
    output_file.write_text(html_content, encoding="utf-8")

    print(f"✅ Styled documentation written to {output_file}")
    print(f"📊 Processed {len(root.findall('.//member'))} documentation members")


def main():
    """Main function to execute the conversion."""
    input_file = (
        "C:\\Users\\rofli\\iCloudDrive\\Source\\NuGetInspectorApp\\"
        "NuGetInspectorApp\\bin\\Debug\\net9.0\\NuGetInspectorApp.xml"
    )
    output_file = "NuGetInspectorApp_Doc_Styled.html"

    try:
        convert_xml_to_html(input_file, output_file)
    except FileNotFoundError as e:
        print(f"❌ Error: {e}")
    except ET.ParseError as e:
        print(f"❌ XML Parse Error: {e}")
    except Exception as e:
        print(f"❌ Unexpected error: {e}")


if __name__ == "__main__":
    main()