// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using System.Text.Json;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Formats JSON strings as human-readable YAML-style markup for display in workbench detail panes.
/// </summary>
static class JsonYamlFormatter
{
    /// <summary>
    /// Formats a JSON string as an indented, human-readable YAML-style representation.
    /// Falls back to wrapping the raw string in muted markup when parsing fails.
    /// </summary>
    /// <param name="json">The JSON string to format.</param>
    /// <param name="mutedMarkup">The SharpConsoleUI markup tag applied to property key labels.</param>
    /// <returns>A formatted markup string.</returns>
    internal static string FormatAsYaml(string json, string mutedMarkup)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return $"[{mutedMarkup}](empty)[/]";
        }

        try
        {
            var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            AppendElement(doc.RootElement, sb, 0, mutedMarkup);
            return sb.ToString().TrimEnd();
        }
        catch
        {
            return $"[{mutedMarkup}]{json}[/]";
        }
    }

    static void AppendElement(JsonElement element, StringBuilder sb, int indent, string mut)
    {
        var pad = new string(' ', indent * 2);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    // Skip JSON Schema noise that clutters the display without adding value.
                    if (IsJsonSchemaNoise(prop.Name))
                    {
                        continue;
                    }

                    if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        sb.AppendLine($"{pad}[{mut}]{prop.Name}:[/]");
                        AppendElement(prop.Value, sb, indent + 1, mut);
                    }
                    else
                    {
                        sb.AppendLine($"{pad}[{mut}]{prop.Name}:[/] {ScalarText(prop.Value)}");
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        sb.AppendLine($"{pad}-");
                        AppendElement(item, sb, indent + 1, mut);
                    }
                    else
                    {
                        sb.AppendLine($"{pad}- {ScalarText(item)}");
                    }
                }

                break;

            default:
                sb.AppendLine($"{pad}{ScalarText(element)}");
                break;
        }
    }

    static bool IsJsonSchemaNoise(string key) =>
        string.Equals(key, "required", StringComparison.Ordinal) ||
        string.Equals(key, "$schema", StringComparison.Ordinal) ||
        string.Equals(key, "definitions", StringComparison.Ordinal) ||
        string.Equals(key, "$defs", StringComparison.Ordinal);

    static string ScalarText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Null => "~",
        _ => element.GetRawText()
    };
}
