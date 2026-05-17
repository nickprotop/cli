// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Jobs;
using Spectre.Console.Rendering;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Builds Spectre.Console renderables for the Chronicle workbench dashboard.
/// </summary>
public static partial class WorkbenchRenderer
{
    /// <summary>Maximum number of list rows shown before the sliding window kicks in.</summary>
    const int MaxListRows = 10;

    /// <summary>Height of the scrollable text viewport in detail views (lines).</summary>
    const int ScrollViewport = 12;

    static readonly string _acc = OutputFormatter.Accent.ToMarkup();
    static readonly string _mut = OutputFormatter.Muted.ToMarkup();
    static readonly string _suc = OutputFormatter.Success.ToMarkup();
    static readonly string _war = OutputFormatter.Warning.ToMarkup();
    static readonly string _dan = OutputFormatter.Danger.ToMarkup();

    static readonly (WorkbenchView V, string Key, string Label)[] _views =
    [
        (WorkbenchView.Overview,         "1", "Overview"),
        (WorkbenchView.Observers,        "2", "Observers"),
        (WorkbenchView.FailedPartitions, "3", "Failures"),
        (WorkbenchView.Jobs,             "4", "Jobs"),
        (WorkbenchView.Recommendations,  "5", "Recommendations"),
        (WorkbenchView.EventLog,         "6", "Event Log"),
        (WorkbenchView.EventTypes,       "7", "Event Types"),
        (WorkbenchView.Projections,      "8", "Projections"),
        (WorkbenchView.ReadModels,       "9", "Read Models"),
        (WorkbenchView.EventStores,      "0", "Event Stores"),
        (WorkbenchView.Namespaces,       string.Empty, "Namespaces")
    ];

    /// <summary>
    /// Builds the full dashboard renderable for the given data and render state.
    /// </summary>
    /// <param name="data">The workbench snapshot to render.</param>
    /// <param name="state">All render-relevant state for this frame.</param>
    /// <returns>The renderable dashboard.</returns>
    public static Rows Build(WorkbenchData data, WorkbenchRenderState state) =>
        new(BuildHeader(data, state), BuildBody(data, state), BuildFooter(data, state));

    static Panel BuildHeader(WorkbenchData data, WorkbenchRenderState state)
    {
        var dot = data.IsConnected ? $"[{_suc}]●[/]" : $"[{_dan}]●[/]";
        var cs = DisplayConnectionString(data.ConnectionString);
        var store = $"[bold]{data.EventStore.EscapeMarkup()}[/][{_mut}] / {data.Namespace.EscapeMarkup()}[/]";
        var titleLine = $"  [{_acc}]◆[/] [bold]CHRONICLE WORKBENCH[/]  [{_mut}]{cs.EscapeMarkup()}[/]  {dot}  {store}";

        // Second line: breadcrumb when in a detail view, otherwise nav tabs.
        string secondLine;
        var breadcrumb = state.Breadcrumb;
        if (breadcrumb is { Count: > 0 })
        {
            var path = string.Join($"  [{_mut}]›[/]  ", breadcrumb.Select(s => s.EscapeMarkup()));
            secondLine = $"  [{_mut}]{path}[/]  [{_mut}]↻ {state.Interval}s[/]";
        }
        else
        {
            var failuresAlert = data.FailedPartitions.Count > 0;
            var recsAlert = data.Recommendations.Count > 0;
            var tabs = string.Join("  ", _views.Select(v =>
            {
                var isActive = v.V == state.View;
                var keyPart = string.IsNullOrEmpty(v.Key) ? string.Empty : $"[[ {v.Key} ]] ";
                var alert = (v.V == WorkbenchView.FailedPartitions && failuresAlert)
                    || (v.V == WorkbenchView.Recommendations && recsAlert);
                if (isActive)
                    return $"[bold {_acc}]{keyPart}{v.Label.EscapeMarkup()}[/]";
                if (alert)
                    return $"[bold {_war}]{keyPart}{v.Label.EscapeMarkup()}[/]";
                return string.IsNullOrEmpty(v.Key)
                    ? $"[{_mut}]{v.Label.EscapeMarkup()}[/]"
                    : $"[{_mut}]{keyPart}[/]{v.Label.EscapeMarkup()}";
            }));
            var refreshIndicator = $"[{_mut}]↻ {state.Interval}s[/]";
            secondLine = $"  {tabs}  {refreshIndicator}";
        }

        return new Panel(new Rows(new Markup(titleLine), new Markup(secondLine)))
            .Border(BoxBorder.Heavy)
            .BorderColor(OutputFormatter.Accent)
            .Padding(0, 0);
    }

