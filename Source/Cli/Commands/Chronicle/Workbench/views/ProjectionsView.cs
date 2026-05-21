// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI.Layout;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Projections navigation item — filterable table of projection definitions with declaration preview in the detail pane.
/// </summary>
public class ProjectionsView : FilterableTableView<ProjectionDefinition>
{
    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns =>
    [
        ("Identifier", TextJustification.Left, null),
        ("Read Model", TextJustification.Left, 35)
    ];

    /// <inheritdoc/>
    protected override string DetailPanelHeader => "PROJECTION";

    /// <inheritdoc/>
    protected override IEnumerable<ProjectionDefinition> GetItems(WorkbenchData data) =>
        data.ProjectionDefinitions.OrderBy(d => d.Identifier);

    /// <inheritdoc/>
    protected override string GetKey(ProjectionDefinition item) => item.Identifier;

    /// <inheritdoc/>
    protected override string[] BuildRow(ProjectionDefinition item) =>
        [item.Identifier, item.ReadModel ?? string.Empty];

    /// <inheritdoc/>
    protected override string RenderDetail(ProjectionDefinition? item, WorkbenchData? data)
    {
        if (item is null)
        {
            return $"[{WorkbenchColors.Muted.ToMarkup()}]Select a projection.[/]";
        }

        var acc = WorkbenchColors.Accent.ToMarkup();
        var mut = WorkbenchColors.Muted.ToMarkup();
        var suc = WorkbenchColors.Success.ToMarkup();

        var lines = new List<string>
        {
            $"[{acc}][bold]PROJECTION[/][/]",
            string.Empty,
            $"  [{mut}]Identifier[/]  {item.Identifier}",
            $"  [{mut}]Read Model[/]  {item.ReadModel ?? "—"}",
            $"  [{mut}]Active[/]      [{(item.IsActive ? suc : mut)}]{(item.IsActive ? "Yes" : "No")}[/]",
            $"  [{mut}]Rewindable[/]  [{(item.IsRewindable ? suc : mut)}]{(item.IsRewindable ? "Yes" : "No")}[/]",
            string.Empty,
            $"  [{acc}]Declaration (preview):[/]"
        };

        var declarations = data?.ProjectionDeclarations ?? new Dictionary<string, string>();
        if (declarations.TryGetValue(item.Identifier, out var declaration) && !string.IsNullOrEmpty(declaration))
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

        return string.Join('\n', lines);
    }

    /// <inheritdoc/>
    protected override bool MatchesFilter(ProjectionDefinition item, string filter) =>
        item.Identifier.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
