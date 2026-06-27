// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Themes;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Overview tab — server health rail on the left, 2×2 metric tiles (Observers / Failures /
/// Recommendations / Jobs) on the right, and a spanning observer-activity sparkline below the tiles.
/// Uses the WinUI-style <see cref="GridControl"/> for layout (layout B).
/// </summary>
public class OverviewView : IWorkbenchView
{
    /// <summary>
    /// History length for the sparklines. Generous so the auto-fitting graphs fill the full panel
    /// width — the control renders only as many bars as it has data points.
    /// </summary>
    const int HistoryCap = 64;
    readonly Queue<double> _observerHistory = new(capacity: HistoryCap);
    readonly Queue<double> _eventHistory = new(capacity: HistoryCap);
    ulong? _lastSeenTail;
    ConsoleWindowSystem? _windowSystem;
    WorkbenchTheme? _themeInstance;
    PanelControl? _healthPanel;
    PanelControl? _observersTile;
    PanelControl? _failuresTile;
    PanelControl? _recommendationsTile;
    PanelControl? _jobsTile;
    SparklineControl? _observerSparkline;
    PanelControl? _activityPanel;
    SparklineControl? _throughputSparkline;
    PanelControl? _throughputPanel;
    PanelControl? _topTypesPanel;
    GridControl? _grid;
    WorkbenchData? _pendingData;

    /// <inheritdoc/>
    public bool IsActive { get; set; }

    WorkbenchTheme Theme => _themeInstance ??= new WorkbenchTheme(_windowSystem!);

    /// <inheritdoc/>
    public void Dispose()
    {
        _healthPanel?.Dispose();
        _observersTile?.Dispose();
        _failuresTile?.Dispose();
        _recommendationsTile?.Dispose();
        _jobsTile?.Dispose();
        _observerSparkline?.Dispose();
        _activityPanel?.Dispose();
        _throughputSparkline?.Dispose();
        _throughputPanel?.Dispose();
        _topTypesPanel?.Dispose();
        _grid?.Dispose();
    }

