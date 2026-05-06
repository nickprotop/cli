// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;
using Cratis.Cli.Commands.Chronicle.Json;
using Spectre.Console.Rendering;

namespace Cratis.Cli;

/// <summary>
/// Provides output formatting for CLI commands in table, plain, json, and json-compact formats.
/// </summary>
public static class OutputFormatter
{
    /// <summary>
    /// The primary accent color used for branding and highlights.
    /// </summary>
    public static readonly Color Accent = new(99, 135, 255);

    /// <summary>
    /// A muted color for secondary information.
    /// </summary>
    public static readonly Color Muted = new(108, 112, 134);

    /// <summary>
    /// The success color.
    /// </summary>
    public static readonly Color Success = new(80, 200, 120);

    /// <summary>
    /// The warning color.
    /// </summary>
    public static readonly Color Warning = new(255, 183, 77);

    /// <summary>
    /// The error color.
    /// </summary>
    public static readonly Color Danger = new(255, 85, 85);

    /// <summary>
    /// A darker accent color (blue-violet) used as the left stop of the banner gradient.
    /// </summary>
    public static readonly Color AccentDark = new(138, 43, 226);

    /// <summary>
    /// A lighter accent color (bright cyan) used as the right stop of the banner gradient.
    /// </summary>
    public static readonly Color AccentLight = new(0, 200, 255);

    /// <summary>
    /// The gradient color stops for the CLI banner (purple → blue → cyan).
    /// </summary>
    public static readonly Color[] BannerGradient = [AccentDark, Accent, AccentLight];

    /// <summary>
    /// The compact JSON serializer options used for CLI output (camelCase, null-omitting).
    /// </summary>
    public static readonly JsonSerializerOptions JsonSerializerOptions = CreateDefaultOptions(indented: false);

    /// <summary>
    /// The indented JSON serializer options used for CLI output (camelCase, null-omitting, indented).
    /// </summary>
    public static readonly JsonSerializerOptions IndentedJsonSerializerOptions = CreateDefaultOptions(indented: true);

    static readonly JsonSerializerOptions _jsonOptions = IndentedJsonSerializerOptions;
    static readonly JsonSerializerOptions _compactJsonOptions = JsonSerializerOptions;

    /// <summary>
    /// Writes data to the console in the specified format.
    /// For <see cref="OutputFormats.JsonCompact"/>, outputs compact (non-indented) JSON.
    /// </summary>
    /// <typeparam name="T">The type of data items.</typeparam>
    /// <param name="format">The output format (table, plain, json, or json-compact).</param>
    /// <param name="data">The data to write.</param>
    /// <param name="columns">Column definitions for tabular output.</param>
    /// <param name="getRow">Function to extract row values from each data item.</param>
    /// <param name="quietProjection">Optional function to extract a single string value for quiet output mode.</param>
    public static void Write<T>(string format, IEnumerable<T> data, string[] columns, Func<T, string[]> getRow, Func<T, string>? quietProjection = null)
    {
        switch (format)
        {
            case OutputFormats.JsonQuiet:
                WriteJsonQuiet(data, quietProjection, getRow);
                break;
            case OutputFormats.Quiet:
                WriteQuiet(data, getRow, quietProjection);
                break;
            case OutputFormats.JsonCompact:
            case OutputFormats.Json:
                WriteJson(data, OptionsFor(format));
                break;
            case OutputFormats.Plain:
                WritePlain(data, columns, getRow);
                break;
            default:
                WriteTable(data, columns, getRow);
                break;
        }
    }