    static Rows BuildFooter(WorkbenchData data, WorkbenchRenderState state)
    {
        var sep = new Rule().RuleStyle(new Style(foreground: OutputFormatter.Muted));

        Markup statusLine;
        switch (state.ActionState)
        {
            case WorkbenchActionState.AwaitingConfirmation:
                var desc = (state.PendingActionDescription ?? string.Empty).EscapeMarkup();
                statusLine = new Markup($"  [{_war}]⚡ {desc}?[/]   [{_war}][[ Y ]][/] Confirm   [{_mut}][[ N ]][/] Cancel");
                break;

            case WorkbenchActionState.Executing:
                var execDesc = (state.PendingActionDescription ?? "Working").EscapeMarkup();
                statusLine = new Markup($"  [{_acc}]⟳ {execDesc}…[/]");
                break;

            case WorkbenchActionState.Completed:
                var result = (state.ActionResult ?? string.Empty).EscapeMarkup();
                var resColor = state.IsActionError ? _dan : _suc;
                var resIcon = state.IsActionError ? "✗" : "✓";
                statusLine = new Markup($"  [{resColor}]{resIcon} {result}[/]   [{_mut}](press any key to dismiss)[/]");
                break;

            default:
                var isDetail = (int)state.View >= 100;
                var connected = data.IsConnected ? $"[{_suc}]✓ connected[/]" : $"[{_dan}]✗ disconnected[/]";
                var seqTail = data.TailSequenceNumber.HasValue
                    ? $"seq# [bold]{data.TailSequenceNumber.Value:N0}[/]"
                    : $"[{_mut}]seq# —[/]";

                if (isDetail)
                {
                    var actions = GetActionHints(state.View);
                    var scrollHint = $"[{_mut}]↑↓ scroll  [/]";
                    statusLine = new Markup($"  {connected}  {seqTail}  {scrollHint}{actions}[{_mut}][[ Esc ]][/] Back");
                }
                else
                {
                    var navHint = HasNavigation(state.View) ? $"[{_mut}]↑↓ select  [/]" : string.Empty;
                    var enterHint = HasDrillDown(state.View) && !state.FilterInputMode ? $"[{_mut}][[ Enter ]][/] detail  " : string.Empty;
                    var actions = !state.FilterInputMode ? GetActionHints(state.View) : string.Empty;
                    string filterIndicator;
                    if (state.FilterInputMode)
                        filterIndicator = $"[{_acc}]/ {state.FilterText.EscapeMarkup()}█[/]  [{_mut}][[ Enter ]] confirm  [[ Esc ]] cancel  [/]";
                    else if (!string.IsNullOrEmpty(state.FilterText))
                        filterIndicator = $"[{_acc}]/{state.FilterText.EscapeMarkup()}[/]  [{_mut}][[ Esc ]] clear  [/]";
                    else
                        filterIndicator = string.Empty;
                    var filterHint = !state.FilterInputMode && IsFilterableView(state.View) && string.IsNullOrEmpty(state.FilterText)
                        ? $"[{_mut}][[ / ]] filter  [/]"
                        : string.Empty;
                    var quit = $"[{_mut}]← → views  [[ +/- ]] interval ({state.Interval}s)  [[ Q ]] Quit[/]";
                    statusLine = new Markup($"  {connected}  {seqTail}  {navHint}{enterHint}{filterHint}{actions}{filterIndicator}{quit}");
                }

                break;
        }

        return new Rows(sep, statusLine);
    }

