// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Themes;

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
    protected override ColorRole DetailColorRole => ColorRole.Secondary;

    /// <inheritdoc/>
    protected override string? PageTitle => "READ MODELS";

    /// <inheritdoc/>
    protected override string EmptyStateMessage => "No read models defined.";

    /// <inheritdoc/>
    public override void PopulateContent(SharpConsoleUI.Controls.ScrollablePanelControl panel, ConsoleWindowSystem windowSystem)
    {
        _windowSystem = windowSystem;
        base.PopulateContent(panel, windowSystem);
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

        var acc = Theme.Accent.ToMarkup();
        var mut = Theme.Muted.ToMarkup();
        var suc = Theme.Success.ToMarkup();
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

        // Open immediately with a placeholder for the Instances tab, then fetch off the UI thread so
        // activating a row never blocks (or deadlocks) the render loop. The fetched content is pushed
        // back into the tab editor on the UI thread once it arrives.
        const string instancesTab = "Instances";
        var loadingContent = OnFetchInstances is null
            ? $"[{mut}](No instance loader configured)[/]"
            : $"[{mut}]Loading instances…[/]";

        List<(string TabName, string Content)> tabs =
        [
            ("Info", infoContent),
            (instancesTab, loadingContent)
        ];

        var overlay = new DetailOverlayWindow();
        var window = overlay.Build(_windowSystem, $" {rm.ContainerName} ", tabs, []);
        _windowSystem.AddWindow(window, activateWindow: true);

        if (OnFetchInstances is null)
        {
            return;
        }

        var windowSystem = _windowSystem;
        _ = Task.Run(async () =>
        {
            var content = await FetchInstancesContentAsync(rm).ConfigureAwait(false);
            windowSystem.EnqueueOnUIThread(() =>
            {
                if (overlay.TabEditors.TryGetValue(instancesTab, out var editor))
                {
                    // The overlay strips markup to plain text for its read-only editors.
                    editor.SetContent(Markup.Remove(content));
                }
            });
        });
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
            return SelectPrompt("a read model");
        }

        var acc = Theme.Accent.ToMarkup();
        var mut = Theme.Muted.ToMarkup();
        var suc = Theme.Success.ToMarkup();
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
    protected override void OnInspect(WorkbenchReadModel item) => OpenDetailOverlay(item);

    async Task<string> FetchInstancesContentAsync(WorkbenchReadModel rm)
    {
        var mut = Theme.Muted.ToMarkup();
        var dan = Theme.Danger.ToMarkup();

        if (OnFetchInstances is null)
        {
            return $"[{mut}](No instance loader configured)[/]";
        }

        try
        {
            var data = await OnFetchInstances(rm.ContainerName, CancellationToken.None)
                .ConfigureAwait(false);

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
