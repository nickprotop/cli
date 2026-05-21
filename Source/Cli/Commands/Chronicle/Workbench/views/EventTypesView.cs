// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Event Types navigation item — filterable table of registered event types with schema details in the right pane.
/// </summary>
public class EventTypesView : FilterableTableView<EventTypeRegistration>
{
    /// <summary>Gets the currently selected event type registration, or <see langword="null"/> if none is selected.</summary>
    public EventTypeRegistration? SelectedEventType => SelectedItem;

    /// <summary>
    /// Gets or sets the callback invoked when the user requests to view observers for the selected event type.
    /// </summary>
    public Action<EventTypeRegistration>? OnViewObservers { get; set; }

    /// <inheritdoc/>
    public override string ViewHelp =>
        "Lists all registered event types and their schemas.\n" +
        "  [V]  Find observers subscribed to the selected event type";

    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns =>
    [
        ("Id", TextJustification.Left, null),
        ("Gen", TextJustification.Right, 6),
        ("Owner", TextJustification.Left, 20)
    ];

    /// <inheritdoc/>
    protected override string DetailPanelHeader => "EVENT TYPE";

    /// <inheritdoc/>
    protected override int DefaultSortColumn => 0;

    /// <inheritdoc/>
    protected override SortDirection DefaultSortDirection => SortDirection.Ascending;

    /// <inheritdoc/>
    protected override bool IsSortableColumn(int columnIndex) => columnIndex == 0;

    /// <inheritdoc/>
    protected override IEnumerable<(string Label, string? Shortcut, Action Execute)> GetContextMenuActions(EventTypeRegistration item)
    {
        if (OnViewObservers is not null)
        {
            yield return ("View observers for this type", "V", () => OnViewObservers(item));
        }
    }

    /// <inheritdoc/>
    protected override IEnumerable<EventTypeRegistration> GetItems(WorkbenchData data) =>
        data.EventTypeRegistrations.OrderBy(r => r.Type.Id).ThenBy(r => r.Type.Generation);

    /// <inheritdoc/>
    protected override string GetKey(EventTypeRegistration item) => $"{item.Type.Id}+{item.Type.Generation}";

    /// <inheritdoc/>
    protected override string[] BuildRow(EventTypeRegistration item) =>
        [item.Type.Id, item.Type.Generation.ToString().PadLeft(6), item.Owner.ToString()];

    /// <inheritdoc/>
    protected override string RenderDetail(EventTypeRegistration? item, WorkbenchData? data)
    {
        if (item is null)
        {
            return $"[{WorkbenchColors.Muted.ToMarkup()}]Select an event type.[/]";
        }

        var acc = WorkbenchColors.Accent.ToMarkup();
        var mut = WorkbenchColors.Muted.ToMarkup();
        var schemaContent = !string.IsNullOrEmpty(item.Schema)
            ? JsonYamlFormatter.FormatAsYaml(item.Schema, mut)
            : $"[{mut}](no schema)[/]";

        return string.Join('\n', new[]
        {
            $"[{mut}]Id[/]           {item.Type.Id}",
            $"[{mut}]Generation[/]   {item.Type.Generation}",
            $"[{mut}]Owner[/]        {item.Owner}",
            $"[{mut}]Source[/]       {item.Source}",
            $"[{mut}]Tombstone[/]    {item.Type.Tombstone}",
            string.Empty,
            $"[{acc}]Schema:[/]",
            schemaContent,
            string.Empty,
            $"[{acc}]Actions:[/]",
            $"  [{mut}][V][/] View observers for this type"
        });
    }

    /// <inheritdoc/>
    protected override bool MatchesFilter(EventTypeRegistration item, string filter)
    {
        if (filter.StartsWith("owner:", StringComparison.OrdinalIgnoreCase))
        {
            return item.Owner.ToString().Contains(filter[6..], StringComparison.OrdinalIgnoreCase);
        }

        if (filter.StartsWith("gen:", StringComparison.OrdinalIgnoreCase))
        {
            return item.Type.Generation.ToString().Contains(filter[4..], StringComparison.OrdinalIgnoreCase);
        }

        return item.Type.Id.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    protected override IEnumerable<string> GetCompletions(string input) =>
    [
        "owner:client",
        "owner:server",
        "gen:1",
        "gen:2"
    ];
}
