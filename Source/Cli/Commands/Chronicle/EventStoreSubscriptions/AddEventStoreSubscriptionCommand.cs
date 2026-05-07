// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Observation.EventStoreSubscriptions;

namespace Cratis.Cli.Commands.Chronicle.EventStoreSubscriptions;

/// <summary>
/// Adds an event store subscription.
/// </summary>
[LlmDescription("Adds an event store subscription to a target event store.")]
[CliCommand("add", "Add an event store subscription", Branch = typeof(ChronicleBranch.EventStoreSubscriptions))]
[CliExample("chronicle", "subscriptions", "add", "orders-from-default", "default", "MyCompany.Sales.OrderPlaced")]
[LlmOutputAdvice("plain", "Plain outputs a simple confirmation message.")]
[LlmOption("<SUBSCRIPTION_ID>", "string", "The unique subscription identifier (positional)")]
[LlmOption("<SOURCE_EVENT_STORE>", "string", "The source event store to subscribe to (positional)")]
[LlmOption("<EVENT_TYPES>", "string", "Comma-separated event types to include in the subscription (positional)")]
public class AddEventStoreSubscriptionCommand : ChronicleCommand<AddEventStoreSubscriptionSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteCommandAsync(IServices services, AddEventStoreSubscriptionSettings settings, string format)
    {
        var eventTypes = settings.EventTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(eventType => new Cratis.Chronicle.Contracts.Events.EventType { Id = eventType })
            .ToList();

        await services.EventStoreSubscriptions.Add(new AddEventStoreSubscriptions
        {
            TargetEventStore = settings.ResolveEventStore(),
            Subscriptions =
            [
                new EventStoreSubscriptionDefinition
                {
                    Identifier = settings.SubscriptionId,
                    SourceEventStore = settings.SourceEventStore,
                    EventTypes = eventTypes
                }
            ]
        });

        OutputFormatter.WriteMessage(format, $"Event store subscription '{settings.SubscriptionId}' added.");
        return ExitCodes.Success;
    }
}
