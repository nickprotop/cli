// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Observers navigation item — sortable table with a filter prompt on the left and bordered detail pane on the right.
/// </summary>
public class ObserversView : IWorkbenchView
{
    TableControl? _table;
    PanelControl? _detailPanel;
    PromptControl? _filterPrompt;
    ulong? _tailSequenceNumber;
    string _currentFilter = string.Empty;
    List<ObserverInformation> _allItems = [];
    WorkbenchData? _pendingData;

    /// <summary>
    /// Gets or sets the callback invoked when the filter input gains or loses focus.
    /// </summary>
    public Action<bool>? OnFilterFocusChanged { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user requests a replay of the selected observer.
    /// </summary>
    public Action<ObserverInformation>? OnReplay { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user requests a bulk replay of all checked observers.
    /// </summary>
    public Action<IReadOnlyList<ObserverInformation>>? OnReplayAll { get; set; }

    /// <summary>
    /// Returns all observers that are currently checked (checkbox mode).
    /// </summary>
    /// <returns>A list of checked <see cref="ObserverInformation"/> items.</returns>
    public IReadOnlyList<ObserverInformation> GetCheckedItems() =>
        [.. (_table?.GetCheckedRows() ?? []).Select(r => r.Tag).OfType<ObserverInformation>()];

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
            .AddColumn("State", SharpConsoleUI.Layout.TextJustification.Left, 18)
            .AddColumn("Id", SharpConsoleUI.Layout.TextJustification.Left, null)
            .AddColumn("Type", SharpConsoleUI.Layout.TextJustification.Left, 16)
            .AddColumn("Seq", SharpConsoleUI.Layout.TextJustification.Right, 12)
            .Interactive()
            .WithCheckboxMode()
            .WithSorting()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .OnSelectedRowChanged((_, _) => RefreshDetail())
            .WithName("ObserversTable")
            .Build();

        _filterPrompt = Controls.Prompt("Filter: ")
            .WithHistory(true)
            .WithTabCompleter((input, _) => GetCompletions(input))
            .OnInputChanged((_, text) =>
            {
                _currentFilter = text ?? string.Empty;
                RebuildFilteredRows();
            })
            .OnGotFocus((_, _) => OnFilterFocusChanged?.Invoke(true))
            .OnLostFocus((_, _) => OnFilterFocusChanged?.Invoke(false))
            .WithName("ObserversFilterPrompt")
            .Build();

        var leftPane = Controls.ScrollablePanel()
            .AddControl(_filterPrompt)
            .AddControl(_table)
            .WithVerticalScroll(ScrollMode.None)
            .WithName("ObserversLeftPane")
            .Build();

        _detailPanel = Controls.Panel()
            .WithContent($"[{WorkbenchColors.Muted.ToMarkup()}]Select an observer.[/]")
            .WithHeader(" OBSERVER ")
            .Rounded()
            .WithBorderColor(WorkbenchColors.Accent)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("ObserverDetailPanel")
            .Build();

        var root = HorizontalGridControl.Create()
            .Column(c => c.Add(leftPane))
            .WithSplitterAfter(0)
            .Column(c => c.Width(45).Add(_detailPanel))
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

        _tailSequenceNumber = data.TailSequenceNumber;
        _allItems = [.. data.Observers.OrderBy(ObserverSortOrder).ThenBy(o => o.Id)];
        RebuildFilteredRows();
    }

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

    IEnumerable<string> GetCompletions(string input) =>
    [
        "state:active",
        "state:replaying",
        "state:disconnected",
        "state:suspended",
        "type:projection",
        "type:reducer",
        "type:reactor"
    ];

    bool MatchesFilter(ObserverInformation obs)
    {
        if (string.IsNullOrEmpty(_currentFilter)) return true;

        var f = _currentFilter;

        if (f.StartsWith("state:", StringComparison.OrdinalIgnoreCase))
        {
            var state = f[6..];
            return obs.RunningState.ToString().Contains(state, StringComparison.OrdinalIgnoreCase);
        }

        if (f.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
        {
            var type = f[5..];
            return obs.Type.ToString().Contains(type, StringComparison.OrdinalIgnoreCase);
        }

        return obs.Id.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    void RebuildFilteredRows()
    {
        if (_table is null) return;

        var selectedKey = (_table.SelectedRow?.Tag as ObserverInformation)?.Id;

        _table.ClearRows();
        foreach (var obs in _allItems.Where(MatchesFilter))
        {
            _table.AddRow(new UITableRow(BuildObserverRow(obs)) { Tag = obs });
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
            if (_table.Rows[i].Tag is ObserverInformation obs && obs.Id == key)
            {
                _table.SelectedRowIndex = i;
                return;
            }
        }
    }

    string[] BuildObserverRow(ObserverInformation obs)
    {
        var stateColor = GetObserverStateColor(obs);
        var icon = GetObserverIcon(obs);

        string seqCell;
        if (obs.RunningState == ObserverRunningState.Replaying && _tailSequenceNumber.HasValue && _tailSequenceNumber.Value > 0)
        {
            var tail = _tailSequenceNumber.Value;
            var current = obs.LastHandledEventSequenceNumber == ulong.MaxValue ? 0UL : obs.LastHandledEventSequenceNumber;
            var pct = (int)Math.Min(100, current * 100 / tail);
            var filledBars = pct / 10;
            var bar = new string('█', filledBars) + new string('░', 10 - filledBars);
            seqCell = $"[{WorkbenchColors.Warning.ToMarkup()}]{bar} {pct}%[/]";
        }
        else
        {
            seqCell = obs.LastHandledEventSequenceNumber == ulong.MaxValue
                ? $"[{WorkbenchColors.Muted.ToMarkup()}]—[/]"
                : obs.LastHandledEventSequenceNumber.ToString("N0");
        }

        return
        [
            $"[{stateColor}]{icon} {obs.RunningState}[/]",
            obs.Id,
            obs.Type.ToString(),
            seqCell
        ];
    }

    void RefreshDetail()
    {
        if (_table is null || _detailPanel is null) return;

        var row = _table.SelectedRow;
        if (row?.Tag is not ObserverInformation obs)
        {
            _detailPanel.Content = $"[{WorkbenchColors.Muted.ToMarkup()}]Select an observer.[/]";
            return;
        }

        var mut = WorkbenchColors.Muted.ToMarkup();
        var stateColor = GetObserverStateColor(obs);

        var lines = new List<string>
        {
            $"[{mut}]Id[/]      {obs.Id}",
            $"[{mut}]Type[/]    {obs.Type}",
            $"[{mut}]State[/]   [{stateColor}]{obs.RunningState}[/]",
            $"[{mut}]Seq[/]     {obs.LastHandledEventSequenceNumber}",
            string.Empty,
            $"[{mut}]Event Types:[/]"
        };

        foreach (var et in (obs.EventTypes ?? []).OrderBy(e => e.Id))
        {
            lines.Add($"  • {et.Id} gen {et.Generation}");
        }

        if (OnReplay is not null)
        {
            lines.Add(string.Empty);
            lines.Add($"[{mut}]Press[/] [bold]R[/] [{mut}]to replay[/]");
        }

        _detailPanel.Content = string.Join('\n', lines);
    }
}
