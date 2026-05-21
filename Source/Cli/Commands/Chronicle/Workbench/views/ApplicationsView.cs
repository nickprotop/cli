// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Security;
using SharpConsoleUI.Layout;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Applications navigation item — filterable table of registered OAuth applications with a detail pane.
/// </summary>
public class ApplicationsView : FilterableTableView<Application>
{
    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns =>
    [
        ("ClientId", TextJustification.Left, null),
        ("Active", TextJustification.Left, 8),
        ("Created", TextJustification.Left, 30)
    ];

    /// <inheritdoc/>
    protected override string DetailPanelHeader => "APPLICATION";

    /// <inheritdoc/>
    protected override IEnumerable<Application> GetItems(WorkbenchData data) =>
        data.Applications.OrderBy(a => a.ClientId);

    /// <inheritdoc/>
    protected override string GetKey(Application item) => item.Id.ToString();

    /// <inheritdoc/>
    protected override string[] BuildRow(Application item)
    {
        var activeColor = item.IsActive ? WorkbenchColors.Success.ToMarkup() : WorkbenchColors.Muted.ToMarkup();
        return
        [
            item.ClientId,
            $"[{activeColor}]{(item.IsActive ? "Yes" : "No")}[/]",
            item.CreatedAt.ToString()
        ];
    }

    /// <inheritdoc/>
    protected override string RenderDetail(Application? item, WorkbenchData? data)
    {
        if (item is null)
        {
            return $"[{WorkbenchColors.Muted.ToMarkup()}]Select an application.[/]";
        }

        var mut = WorkbenchColors.Muted.ToMarkup();
        var suc = WorkbenchColors.Success.ToMarkup();
        var activeColor = item.IsActive ? suc : mut;

        return string.Join(
            "\n",
            $"[{mut}]Id[/]        {item.Id}",
            $"[{mut}]ClientId[/]  {item.ClientId}",
            $"[{mut}]Active[/]    [{activeColor}]{(item.IsActive ? "Yes" : "No")}[/]",
            $"[{mut}]Created[/]   {item.CreatedAt}");
    }

    /// <inheritdoc/>
    protected override bool MatchesFilter(Application item, string filter) =>
        item.ClientId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        item.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase);
}
