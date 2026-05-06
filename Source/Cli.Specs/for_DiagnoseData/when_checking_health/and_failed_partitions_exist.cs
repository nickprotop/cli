// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.for_DiagnoseData.when_checking_health;

[Collection(CliSpecsCollection.Name)]
public class and_failed_partitions_exist : Specification
{
    DiagnoseData _data;
    bool _result;

    void Establish() => _data = new DiagnoseData(
        ConnectionString: "chronicle://localhost:35000",
        EventStore: "default",
        Namespace: "Default",
        ServerReachable: true,
        ServerVersion: "15.6.1",
        LatestServerVersion: null,
        EventStores: ["default"],
        ActiveObservers: 5,
        ReplayingObservers: 0,
        SuspendedObservers: 0,
        DisconnectedObservers: 0,
        FailedPartitions: 3,
        PendingRecommendations: 0,
        EventSequenceTail: 12345,
        CapturedAt: DateTimeOffset.UtcNow);

    void Because() => _result = _data.IsHealthy;

    [Fact] void should_not_be_healthy() => _result.ShouldBeFalse();
}