    static IRenderable BuildBody(WorkbenchData data, WorkbenchRenderState state) =>
        state.View switch
        {
            WorkbenchView.Overview => BuildOverview(data),
            WorkbenchView.Observers => BuildObserversView(data, state.SelectedIndex, state.FilterText),
            WorkbenchView.FailedPartitions => BuildFailedView(data, state.SelectedIndex),
            WorkbenchView.Jobs => BuildJobsView(data, state.SelectedIndex),
            WorkbenchView.Recommendations => BuildRecommendationsView(data, state.SelectedIndex),
            WorkbenchView.EventLog => BuildEventLogView(data, state.SelectedIndex, state.FilterText, state.EventLogAscending, state.EventLogPage),
            WorkbenchView.EventTypes => BuildEventTypesView(data, state.SelectedIndex, state.FilterText),
            WorkbenchView.Projections => BuildProjectionsView(data, state.SelectedIndex, state.FilterText),
            WorkbenchView.ReadModels => BuildReadModelsView(data, state.SelectedIndex, state.FilterText),
            WorkbenchView.EventStores => BuildEventStoresView(data, state.SelectedIndex),
            WorkbenchView.Namespaces => BuildNamespacesView(data, state.SelectedIndex),
            WorkbenchView.ObserverDetail => BuildObserverDetailPage(data, state.FocusedId, state.SelectedIndex),
            WorkbenchView.FailedPartitionDetail => BuildFailedPartitionDetailPage(data, state.FocusedId, state.ScrollOffset),
            WorkbenchView.EventDetail => BuildEventDetailPage(data, state.FocusedId, state.ScrollOffset),
            WorkbenchView.EventTypeDetail => BuildEventTypeDetailPage(data, state.FocusedId, state.ScrollOffset),
            WorkbenchView.ProjectionDetail => BuildProjectionDetailPage(data, state.FocusedId, state.ScrollOffset),
            WorkbenchView.ReadModelDetail => BuildReadModelDetailPage(data, state.FocusedId, state.ScrollOffset),
            _ => new Markup(string.Empty)
        };

    static bool IsFilterableView(WorkbenchView view) =>
        view is WorkbenchView.Observers or WorkbenchView.EventTypes
            or WorkbenchView.EventLog or WorkbenchView.Projections
            or WorkbenchView.ReadModels;

    static bool HasNavigation(WorkbenchView view) =>
        view is WorkbenchView.Observers or WorkbenchView.FailedPartitions
            or WorkbenchView.Jobs or WorkbenchView.Recommendations
            or WorkbenchView.EventLog or WorkbenchView.EventTypes
            or WorkbenchView.Projections or WorkbenchView.ReadModels
            or WorkbenchView.EventStores or WorkbenchView.Namespaces
            or WorkbenchView.ObserverDetail;

    static bool HasDrillDown(WorkbenchView view) =>
        view is WorkbenchView.Observers or WorkbenchView.FailedPartitions
            or WorkbenchView.EventLog or WorkbenchView.EventTypes
            or WorkbenchView.Projections or WorkbenchView.ReadModels
            or WorkbenchView.EventStores or WorkbenchView.Namespaces
            or WorkbenchView.ObserverDetail;

    static string GetActionHints(WorkbenchView view) => view switch
    {
        WorkbenchView.Observers => $"[{_mut}][[ R ]][/] Replay  ",
        WorkbenchView.FailedPartitions => $"[{_mut}][[ T ]][/] Retry  [{_mut}][[ P ]][/] Replay partition  ",
        WorkbenchView.Jobs => $"[{_mut}][[ S ]][/] Stop  [{_mut}][[ U ]][/] Resume  ",
        WorkbenchView.Recommendations => $"[{_mut}][[ A ]][/] Apply  [{_mut}][[ I ]][/] Ignore  ",
        WorkbenchView.EventLog => $"[{_mut}][[ S ]][/] Sort  [{_mut}][[ PgDn ]][/] Older  [{_mut}][[ PgUp ]][/] Newer  ",
        WorkbenchView.ObserverDetail => $"[{_mut}][[ R ]][/] Replay  [{_mut}][[ P ]][/] Projection  ",
        WorkbenchView.FailedPartitionDetail => $"[{_mut}][[ T ]][/] Retry  [{_mut}][[ P ]][/] Replay partition  ",
        WorkbenchView.EventDetail => $"[{_mut}][[ T ]][/] View event type  ",
        WorkbenchView.EventStores => $"[{_mut}][[ Enter ]][/] Switch store  ",
        WorkbenchView.Namespaces => $"[{_mut}][[ Enter ]][/] Switch namespace  ",
        _ => string.Empty
    };