    /// <inheritdoc/>
    public void PopulateContent(SharpConsoleUI.Controls.ScrollablePanelControl panel, ConsoleWindowSystem windowSystem)
    {
        // PopulateContent runs on every navigation to this view, so release the previous build first.
        _grid?.Dispose();

        _windowSystem = windowSystem;
        _themeInstance = new WorkbenchTheme(windowSystem);

        // ── Left rail: Server Health panel ─────────────────────────────────────────────────────────
        _healthPanel = Controls.Panel()
            .WithContent("Loading...")
            .WithHeader(" SERVER HEALTH ")
            .Rounded()
            .WithColorRole(ColorRole.Primary)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("OverviewHealthPanel")
            .Build();

        // ── 2×2 metric tiles ───────────────────────────────────────────────────────────────────────
        _observersTile = Controls.Panel()
            .WithContent("Loading...")
            .WithHeader(" OBSERVERS ")
            .Rounded()
            .WithColorRole(ColorRole.Primary)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("OverviewObserversTile")
            .Build();

        _failuresTile = Controls.Panel()
            .WithContent("Loading...")
            .WithHeader(" FAILURES ")
            .Rounded()
            .WithColorRole(ColorRole.Primary)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("OverviewFailuresTile")
            .Build();

        _recommendationsTile = Controls.Panel()
            .WithContent("Loading...")
            .WithHeader(" RECOMMENDATIONS ")
            .Rounded()
            .WithColorRole(ColorRole.Primary)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("OverviewRecommendationsTile")
            .Build();

        _jobsTile = Controls.Panel()
            .WithContent("Loading...")
            .WithHeader(" JOBS ")
            .Rounded()
            .WithColorRole(ColorRole.Primary)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("OverviewJobsTile")
            .Build();

        // ── Observer activity sparkline, boxed in a panel to match the tiles ───────────────────────
        // AutoFitDataPoints makes the bars fill the full panel width; an explicit min/max range keeps a
        // constant series from filling the entire height (it sits at its proportional level with headroom).
        // A theme-derived vertical gradient (dim accent → bright accent) makes the bars glow toward the top.
        _observerSparkline = new SparklineBuilder()
            .WithColorRole(ColorRole.Primary)
            .WithGradient(ColorGradient.FromColors(Theme.DimAccent, Theme.Accent))
            .WithAutoFitDataPoints()
            .WithMinValue(0)
            .WithData([0])
            .Build();

        // Top padding gives the bars breathing room below the header.
        _activityPanel = Controls.Panel()
            .WithHeader(" OBSERVERS OVER TIME ")
            .Rounded()
            .WithColorRole(ColorRole.Primary)
            .WithPadding(1, 1, 1, 0)
            .FillVertical()
            .AddControl(_observerSparkline)
            .WithName("OverviewActivityPanel")
            .Build();

        // ── Event throughput sparkline (new events per refresh tick) ───────────────────────────────
        _throughputSparkline = new SparklineBuilder()
            .WithColorRole(ColorRole.Info)
            .WithGradient(ColorGradient.FromColors(Theme.DimAccent, Theme.Teal))
            .WithAutoFitDataPoints()
            .WithMinValue(0)
            .WithData([0])
            .Build();

        _throughputPanel = Controls.Panel()
            .WithHeader(" EVENT THROUGHPUT ")
            .Rounded()
            .WithColorRole(ColorRole.Info)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .AddControl(_throughputSparkline)
            .WithName("OverviewThroughputPanel")
            .Build();

        // ── Top event types bar list (from RecentEvents) ───────────────────────────────────────────
        _topTypesPanel = Controls.Panel()
            .WithContent("Loading...")
            .WithHeader(" TOP EVENT TYPES ")
            .Rounded()
            .WithColorRole(ColorRole.Info)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("OverviewTopTypesPanel")
            .Build();

        // ── Dashboard grid (single GridControl, added directly to the panel) ───────────────────────
        // Columns: health rail | tile/graph A | tile/graph B. Rows: two tile rows, an activity row,
        // and a graph row. The health rail spans all four rows on the left.
        // Rows: two tile rows, an activity row, a graph row — all fixed height — then a trailing Star
        // row that absorbs the remaining height so the dashboard stays compact and top-aligned. The
        // health rail spans the four content rows on the left.
        _grid = Controls.Grid()
            .Columns(GridLength.Star(1), GridLength.Star(1), GridLength.Star(1))
            .Rows(GridLength.Cells(7), GridLength.Cells(7), GridLength.Cells(6), GridLength.Cells(8), GridLength.Star(1))
            .RowGap(1)
            .ColumnGap(1)
            .WithColorRole(ColorRole.Primary)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .ColumnSplitterAfter(0)
            .Place(_healthPanel, 0, 0, rowSpan: 5)
            .Place(_observersTile, 0, 1)
            .Place(_failuresTile, 0, 2)
            .Place(_recommendationsTile, 1, 1)
            .Place(_jobsTile, 1, 2)
            .Place(_activityPanel, 2, 1, colSpan: 2)
            .Place(_throughputPanel, 3, 1)
            .Place(_topTypesPanel, 3, 2)
            .Build();

        if (_pendingData is not null)
        {
            UpdateData(_pendingData);
        }

        panel.ClearContents();
        panel.AddControl(_grid);
    }

