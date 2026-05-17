// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// The active view displayed in the workbench dashboard.
/// </summary>
public enum WorkbenchView
{
    // Primary views — cycled with ← → and accessed with keys 1–8.

    /// <summary>Overview showing server health, observer counts, and any failures.</summary>
    Overview = 0,

    /// <summary>Full list of all observers with their running states and sequence lag.</summary>
    Observers = 1,

    /// <summary>List of observer partitions that have failed and need attention.</summary>
    FailedPartitions = 2,

    /// <summary>Background jobs running on the Chronicle server.</summary>
    Jobs = 3,

    /// <summary>Pending recommendations from the Chronicle server, with apply and ignore actions.</summary>
    Recommendations = 4,

    /// <summary>The live event log — last 50 events, newest first.</summary>
    EventLog = 5,

    /// <summary>All registered event types with their JSON schemas.</summary>
    EventTypes = 6,

    /// <summary>Projection definitions and their full declarations.</summary>
    Projections = 7,

    /// <summary>Read model definitions and live instances.</summary>
    ReadModels = 8,

    /// <summary>All event stores on the Chronicle server, with the ability to switch the active store.</summary>
    EventStores = 9,

    /// <summary>All namespaces in the current event store, with the ability to switch the active namespace.</summary>
    Namespaces = 10,

    // Detail views — entered with Enter from a primary view, exited with Escape.
    // Values ≥ 100 are never shown in the tab bar.

    /// <summary>Full-screen detail for a single observer, with a navigable event-type sub-list.</summary>
    ObserverDetail = 100,

    /// <summary>Full-screen detail for a single failed partition, including attempt history.</summary>
    FailedPartitionDetail = 101,

    /// <summary>Full-screen detail for a single appended event, including its full JSON content.</summary>
    EventDetail = 102,

    /// <summary>Full-screen detail for a single event type, including its JSON schema.</summary>
    EventTypeDetail = 103,

    /// <summary>Full-screen detail for a single projection, including its full declaration.</summary>
    ProjectionDetail = 104,

    /// <summary>Full-screen detail for a read model, showing its live instances.</summary>
    ReadModelDetail = 105,
}
