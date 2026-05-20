// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Event Log navigation item — filterable, sortable table of recent events with a bordered detail pane showing event content.
/// </summary>
public class EventLogView : IWorkbenchView
{
    TableControl? _table;
    PanelControl? _detailPanel;
    PromptControl? _filterPrompt;
    string _currentFilter = string.Empty;
    List<AppendedEvent> _allEvents = [];
    WorkbenchData? _pendingData;

    /// <summary>
    /// Gets or sets the callback invoked when the filter input gains or loses focus.
    /// </summary>
    public Action<bool>? OnFilterFocusChanged { get; set; }

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
            .AddColumn("#", SharpConsoleUI.Layout.TextJustification.Right, 10)
            .AddColumn("Occurred", SharpConsoleUI.Layout.TextJustification.Left, 22)
            .AddColumn("Event Type", SharpConsoleUI.Layout.TextJustification.Left, null)
            .AddColumn("Source", SharpConsoleUI.Layout.TextJustification.Left, 30)
            .Interactive()
            .WithSorting()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .OnSelectedRowChanged((_, _) => RefreshDetail())
            .WithName("EventLogTable")
            .Build();

        _filterPrompt = Controls.Prompt("Filter: ")
            .WithHistory(true)
            .WithTabCompleter((input, _) => GetCompletions())
            .OnInputChanged((_, text) =>
            {
                _currentFilter = text ?? string.Empty;
                RebuildFilteredRows();
            })
            .OnGotFocus((_, _) => OnFilterFocusChanged?.Invoke(true))
            .OnLostFocus((_, _) => OnFilterFocusChanged?.Invoke(false))
            .WithName("EventLogFilterPrompt")
            .Build();

        var leftPane = Controls.ScrollablePanel()
            .AddControl(_filterPrompt)
            .AddControl(_table)
            .WithVerticalScroll(ScrollMode.None)
            .WithName("EventLogLeftPane")
            .Build();

        _detailPanel = Controls.Panel()
            .WithContent($"[{WorkbenchColors.Muted.ToMarkup()}]Select an event.[/]")
            .WithHeader(" EVENT ")
            .Rounded()
            .WithBorderColor(WorkbenchColors.Accent)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("EventLogDetailPanel")
            .Build();

        var root = HorizontalGridControl.Create()
            .Column(c => c.Add(leftPane))
            .WithSplitterAfter(0)
            .Column(c => c.Width(55).Add(_detailPanel))
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

        _allEvents = [.. data.RecentEvents];
        RebuildFilteredRows();
    }

    IEnumerable<string> GetCompletions() =>
        _allEvents
            .Select(e => $"type:{e.Context.EventType.Id}")
            .Distinct()
            .Order();

    bool MatchesFilter(AppendedEvent evt)
    {
        if (string.IsNullOrEmpty(_currentFilter)) return true;

        var f = _currentFilter;

        if (f.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
        {
            var type = f[5..];
            return evt.Context.EventType.Id.Contains(type, StringComparison.OrdinalIgnoreCase);
        }

        return evt.Context.EventType.Id.Contains(f, StringComparison.OrdinalIgnoreCase) ||
               (evt.Context.EventSourceId ?? string.Empty).Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    void RebuildFilteredRows()
    {
        if (_table is null) return;

        var selectedKey = (_table.SelectedRow?.Tag as AppendedEvent)?.Context.SequenceNumber.ToString();

        _table.ClearRows();
        foreach (var evt in _allEvents.Where(MatchesFilter))
        {
            _table.AddRow(new UITableRow(
            [
                evt.Context.SequenceNumber.ToString(),
                evt.Context.Occurred.ToString(),
                evt.Context.EventType.Id,
                evt.Context.EventSourceId ?? string.Empty
            ])
            { Tag = evt });
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
            if (_table.Rows[i].Tag is AppendedEvent evt &&
                evt.Context.SequenceNumber.ToString() == key)
            {
                _table.SelectedRowIndex = i;
                return;
            }
        }
    }

    void RefreshDetail()
    {
        if (_table is null || _detailPanel is null) return;

        if (_table.SelectedRow?.Tag is not AppendedEvent evt)
        {
            _detailPanel.Content = $"[{WorkbenchColors.Muted.ToMarkup()}]Select an event.[/]";
            return;
        }

        var acc = WorkbenchColors.Accent.ToMarkup();
        var mut = WorkbenchColors.Muted.ToMarkup();

        var lines = new List<string>
        {
            $"[{mut}]Seq#[/]        {evt.Context.SequenceNumber}",
            $"[{mut}]Type[/]        [{acc}]{evt.Context.EventType.Id}[/] gen {evt.Context.EventType.Generation}",
            $"[{mut}]Source[/]      {evt.Context.EventSourceId ?? "—"}",
            $"[{mut}]Occurred[/]    {evt.Context.Occurred}",
            $"[{mut}]Correlation[/] {evt.Context.CorrelationId}",
            string.Empty,
            $"[{acc}]Content:[/]"
        };

        if (!string.IsNullOrEmpty(evt.Content))
        {
            foreach (var line in evt.Content.Split('\n').Take(30))
            {
                lines.Add($"[{mut}]{line.TrimEnd()}[/]");
            }
        }
        else
        {
            lines.Add($"[{mut}](no content)[/]");
        }

        _detailPanel.Content = string.Join('\n', lines);
    }
}