    /// <inheritdoc/>
    public void UpdateData(WorkbenchData data)
    {
        _pendingData = data;
        if (_healthPanel is null) return;

        var suc = Theme.Success.ToMarkup();
        var dan = Theme.Danger.ToMarkup();
        var mut = Theme.Muted.ToMarkup();
        var war = Theme.Warning.ToMarkup();
        var acc = Theme.Accent.ToMarkup();

        // ── Server Health rail ─────────────────────────────────────────────────────────────────────
        var connStatus = data.IsConnected
            ? $"[{suc}]● Connected[/]"
            : $"[{dan}]○ Disconnected[/]";
        var version = data.ServerVersion is not null
            ? $"[{mut}]v{data.ServerVersion}[/]"
            : $"[{mut}]unknown[/]";
        var seq = data.TailSequenceNumber.HasValue
            ? $"[bold]#{data.TailSequenceNumber.Value:N0}[/]"
            : $"[{mut}]—[/]";

        _healthPanel.Content =
            $"{connStatus}   [{mut}]{version}[/]\n" +
            "\n" +
            $"[{acc}][bold]CONTEXT[/][/]\n" +
            $"  [{mut}]Store[/]      [bold]{data.EventStore}[/]\n" +
            $"  [{mut}]Namespace[/]  [bold]{data.Namespace}[/]\n" +
            $"  [{mut}]Tail seq[/]   {seq}\n" +
            "\n" +
            $"[{acc}][bold]CATALOG[/][/]\n" +
            $"  [bold]{data.EventTypeRegistrations.Count,4}[/] [{mut}]event types[/]\n" +
            $"  [bold]{data.ProjectionDefinitions.Count,4}[/] [{mut}]projections[/]\n" +
            $"  [bold]{data.ReadModelDefinitions.Count,4}[/] [{mut}]read models[/]\n" +
            $"  [bold]{data.EventStoreSubscriptions.Count,4}[/] [{mut}]subscriptions[/]\n" +
            "\n" +
            $"[{mut}]{data.ConnectionString}[/]\n" +
            "\n" +
            $"[{mut}]⟳ updated {FormatAge(data.CapturedAt)}[/]";

        // ── Observers tile ─────────────────────────────────────────────────────────────────────────
        ColorRole observersRole;
        if (data.DisconnectedObservers > 0) observersRole = ColorRole.Danger;
        else if (data.ReplayingObservers > 0) observersRole = ColorRole.Warning;
        else observersRole = ColorRole.Primary;
        _observersTile!.ColorRole = observersRole;

        string obsColor;
        if (data.DisconnectedObservers > 0) obsColor = dan;
        else if (data.ReplayingObservers > 0) obsColor = war;
        else obsColor = suc;

        // Hero number (the total) on the headline row, then a compact, color-coded state breakdown.
        // Zero-count states are dimmed so the eye lands on what is actually present.
        string ObsStat(string glyph, string color, int n, string label) =>
            n > 0
                ? $"[{color}]{glyph}[/] [bold]{n}[/] [{mut}]{label}[/]"
                : $"[{mut}]{glyph} {n} {label}[/]";

        _observersTile.Content =
            $"[{obsColor}][bold]{data.Observers.Count}[/][/] [{mut}]observers[/]\n" +
            "\n" +
            $"{ObsStat("●", suc, data.ActiveObservers, "active")}   {ObsStat("▲", war, data.ReplayingObservers, "replaying")}\n" +
            $"{ObsStat("○", mut, data.SuspendedObservers, "suspended")}   {ObsStat("⊘", dan, data.DisconnectedObservers, "disconnected")}";

        // ── Failures tile ──────────────────────────────────────────────────────────────────────────
        // Hero stat: a big green ✓ when healthy, a big danger count + call-to-action when not.
        _failuresTile!.ColorRole = data.FailedPartitions.Count > 0 ? ColorRole.Danger : ColorRole.Primary;
        _failuresTile.Content = data.FailedPartitions.Count > 0
            ? $"[{dan}][bold]{data.FailedPartitions.Count}[/][/] [{mut}]failed partition{(data.FailedPartitions.Count == 1 ? string.Empty : "s")}[/]\n\n[{dan}]⚠[/] [{mut}]needs attention[/]   [{acc}]press 3[/]"
            : $"[{suc}][bold]✓[/][/] [{mut}]all partitions healthy[/]";

        // ── Recommendations tile ───────────────────────────────────────────────────────────────────
        _recommendationsTile!.ColorRole = data.Recommendations.Count > 0 ? ColorRole.Warning : ColorRole.Primary;
        _recommendationsTile.Content = data.Recommendations.Count > 0
            ? $"[{war}][bold]{data.Recommendations.Count}[/][/] [{mut}]pending recommendation{(data.Recommendations.Count == 1 ? string.Empty : "s")}[/]\n\n[{war}]![/] [{mut}]review suggested[/]   [{acc}]press 5[/]"
            : $"[{suc}][bold]✓[/][/] [{mut}]no recommendations[/]";

        // ── Jobs tile ──────────────────────────────────────────────────────────────────────────────
        _jobsTile!.ColorRole = ColorRole.Primary;
        _jobsTile.Content = data.Jobs.Count > 0
            ? $"[{acc}][bold]{data.Jobs.Count}[/][/] [{mut}]job{(data.Jobs.Count == 1 ? string.Empty : "s")} running[/]"
            : $"[{suc}][bold]✓[/][/] [{mut}]no jobs running[/]";

        // ── Graphs (observers over time, event throughput, top event types) ────────────────────────
        UpdateObserverSparkline(data.Observers.Count);
        UpdateThroughput(data.TailSequenceNumber);
        UpdateTopTypes(data, mut, Theme.Teal.ToMarkup());
    }

