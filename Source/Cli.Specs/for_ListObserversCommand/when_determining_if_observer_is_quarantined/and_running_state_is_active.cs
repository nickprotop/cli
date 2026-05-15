// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Observation;

namespace Cratis.Cli.for_ListObserversCommand.when_determining_if_observer_is_quarantined;

public class and_running_state_is_active : Specification
{
    bool _isQuarantined;

    void Because() => _isQuarantined = ListObserversCommand.IsQuarantined(new ObserverInformation
    {
        RunningState = ObserverRunningState.Active
    });

    [Fact] void should_return_false() => _isQuarantined.ShouldBeFalse();
}
