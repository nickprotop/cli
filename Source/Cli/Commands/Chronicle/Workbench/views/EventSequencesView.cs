// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI.Layout;
using SharpConsoleUI.Themes;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Event Sequences navigation item — filterable, sortable table of recent events with a detail pane showing event content.
/// </summary>
public class EventSequencesView : FilterableTableView<AppendedEvent>
{
    /// <summary>Gets the currently selected event, or <see langword="null"/> if none is selected.</summary>
    public AppendedEvent? SelectedEvent => SelectedItem;

    /// <summary>
    /// Gets or sets the callback invoked when the user requests to view the event type definition.
    /// </summary>
    public Action<AppendedEvent>? OnViewEventTypeDefinition { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user requests to view observers for this event type.
    /// </summary>
    public Action<AppendedEvent>? OnViewObserversForType { get; set; }

    /// <inheritdoc/>
    public override string ViewHelp =>
        "Shows recent events appended to the event log.\n" +
        "  [D]  Navigate to the selected event's type definition\n" +
        "  [V]  Find observers subscribed to this event type";

    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns =>
    [
        ("#", TextJustification.Right, 14),
        ("Occurred", TextJustification.Left, 22),
        ("Event Type", TextJustification.Left, null),
        ("Source", TextJustification.Left, 30)
    ];

    /// <inheritdoc/>
    protected override string DetailPanelHeader => "EVENT";

    /// <inheritdoc/>
    protected override ColorRole DetailColorRole => ColorRole.Info;

    /// <inheritdoc/>
    protected override string? PageTitle => "EVENT SEQUENCES";

    /// <inheritdoc/>
    protected override string EmptyStateMessage => "No events yet.";

    /// <inheritdoc/>
    protected override IEnumerable<AppendedEvent> GetItems(WorkbenchData data) => data.RecentEvents;

    /// <inheritdoc/>
    protected override string GetKey(AppendedEvent item) => item.Context.SequenceNumber.ToString();

    /// <inheritdoc/>
    protected override string[] BuildRow(AppendedEvent item) =>
    [
        item.Context.SequenceNumber.ToString().PadLeft(14),
        FormatRelativeTime(item.Context.Occurred),
        item.Context.EventType.Id,
        item.Context.EventSourceId ?? string.Empty
    ];

    /// <inheritdoc/>
    protected override IReadOnlyList<ViewAction> GetToolbarActionTemplate()
    {
        List<ViewAction> actions = [];
        if (OnViewEventTypeDefinition is not null)
        {
            actions.Add(SingleAction("View definition", ConsoleKey.D, item => OnViewEventTypeDefinition(item)));
        }

        if (OnViewObserversForType is not null)
        {
            actions.Add(SingleAction("View observers", ConsoleKey.V, item => OnViewObserversForType(item)));
        }

        return actions;
    }

    /// <inheritdoc/>
    protected override IComparer<AppendedEvent> GetColumnComparer(int columnIndex) => columnIndex switch
    {
        0 => Comparer<AppendedEvent>.Create((a, b) =>
            a.Context.SequenceNumber.CompareTo(b.Context.SequenceNumber)),
        1 => Comparer<AppendedEvent>.Create((a, b) =>
            ((DateTimeOffset)a.Context.Occurred).CompareTo((DateTimeOffset)b.Context.Occurred)),
        _ => base.GetColumnComparer(columnIndex)
    };

    /// <inheritdoc/>
    protected override void OnInspect(AppendedEvent item) => OnViewEventTypeDefinition?.Invoke(item);

    /// <inheritdoc/>
    protected override string RenderDetail(AppendedEvent? item, WorkbenchData? data)
    {
        if (item is null)
        {
            return SelectPrompt("an event");
        }

        var acc = Theme.Accent.ToMarkup();
        var mut = Theme.Muted.ToMarkup();
        var contentText = !string.IsNullOrEmpty(item.Content)
            ? JsonYamlFormatter.FormatAsYaml(item.Content, mut)
            : $"[{mut}](no content)[/]";

        return string.Join('\n', new[]
        {
            $"[{mut}]Seq#[/]        {item.Context.SequenceNumber:N0}",
            $"[{mut}]Type[/]        [{acc}]{item.Context.EventType.Id}[/] gen {item.Context.EventType.Generation}",
            $"[{mut}]Source[/]      {item.Context.EventSourceId ?? "—"}",
            $"[{mut}]Occurred[/]    {item.Context.Occurred}",
            $"[{mut}]Correlation[/] {item.Context.CorrelationId}",
            string.Empty,
            $"[{acc}]Content:[/]",
            $"  [{mut}][D][/] View event type definition",
            $"  [{mut}][V][/] View observers for this type",
            contentText
        });
    }

    /// <inheritdoc/>
    protected override bool MatchesFilter(AppendedEvent item, string filter)
    {
        if (filter.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
        {
            return item.Context.EventType.Id.Contains(filter[5..], StringComparison.OrdinalIgnoreCase);
        }

        return item.Context.EventType.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               (item.Context.EventSourceId ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    protected override IEnumerable<string> GetCompletions(string input)
    {
        var items = PendingData?.RecentEvents ?? [];
        return items
            .Select(e => $"type:{e.Context.EventType.Id}")
            .Distinct()
            .Where(c => c.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Order();
    }

    /// <summary>
    /// Formats a <see cref="DateTimeOffset"/> as a human-readable relative time (e.g., "3s ago", "12m ago").
    /// Falls back to an absolute format for timestamps older than 7 days.
    /// </summary>
    /// <param name="occurred">The event timestamp.</param>
    /// <returns>A relative or absolute time string.</returns>
    static string FormatRelativeTime(DateTimeOffset occurred)
    {
        var diff = DateTimeOffset.UtcNow - occurred.ToUniversalTime();
        if (diff.TotalSeconds < 60) return $"{(int)diff.TotalSeconds}s ago";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return occurred.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    }
}