    /// <summary>
    /// Formats how long ago a snapshot was captured, relative to now.
    /// </summary>
    /// <param name="capturedAt">When the snapshot was captured.</param>
    /// <returns>A short relative-time label such as "just now" or "12s ago".</returns>
    static string FormatAge(DateTimeOffset capturedAt)
    {
        var seconds = (int)Math.Max(0, (DateTimeOffset.Now - capturedAt).TotalSeconds);
        return seconds <= 1 ? "just now" : $"{seconds}s ago";
    }

    void UpdateObserverSparkline(int totalObservers)
    {
        if (_observerSparkline is null) return;

        _observerHistory.Enqueue(totalObservers);
        while (_observerHistory.Count > HistoryCap)
        {
            _observerHistory.Dequeue();
        }

        // Give the series ~25% headroom above its peak so a steady value reads as a mid-height flat
        // line (not a solid full-height block) and a rise has room to climb into.
        var peak = _observerHistory.Count > 0 ? _observerHistory.Max() : 0;
        _observerSparkline.MaxValue = Math.Max(1, Math.Ceiling(peak * 1.25));
        _observerSparkline.SetDataPoints(_observerHistory);
    }

    /// <summary>
    /// Tracks new events per refresh tick (tail-sequence delta) and feeds the throughput sparkline.
    /// </summary>
    /// <param name="tail">The current tail sequence number, or null when unavailable.</param>
    void UpdateThroughput(ulong? tail)
    {
        if (_throughputSparkline is null) return;

        double delta = 0;
        if (tail.HasValue)
        {
            if (_lastSeenTail.HasValue && tail.Value >= _lastSeenTail.Value)
            {
                delta = tail.Value - _lastSeenTail.Value;
            }

            _lastSeenTail = tail.Value;
        }

        _eventHistory.Enqueue(delta);
        while (_eventHistory.Count > HistoryCap)
        {
            _eventHistory.Dequeue();
        }

        _throughputSparkline.SetDataPoints(_eventHistory);

        // The sparkline is blank when every recent delta is 0, which reads as "broken". Reflect the
        // idle/active state in the panel header so the empty box reads as intentional.
        if (_throughputPanel is { } panel)
        {
            panel.Header = _eventHistory.Any(v => v > 0)
                ? $" EVENT THROUGHPUT · +{(long)delta}/tick "
                : " EVENT THROUGHPUT · idle ";
        }
    }

    /// <summary>
    /// Renders the most frequent event types from the recent-events window as a horizontal bar list.
    /// </summary>
    /// <param name="data">The current snapshot.</param>
    /// <param name="mut">Muted color markup.</param>
    /// <param name="accent">Accent color markup for the type names.</param>
    void UpdateTopTypes(WorkbenchData data, string mut, string accent)
    {
        if (_topTypesPanel is null) return;

        var counts = data.RecentEvents
            .GroupBy(e => e.Context.EventType.Id)
            .Select(g => (Type: g.Key, Count: g.Count()))
            .OrderByDescending(t => t.Count)
            .Take(5)
            .ToList();

        if (counts.Count == 0)
        {
            _topTypesPanel.Content = $"[{mut}]No recent events[/]";
            return;
        }

        var max = counts[0].Count;
        var lines = counts.Select(t =>
        {
            var name = t.Type.Length > 22 ? t.Type[..21] + "…" : t.Type;
            return $"[{accent}]{name,-22}[/] {WorkbenchUi.GradientBar(t.Count, max, 12, Theme.Teal, Theme.Accent, Theme.Muted)} [{mut}]{t.Count}[/]";
        });

        _topTypesPanel.Content = string.Join('\n', lines);
    }
}
