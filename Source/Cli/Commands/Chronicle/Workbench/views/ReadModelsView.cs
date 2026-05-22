// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Read Models navigation item — filterable table of read model definitions with metadata in the detail pane.
/// Pressing Enter on a selected row opens a detail overlay showing definition info and live instances.
/// </summary>
public class ReadModelsView : FilterableTableView<WorkbenchReadModel>
{
    ConsoleWindowSystem? _windowSystem;

    /// <summary>
    /// Gets or sets the callback invoked when the user activates a read model row (Enter).
    /// Receives the container name and a cancellation token; returns a <see cref="WorkbenchData"/>
    /// snapshot with read model instances populated.
    /// </summary>
    public Func<string, CancellationToken, Task<WorkbenchData>>? OnFetchInstances { get; set; }

    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns =>
    [
        ("Container", TextJustification.Left, null),
        ("Owner", TextJustification.Left, 12),
        ("Source", TextJustification.Left, 14)
    ];

    /// <inheritdoc/>
    protected override string DetailPanelHeader => "READ MODEL";

    /// <inheritdoc/>
    protected override SharpConsoleUI.Color DetailBorderColor => WorkbenchColors.Mauve;

    /// <inheritdoc/>
    public override IWindowControl BuildContent(ConsoleWindowSystem windowSystem)
    {
        _windowSystem = windowSystem;
        return base.BuildContent(windowSystem);
    }

    /// <summary>
    /// Opens a detail overlay for the currently selected read model row, if any.
    /// </summary>
    public void OpenSelectedDetailOverlay()
    {
        if (SelectedItem is WorkbenchReadModel rm)
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
        if (_windowSystem is null)
        {
            return;
        }

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

    /// <inheritdoc/>
    protected override IEnumerable<WorkbenchReadModel> GetItems(WorkbenchData data) =>
        data.ReadModelDefinitions.OrderBy(d => d.ContainerName);

    /// <inheritdoc/>
    protected override string GetKey(WorkbenchReadModel item) => item.ContainerName;

    /// <inheritdoc/>
    protected override string[] BuildRow(WorkbenchReadModel item) =>
        [item.ContainerName, item.Owner, item.Source];

    /// <inheritdoc/>
    protected override string RenderDetail(WorkbenchReadModel? item, WorkbenchData? data)
    {
        if (item is null)
        {
            return $"[{WorkbenchColors.Muted.ToMarkup()}]Select a read model.[/]";
        }

        var acc = WorkbenchColors.Accent.ToMarkup();
        var mut = WorkbenchColors.Muted.ToMarkup();
        var suc = WorkbenchColors.Success.ToMarkup();
        var queryableColor = item.IsQueryable ? suc : mut;

        return string.Join(
            "\n",
            $"[{acc}][bold]READ MODEL[/][/]",
            string.Empty,
            $"  [{mut}]Container[/]    {item.ContainerName}",
            $"  [{mut}]Display Name[/] {item.DisplayName}",
            $"  [{mut}]Owner[/]        {item.Owner}",
            $"  [{mut}]Source[/]       {item.Source}",
            $"  [{mut}]Queryable[/]    [{queryableColor}]{(item.IsQueryable ? "Yes" : "No")}[/]",
            $"  [{mut}]Identifier[/]   {item.Identifier}",
            string.Empty,
            $"[{mut}]Press[/] [bold]Enter[/] [{mut}]to view instances[/]");
    }

    /// <inheritdoc/>
    protected override bool MatchesFilter(WorkbenchReadModel item, string filter)
    {
        if (filter.StartsWith("owner:", StringComparison.OrdinalIgnoreCase))
        {
            return item.Owner.Contains(filter[6..], StringComparison.OrdinalIgnoreCase);
        }

        return item.ContainerName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    protected override IEnumerable<string> GetCompletions(string input) =>
    [
        "owner:client",
        "owner:server"
    ];

    /// <inheritdoc/>
    protected override void OnRowActivated(WorkbenchReadModel item) => OpenDetailOverlay(item);

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
