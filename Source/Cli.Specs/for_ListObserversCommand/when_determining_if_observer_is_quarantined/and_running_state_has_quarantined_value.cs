// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Observation;

namespace Cratis.Cli.for_ListObserversCommand.when_determining_if_observer_is_quarantined;

public class and_running_state_has_quarantined_value : Specification
{
    bool _isQuarantined;

    void Because() => _isQuarantined = ListObserversCommand.IsQuarantined(new ObserverInformation
    {
        RunningState = ObserverRunningState.Quarantined
    });

    [Fact] void should_return_true() => _isQuarantined.ShouldBeTrue();
}
