// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json;
using Cratis.Chronicle.Contracts.Identities;
using Cratis.Chronicle.Contracts.Jobs;
using Cratis.Chronicle.Contracts.Observation.EventStoreSubscriptions;
using Cratis.Cli.Commands.Chronicle.ReadModels;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Fetches all Chronicle server data needed by the workbench in a single async snapshot.
/// All independent gRPC calls are executed in parallel to minimise fetch latency.
/// </summary>
/// <param name="services">The Chronicle gRPC service clients.</param>
/// <param name="settings">The resolved workbench settings (connection string, event store, namespace).</param>
public class WorkbenchDataService(IServices services, WorkbenchSettings settings)
{
    /// <summary>Total events fetched from the server per refresh — determines how many pages are available in the Event Log view.</summary>
    public const int EventLogFetchWindow = 500;

    static readonly JsonSerializerOptions _instanceJsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Fetches a point-in-time snapshot of all data from the Chronicle server.
    /// All independent gRPC calls run in parallel; only the recent-events fetch waits on the tail-sequence result.
    /// </summary>
    /// <param name="activeEventStore">Override for the active event store (null uses settings default).</param>
    /// <param name="activeNamespace">Override for the active namespace (null uses settings default).</param>
    /// <param name="readModelContainerName">Container name when fetching read model instances (non-null triggers an instances fetch).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="WorkbenchData"/> snapshot.</returns>
    public async Task<WorkbenchData> FetchAsync(
        string? activeEventStore,
        string? activeNamespace,
        string? readModelContainerName,
        CancellationToken ct)
    {
        var eventStore = activeEventStore ?? settings.ResolveEventStore();
        var ns = activeNamespace ?? settings.ResolveNamespace();
        var connectionString = settings.ResolveConnectionString();

        // Run all independent calls in parallel.
        var versionTask = FetchVersionAsync();
        var eventStoresTask = FetchEventStoresAsync();
        var observersTask = FetchObserversAsync(eventStore, ns);
        var failedPartitionsTask = FetchFailedPartitionsAsync(eventStore, ns);
        var jobsTask = FetchJobsAsync(eventStore, ns);
        var recommendationsTask = FetchRecommendationsAsync(eventStore, ns);
        var tailTask = FetchTailSequenceNumberAsync(eventStore, ns);
        var eventTypesTask = FetchEventTypesAsync(eventStore);
        var projectionsTask = FetchProjectionDefinitionsAsync(eventStore);
        var declarationsTask = FetchProjectionDeclarationsAsync(eventStore);
        var readModelsTask = FetchReadModelDefinitionsAsync(eventStore);
        var namespacesTask = FetchNamespacesAsync(eventStore);
        var applicationsTask = FetchApplicationsAsync();
        var usersTask = FetchUsersAsync();
        var identitiesTask = FetchIdentitiesAsync(eventStore, ns);
        var subscriptionsTask = FetchSubscriptionsAsync(eventStore);

        await Task.WhenAll(versionTask, eventStoresTask, observersTask, failedPartitionsTask, jobsTask, recommendationsTask, tailTask, eventTypesTask, projectionsTask, declarationsTask, readModelsTask, namespacesTask, applicationsTask, usersTask, identitiesTask, subscriptionsTask).ConfigureAwait(false);

        // All tasks are complete — awaiting them returns immediately.
        var (serverVersion, isConnected) = await versionTask.ConfigureAwait(false);
        var tailSequenceNumber = await tailTask.ConfigureAwait(false);
        var eventStoreNames = await eventStoresTask.ConfigureAwait(false);
        var observers = await observersTask.ConfigureAwait(false);
        var failedPartitions = await failedPartitionsTask.ConfigureAwait(false);
        var jobs = await jobsTask.ConfigureAwait(false);
        var recommendations = await recommendationsTask.ConfigureAwait(false);
        var eventTypeRegistrations = await eventTypesTask.ConfigureAwait(false);
        var projectionDefinitions = await projectionsTask.ConfigureAwait(false);
        var projectionDeclarations = await declarationsTask.ConfigureAwait(false);
        var readModelDefinitions = await readModelsTask.ConfigureAwait(false);
        var namespaceNames = await namespacesTask.ConfigureAwait(false);
        var applications = await applicationsTask.ConfigureAwait(false);
        var users = await usersTask.ConfigureAwait(false);
        var identities = await identitiesTask.ConfigureAwait(false);
        var subscriptions = await subscriptionsTask.ConfigureAwait(false);

        // Recent events depends on the tail — fetch after the parallel batch.
        var recentEvents = await FetchRecentEventsAsync(eventStore, ns, tailSequenceNumber).ConfigureAwait(false);

        // Read model instances are optional and fetched only when a container is specified.
        var (readModelInstances, readModelInstancesTotalCount, readModelInstancesError) =
            await FetchReadModelInstancesAsync(eventStore, ns, readModelContainerName).ConfigureAwait(false);

        return new WorkbenchData(
            ConnectionString: connectionString,
            EventStore: eventStore,
            Namespace: ns,
            IsConnected: isConnected,
            ServerVersion: serverVersion,
            EventStoreNames: eventStoreNames,
            Observers: observers,
            FailedPartitions: failedPartitions,
            Jobs: jobs,
            Recommendations: recommendations,
            TailSequenceNumber: tailSequenceNumber,
            CapturedAt: DateTimeOffset.Now,
            FetchError: null,
            EventTypeRegistrations: eventTypeRegistrations,
            ProjectionDefinitions: projectionDefinitions,
            ProjectionDeclarations: projectionDeclarations,
            RecentEvents: recentEvents,
            ReadModelDefinitions: readModelDefinitions,
            NamespaceNames: namespaceNames,
            ReadModelInstances: readModelInstances,
            ReadModelInstancesTotalCount: readModelInstancesTotalCount,
            ReadModelInstancesError: readModelInstancesError,
            Applications: applications,
            Users: users,
            Identities: identities,
            EventStoreSubscriptions: subscriptions);
    }

