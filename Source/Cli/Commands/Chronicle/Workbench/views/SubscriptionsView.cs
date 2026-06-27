// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Observation.EventStoreSubscriptions;
using SharpConsoleUI.Layout;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Subscriptions navigation item — filterable table of event store subscriptions with a detail pane.
/// </summary>
public class SubscriptionsView : FilterableTableView<EventStoreSubscriptionDefinition>
{
    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns =>
    [
        ("Identifier", TextJustification.Left, null),
        ("Source Store", TextJustification.Left, 30),
        ("Event Types", TextJustification.Right, 12)
    ];

    /// <inheritdoc/>
    protected override string DetailPanelHeader => "SUBSCRIPTION";

    /// <inheritdoc/>
    protected override string? PageTitle => "SUBSCRIPTIONS";

    /// <inheritdoc/>
    protected override IEnumerable<EventStoreSubscriptionDefinition> GetItems(WorkbenchData data) =>
        data.EventStoreSubscriptions.OrderBy(s => s.Identifier);

    /// <inheritdoc/>
    protected override string GetKey(EventStoreSubscriptionDefinition item) => item.Identifier;

    /// <inheritdoc/>
    protected override string[] BuildRow(EventStoreSubscriptionDefinition item) =>
    [
        item.Identifier,
        item.SourceEventStore,
        (item.EventTypes?.Count ?? 0).ToString()
    ];

    /// <inheritdoc/>
    protected override string RenderDetail(EventStoreSubscriptionDefinition? item, WorkbenchData? data)
    {
        if (item is null)
        {
            return SelectPrompt("a subscription");
        }

        var mut = Theme.Muted.ToMarkup();
        var acc = Theme.Accent.ToMarkup();

        var lines = new List<string>
        {
            $"[{mut}]Identifier[/]   {item.Identifier}",
            $"[{mut}]Source Store[/] {item.SourceEventStore}",
            string.Empty,
            $"[{acc}]Event Types:[/]"
        };

        foreach (var et in item.EventTypes ?? [])
        {
            lines.Add($"  • {et.Id}");
        }

        return string.Join('\n', lines);
    }

    /// <inheritdoc/>
    protected override bool MatchesFilter(EventStoreSubscriptionDefinition item, string filter) =>
        item.Identifier.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        item.SourceEventStore.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
