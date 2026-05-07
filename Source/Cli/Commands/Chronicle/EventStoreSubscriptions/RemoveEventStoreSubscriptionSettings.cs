// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.EventStoreSubscriptions;

/// <summary>
/// Settings for removing an event store subscription.
/// </summary>
public class RemoveEventStoreSubscriptionSettings : EventStoreSettings
{
    /// <summary>
    /// Gets or sets the subscription identifier.
    /// </summary>
    [CommandArgument(0, "<SUBSCRIPTION_ID>")]
    [Description("Subscription identifier (from 'cratis chronicle subscriptions list')")]
    public string SubscriptionId { get; set; } = string.Empty;
}