    /// <summary>
    /// Fetches only the new events appended since <paramref name="afterSequenceNumber"/> for the live event stream.
    /// </summary>
    /// <param name="afterSequenceNumber">The last known sequence number; only events with higher numbers are returned.</param>
    /// <param name="activeEventStore">Override for the active event store (null uses settings default).</param>
    /// <param name="activeNamespace">Override for the active namespace (null uses settings default).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>New events ordered oldest-first, or an empty list if none.</returns>
    public async Task<IReadOnlyList<AppendedEvent>> FetchNewEventsAsync(
        ulong afterSequenceNumber,
        string? activeEventStore,
        string? activeNamespace,
        CancellationToken ct)
    {
        var eventStore = activeEventStore ?? settings.ResolveEventStore();
        var ns = activeNamespace ?? settings.ResolveNamespace();

        try
        {
            var eventsResp = await services.EventSequences.GetEventsFromEventSequenceNumber(
                new GetFromEventSequenceNumberRequest
                {
                    EventStore = eventStore,
                    Namespace = ns,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId,
                    FromEventSequenceNumber = afterSequenceNumber + 1
                }).ConfigureAwait(false);
            return [.. eventsResp.Events.OrderBy(e => e.Context.SequenceNumber)];
        }
        catch
        {
            return [];
        }
    }

    async Task<(string? Version, bool IsConnected)> FetchVersionAsync()
    {
        try
        {
            var info = await services.Server.GetVersionInfo().ConfigureAwait(false);
            return (info.Version, true);
        }
        catch
        {
            return (null, false);
        }
    }

    async Task<IReadOnlyList<string>> FetchEventStoresAsync()
    {
        try { return [.. await services.EventStores.GetEventStores().ConfigureAwait(false)]; }
        catch { return []; }
    }

    async Task<IReadOnlyList<ObserverInformation>> FetchObserversAsync(string eventStore, string ns)
    {
        try
        {
            return [.. await services.Observers.GetObservers(new AllObserversRequest
            {
                EventStore = eventStore,
                Namespace = ns
            }).ConfigureAwait(false)];
        }
        catch { return []; }
    }

    async Task<IReadOnlyList<FailedPartition>> FetchFailedPartitionsAsync(string eventStore, string ns)
    {
        try
        {
            return [.. await services.FailedPartitions.GetFailedPartitions(new GetFailedPartitionsRequest
            {
                EventStore = eventStore,
                Namespace = ns
            }).ConfigureAwait(false)];
        }
        catch { return []; }
    }

    async Task<IReadOnlyList<Job>> FetchJobsAsync(string eventStore, string ns)
    {
        try
        {
            return [.. (await services.Jobs.GetJobs(new GetJobsRequest
            {
                EventStore = eventStore,
                Namespace = ns
            }).ConfigureAwait(false)) ?? []];
        }
        catch { return []; }
    }

    async Task<IReadOnlyList<Recommendation>> FetchRecommendationsAsync(string eventStore, string ns)
    {
        try
        {
            return [.. await services.Recommendations.GetRecommendations(new GetRecommendationsRequest
            {
                EventStore = eventStore,
                Namespace = ns
            }).ConfigureAwait(false)];
        }
        catch { return []; }
    }

    async Task<ulong?> FetchTailSequenceNumberAsync(string eventStore, string ns)
    {
        try
        {
            var tail = await services.EventSequences.GetTailSequenceNumber(new GetTailSequenceNumberRequest
            {
                EventStore = eventStore,
                Namespace = ns,
                EventSequenceId = CliDefaults.DefaultEventSequenceId
            }).ConfigureAwait(false);
            return tail.SequenceNumber == ulong.MaxValue ? null : tail.SequenceNumber;
        }
        catch { return null; }
    }

    async Task<IReadOnlyList<EventTypeRegistration>> FetchEventTypesAsync(string eventStore)
    {
        try
        {
            return [.. await services.EventTypes.GetAllRegistrations(
                new GetAllEventTypesRequest { EventStore = eventStore }).ConfigureAwait(false)];
        }
        catch { return []; }
    }

