// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Failed Partitions navigation item — filterable table of failed partitions with retry/replay actions in the bordered detail pane.
/// </summary>
public class FailedPartitionsView : IWorkbenchView
{
    TableControl? _table;
    PanelControl? _detailPanel;
    PromptControl? _filterPrompt;
    string _currentFilter = string.Empty;
    List<FailedPartition> _allItems = [];
    WorkbenchData? _pendingData;

    /// <summary>
    /// Gets or sets the callback invoked when the filter input gains or loses focus.
    /// </summary>
    public Action<bool>? OnFilterFocusChanged { get; set; }

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
    /// Returns all failed partitions that are currently checked (checkbox mode).
    /// </summary>
    /// <returns>A list of checked <see cref="FailedPartition"/> items.</returns>
    public IReadOnlyList<FailedPartition> GetCheckedItems() =>
        [.. (_table?.GetCheckedRows() ?? []).Select(r => r.Tag).OfType<FailedPartition>()];

    /// <inheritdoc/>
    public void Dispose()
    {
        _table?.Dispose();
        _detailPanel?.Dispose();
        _filterPrompt?.Dispose();
    }

    /// <inheritdoc/>
    public IWindowControl BuildContent(ConsoleWindowSystem windowSystem)
    {
        _table = Controls.Table()
            .AddColumn("Observer", SharpConsoleUI.Layout.TextJustification.Left, null)
            .AddColumn("Partition", SharpConsoleUI.Layout.TextJustification.Left, 30)
            .AddColumn("Attempts", SharpConsoleUI.Layout.TextJustification.Right, 10)
            .Interactive()
            .WithCheckboxMode()
            .WithSorting()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .OnSelectedRowChanged((_, _) => RefreshDetail())
            .WithName("FailedPartitionsTable")
            .Build();

        _filterPrompt = Controls.Prompt("Filter: ")
            .WithHistory(true)
            .OnInputChanged((_, text) =>
            {
                _currentFilter = text ?? string.Empty;
                RebuildFilteredRows();
            })
            .OnGotFocus((_, _) => OnFilterFocusChanged?.Invoke(true))
            .OnLostFocus((_, _) => OnFilterFocusChanged?.Invoke(false))
            .WithName("FailedPartitionsFilterPrompt")
            .Build();

        var leftPane = Controls.ScrollablePanel()
            .AddControl(_filterPrompt)
            .AddControl(_table)
            .WithVerticalScroll(ScrollMode.None)
            .WithName("FailedPartitionsLeftPane")
            .Build();

        _detailPanel = Controls.Panel()
            .WithContent($"[{WorkbenchColors.Muted.ToMarkup()}]Select a failed partition.[/]")
            .WithHeader(" FAILED PARTITION ")
            .Rounded()
            .WithBorderColor(WorkbenchColors.Danger)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("FailedPartitionDetailPanel")
            .Build();

        var root = HorizontalGridControl.Create()
            .Column(c => c.Add(leftPane))
            .WithSplitterAfter(0)
            .Column(c => c.Width(50).Add(_detailPanel))
            .Build();

        // Apply any data that arrived before controls were ready (NavigationView lazy init).
        if (_pendingData is not null)
            UpdateData(_pendingData);

        return root;
    }

    /// <inheritdoc/>
    public void ActivateFilter(Window window)
    {
        if (_filterPrompt is not null)
        {
            window.FocusControl(_filterPrompt);
        }
    }

    /// <inheritdoc/>
    public void ClearFilter()
    {
        _currentFilter = string.Empty;
        _filterPrompt?.SetInput(string.Empty);
        RebuildFilteredRows();
    }

    /// <inheritdoc/>
    public void UpdateData(WorkbenchData data)
    {
        _pendingData = data;
        if (_table is null) return;

        _allItems = [.. data.FailedPartitions.OrderByDescending(p => p.Attempts.Count())];
        RebuildFilteredRows();
    }

    bool MatchesFilter(FailedPartition fp)
    {
        if (string.IsNullOrEmpty(_currentFilter)) return true;

        return fp.ObserverId.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase) ||
               fp.Partition.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase);
    }

    void RebuildFilteredRows()
    {
        if (_table is null) return;

        var selectedKey = _table.SelectedRow?.Tag is FailedPartition sel
            ? $"{sel.ObserverId}/{sel.Partition}"
            : null;

        _table.ClearRows();
        foreach (var fp in _allItems.Where(MatchesFilter))
        {
            _table.AddRow(new UITableRow([fp.ObserverId, fp.Partition, fp.Attempts.Count().ToString()]) { Tag = fp });
        }

        if (selectedKey is not null)
        {
            RestoreSelection(selectedKey);
        }

        RefreshDetail();
    }

    void RestoreSelection(string key)
    {
        if (_table is null) return;

        for (var i = 0; i < _table.Rows.Count; i++)
        {
            if (_table.Rows[i].Tag is FailedPartition fp &&
                $"{fp.ObserverId}/{fp.Partition}" == key)
            {
                _table.SelectedRowIndex = i;
                return;
            }
        }
    }

    void RefreshDetail()
    {
        if (_table is null || _detailPanel is null) return;

        if (_table.SelectedRow?.Tag is not FailedPartition fp)
        {
            _detailPanel.Content = $"[{WorkbenchColors.Muted.ToMarkup()}]Select a failed partition.[/]";
            return;
        }

        var mut = WorkbenchColors.Muted.ToMarkup();
        var dan = WorkbenchColors.Danger.ToMarkup();
        var acc = WorkbenchColors.Accent.ToMarkup();

        var lines = new List<string>
        {
            $"[{mut}]Observer[/]  {fp.ObserverId}",
            $"[{mut}]Partition[/] [{dan}]{fp.Partition}[/]",
            $"[{mut}]Attempts[/]  {fp.Attempts.Count()}",
            string.Empty,
            $"[{acc}]Last Attempts:[/]"
        };

        foreach (var attempt in fp.Attempts.OrderByDescending(a => a.Occurred).Take(5))
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
            if (OnRetryPartition is not null) lines.Add($"[{mut}]Press[/] [bold]T[/] [{mut}]to retry[/]");
            if (OnReplayPartition is not null) lines.Add($"[{mut}]Press[/] [bold]P[/] [{mut}]to replay[/]");
        }

        _detailPanel.Content = string.Join('\n', lines);
    }
}