    /// <summary>
    /// Writes a single object to the console in the specified format.
    /// For <see cref="OutputFormats.JsonCompact"/>, emits compact JSON.
    /// </summary>
    /// <typeparam name="T">The type of the object.</typeparam>
    /// <param name="format">The output format.</param>
    /// <param name="data">The object to write.</param>
    /// <param name="render">Function to render the object as text for non-json formats.</param>
    public static void WriteObject<T>(string format, T data, Action<T>? render = null)
    {
        if (string.Equals(format, OutputFormats.Quiet, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(format, OutputFormats.Json, StringComparison.Ordinal) || string.Equals(format, OutputFormats.JsonCompact, StringComparison.Ordinal) || string.Equals(format, OutputFormats.JsonQuiet, StringComparison.Ordinal))
        {
            WriteJsonSafe(data, OptionsFor(format));
            return;
        }

        if (render is not null)
        {
            render(data);
            return;
        }

        WriteJsonSafe(data);
    }

    /// <summary>
    /// Writes a simple message to the console.
    /// For <see cref="OutputFormats.JsonCompact"/>, emits compact JSON.
    /// </summary>
    /// <param name="format">The output format.</param>
    /// <param name="message">The message text.</param>
    public static void WriteMessage(string format, string message)
    {
        if (string.Equals(format, OutputFormats.Quiet, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(format, OutputFormats.Json, StringComparison.Ordinal) || string.Equals(format, OutputFormats.JsonCompact, StringComparison.Ordinal))
        {
            var json = JsonSerializer.Serialize(new { message }, OptionsFor(format));
            Console.WriteLine(json);
            return;
        }

        AnsiConsole.Write(new Markup($"  [green]\u2713[/] {message.EscapeMarkup()}"));
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Writes an error message and exits with the given code.
    /// </summary>
    /// <param name="format">The output format.</param>
    /// <param name="error">The error message.</param>
    /// <param name="suggestion">An optional suggestion for resolution.</param>
    /// <param name="errorCode">An optional machine-readable error code included in JSON output.</param>
    public static void WriteError(string format, string error, string? suggestion = null, string? errorCode = null)
    {
        if (string.Equals(format, OutputFormats.Json, StringComparison.Ordinal) || string.Equals(format, OutputFormats.JsonCompact, StringComparison.Ordinal) || string.Equals(format, OutputFormats.Quiet, StringComparison.Ordinal))
        {
            var errorObj = new Dictionary<string, string>();
            if (errorCode is not null)
            {
                errorObj["error"] = errorCode;
                errorObj["message"] = error;
            }
            else
            {
                errorObj["error"] = error;
            }

            if (suggestion is not null)
            {
                errorObj["suggestion"] = suggestion;
            }

            var json = JsonSerializer.Serialize(errorObj, OptionsFor(format));
            Console.Error.WriteLine(json);
            return;
        }

        var content = new Markup($"[bold]{error.EscapeMarkup()}[/]");
        var panel = new Panel(content)
            .Header(" Error ")
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Danger))
            .Padding(1, 0);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(panel);

        if (suggestion is not null)
        {
            AnsiConsole.MarkupLine($"  [{Muted.ToMarkup()}]\u2192 {suggestion.EscapeMarkup()}[/]");
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Writes a labeled value pair with consistent formatting.
    /// </summary>
    /// <param name="label">The label text.</param>
    /// <param name="value">The value text.</param>
    /// <param name="labelWidth">The width for label alignment.</param>
    public static void WriteLabel(string label, string value, int labelWidth = 20)
    {
        AnsiConsole.Markup($"  [{Accent.ToMarkup()}]{label.EscapeMarkup().PadRight(labelWidth)}[/]");
        AnsiConsole.MarkupLine(value.EscapeMarkup());
    }

    /// <summary>
    /// Writes a labeled value pair where the value is dimmed (for missing/default values).
    /// </summary>
    /// <param name="label">The label text.</param>
    /// <param name="value">The value text.</param>
    /// <param name="labelWidth">The width for label alignment.</param>
    public static void WriteLabelDim(string label, string value, int labelWidth = 20)
    {
        AnsiConsole.Markup($"  [{Accent.ToMarkup()}]{label.EscapeMarkup().PadRight(labelWidth)}[/]");
        AnsiConsole.MarkupLine($"[{Muted.ToMarkup()}]{value.EscapeMarkup()}[/]");
    }

    /// <summary>
    /// Writes a section header with a horizontal rule.
    /// </summary>
    /// <param name="title">The section title.</param>
    public static void WriteSection(string title)
    {
        AnsiConsole.WriteLine();
        var rule = new Rule($"[bold]{title.EscapeMarkup()}[/]")
            .RuleStyle(new Style(Muted))
            .LeftJustified();
        AnsiConsole.Write(rule);
    }

    static JsonSerializerOptions OptionsFor(string format) =>
        string.Equals(format, OutputFormats.JsonCompact, StringComparison.Ordinal) || string.Equals(format, OutputFormats.Quiet, StringComparison.Ordinal) || string.Equals(format, OutputFormats.JsonQuiet, StringComparison.Ordinal) ? _compactJsonOptions : _jsonOptions;

    static JsonSerializerOptions CreateDefaultOptions(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new EnumConverterFactory());

        // JsonStringEnumConverter handles plain System.Enum fields that EnumConverterFactory
        // (which targets Cratis concept types) leaves as integers.
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ConceptAsJsonConverterFactory());
        AddCliConverters(options);

        return options;
    }

    static void AddCliConverters(JsonSerializerOptions options)
    {
        options.Converters.Add(new SerializableDateTimeOffsetJsonConverter());
        options.Converters.Add(new ContractsEventTypeFromDefinitionsDictionaryConverter());
        options.Converters.Add(new ContractsEventTypeJoinDefinitionsDictionaryConverter());
        options.Converters.Add(new ContractsEventTypeRemovedWithDefinitionsDictionaryConverter());
        options.Converters.Add(new ContractsEventTypeRemovedWithJoinDefinitionsDictionaryConverter());
    }

    static void WriteJson<T>(IEnumerable<T> data, JsonSerializerOptions? options = null)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, options ?? _jsonOptions);
            Console.WriteLine(json);
        }
        catch (NotSupportedException ex)
        {
            WriteError(OutputFormats.Json, $"Failed to serialize output as JSON: {ex.Message}");
        }
    }

    static void WriteJsonSafe<T>(T data, JsonSerializerOptions? options = null)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, options ?? _jsonOptions);
            Console.WriteLine(json);
        }
        catch (NotSupportedException ex)
        {
            WriteError(OutputFormats.Json, $"Failed to serialize output as JSON: {ex.Message}");
        }
    }

    static void WriteTable<T>(IEnumerable<T> data, string[] columns, Func<T, string[]> getRow)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Muted);

        foreach (var column in columns)
        {
            table.AddColumn(new TableColumn($"[bold]{column.EscapeMarkup()}[/]").Padding(1, 0));
        }

        foreach (var item in data)
        {
            table.AddRow(getRow(item).Select(v => new Markup(v.EscapeMarkup()) as IRenderable).ToArray());
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
    }

    static void WriteQuiet<T>(IEnumerable<T> data, Func<T, string[]> getRow, Func<T, string>? quietProjection)
    {
        foreach (var item in data)
        {
            Console.WriteLine(quietProjection is not null ? quietProjection(item) : getRow(item)[0]);
        }
    }

    static void WriteJsonQuiet<T>(IEnumerable<T> data, Func<T, string>? quietProjection, Func<T, string[]> getRow)
    {
        var items = data.Select(item => quietProjection is not null ? quietProjection(item) : getRow(item)[0]);
        var json = JsonSerializer.Serialize(items, _compactJsonOptions);
        Console.WriteLine(json);
    }

    static void WritePlain<T>(IEnumerable<T> data, string[] columns, Func<T, string[]> getRow)
    {
        Console.WriteLine(string.Join('\t', columns));
        foreach (var item in data)
        {
            Console.WriteLine(string.Join('\t', getRow(item)));
        }
    }
}
