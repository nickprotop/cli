// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.for_ClearObserverQuarantineCommand.when_invoking_clear_quarantine;

public class and_method_does_not_exist : Specification
{
    bool _result;

    async Task Because() => _result = await ClearObserverQuarantineInvoker.TryClear(new FakeObservers(), "event-store", "namespace", "observer-id", "event-log");

    [Fact] void should_return_false() => _result.ShouldBeFalse();

    class FakeObservers;
}
