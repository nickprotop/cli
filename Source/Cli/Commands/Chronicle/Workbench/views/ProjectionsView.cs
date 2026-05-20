// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Projections navigation item — filterable table of projection definitions with declaration preview in the detail pane.
/// </summary>
public class ProjectionsView : IWorkbenchView
{
    TableControl? _table;
    MarkupControl? _detailPane;
    PromptControl? _filterPrompt;
    IReadOnlyDictionary<string, string> _declarations = new Dictionary<string, string>();
    string _currentFilter = string.Empty;
    List<ProjectionDefinition> _allItems = [];
    WorkbenchData? _pendingData;

    /// <summary>
    /// Gets or sets the callback invoked when the filter input gains or loses focus.
    /// </summary>
    public Action<bool>? OnFilterFocusChanged { get; set; }

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
        _table = Controls.Table()
            .AddColumn("Identifier", SharpConsoleUI.Layout.TextJustification.Left, null)
            .AddColumn("Read Model", SharpConsoleUI.Layout.TextJustification.Left, 35)
            .Interactive()
            .WithSorting()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .OnSelectedRowChanged((_, _) => RefreshDetail())
            .WithName("ProjectionsTable")
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
            .WithName("ProjectionsFilterPrompt")
            .Build();

        var leftPane = Controls.ScrollablePanel()
            .AddControl(_filterPrompt)
            .AddControl(_table)
            .WithVerticalScroll(ScrollMode.None)
            .WithName("ProjectionsLeftPane")
            .Build();

        _detailPane = new MarkupControl([$"[{WorkbenchColors.Muted.ToMarkup()}]Select a projection.[/]"])
        {
            Name = "ProjectionDetail",
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
            .Column(c => c.Width(55).Add(detailScroll))
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

        _declarations = data.ProjectionDeclarations;
        _allItems = [.. data.ProjectionDefinitions.OrderBy(d => d.Identifier)];
        RebuildFilteredRows();
    }

    bool MatchesFilter(ProjectionDefinition def)
    {
        if (string.IsNullOrEmpty(_currentFilter)) return true;

        return def.Identifier.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase);
    }

    void RebuildFilteredRows()
    {
        if (_table is null) return;

        var selectedKey = (_table.SelectedRow?.Tag as ProjectionDefinition)?.Identifier;

        _table.ClearRows();
        foreach (var def in _allItems.Where(MatchesFilter))
        {
            _table.AddRow(new UITableRow([def.Identifier, def.ReadModel ?? string.Empty]) { Tag = def });
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
            if (_table.Rows[i].Tag is ProjectionDefinition def && def.Identifier == key)
            {
                _table.SelectedRowIndex = i;
                return;
            }
        }
    }

    void RefreshDetail()
    {
        if (_table is null || _detailPane is null) return;

        if (_table.SelectedRow?.Tag is not ProjectionDefinition def)
        {
            _detailPane.Text = $"[{WorkbenchColors.Muted.ToMarkup()}]Select a projection.[/]";
            return;
        }

        var acc = WorkbenchColors.Accent.ToMarkup();
        var mut = WorkbenchColors.Muted.ToMarkup();
        var suc = WorkbenchColors.Success.ToMarkup();

        var lines = new List<string>
        {
            $"[{acc}][bold]PROJECTION[/][/]",
            string.Empty,
            $"  [{mut}]Identifier[/]  {def.Identifier}",
            $"  [{mut}]Read Model[/]  {def.ReadModel ?? "—"}",
            $"  [{mut}]Active[/]      [{(def.IsActive ? suc : mut)}]{(def.IsActive ? "Yes" : "No")}[/]",
            $"  [{mut}]Rewindable[/]  [{(def.IsRewindable ? suc : mut)}]{(def.IsRewindable ? "Yes" : "No")}[/]",
            string.Empty,
            $"  [{acc}]Declaration (preview):[/]"
        };

        if (_declarations.TryGetValue(def.Identifier, out var declaration) && !string.IsNullOrEmpty(declaration))
        {
            foreach (var line in declaration.Split('\n').Take(40))
            {
                lines.Add($"  [{mut}]{line.TrimEnd()}[/]");
            }
        }
        else
        {
            lines.Add($"  [{mut}](no declaration available)[/]");
        }

        _detailPane.Text = string.Join('\n', lines);
    }
}