    static string StateIcon(ObserverRunningState state) => state switch
    {
        ObserverRunningState.Active => $"[{_suc}]●[/]",
        ObserverRunningState.Replaying => $"[{_war}]▲[/]",
        ObserverRunningState.Suspended => $"[{_mut}]○[/]",
        ObserverRunningState.Disconnected => $"[{_mut}]⊘[/]",
        _ => $"[{_mut}]·[/]"
    };

    static string StateName(ObserverRunningState state) => state switch
    {
        ObserverRunningState.Active => $"[{_suc}]Active[/]",
        ObserverRunningState.Replaying => $"[{_war}]Replaying[/]",
        ObserverRunningState.Suspended => $"[{_mut}]Suspended[/]",
        ObserverRunningState.Disconnected => $"[{_mut}]Disconnected[/]",
        _ => state.ToString().EscapeMarkup()
    };

    static int StateOrder(ObserverRunningState state) => state switch
    {
        ObserverRunningState.Disconnected => 0,
        ObserverRunningState.Replaying => 1,
        ObserverRunningState.Active => 2,
        ObserverRunningState.Suspended => 3,
        _ => 4
    };

    static ulong? ComputeLag(ObserverInformation obs, ulong? tail)
    {
        if (!tail.HasValue || obs.LastHandledEventSequenceNumber == ulong.MaxValue)
        {
            return tail;
        }

        return tail.Value > obs.LastHandledEventSequenceNumber
            ? tail.Value - obs.LastHandledEventSequenceNumber
            : 0;
    }

    static string ProgressBar(int success, int total, int width = 16)
    {
        if (total == 0) return $"[{_mut}]{new string('░', width)}[/]";
        var filled = (int)Math.Min(width, (long)success * width / total);
        return $"[{_suc}]{new string('█', filled)}[/][{_mut}]{new string('░', width - filled)}[/]";
    }

    static string FormatSeq(ulong seq) =>
        seq == ulong.MaxValue ? $"[{_mut}]—[/]" : seq.ToString("N0");

    /// <summary>
    /// Renders a block of text with a scrollable viewport and a "lines X–Y of Z" indicator.
    /// </summary>
    /// <param name="title">The panel header title.</param>
    /// <param name="lines">The full list of lines to display.</param>
    /// <param name="scrollOffset">The current scroll position (first visible line index).</param>
    /// <param name="borderColor">The panel border color.</param>
    /// <returns>A panel showing the visible lines with a scroll indicator.</returns>
    static Panel ScrollableText(string title, IReadOnlyList<string> lines, int scrollOffset, Color borderColor)
    {
        var totalLines = lines.Count;
        var start = Math.Max(0, Math.Min(scrollOffset, Math.Max(0, totalLines - ScrollViewport)));
        const int maxOffset = ScrollViewport - 1;
        var end = Math.Min(totalLines - 1, start + maxOffset);
        var visible = lines.Skip(start).Take(ScrollViewport).Select(l => (IRenderable)new Markup(l)).ToList();
        var indicator = totalLines > ScrollViewport
            ? $"\n  [{_mut}]lines {start + 1}–{end + 1} of {totalLines}  ↑↓ scroll[/]"
            : string.Empty;

        var content = visible.Count > 0
            ? (IRenderable)new Rows([.. visible, new Markup(indicator)])
            : new Markup($"[{_mut}](empty)[/]");

        return new Panel(content)
            .Header($"[{_acc}] {title.EscapeMarkup()} [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(borderColor)
            .Expand();
    }

    static (int Start, int End) ListWindow(int count, int selectedIndex)
    {
        if (count <= MaxListRows) return (0, count - 1);
        const int maxOffset = MaxListRows - 1;
        var start = Math.Max(0, selectedIndex - (MaxListRows / 2));
        var end = Math.Min(count - 1, start + maxOffset);
        start = Math.Max(0, end - maxOffset);
        return (start, end);
    }

    static string DisplayConnectionString(string connectionString)
    {
        try
        {
            var withoutScheme = connectionString.Replace("chronicle://", string.Empty, StringComparison.OrdinalIgnoreCase);
            var afterAuth = withoutScheme.Contains('@') ? withoutScheme[(withoutScheme.IndexOf('@') + 1)..] : withoutScheme;
            var hostPort = afterAuth.Split('?')[0].TrimEnd('/');
            return $"chronicle://{hostPort}";
        }
        catch
        {
            return connectionString;
        }
    }
}
