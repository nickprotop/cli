// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Event Stores tab — list of available event stores with a Switch action.
/// Selecting an entry and pressing Enter switches the active event store.
/// </summary>
public class EventStoresView : IWorkbenchView
{
    TableControl? _table;
    MarkupControl? _helpPane;
    WorkbenchData? _pendingData;

    /// <inheritdoc/>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user switches to a different event store.
    /// </summary>
    public Action<string>? OnSwitch { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        _table?.Dispose();
        _helpPane?.Dispose();
    }

    /// <inheritdoc/>
    public IWindowControl BuildContent(ConsoleWindowSystem windowSystem)
    {
        _table = Controls.Table()
            .AddColumn("Event Store", SharpConsoleUI.Layout.TextJustification.Left, null)
            .Interactive()
            .WithFiltering()
            .WithSorting()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .OnRowActivated((_, _) => SwitchToSelected())
            .WithName("EventStoresTable")
            .Build();

        _helpPane = new MarkupControl(
        [
            $"[{WorkbenchColors.Accent.ToMarkup()}][bold]SWITCH EVENT STORE[/][/]",
            string.Empty,
            $"  [{WorkbenchColors.Muted.ToMarkup()}]Select a store and press[/] [bold]Enter[/] [{WorkbenchColors.Muted.ToMarkup()}]to switch.[/]"
        ])
        { Name = "EventStoresHelp" };

        var root = HorizontalGridControl.Create()
            .Column(c => c.Add(_table))
            .WithSplitterAfter(0)
            .Column(c => c.Width(44).Add(_helpPane))
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
        if (_table is null) return;

        var selectedKey = _table.SelectedRow?.Tag as string;

        _table.ClearRows();
        foreach (var name in data.EventStoreNames.Order())
        {
            _table.AddRow(new UITableRow([name]) { Tag = name });
        }

        if (selectedKey is not null)
        {
            RestoreSelection(selectedKey);
        }

        if (_helpPane is null) return;

        var acc = WorkbenchColors.Accent.ToMarkup();
        var mut = WorkbenchColors.Muted.ToMarkup();
        var suc = WorkbenchColors.Success.ToMarkup();

        var lines = new List<string>
        {
            $"[{acc}][bold]SWITCH EVENT STORE[/][/]",
            string.Empty,
            $"[{mut}]Active[/]     [{suc}]{data.EventStore}[/]",
            $"[{mut}]Available[/]  {data.EventStoreNames.Count}",
            string.Empty,
            $"  [{mut}]Select a store and press[/] [bold]Enter[/]",
            $"  [{mut}]to make it the active event store.[/]"
        };

        _helpPane.Text = string.Join('\n', lines);
    }

    void RestoreSelection(string key)
    {
        if (_table is null) return;

        for (var i = 0; i < _table.Rows.Count; i++)
        {
            if (_table.Rows[i].Tag is string name && name == key)
            {
                _table.SelectedRowIndex = i;
                return;
            }
        }
    }

    void SwitchToSelected()
    {
        if (_table?.SelectedRow?.Tag is string storeName)
        {
            OnSwitch?.Invoke(storeName);
        }
    }
}
