// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Read Models navigation item — filterable table of read model definitions with metadata in the detail pane.
/// Pressing Enter on a selected row opens a detail overlay showing definition info and live instances.
/// </summary>
public class ReadModelsView : IWorkbenchView
{
    ConsoleWindowSystem? _windowSystem;
    TableControl? _table;
    MarkupControl? _detailPane;
    PromptControl? _filterPrompt;
    string _currentFilter = string.Empty;
    List<WorkbenchReadModel> _allItems = [];
    WorkbenchData? _pendingData;

    /// <summary>
    /// Gets or sets the callback invoked when the filter input gains or loses focus.
    /// </summary>
    public Action<bool>? OnFilterFocusChanged { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user activates a read model row (Enter).
    /// Receives the container name and a cancellation token; returns a <see cref="WorkbenchData"/>
    /// snapshot with read model instances populated.
    /// </summary>
    public Func<string, CancellationToken, Task<WorkbenchData>>? OnFetchInstances { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        _table?.Dispose();
        _detailPane?.Dispose();
        _filterPrompt?.Dispose();
    }

    /// <inheritdoc/>
    public IWindowControl BuildContent(ConsoleWindowSystem windowSystem)
    {
        _windowSystem = windowSystem;

        _table = Controls.Table()
            .AddColumn("Container", SharpConsoleUI.Layout.TextJustification.Left, null)
            .AddColumn("Owner", SharpConsoleUI.Layout.TextJustification.Left, 12)
            .AddColumn("Source", SharpConsoleUI.Layout.TextJustification.Left, 14)
            .Interactive()
            .WithSorting()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .OnSelectedRowChanged((_, _) => RefreshDetail())
            .WithName("ReadModelsTable")
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
            .WithName("ReadModelsFilterPrompt")
            .Build();

        var leftPane = Controls.ScrollablePanel()
            .AddControl(_filterPrompt)
            .AddControl(_table)
            .WithVerticalScroll(ScrollMode.None)
            .WithName("ReadModelsLeftPane")
            .Build();

        _detailPane = new MarkupControl([$"[{WorkbenchColors.Muted.ToMarkup()}]Select a read model.[/]"])
        {
            Name = "ReadModelDetail",
            Wrap = true
        };

        var detailScroll = Controls.ScrollablePanel()
            .AddControl(_detailPane)
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithPadding(1, 0, 1, 0)
            .Build();

        var root = HorizontalGridControl.Create()
            .Column(c => c.Add(leftPane))
            .WithSplitterAfter(0)
            .Column(c => c.Width(50).Add(detailScroll))
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

        _allItems = [.. data.ReadModelDefinitions.OrderBy(d => d.ContainerName)];
        RebuildFilteredRows();
    }

    /// <summary>
    /// Opens a detail overlay for the currently selected read model row, if any.
    /// No-op when no row is selected.
    /// </summary>
    public void OpenSelectedDetailOverlay()
    {
        if (_table?.SelectedRow?.Tag is WorkbenchReadModel rm)
        {
            OpenDetailOverlay(rm);
        }
    }

    /// <summary>
    /// Opens a detail overlay for the given read model, fetching instances if <see cref="OnFetchInstances"/> is wired.
    /// </summary>
    /// <param name="rm">The read model to display.</param>
    public void OpenDetailOverlay(WorkbenchReadModel rm)
    {
        if (_windowSystem is null) return;

        var acc = WorkbenchColors.Accent.ToMarkup();
        var mut = WorkbenchColors.Muted.ToMarkup();
        var suc = WorkbenchColors.Success.ToMarkup();
        var queryableColor = rm.IsQueryable ? suc : mut;

        var infoContent = string.Join(
            "\n",
            $"[{acc}][bold]{rm.ContainerName}[/][/]",
            string.Empty,
            $"[{mut}]Container[/]    {rm.ContainerName}",
            $"[{mut}]Display Name[/] {rm.DisplayName}",
            $"[{mut}]Owner[/]        {rm.Owner}",
            $"[{mut}]Source[/]       {rm.Source}",
            $"[{mut}]Queryable[/]    [{queryableColor}]{(rm.IsQueryable ? "Yes" : "No")}[/]",
            $"[{mut}]Identifier[/]   {rm.Identifier}");

        var instancesContent = BuildInstancesContent(rm);

        List<(string TabName, string Content)> tabs =
        [
            ("Info", infoContent),
            ("Instances", instancesContent)
        ];

        var overlay = new DetailOverlayWindow();
        var window = overlay.Build(_windowSystem, $" {rm.ContainerName} ", tabs, []);
        _windowSystem.AddWindow(window, activateWindow: true);
    }

    static IEnumerable<string> GetCompletions() =>
    [
        "owner:client",
        "owner:server"
    ];

    bool MatchesFilter(WorkbenchReadModel rm)
    {
        if (string.IsNullOrEmpty(_currentFilter)) return true;

        var f = _currentFilter;

        if (f.StartsWith("owner:", StringComparison.OrdinalIgnoreCase))
        {
            var owner = f[6..];
            return rm.Owner.Contains(owner, StringComparison.OrdinalIgnoreCase);
        }

        return rm.ContainerName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
               rm.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    void RebuildFilteredRows()
    {
        if (_table is null) return;

        var selectedKey = (_table.SelectedRow?.Tag as WorkbenchReadModel)?.ContainerName;

        _table.ClearRows();
        foreach (var rm in _allItems.Where(MatchesFilter))
        {
            _table.AddRow(new UITableRow([rm.ContainerName, rm.Owner, rm.Source]) { Tag = rm });
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
            if (_table.Rows[i].Tag is WorkbenchReadModel rm && rm.ContainerName == key)
            {
                _table.SelectedRowIndex = i;
                return;
            }
        }
    }

    void RefreshDetail()
    {
        if (_table is null || _detailPane is null) return;

        if (_table.SelectedRow?.Tag is not WorkbenchReadModel rm)
        {
            _detailPane.Text = $"[{WorkbenchColors.Muted.ToMarkup()}]Select a read model.[/]";
            return;
        }

        var acc = WorkbenchColors.Accent.ToMarkup();
        var mut = WorkbenchColors.Muted.ToMarkup();
        var suc = WorkbenchColors.Success.ToMarkup();
        var queryableColor = rm.IsQueryable ? suc : mut;

        _detailPane.Text = string.Join(
            "\n",
            $"[{acc}][bold]READ MODEL[/][/]",
            string.Empty,
            $"  [{mut}]Container[/]    {rm.ContainerName}",
            $"  [{mut}]Display Name[/] {rm.DisplayName}",
            $"  [{mut}]Owner[/]        {rm.Owner}",
            $"  [{mut}]Source[/]       {rm.Source}",
            $"  [{mut}]Queryable[/]    [{queryableColor}]{(rm.IsQueryable ? "Yes" : "No")}[/]",
            $"  [{mut}]Identifier[/]   {rm.Identifier}",
            string.Empty,
            $"[{mut}]Press[/] [bold]Enter[/] [{mut}]to view instances[/]");
    }

    string BuildInstancesContent(WorkbenchReadModel rm)
    {
        var mut = WorkbenchColors.Muted.ToMarkup();
        var dan = WorkbenchColors.Danger.ToMarkup();

        if (OnFetchInstances is null)
        {
            return $"[{mut}](No instance loader configured)[/]";
        }

        try
        {
            var data = OnFetchInstances(rm.ContainerName, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (data.ReadModelInstancesError is not null)
            {
                return $"[{dan}]Error: {data.ReadModelInstancesError}[/]";
            }

            if (data.ReadModelInstances.Count == 0)
            {
                return $"[{mut}](No instances found)[/]";
            }

            var separator = $"\n[{mut}]{new string('─', 60)}[/]\n";
            var total = data.ReadModelInstancesTotalCount;
            var shown = data.ReadModelInstances.Count;
            var header = $"[{mut}]Showing {shown} of {total} instance{(total == 1 ? string.Empty : "s")}[/]\n";

            return header + separator + string.Join(separator, data.ReadModelInstances);
        }
        catch (Exception ex)
        {
            return $"[{dan}]Error fetching instances: {ex.Message}[/]";
        }
    }
}
