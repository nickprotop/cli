// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable RCS1251, SA1502, CA1034 // Marker types: intentionally empty and nested for branch hierarchy

namespace Cratis.Cli.Registration;

/// <summary>
/// Chronicle server commands branch. Contains all sub-branches for event stores,
/// observers, events, etc.
/// </summary>
[CliBranch("chronicle", "Commands for interacting with a Chronicle server. Contains sub-branches for event stores, namespaces, event types, events, observers, event store subscriptions, projections, read models, jobs, failed partitions, recommendations, identities, auth, users, and applications.")]
public static class ChronicleBranch
{
    /// <summary>Event store management.</summary>
    [CliBranch("event-stores", "List and discover event stores registered on the Chronicle server. Use to find valid --event-store values.")]
    public static class EventStores { }

    /// <summary>Namespace management within an event store.</summary>
    [CliBranch("namespaces", "List namespaces within an event store. Use to discover valid --namespace values.")]
    public static class Namespaces { }

    /// <summary>Event type inspection.</summary>
    [CliBranch("event-types", "List and inspect event type registrations and their JSON Schema definitions. Use to explore the domain schema.")]
    public static class EventTypes { }

    /// <summary>Event querying and inspection.</summary>
    [CliBranch("events", "Query and retrieve raw events from an event sequence. Supports filtering by type, source, sequence range, and time range.")]
    public static class Events { }

    /// <summary>Observer management (reactors, reducers, projections).</summary>
    [CliBranch("observers", "Manage observers (projections, reactors, reducers, client observers). Supports listing, inspecting, replaying, and recovering failed partitions.")]
    public static class Observers { }

    /// <summary>Event store subscription management.</summary>
    [CliBranch("event-store-subscriptions", "Manage event store subscriptions for cross-store event flow. Supports listing, adding, and removing subscriptions.")]
    public static class EventStoreSubscriptions { }

    /// <summary>Failed partition inspection.</summary>
    [CliBranch("failed-partitions", "List and inspect observer partitions that have failed and are paused. Use to diagnose and recover from processing failures.")]
    public static class FailedPartitions { }

    /// <summary>System recommendations.</summary>
    [CliBranch("recommendations", "List, perform, and ignore automated maintenance recommendations from the Chronicle server (e.g. schema migrations, projection rebuilds).")]
    public static class Recommendations { }

    /// <summary>Background job management.</summary>
    [CliBranch("jobs", "List, inspect, stop, and resume long-running background jobs such as replay and migration jobs.")]
    public static class Jobs { }

    /// <summary>Identity inspection.</summary>
    [CliBranch("identities", "List known identities (users who have interacted with the system). Use to map event actor subjects to display names and emails.")]
    public static class Identities { }

    /// <summary>Projection management.</summary>
    [CliBranch("projections", "List and inspect projection definitions including event mappings, pipeline configuration, and model schemas.")]
    public static class Projections { }

    /// <summary>Read model data inspection.</summary>
    [CliBranch("read-models", "Inspect current projected state. List definitions, retrieve instances by key, browse all instances, and view snapshots and replay history.")]
    public static class ReadModels { }

    /// <summary>Authentication management.</summary>
    [CliBranch("auth", "Manage authentication. Check current token status, log in with username and password, and log out to clear cached credentials.")]
    public static class Auth { }

    /// <summary>User management.</summary>
    [CliBranch("users", "Manage Chronicle user accounts. List, add, and remove users.")]
    public static class Users { }

    /// <summary>OAuth application management.</summary>
    [CliBranch("applications", "Manage OAuth client application registrations. List, add, and remove applications.")]
    public static class Applications { }
}
