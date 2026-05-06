// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.Diagnose;

/// <summary>
/// Holds the results of a single diagnostic sweep of the Chronicle server.
/// </summary>
/// <param name="ConnectionString">The resolved connection string (credentials redacted in display).</param>
/// <param name="EventStore">The resolved event store name.</param>
/// <param name="Namespace">The resolved namespace name.</param>
/// <param name="ServerReachable">Whether the server responded to the connection attempt.</param>
/// <param name="ServerVersion">The server version string, or null when unreachable.</param>
/// <param name="LatestServerVersion">The latest available server version from the package feed, or null if up to date or the check was unavailable.</param>
/// <param name="EventStores">The list of event stores on the server.</param>
/// <param name="ActiveObservers">Number of observers in the Active state.</param>
/// <param name="ReplayingObservers">Number of observers in the Replaying state.</param>
/// <param name="SuspendedObservers">Number of observers in the Suspended state.</param>
/// <param name="DisconnectedObservers">Number of observers in the Disconnected state.</param>
/// <param name="FailedPartitions">Number of failed partitions requiring attention.</param>
/// <param name="PendingRecommendations">Number of pending system recommendations.</param>
/// <param name="EventSequenceTail">The tail (highest) sequence number of the event log, or null if unavailable.</param>
/// <param name="CapturedAt">The point in time this snapshot was captured.</param>
public record DiagnoseData(
    string ConnectionString,
    string EventStore,
    string Namespace,
    bool ServerReachable,
    string? ServerVersion,
    string? LatestServerVersion,
    IReadOnlyList<string> EventStores,
    int ActiveObservers,
    int ReplayingObservers,
    int SuspendedObservers,
    int DisconnectedObservers,
    int FailedPartitions,
    int PendingRecommendations,
    ulong? EventSequenceTail,
    DateTimeOffset CapturedAt)
{
    /// <summary>
    /// Gets a value indicating whether a newer server version is available.
    /// </summary>
    public bool HasServerUpdateAvailable => LatestServerVersion is not null;

    /// <summary>
    /// Gets the total number of observers across all states.
    /// </summary>
    public int TotalObservers => ActiveObservers + ReplayingObservers + SuspendedObservers + DisconnectedObservers;

    /// <summary>
    /// Gets a value indicating whether the system is healthy (no failures, server reachable).
    /// </summary>
    public bool IsHealthy =>
        ServerReachable &&
        FailedPartitions == 0;
}
