// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Observation.EventStoreSubscriptions;

namespace Cratis.Cli.Commands.Chronicle.EventStoreSubscriptions;

/// <summary>
/// Lists event store subscriptions for a target event store.
/// </summary>
[LlmDescription("Lists event store subscriptions configured for the target event store.")]
[CliCommand("list", "List event store subscriptions", Branch = typeof(ChronicleBranch.EventStoreSubscriptions))]
[CliExample("chronicle", "subscriptions", "list", "--event-store", "system")]
[LlmOutputAdvice("plain", "Use plain for consistency with other listing commands.")]
public class ListEventStoreSubscriptionsCommand : ChronicleCommand<EventStoreSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteCommandAsync(IServices services, EventStoreSettings settings, string format)
    {
        var subscriptions = await services.EventStoreSubscriptions.GetSubscriptions(new GetEventStoreSubscriptionsRequest
        {
            TargetEventStore = settings.ResolveEventStore()
        });

        OutputFormatter.Write(
            format,
            subscriptions,
            ["Identifier", "SourceEventStore", "EventTypes"],
            subscription =>
            [
                subscription.Identifier,
                subscription.SourceEventStore,
                string.Join(", ", (subscription.EventTypes ?? []).Select(eventType => eventType.Id))
            ]);

        return ExitCodes.Success;
    }
}
