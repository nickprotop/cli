// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Event Types navigation item — filterable table of registered event types with schema details in the bordered right pane.
/// </summary>
public class EventTypesView : IWorkbenchView
{
    TableControl? _table;
    PanelControl? _detailPanel;
    PromptControl? _filterPrompt;
    string _currentFilter = string.Empty;
    List<EventTypeRegistration> _allItems = [];
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
            .AddColumn("Id", SharpConsoleUI.Layout.TextJustification.Left, null)
            .AddColumn("Gen", SharpConsoleUI.Layout.TextJustification.Right, 6)
            .AddColumn("Owner", SharpConsoleUI.Layout.TextJustification.Left, 20)
            .Interactive()
            .WithSorting()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .OnSelectedRowChanged((_, _) => RefreshDetail())
            .WithName("EventTypesTable")
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
            .WithName("EventTypesFilterPrompt")
            .Build();

        var leftPane = Controls.ScrollablePanel()
            .AddControl(_filterPrompt)
            .AddControl(_table)
            .WithVerticalScroll(ScrollMode.None)
            .WithName("EventTypesLeftPane")
            .Build();

        _detailPanel = Controls.Panel()
            .WithContent($"[{WorkbenchColors.Muted.ToMarkup()}]Select an event type.[/]")
            .WithHeader(" EVENT TYPE ")
            .Rounded()
            .WithBorderColor(WorkbenchColors.Accent)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("EventTypeDetailPanel")
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

        _allItems = [.. data.EventTypeRegistrations.OrderBy(r => r.Type.Id).ThenBy(r => r.Type.Generation)];
        RebuildFilteredRows();
    }

    static IEnumerable<string> GetCompletions() =>
    [
        "owner:client",
        "owner:server",
        "gen:1",
        "gen:2"
    ];

    bool MatchesFilter(EventTypeRegistration reg)
    {
        if (string.IsNullOrEmpty(_currentFilter)) return true;

        var f = _currentFilter;

        if (f.StartsWith("owner:", StringComparison.OrdinalIgnoreCase))
        {
            var owner = f[6..];
            return reg.Owner.ToString().Contains(owner, StringComparison.OrdinalIgnoreCase);
        }

        if (f.StartsWith("gen:", StringComparison.OrdinalIgnoreCase))
        {
            var gen = f[4..];
            return reg.Type.Generation.ToString().Contains(gen, StringComparison.OrdinalIgnoreCase);
        }

        return reg.Type.Id.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    void RebuildFilteredRows()
    {
        if (_table is null) return;

        var selectedKey = _table.SelectedRow?.Tag is EventTypeRegistration sel
            ? $"{sel.Type.Id}+{sel.Type.Generation}"
            : null;

        _table.ClearRows();
        foreach (var reg in _allItems.Where(MatchesFilter))
        {
            _table.AddRow(new UITableRow([reg.Type.Id, reg.Type.Generation.ToString(), reg.Owner.ToString()]) { Tag = reg });
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
            if (_table.Rows[i].Tag is EventTypeRegistration reg &&
                $"{reg.Type.Id}+{reg.Type.Generation}" == key)
            {
                _table.SelectedRowIndex = i;
                return;
            }
        }
    }

    void RefreshDetail()
    {
        if (_table is null || _detailPanel is null) return;

        if (_table.SelectedRow?.Tag is not EventTypeRegistration reg)
        {
            _detailPanel.Content = $"[{WorkbenchColors.Muted.ToMarkup()}]Select an event type.[/]";
            return;
        }

        var acc = WorkbenchColors.Accent.ToMarkup();
        var mut = WorkbenchColors.Muted.ToMarkup();

        var lines = new List<string>
        {
            $"[{mut}]Id[/]           {reg.Type.Id}",
            $"[{mut}]Generation[/]   {reg.Type.Generation}",
            $"[{mut}]Owner[/]        {reg.Owner}",
            $"[{mut}]Source[/]       {reg.Source}",
            $"[{mut}]Tombstone[/]    {reg.Type.Tombstone}",
            string.Empty,
            $"[{acc}]Schema:[/]"
        };

        if (!string.IsNullOrEmpty(reg.Schema))
        {
            foreach (var line in reg.Schema.Split('\n').Take(40))
            {
                lines.Add($"[{mut}]{line.TrimEnd()}[/]");
            }
        }
        else
        {
            lines.Add($"[{mut}](no schema)[/]");
        }

        _detailPanel.Content = string.Join('\n', lines);
    }
}
