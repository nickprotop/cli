// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.EventStoreSubscriptions;

/// <summary>
/// Settings for adding an event store subscription.
/// </summary>
public class AddEventStoreSubscriptionSettings : EventStoreSettings
{
    /// <summary>
    /// Gets or sets the subscription identifier.
    /// </summary>
    [CommandArgument(0, "<SUBSCRIPTION_ID>")]
    [Description("Unique identifier for the subscription")]
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source event store.
    /// </summary>
    [CommandArgument(1, "<SOURCE_EVENT_STORE>")]
    [Description("Source event store to subscribe from")]
    public string SourceEventStore { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a comma-separated list of event types.
    /// </summary>
    [CommandArgument(2, "<EVENT_TYPES>")]
    [Description("Comma-separated event types to subscribe to")]
    public string EventTypes { get; set; } = string.Empty;
}