    async Task<IReadOnlyList<ProjectionDefinition>> FetchProjectionDefinitionsAsync(string eventStore)
    {
        try
        {
            return [.. await services.Projections.GetAllDefinitions(
                new GetAllDefinitionsRequest { EventStore = eventStore }).ConfigureAwait(false)];
        }
        catch { return []; }
    }

    async Task<IReadOnlyDictionary<string, string>> FetchProjectionDeclarationsAsync(string eventStore)
    {
        try
        {
            var declarations = await services.Projections.GetAllDeclarations(
                new GetAllDeclarationsRequest { EventStore = eventStore }).ConfigureAwait(false);
            return declarations.ToDictionary(d => d.Identifier, d => d.Declaration ?? string.Empty);
        }
        catch { return new Dictionary<string, string>(); }
    }

    async Task<IReadOnlyList<AppendedEvent>> FetchRecentEventsAsync(string eventStore, string ns, ulong? tailSequenceNumber)
    {
        try
        {
            if (tailSequenceNumber is null or 0) return [];
            var fromSeq = tailSequenceNumber.Value >= EventLogFetchWindow
                ? tailSequenceNumber.Value - EventLogFetchWindow + 1
                : 0;
            var eventsResp = await services.EventSequences.GetEventsFromEventSequenceNumber(
                new GetFromEventSequenceNumberRequest
                {
                    EventStore = eventStore,
                    Namespace = ns,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId,
                    FromEventSequenceNumber = fromSeq
                }).ConfigureAwait(false);
            return [.. eventsResp.Events.OrderByDescending(e => e.Context.SequenceNumber)];
        }
        catch { return []; }
    }

    async Task<IReadOnlyList<WorkbenchReadModel>> FetchReadModelDefinitionsAsync(string eventStore)
    {
        try
        {
            var defs = await services.ReadModels.GetDefinitions(
                new GetDefinitionsRequest { EventStore = eventStore }).ConfigureAwait(false);
            return [.. defs.ReadModels.Select(rm => new WorkbenchReadModel(
                rm.ContainerName,
                rm.DisplayName,
                rm.Owner.ToString(),
                !string.Equals(rm.Owner.ToString(), "Client", StringComparison.Ordinal),
                rm.Source.ToString(),
                rm.Type?.Identifier ?? string.Empty))];
        }
        catch { return []; }
    }

    async Task<IReadOnlyList<string>> FetchNamespacesAsync(string eventStore)
    {
        try
        {
            return [.. await services.Namespaces.GetNamespaces(
                new GetNamespacesRequest { EventStore = eventStore }).ConfigureAwait(false)];
        }
        catch { return []; }
    }

    async Task<IReadOnlyList<Application>> FetchApplicationsAsync()
    {
        try { return [.. await services.Applications.GetAll().ConfigureAwait(false) ?? []]; }
        catch { return []; }
    }

    async Task<IReadOnlyList<User>> FetchUsersAsync()
    {
        try { return [.. await services.Users.GetAll().ConfigureAwait(false) ?? []]; }
        catch { return []; }
    }

    async Task<IReadOnlyList<Identity>> FetchIdentitiesAsync(string eventStore, string ns)
    {
        try
        {
            return [.. await services.Identities.GetIdentities(new GetIdentitiesRequest
            {
                EventStore = eventStore,
                Namespace = ns
            }).ConfigureAwait(false)];
        }
        catch { return []; }
    }

    async Task<IReadOnlyList<EventStoreSubscriptionDefinition>> FetchSubscriptionsAsync(string eventStore)
    {
        try
        {
            return [.. await services.EventStoreSubscriptions.GetSubscriptions(new GetEventStoreSubscriptionsRequest
            {
                TargetEventStore = eventStore
            }).ConfigureAwait(false)];
        }
        catch { return []; }
    }

    async Task<(IReadOnlyList<string> Instances, int TotalCount, string? Error)> FetchReadModelInstancesAsync(
        string eventStore,
        string ns,
        string? containerName)
    {
        if (string.IsNullOrEmpty(containerName)) return ([], 0, null);
        try
        {
            var instResp = await services.ReadModels.GetInstances(new GetInstancesRequest
            {
                EventStore = eventStore,
                Namespace = ns,
                ReadModel = containerName,
                Page = 0,
                PageSize = 20
            }).ConfigureAwait(false);
            var totalCount = (int)Math.Min(instResp.TotalCount, int.MaxValue);
            var instances = (IReadOnlyList<string>)[.. (instResp.Instances ?? [])
                .Select(ReadModelJsonCleaner.CleanInstance)
                .Where(o => o is not null)
                .Select(o => JsonSerializer.Serialize(o, _instanceJsonOptions))];
            return (instances, totalCount, null);
        }
        catch (Exception ex)
        {
            return ([], 0, ex.Message);
        }
    }
}
