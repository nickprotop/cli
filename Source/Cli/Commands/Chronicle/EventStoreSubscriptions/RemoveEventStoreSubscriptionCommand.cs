// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Observation.EventStoreSubscriptions;

namespace Cratis.Cli.Commands.Chronicle.EventStoreSubscriptions;

/// <summary>
/// Removes an event store subscription.
/// </summary>
[LlmDescription("Removes an event store subscription from a target event store. Destructive — prompts for confirmation unless --yes is specified.")]
[CliCommand("remove", "Remove an event store subscription", Branch = typeof(ChronicleBranch.EventStoreSubscriptions), DynamicCompletion = "subscriptions")]
[CliExample("chronicle", "subscriptions", "remove", "orders-from-default")]
[LlmOutputAdvice("plain", "Plain outputs a simple confirmation message.")]
[LlmOption("<SUBSCRIPTION_ID>", "string", "The unique subscription identifier to remove (positional)")]
public class RemoveEventStoreSubscriptionCommand : ChronicleCommand<RemoveEventStoreSubscriptionSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteCommandAsync(IServices services, RemoveEventStoreSubscriptionSettings settings, string format)
    {
        if (!ConfirmationHelper.ShouldProceed(settings, $"Are you sure you want to remove event store subscription '{settings.SubscriptionId}'?"))
        {
            OutputFormatter.WriteMessage(format, "Aborted.");
            return ExitCodes.Success;
        }

        await services.EventStoreSubscriptions.Remove(new RemoveEventStoreSubscriptions
        {
            TargetEventStore = settings.ResolveEventStore(),
            SubscriptionIds = [settings.SubscriptionId]
        });

        OutputFormatter.WriteMessage(format, $"Event store subscription '{settings.SubscriptionId}' removed.");
        return ExitCodes.Success;
    }
}
