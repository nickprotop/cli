// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI.Layout;
using SharpConsoleUI.Themes;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// A single switchable named row: a name and whether it is the currently active one.
/// </summary>
/// <param name="Name">The name (event store or namespace).</param>
/// <param name="IsActive">Whether this is the currently active entry.</param>
public record NamedActiveRow(string Name, bool IsActive);

/// <summary>
/// Shared base for the Event Stores and Namespaces tabs — a filterable table of named entries with an
/// active indicator and a detail pane, where activating an entry switches to it. Subclasses supply the
/// entity noun, the data source, and any extra detail lines.
/// </summary>
public abstract class SwitchableNameView : FilterableTableView<NamedActiveRow>
{
    /// <summary>
    /// Gets or sets the callback invoked when the user switches to a different entry.
    /// </summary>
    public Action<string>? OnSwitch { get; set; }

    /// <summary>
    /// Gets the singular entity noun used in column headers and detail text (e.g. "event store", "namespace").
    /// </summary>
    protected abstract string Noun { get; }

    /// <inheritdoc/>
    protected override ColorRole DetailColorRole => ColorRole.Info;

    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns =>
    [
        (string.Empty, TextJustification.Center, 3),
        (ColumnHeader, TextJustification.Left, null)
    ];

    /// <summary>
    /// Gets the column header for the name column (e.g. "Event Store", "Namespace").
    /// </summary>
    protected abstract string ColumnHeader { get; }

    /// <summary>
    /// Returns the available names and the currently active name from the snapshot.
    /// </summary>
    /// <param name="data">The current snapshot.</param>
    /// <returns>The full name list and the active name.</returns>
    protected abstract (IReadOnlyList<string> Names, string Active) Source(WorkbenchData data);

    /// <inheritdoc/>
    protected override IEnumerable<NamedActiveRow> GetItems(WorkbenchData data)
    {
        var (names, active) = Source(data);
        return names
            .Order()
            .Select(name => new NamedActiveRow(name, string.Equals(name, active, StringComparison.Ordinal)));
    }

    /// <inheritdoc/>
    protected override string GetKey(NamedActiveRow item) => item.Name;

    /// <inheritdoc/>
    protected override string[] BuildRow(NamedActiveRow item) =>
    [
        item.IsActive ? $"[{Theme.Success.ToMarkup()}]▸[/]" : string.Empty,
        item.IsActive ? $"[bold]{item.Name}[/]" : item.Name
    ];

    /// <inheritdoc/>
    protected override string RenderDetail(NamedActiveRow? item, WorkbenchData? data)
    {
        if (item is null)
        {
            return SelectPrompt($"a {Noun}");
        }

        var mut = Theme.Muted.ToMarkup();
        var suc = Theme.Success.ToMarkup();
        var acc = Theme.Accent.ToMarkup();

        var status = item.IsActive
            ? $"[{suc}]▸ active[/]"
            : $"[{mut}]press Enter to switch[/]";

        var lines = new List<string>
        {
            $"[{acc}][bold]{item.Name}[/][/]",
            string.Empty,
            $"[{mut}]Status[/]     {status}"
        };

        lines.AddRange(ExtraDetailLines(item, data, mut));
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Returns extra detail lines appended after the status line. The default shows the available count;
    /// subclasses can prepend context (e.g. the owning event store).
    /// </summary>
    /// <param name="item">The selected row.</param>
    /// <param name="data">The current snapshot, or null.</param>
    /// <param name="mut">Muted color markup for labels.</param>
    /// <returns>The extra detail lines.</returns>
    protected virtual IEnumerable<string> ExtraDetailLines(NamedActiveRow item, WorkbenchData? data, string mut)
    {
        var count = data is null ? 0 : Source(data).Names.Count;
        return [$"[{mut}]Available[/]  {count} {Noun}(s)"];
    }

    /// <inheritdoc/>
    protected override bool MatchesFilter(NamedActiveRow item, string filter) =>
        item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    protected override IReadOnlyList<ViewAction> GetToolbarActionTemplate() =>
    [
        new ViewAction(
            "Switch",
            "S",
            ConsoleKey.S,
            default,
            () =>
            {
                if (SelectedItem is { IsActive: false } row)
                {
                    OnSwitch?.Invoke(row.Name);
                }
            },
            Enabled: SelectedItem is { IsActive: false })
    ];
}
