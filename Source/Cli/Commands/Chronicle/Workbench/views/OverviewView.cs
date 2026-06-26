// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
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
    /// <summary>Width of the navigation rail (matches WorkbenchNavigation.WithNavWidth).</summary>
    const int NavRailWidth = 28;

    /// <summary>Horizontal chrome (rounded content border + padding) around the content area.</summary>
    const int ContentChromeWidth = 4;

    /// <summary>Vertical chrome (top/bottom bars + content border) around the content area.</summary>
    const int ContentChromeHeight = 10;

    readonly Queue<double> _observerHistory = new(capacity: 10);
    ConsoleWindowSystem? _windowSystem;
    WorkbenchTheme? _themeInstance;
    PanelControl? _healthPanel;
    PanelControl? _observersTile;
    PanelControl? _failuresTile;
    PanelControl? _recommendationsTile;
    PanelControl? _jobsTile;
    SparklineControl? _observerSparkline;
    GridControl? _outerGrid;
    GridControl? _rightGrid;
    WorkbenchData? _pendingData;

    /// <inheritdoc/>
    public bool IsActive { get; set; }

    WorkbenchTheme Theme => _themeInstance ??= new WorkbenchTheme(_windowSystem!);

    /// <inheritdoc/>
    public void Dispose()
    {
        UnsubscribeFromResize();

        _healthPanel?.Dispose();
        _observersTile?.Dispose();
        _failuresTile?.Dispose();
        _recommendationsTile?.Dispose();
        _jobsTile?.Dispose();
        _observerSparkline?.Dispose();
        _rightGrid?.Dispose();
        _outerGrid?.Dispose();
    }

    /// <inheritdoc/>
    public void PopulateContent(SharpConsoleUI.Controls.ScrollablePanelControl panel, ConsoleWindowSystem windowSystem)
    {
        // PopulateContent runs on every navigation to this view, so release the previous build's
        // resize subscription and controls first to avoid duplicate handlers and leaked controls.
        UnsubscribeFromResize();
        _rightGrid?.Dispose();
        _outerGrid?.Dispose();

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
            .WithName("OverviewObserversTile")
            .Build();

        _failuresTile = Controls.Panel()
            .WithContent("Loading...")
            .WithHeader(" FAILURES ")
            .Rounded()
            .WithColorRole(ColorRole.Primary)
            .WithPadding(1, 0, 1, 0)
            .WithName("OverviewFailuresTile")
            .Build();

        _recommendationsTile = Controls.Panel()
            .WithContent("Loading...")
            .WithHeader(" RECOMMENDATIONS ")
            .Rounded()
            .WithColorRole(ColorRole.Primary)
            .WithPadding(1, 0, 1, 0)
            .WithName("OverviewRecommendationsTile")
            .Build();

        _jobsTile = Controls.Panel()
            .WithContent("Loading...")
            .WithHeader(" JOBS ")
            .Rounded()
            .WithColorRole(ColorRole.Primary)
            .WithPadding(1, 0, 1, 0)
            .WithName("OverviewJobsTile")
            .Build();

        // ── Observer activity sparkline (spans bottom of the right area) ───────────────────────────
        _observerSparkline = new SparklineBuilder()
            .WithHeight(3)
            .WithColorRole(ColorRole.Primary)
            .WithTitle("observer activity", Theme.Muted)
            .WithData([0])
            .Build();

        // ── Right inner grid: 2 cols × 4 rows (2×2 tiles + sparkline band + slack) ─────────────────
        _rightGrid = Controls.Grid()
            .Columns(GridLength.Star(1), GridLength.Star(1))

            // Tile rows are fixed-height (compact); the sparkline gets a fixed band too so it does not
            // balloon into a tall sparse column. The trailing Star row absorbs the remaining height as
            // empty space, keeping the dashboard top-aligned.
            .Rows(GridLength.Cells(7), GridLength.Cells(7), GridLength.Cells(5), GridLength.Star(1))
            .RowGap(1)
            .ColumnGap(1)
            .WithColorRole(ColorRole.Primary)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .Place(_observersTile, 0, 0)
            .Place(_failuresTile, 0, 1)
            .Place(_recommendationsTile, 1, 0)
            .Place(_jobsTile, 1, 1)
            .Place(_observerSparkline, 2, 0, colSpan: 2)
            .Build();

        // ── Outer grid: health rail (1*) | right tiles area (2*) ──────────────────────────────────
        // GridControl.MeasureDOM returns LayoutSize.Zero — it does NOT measure-to-content, so when
        // mounted as a panel child it would collapse to nothing. Give it an explicit size from the
        // available content area, and re-apply it on terminal resize (subscription below).
        var (contentWidth, contentHeight) = ContentSize();
        _outerGrid = Controls.Grid()
            .Columns(GridLength.Star(1), GridLength.Star(2))
            .Rows(GridLength.Star(1))
            .ColumnGap(1)
            .WithColorRole(ColorRole.Primary)
            .WithSize(contentWidth, contentHeight)
            .ColumnSplitterAfter(0)
            .Place(_healthPanel, 0, 0)
            .Place(_rightGrid, 0, 1)
            .Build();

        // The explicit size is computed from the terminal dimensions, so re-apply it whenever the
        // terminal is resized — otherwise the dashboard would stay locked at its build-time size.
        windowSystem.WindowResized += OnTerminalResized;

        if (_pendingData is not null)
        {
            UpdateData(_pendingData);
        }

        panel.ClearContents();
        panel.AddControl(_outerGrid);
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
            : $"[{dan}]● Disconnected[/]";
        var version = data.ServerVersion is not null
            ? $"[{mut}]v{data.ServerVersion}[/]"
            : $"[{mut}]unknown[/]";
        var seq = data.TailSequenceNumber.HasValue
            ? $"[bold]#{data.TailSequenceNumber.Value:N0}[/]"
            : $"[{mut}]—[/]";

        _healthPanel.Content =
            "[bold]CONTEXT[/]\n" +
            $"  [{mut}]Store[/]     [{acc}]{data.EventStore}[/]\n" +
            $"  [{mut}]Namespace[/] [{acc}]{data.Namespace}[/]\n" +
            "\n" +
            $"[{mut}]Status[/]   {connStatus}\n" +
            $"[{mut}]Version[/]  {version}\n" +
            $"[{mut}]Tail seq[/] {seq}\n" +
            $"[{mut}]Server[/]   [{mut}]{data.ConnectionString}[/]";

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

        _observersTile.Content =
            $"[{suc}]●[/] Active       [bold]{data.ActiveObservers}[/]\n" +
            $"[{war}]▲[/] Replaying    [bold]{data.ReplayingObservers}[/]\n" +
            $"[{mut}]○[/] Suspended    [bold]{data.SuspendedObservers}[/]\n" +
            $"[{dan}]⊘[/] Disconnected [bold]{data.DisconnectedObservers}[/]\n" +
            $"[{mut}]━[/] Total        [{obsColor}][bold]{data.Observers.Count}[/][/]";

        // ── Failures tile ──────────────────────────────────────────────────────────────────────────
        _failuresTile!.ColorRole = data.FailedPartitions.Count > 0
            ? ColorRole.Danger
            : ColorRole.Primary;

        _failuresTile.Content = data.FailedPartitions.Count > 0
            ? $"[{dan}]⚠[/] [bold]{data.FailedPartitions.Count}[/] failed partition{(data.FailedPartitions.Count == 1 ? string.Empty : "s")}\n[{mut}]→ press 3[/]"
            : $"[{suc}]✓[/] No failed partitions";

        // ── Recommendations tile ───────────────────────────────────────────────────────────────────
        _recommendationsTile!.ColorRole = data.Recommendations.Count > 0
            ? ColorRole.Warning
            : ColorRole.Primary;

        _recommendationsTile.Content = data.Recommendations.Count > 0
            ? $"[{war}]![/] [bold]{data.Recommendations.Count}[/] pending recommendation{(data.Recommendations.Count == 1 ? string.Empty : "s")}\n[{mut}]→ press 5[/]"
            : $"[{suc}]✓[/] No pending recommendations";

        // ── Jobs tile ──────────────────────────────────────────────────────────────────────────────
        _jobsTile!.ColorRole = ColorRole.Primary;
        _jobsTile.Content = $"[bold]{data.Jobs.Count}[/]\n[{mut}]running[/]";

        // ── Observer sparkline ─────────────────────────────────────────────────────────────────────
        UpdateObserverSparkline(data.Observers.Count);
    }

    /// <summary>
    /// Computes the explicit content area size for the outer grid from the current terminal
    /// dimensions, less the navigation rail and content-border chrome.
    /// </summary>
    /// <returns>The width and height in character cells.</returns>
    static (int Width, int Height) ContentSize() =>
        (Math.Max(40, Console.WindowWidth - NavRailWidth - ContentChromeWidth),
         Math.Max(10, Console.WindowHeight - ContentChromeHeight));

    void UpdateObserverSparkline(int totalObservers)
    {
        if (_observerSparkline is null) return;

        _observerHistory.Enqueue(totalObservers);
        while (_observerHistory.Count > 10)
        {
            _observerHistory.Dequeue();
        }

        _observerSparkline.SetDataPoints(_observerHistory);
    }

    /// <summary>
    /// Re-applies the explicit grid size when the terminal is resized, since the GridControl is
    /// sized from the terminal dimensions rather than measuring its own content.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="size">The new terminal size.</param>
    void OnTerminalResized(object? sender, SharpConsoleUI.Helpers.Size size)
    {
        if (_outerGrid is null)
        {
            return;
        }

        var (width, height) = ContentSize();
        _outerGrid.Width = width;
        _outerGrid.Height = height;
    }

    /// <summary>
    /// Detaches the terminal-resize handler if it was attached.
    /// </summary>
    void UnsubscribeFromResize()
    {
        if (_windowSystem is null)
        {
            return;
        }

        _windowSystem.WindowResized -= OnTerminalResized;
    }
}
