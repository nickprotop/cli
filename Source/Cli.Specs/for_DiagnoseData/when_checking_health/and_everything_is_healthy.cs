// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.for_DiagnoseData.when_checking_health;

[Collection(CliSpecsCollection.Name)]
public class and_everything_is_healthy : Specification
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
        ReplayingObservers: 3,
        SuspendedObservers: 2,
        DisconnectedObservers: 1,
        FailedPartitions: 0,
        PendingRecommendations: 12,
        EventSequenceTail: 12345,
        CapturedAt: DateTimeOffset.UtcNow);

    void Because() => _result = _data.IsHealthy;

    [Fact] void should_be_healthy() => _result.ShouldBeTrue();
}
