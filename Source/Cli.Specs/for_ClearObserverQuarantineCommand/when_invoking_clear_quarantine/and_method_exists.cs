// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.for_ClearObserverQuarantineCommand.when_invoking_clear_quarantine;

public class and_method_exists : Specification
{
    FakeObservers _observers;
    bool _result;

    void Establish() => _observers = new();

    async Task Because() => _result = await ClearObserverQuarantineInvoker.TryClear(_observers, "event-store", "namespace", "observer-id", "event-log");

    [Fact] void should_invoke_clear_quarantine() => _observers.WasInvoked.ShouldBeTrue();
    [Fact] void should_return_true() => _result.ShouldBeTrue();
    [Fact] void should_set_event_store() => _observers.Command.EventStore.ShouldEqual("event-store");
    [Fact] void should_set_namespace() => _observers.Command.Namespace.ShouldEqual("namespace");
    [Fact] void should_set_observer_id() => _observers.Command.ObserverId.ShouldEqual("observer-id");
    [Fact] void should_set_event_sequence_id() => _observers.Command.EventSequenceId.ShouldEqual("event-log");

    class FakeObservers
    {
        public bool WasInvoked { get; private set; }

        public FakeClearObserverQuarantine Command { get; private set; } = null!;

        public Task ClearObserverQuarantine(FakeClearObserverQuarantine command, int ignored = 0)
        {
            WasInvoked = true;
            Command = command;
            return Task.CompletedTask;
        }
    }

    class FakeClearObserverQuarantine
    {
        public string EventStore { get; set; } = string.Empty;

        public string Namespace { get; set; } = string.Empty;

        public string ObserverId { get; set; } = string.Empty;

        public string EventSequenceId { get; set; } = string.Empty;
    }
}
