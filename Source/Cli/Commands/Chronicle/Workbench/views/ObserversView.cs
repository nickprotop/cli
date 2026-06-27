// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Themes;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Observers navigation item — sortable, filterable table with a detail pane showing observer state and event types.
/// </summary>
public class ObserversView : FilterableTableView<ObserverInformation>
{
    /// <summary>Gets the currently selected observer, or <see langword="null"/> if none is selected.</summary>
    public ObserverInformation? SelectedObserver => SelectedItem;

    /// <inheritdoc/>
    public override string ViewHelp =>
        "Lists all registered observers and their current running state.\n" +
        "  [R]  Replay the selected observer from the beginning\n" +
        "  [Space]  Check / uncheck row for bulk operations\n" +
        "  Check rows + toolbar / right-click → replay all checked";

    /// <summary>
    /// Gets or sets the callback invoked when the user requests a replay of the selected observer.
    /// </summary>
    public Action<ObserverInformation>? OnReplay { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user requests a bulk replay of all checked observers.
    /// </summary>
    public Action<IReadOnlyList<ObserverInformation>>? OnReplayAll { get; set; }

    /// <summary>
    /// Gets all observers that are currently checked (checkbox mode).
    /// </summary>
    public IReadOnlyList<ObserverInformation> Checked => CheckedItems;

    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns =>
    [
        ("State", TextJustification.Left, 18),
        ("Id", TextJustification.Left, null),
        ("Type", TextJustification.Left, 16)
    ];

    /// <inheritdoc/>
    protected override string DetailPanelHeader => "OBSERVER";

    /// <inheritdoc/>
    protected override ColorRole DetailColorRole => ColorRole.Warning;

    /// <inheritdoc/>
    protected override bool HasCheckboxMode => true;

    /// <inheritdoc/>
    protected override string? PageTitle => "OBSERVERS";

    /// <inheritdoc/>
    protected override IReadOnlyList<ViewAction> GetToolbarActionTemplate()
    {
        List<ViewAction> actions = [];
        if (OnReplay is not null)
        {
            actions.Add(SingleAction("Replay observer", ConsoleKey.R, item => OnReplay(item)));
        }

        if (OnReplayAll is not null)
        {
            actions.Add(BulkAction("Replay", items => OnReplayAll(items)));
        }

        return actions;
    }

    /// <inheritdoc/>
    protected override IComparer<ObserverInformation> GetColumnComparer(int columnIndex) => columnIndex switch
    {
        0 => Comparer<ObserverInformation>.Create((a, b) => ObserverSortOrder(a).CompareTo(ObserverSortOrder(b))),
        _ => base.GetColumnComparer(columnIndex)
    };

    /// <inheritdoc/>
    protected override IEnumerable<ObserverInformation> GetItems(WorkbenchData data) =>
        data.Observers.OrderBy(ObserverSortOrder).ThenBy(o => o.Id);

    /// <inheritdoc/>
    protected override string GetKey(ObserverInformation item) => item.Id;

    /// <inheritdoc/>
    protected override string[] BuildRow(ObserverInformation item)
    {
        var stateColor = GetObserverStateColor(item);
        var icon = GetObserverIcon(item);

        return
        [
            $"[{stateColor}]{icon} {item.RunningState}[/]",
            item.Id,
            item.Type.ToString()
        ];
    }

    /// <inheritdoc/>
    protected override string RenderDetail(ObserverInformation? item, WorkbenchData? data)
    {
        if (item is null)
        {
            return SelectPrompt("an observer");
        }

        var mut = Theme.Muted.ToMarkup();
        var stateColor = GetObserverStateColor(item);

        var lines = new List<string>
        {
            $"[{mut}]Id[/]      {item.Id}",
            $"[{mut}]Type[/]    {item.Type}",
            $"[{mut}]State[/]   [{stateColor}]{item.RunningState}[/]",
            $"[{mut}]Last seq[/] {(item.LastHandledEventSequenceNumber == ulong.MaxValue ? "N/A" : item.LastHandledEventSequenceNumber.ToString("N0"))}",
            string.Empty,
            $"[{mut}]Event Types:[/]"
        };

        foreach (var et in (item.EventTypes ?? []).OrderBy(e => e.Id))
        {
            lines.Add($"  • {et.Id} gen {et.Generation}");
        }

        if (OnReplay is not null)
        {
            lines.Add(string.Empty);
            lines.Add($"[{mut}]Press[/] [bold]R[/] [{mut}]to replay[/]");
        }

        return string.Join('\n', lines);
    }

    /// <inheritdoc/>
    protected override bool MatchesFilter(ObserverInformation item, string filter)
    {
        if (filter.StartsWith("state:", StringComparison.OrdinalIgnoreCase))
        {
            return item.RunningState.ToString().Contains(filter[6..], StringComparison.OrdinalIgnoreCase);
        }

        if (filter.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
        {
            return item.Type.ToString().Contains(filter[5..], StringComparison.OrdinalIgnoreCase);
        }

        // event:TypeId — match observers that subscribe to the given event type ID
        if (filter.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
        {
            var eventTypeId = filter[6..];
            return (item.EventTypes ?? []).Any(et => et.Id.Contains(eventTypeId, StringComparison.OrdinalIgnoreCase));
        }

        return item.Id.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    protected override IEnumerable<string> GetCompletions(string input) =>
    [
        "state:active",
        "state:replaying",
        "state:disconnected",
        "state:suspended",
        "type:projection",
        "type:reducer",
        "type:reactor"
    ];

    static int ObserverSortOrder(ObserverInformation o) => ObserverPresentation.SortOrder(o);

    static string GetObserverIcon(ObserverInformation obs) => ObserverPresentation.Icon(obs);

    string GetObserverStateColor(ObserverInformation obs) => ObserverPresentation.StateColor(obs, Theme);
}
