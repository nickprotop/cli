// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Identities;
using SharpConsoleUI.Layout;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Identities navigation item — filterable table of known identities with a detail pane.
/// </summary>
public class IdentitiesView : FilterableTableView<Identity>
{
    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns =>
    [
        ("Name", TextJustification.Left, null),
        ("UserName", TextJustification.Left, 30),
        ("Subject", TextJustification.Left, 40)
    ];

    /// <inheritdoc/>
    protected override string DetailPanelHeader => "IDENTITY";

    /// <inheritdoc/>
    protected override IEnumerable<Identity> GetItems(WorkbenchData data) =>
        data.Identities.OrderBy(i => i.Name);

    /// <inheritdoc/>
    protected override string GetKey(Identity item) => item.Subject;

    /// <inheritdoc/>
    protected override string[] BuildRow(Identity item) =>
        [item.Name, item.UserName, item.Subject];

    /// <inheritdoc/>
    protected override string RenderDetail(Identity? item, WorkbenchData? data)
    {
        if (item is null)
        {
            return $"[{WorkbenchColors.Muted.ToMarkup()}]Select an identity.[/]";
        }

        var mut = WorkbenchColors.Muted.ToMarkup();

        return string.Join(
            "\n",
            $"[{mut}]Subject[/]  {item.Subject}",
            $"[{mut}]Name[/]     {item.Name}",
            $"[{mut}]UserName[/] {item.UserName}");
    }

    /// <inheritdoc/>
    protected override bool MatchesFilter(Identity item, string filter) =>
        item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        item.UserName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        item.Subject.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
