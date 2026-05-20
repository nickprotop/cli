// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Recommendations tab — table of pending recommendations with apply/ignore actions in the bordered detail pane.
/// </summary>
public class RecommendationsView : IWorkbenchView
{
    TableControl? _table;
    PanelControl? _detailPanel;
    WorkbenchData? _pendingData;

    /// <summary>
    /// Gets or sets the callback invoked when the user applies a recommendation.
    /// </summary>
    public Action<Recommendation>? OnApply { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user ignores a recommendation.
    /// </summary>
    public Action<Recommendation>? OnIgnore { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user requests a bulk apply of all checked recommendations.
    /// </summary>
    public Action<IReadOnlyList<Recommendation>>? OnApplyAll { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user requests a bulk ignore of all checked recommendations.
    /// </summary>
    public Action<IReadOnlyList<Recommendation>>? OnIgnoreAll { get; set; }

    /// <summary>
    /// Returns all recommendations that are currently checked (checkbox mode).
    /// </summary>
    /// <returns>A list of checked <see cref="Recommendation"/> items.</returns>
    public IReadOnlyList<Recommendation> GetCheckedItems() =>
        [.. (_table?.GetCheckedRows() ?? []).Select(r => r.Tag).OfType<Recommendation>()];

    /// <inheritdoc/>
    public void Dispose()
    {
        _table?.Dispose();
        _detailPanel?.Dispose();
    }

    /// <inheritdoc/>
    public IWindowControl BuildContent(ConsoleWindowSystem windowSystem)
    {
        _table = Controls.Table()
            .AddColumn("Name", SharpConsoleUI.Layout.TextJustification.Left, null)
            .AddColumn("Type", SharpConsoleUI.Layout.TextJustification.Left, 20)
            .Interactive()
            .WithCheckboxMode()
            .WithFiltering()
            .WithSorting()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .OnSelectedRowChanged((_, _) => RefreshDetail())
            .WithName("RecommendationsTable")
            .Build();

        _detailPanel = Controls.Panel()
            .WithContent($"[{WorkbenchColors.Muted.ToMarkup()}]Select a recommendation.[/]")
            .WithHeader(" RECOMMENDATION ")
            .Rounded()
            .WithBorderColor(WorkbenchColors.Warning)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("RecommendationDetailPanel")
            .Build();

        var root = HorizontalGridControl.Create()
            .Column(c => c.Add(_table))
            .WithSplitterAfter(0)
            .Column(c => c.Width(50).Add(_detailPanel))
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

        var selectedKey = (_table.SelectedRow?.Tag as Recommendation)?.Id.ToString();

        _table.ClearRows();
        foreach (var rec in data.Recommendations)
        {
            _table.AddRow(new UITableRow([rec.Name ?? rec.Id.ToString(), rec.Type ?? "—"]) { Tag = rec });
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
            if (_table.Rows[i].Tag is Recommendation rec && rec.Id.ToString() == key)
            {
                _table.SelectedRowIndex = i;
                return;
            }
        }
    }

    void RefreshDetail()
    {
        if (_table is null || _detailPanel is null) return;

        if (_table.SelectedRow?.Tag is not Recommendation rec)
        {
            _detailPanel.Content = $"[{WorkbenchColors.Muted.ToMarkup()}]Select a recommendation.[/]";
            return;
        }

        var mut = WorkbenchColors.Muted.ToMarkup();

        var lines = new List<string>
        {
            $"[{mut}]Name[/]  {rec.Name ?? rec.Id.ToString()}",
            $"[{mut}]Type[/]  {rec.Type ?? "—"}",
        };

        if (!string.IsNullOrEmpty(rec.Description))
        {
            lines.Add(string.Empty);
            lines.Add($"[{mut}]Description:[/]");
            lines.Add($"  {rec.Description}");
        }

        lines.Add(string.Empty);
        if (OnApply is not null) lines.Add($"[{mut}]Press[/] [bold]A[/] [{mut}]to apply[/]");
        if (OnIgnore is not null) lines.Add($"[{mut}]Press[/] [bold]I[/] [{mut}]to ignore[/]");

        _detailPanel.Content = string.Join('\n', lines);
    }
}
