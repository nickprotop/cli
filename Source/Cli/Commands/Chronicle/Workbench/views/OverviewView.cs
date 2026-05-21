// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Overview tab — server health card, observer stats, and attention items (failures + recommendations),
/// laid out as three bordered rounded panels side by side.
/// </summary>
public class OverviewView : IWorkbenchView
{
    readonly Queue<double> _observerHistory = new(capacity: 10);
    PanelControl? _healthPanel;
    PanelControl? _observerPanel;
    PanelControl? _attentionPanel;
    SparklineControl? _observerSparkline;
    WorkbenchData? _pendingData;

    /// <inheritdoc/>
    public bool IsActive { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        _healthPanel?.Dispose();
        _observerPanel?.Dispose();
        _attentionPanel?.Dispose();
        _observerSparkline?.Dispose();
    }

    /// <inheritdoc/>
    public IWindowControl BuildContent(ConsoleWindowSystem windowSystem)
    {
        _healthPanel = Controls.Panel()
            .WithContent("Loading...")
            .WithHeader(" SERVER HEALTH ")
            .Rounded()
            .WithBorderColor(WorkbenchColors.Accent)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("OverviewHealthPanel")
            .Build();

        _observerSparkline = new SparklineBuilder()
            .WithHeight(3)
            .WithBarColor(WorkbenchColors.Accent)
            .WithTitle("observer count history", WorkbenchColors.Muted)
            .WithData([0])
            .Build();

        _observerPanel = Controls.Panel()
            .WithContent("Loading...")
            .WithHeader(" OBSERVERS ")
            .Rounded()
            .WithBorderColor(WorkbenchColors.Accent)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("OverviewObserversPanel")
            .Build();

        _attentionPanel = Controls.Panel()
            .WithContent("Loading...")
            .WithHeader(" ATTENTION ")
            .Rounded()
            .WithBorderColor(WorkbenchColors.Accent)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("OverviewAttentionPanel")
            .Build();

        var root = Controls.HorizontalGrid()
            .Column(col => col.Add(_healthPanel))
            .Column(col => col.Add(_observerPanel).Add(_observerSparkline))
            .Column(col => col.Add(_attentionPanel))
            .Build();

        // Apply any data that arrived before controls were ready (NavigationView lazy init).
        if (_pendingData is not null)
            UpdateData(_pendingData);

        return root;
    }

    /// <inheritdoc/>
    public void UpdateData(WorkbenchData data)
    {
        _pendingData = data;
        if (_healthPanel is null) return;

        var suc = WorkbenchColors.Success.ToMarkup();
        var dan = WorkbenchColors.Danger.ToMarkup();
        var mut = WorkbenchColors.Muted.ToMarkup();
        var war = WorkbenchColors.Warning.ToMarkup();

        var connStatus = data.IsConnected
            ? $"[{suc}]● Connected[/]"
            : $"[{dan}]● Disconnected[/]";
        var version = data.ServerVersion is not null
            ? $"[{mut}]v{data.ServerVersion}[/]"
            : $"[{mut}]unknown[/]";
        var seq = data.TailSequenceNumber.HasValue
            ? $"[bold]#{data.TailSequenceNumber.Value:N0}[/]"
            : $"[{mut}]—[/]";

        var acc = WorkbenchColors.Accent.ToMarkup();
        _healthPanel.Content =
            "[bold]CONTEXT[/]\n" +
            $"  [{mut}]Store[/]     [{acc}]{data.EventStore}[/]\n" +
            $"  [{mut}]Namespace[/] [{acc}]{data.Namespace}[/]\n" +
            "\n" +
            $"[{mut}]Status[/]   {connStatus}\n" +
            $"[{mut}]Version[/]  {version}\n" +
            $"[{mut}]Tail seq[/] {seq}\n" +
            $"[{mut}]Server[/]   [{mut}]{data.ConnectionString}[/]";

        string obsColor;
        if (data.DisconnectedObservers > 0) obsColor = dan;
        else if (data.ReplayingObservers > 0) obsColor = war;
        else obsColor = suc;

        _observerPanel!.Content =
            $"[{suc}]●[/] Active       [bold]{data.ActiveObservers}[/]\n" +
            $"[{war}]▲[/] Replaying    [bold]{data.ReplayingObservers}[/]\n" +
            $"[{mut}]○[/] Suspended    [bold]{data.SuspendedObservers}[/]\n" +
            $"[{dan}]⊘[/] Disconnected [bold]{data.DisconnectedObservers}[/]\n" +
            $"[{mut}]━[/] Total        [{obsColor}][bold]{data.Observers.Count}[/][/]";

        UpdateObserverSparkline(data.Observers.Count);

        var hasAttention = data.FailedPartitions.Count > 0 || data.Recommendations.Count > 0;
        if (hasAttention)
        {
            _attentionPanel!.BorderColor = WorkbenchColors.Warning;
        }
        else
        {
            _attentionPanel!.BorderColor = WorkbenchColors.Accent;
        }

        var attentionLines = new List<string>();

        if (data.FailedPartitions.Count > 0)
        {
            attentionLines.Add($"[{dan}]⚠[/] [bold]{data.FailedPartitions.Count}[/] failed partition{(data.FailedPartitions.Count == 1 ? string.Empty : "s")}  [{mut}]→ press 3[/]");
        }
        else
        {
            attentionLines.Add($"[{suc}]✓[/] No failed partitions");
        }

        if (data.Recommendations.Count > 0)
        {
            attentionLines.Add($"[{war}]![/] [bold]{data.Recommendations.Count}[/] pending recommendation{(data.Recommendations.Count == 1 ? string.Empty : "s")}  [{mut}]→ press 5[/]");
        }
        else
        {
            attentionLines.Add($"[{suc}]✓[/] No pending recommendations");
        }

        _attentionPanel!.Content = string.Join('\n', attentionLines);
    }

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
}
