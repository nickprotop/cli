// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI.Layout;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Observers navigation item — sortable, filterable table with a detail pane showing observer state and event types.
/// </summary>
public class ObserversView : FilterableTableView<ObserverInformation>
{
    /// <summary>Gets the currently selected observer, or <see langword="null"/> if none is selected.</summary>
    public ObserverInformation? SelectedObserver => SelectedItem;

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
    protected override bool HasCheckboxMode => true;

    /// <inheritdoc/>
    protected override IEnumerable<(string Label, string? Shortcut, Action Execute)> GetContextMenuActions(ObserverInformation item)
    {
        if (OnReplay is not null)
        {
            yield return ("Replay observer", "R", () => OnReplay(item));
        }

        var checkedCount = Checked.Count;
        if (OnReplayAll is not null && checkedCount > 1)
        {
            yield return ($"Replay {checkedCount} checked", null, () => OnReplayAll(Checked));
        }
    }

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
            return $"[{WorkbenchColors.Muted.ToMarkup()}]Select an observer.[/]";
        }

        var mut = WorkbenchColors.Muted.ToMarkup();
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

    static int ObserverSortOrder(ObserverInformation o) => o.RunningState switch
    {
        ObserverRunningState.Disconnected => 0,
        ObserverRunningState.Replaying => 1,
        ObserverRunningState.Active => 2,
        ObserverRunningState.Suspended => 3,
        _ => 4
    };

    static string GetObserverStateColor(ObserverInformation obs) => obs.RunningState switch
    {
        ObserverRunningState.Active => WorkbenchColors.Success.ToMarkup(),
        ObserverRunningState.Replaying => WorkbenchColors.Warning.ToMarkup(),
        ObserverRunningState.Disconnected => WorkbenchColors.Danger.ToMarkup(),
        _ => WorkbenchColors.Muted.ToMarkup()
    };

    static string GetObserverIcon(ObserverInformation obs) => obs.RunningState switch
    {
        ObserverRunningState.Active => "●",
        ObserverRunningState.Replaying => "▲",
        ObserverRunningState.Disconnected => "⊘",
        _ => "○"
    };
}
