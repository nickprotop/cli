// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Namespaces tab — list of available namespaces in the current event store with a Switch action.
/// Selecting an entry and pressing Enter switches the active namespace.
/// </summary>
public class NamespacesView : IWorkbenchView
{
    TableControl? _table;
    MarkupControl? _helpPane;
    WorkbenchData? _pendingData;

    /// <summary>
    /// Gets or sets the callback invoked when the user switches to a different namespace.
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
            .AddColumn("Namespace", SharpConsoleUI.Layout.TextJustification.Left, null)
            .Interactive()
            .WithFiltering()
            .WithSorting()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .OnRowActivated((_, _) => SwitchToSelected())
            .WithName("NamespacesTable")
            .Build();

        _helpPane = new MarkupControl(
        [
            $"[{WorkbenchColors.Accent.ToMarkup()}][bold]SWITCH NAMESPACE[/][/]",
            string.Empty,
            $"  [{WorkbenchColors.Muted.ToMarkup()}]Select a namespace and press[/] [bold]Enter[/] [{WorkbenchColors.Muted.ToMarkup()}]to switch.[/]"
        ])
        { Name = "NamespacesHelp" };

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
        foreach (var name in data.NamespaceNames.Order())
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
            $"[{acc}][bold]SWITCH NAMESPACE[/][/]",
            string.Empty,
            $"[{mut}]Active[/]     [{suc}]{data.Namespace}[/]",
            $"[{mut}]Store[/]      [{mut}]{data.EventStore}[/]",
            $"[{mut}]Available[/]  {data.NamespaceNames.Count}",
            string.Empty,
            $"  [{mut}]Select a namespace and press[/] [bold]Enter[/]",
            $"  [{mut}]to make it the active namespace.[/]"
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
        if (_table?.SelectedRow?.Tag is string nsName)
        {
            OnSwitch?.Invoke(nsName);
        }
    }
}
