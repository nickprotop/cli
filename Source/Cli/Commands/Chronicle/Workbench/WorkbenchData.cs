// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Identities;
using Cratis.Chronicle.Contracts.Jobs;
using Cratis.Chronicle.Contracts.Observation.EventStoreSubscriptions;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Lightweight read-model descriptor used within the workbench (avoids coupling to the concrete contract type).
/// </summary>
/// <param name="ContainerName">The container / collection name used to query instances.</param>
/// <param name="DisplayName">Human-readable display name.</param>
/// <param name="Owner">Owner (Client or Server).</param>
/// <param name="IsQueryable">Whether instances can be fetched from the server.</param>
/// <param name="Source">The source that produced this read model.</param>
/// <param name="Identifier">The type identifier string.</param>
public record WorkbenchReadModel(
    string ContainerName,
    string DisplayName,
    string Owner,
    bool IsQueryable,
    string Source,
    string Identifier);

/// <summary>
/// A point-in-time snapshot of all data fetched from the Chronicle server for the workbench.
/// </summary>
/// <param name="ConnectionString">The resolved connection string (may contain credentials).</param>
/// <param name="EventStore">The event store name.</param>
/// <param name="Namespace">The namespace name.</param>
/// <param name="IsConnected">Whether the server was reachable during this fetch.</param>
/// <param name="ServerVersion">The server version string, or <see langword="null"/> if unreachable.</param>
/// <param name="EventStoreNames">All event store names registered on the server.</param>
/// <param name="Observers">All observers in the event store namespace.</param>
/// <param name="FailedPartitions">All failed observer partitions in the event store namespace.</param>
/// <param name="Jobs">All background jobs in the event store namespace.</param>
/// <param name="Recommendations">All pending recommendations in the event store namespace.</param>
/// <param name="TailSequenceNumber">The current tail of the event log, or <see langword="null"/> if unavailable.</param>
/// <param name="CapturedAt">When this snapshot was captured.</param>
/// <param name="FetchError">An error message from the last fetch attempt, or <see langword="null"/> if successful.</param>
/// <param name="EventTypeRegistrations">All registered event types in the event store.</param>
/// <param name="ProjectionDefinitions">All projection definitions in the event store.</param>
/// <param name="ProjectionDeclarations">Full projection declarations (identifier → declaration text).</param>
/// <param name="RecentEvents">The last 50 events from the event log, newest first.</param>
/// <param name="ReadModelDefinitions">All read-model definitions registered in the event store.</param>
/// <param name="NamespaceNames">All namespace names available in the current event store.</param>
/// <param name="ReadModelInstances">Instances for the currently focused read model (populated in ReadModelDetail view only).</param>
/// <param name="ReadModelInstancesTotalCount">Total instance count for the currently focused read model.</param>
/// <param name="ReadModelInstancesError">Error message from the last read model instances fetch, or <see langword="null"/>.</param>
/// <param name="Applications">All registered OAuth applications on the Chronicle server.</param>
/// <param name="Users">All registered users on the Chronicle server.</param>
/// <param name="Identities">All known identities in the current event store namespace.</param>
/// <param name="EventStoreSubscriptions">All event store subscriptions configured for the current event store.</param>
public record WorkbenchData(
    string ConnectionString,
    string EventStore,
    string Namespace,
    bool IsConnected,
    string? ServerVersion,
    IReadOnlyList<string> EventStoreNames,
    IReadOnlyList<ObserverInformation> Observers,
    IReadOnlyList<FailedPartition> FailedPartitions,
    IReadOnlyList<Job> Jobs,
    IReadOnlyList<Recommendation> Recommendations,
    ulong? TailSequenceNumber,
    DateTimeOffset CapturedAt,
    string? FetchError,
    IReadOnlyList<EventTypeRegistration> EventTypeRegistrations,
    IReadOnlyList<ProjectionDefinition> ProjectionDefinitions,
    IReadOnlyDictionary<string, string> ProjectionDeclarations,
    IReadOnlyList<AppendedEvent> RecentEvents,
    IReadOnlyList<WorkbenchReadModel> ReadModelDefinitions,
    IReadOnlyList<string> NamespaceNames,
    IReadOnlyList<string> ReadModelInstances,
    int ReadModelInstancesTotalCount,
    string? ReadModelInstancesError,
    IReadOnlyList<Application> Applications,
    IReadOnlyList<User> Users,
    IReadOnlyList<Identity> Identities,
    IReadOnlyList<EventStoreSubscriptionDefinition> EventStoreSubscriptions)
{
    /// <summary>
    /// Gets the number of observers in the <see cref="ObserverRunningState.Active"/> state.
    /// </summary>
    public int ActiveObservers => Observers.Count(o => o.RunningState == ObserverRunningState.Active);

    /// <summary>
    /// Gets the number of observers in the <see cref="ObserverRunningState.Replaying"/> state.
    /// </summary>
    public int ReplayingObservers => Observers.Count(o => o.RunningState == ObserverRunningState.Replaying);

    /// <summary>
    /// Gets the number of observers in the <see cref="ObserverRunningState.Suspended"/> state.
    /// </summary>
    public int SuspendedObservers => Observers.Count(o => o.RunningState == ObserverRunningState.Suspended);

    /// <summary>
    /// Gets the number of observers in the <see cref="ObserverRunningState.Disconnected"/> state.
    /// </summary>
    public int DisconnectedObservers => Observers.Count(o => o.RunningState == ObserverRunningState.Disconnected);

    /// <summary>
    /// Creates a loading placeholder snapshot before the first real fetch completes.
    /// </summary>
    /// <param name="settings">The workbench settings used to resolve connection and store details.</param>
    /// <returns>A <see cref="WorkbenchData"/> with empty collections and <see cref="IsConnected"/> set to <see langword="false"/>.</returns>
    public static WorkbenchData Loading(WorkbenchSettings settings) => new(
        ConnectionString: settings.ResolveConnectionString(),
        EventStore: settings.ResolveEventStore(),
        Namespace: settings.ResolveNamespace(),
        IsConnected: false,
        ServerVersion: null,
        EventStoreNames: [],
        Observers: [],
        FailedPartitions: [],
        Jobs: [],
        Recommendations: [],
        TailSequenceNumber: null,
        CapturedAt: DateTimeOffset.Now,
        FetchError: null,
        EventTypeRegistrations: [],
        ProjectionDefinitions: [],
        ProjectionDeclarations: new Dictionary<string, string>(),
        RecentEvents: [],
        ReadModelDefinitions: [],
        NamespaceNames: [],
        ReadModelInstances: [],
        ReadModelInstancesTotalCount: 0,
        ReadModelInstancesError: null,
        Applications: [],
        Users: [],
        Identities: [],
        EventStoreSubscriptions: []);
}
