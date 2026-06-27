// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Themes;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Failed Partitions navigation item — filterable table of failed partitions with retry/replay actions.
/// </summary>
public class FailedPartitionsView : FilterableTableView<FailedPartition>
{
    /// <summary>Gets the currently selected failed partition, or <see langword="null"/> if none is selected.</summary>
    public FailedPartition? SelectedPartition => SelectedItem;

    /// <inheritdoc/>
    public override string ViewHelp =>
        "Lists partitions that have failed during event processing.\n" +
        "  [T]  Retry the selected partition (re-process from last failure)\n" +
        "  [P]  Replay the selected partition from the beginning\n" +
        "  [Space]  Check / uncheck row for bulk operations\n" +
        "  Check rows + toolbar / right-click → bulk retry / replay";

    /// <summary>
    /// Gets or sets the callback invoked when the user requests a partition retry.
    /// </summary>
    public Action<FailedPartition>? OnRetryPartition { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user requests a partition replay.
    /// </summary>
    public Action<FailedPartition>? OnReplayPartition { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user requests a bulk retry of all checked partitions.
    /// </summary>
    public Action<IReadOnlyList<FailedPartition>>? OnRetryAll { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user requests a bulk replay of all checked partitions.
    /// </summary>
    public Action<IReadOnlyList<FailedPartition>>? OnReplayAll { get; set; }

    /// <summary>
    /// Gets all failed partitions that are currently checked (checkbox mode).
    /// </summary>
    public IReadOnlyList<FailedPartition> Checked => CheckedItems;

    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns =>
    [
        ("Observer", TextJustification.Left, null),
        ("Partition", TextJustification.Left, 30),
        ("Attempts", TextJustification.Right, 10)
    ];

    /// <inheritdoc/>
    protected override string DetailPanelHeader => "FAILED PARTITION";

    /// <inheritdoc/>
    protected override ColorRole DetailColorRole => ColorRole.Danger;

    /// <inheritdoc/>
    protected override bool HasCheckboxMode => true;

    /// <inheritdoc/>
    protected override string? PageTitle => "FAILURES";

    /// <inheritdoc/>
    protected override string EmptyStateMessage => "No failed partitions — all healthy.";

    /// <inheritdoc/>
    protected override IReadOnlyList<ViewAction> GetToolbarActionTemplate()
    {
        List<ViewAction> actions = [];
        if (OnRetryPartition is not null)
        {
            actions.Add(SingleAction("Retry partition", ConsoleKey.T, item => OnRetryPartition(item)));
        }

        if (OnReplayPartition is not null)
        {
            actions.Add(SingleAction("Replay partition", ConsoleKey.P, item => OnReplayPartition(item)));
        }

        if (OnRetryAll is not null)
        {
            actions.Add(BulkAction("Retry", items => OnRetryAll(items)));
        }

        if (OnReplayAll is not null)
        {
            actions.Add(BulkAction("Replay", items => OnReplayAll(items)));
        }

        return actions;
    }

    /// <inheritdoc/>
    protected override IEnumerable<FailedPartition> GetItems(WorkbenchData data) =>
        data.FailedPartitions.OrderByDescending(p => p.Attempts.Count());

    /// <inheritdoc/>
    protected override string GetKey(FailedPartition item) => $"{item.ObserverId}/{item.Partition}";

    /// <inheritdoc/>
    protected override string[] BuildRow(FailedPartition item) =>
        [item.ObserverId, item.Partition, item.Attempts.Count().ToString().PadLeft(10)];

    /// <inheritdoc/>
    protected override string RenderDetail(FailedPartition? item, WorkbenchData? data)
    {
        if (item is null)
        {
            return SelectPrompt("a failed partition");
        }

        var mut = Theme.Muted.ToMarkup();
        var dan = Theme.Danger.ToMarkup();
        var acc = Theme.Accent.ToMarkup();

        var lines = new List<string>
        {
            $"[{mut}]Observer[/]  {item.ObserverId}",
            $"[{mut}]Partition[/] [{dan}]{item.Partition}[/]",
            $"[{mut}]Attempts[/]  {item.Attempts.Count()}",
            string.Empty,
            $"[{acc}]Last Attempts:[/]"
        };

        foreach (var attempt in item.Attempts.OrderByDescending(a => a.Occurred).Take(5))
        {
            lines.Add($"  [{mut}]{attempt.Occurred}[/]");
            var firstMessage = attempt.Messages?.FirstOrDefault();
            if (!string.IsNullOrEmpty(firstMessage))
            {
                var msg = firstMessage.Length > 80 ? firstMessage[..77] + "…" : firstMessage;
                lines.Add($"  [{dan}]{msg}[/]");
            }
        }

        if (OnRetryPartition is not null || OnReplayPartition is not null)
        {
            lines.Add(string.Empty);
            if (OnRetryPartition is not null)
            {
                lines.Add($"[{mut}]Press[/] [bold]T[/] [{mut}]to retry[/]");
            }

            if (OnReplayPartition is not null)
            {
                lines.Add($"[{mut}]Press[/] [bold]P[/] [{mut}]to replay[/]");
            }
        }

        return string.Join('\n', lines);
    }

    /// <inheritdoc/>
    protected override bool MatchesFilter(FailedPartition item, string filter) =>
        item.ObserverId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        item.Partition.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
