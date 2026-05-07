// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using context = Cratis.Cli.Integration.Chronicle.for_EventStoreSubscriptions.when_adding_and_removing_event_store_subscription.context;

namespace Cratis.Cli.Integration.Chronicle.for_EventStoreSubscriptions;

[Collection(ChronicleCollection.Name)]
public class when_adding_and_removing_event_store_subscription(context context) : CliGiven<context>(context)
{
    public class context : given.a_connected_cli
    {
        public CliCommandResult AddResult = null!;
        public CliCommandResult RemoveResult = null!;
        public bool SubscriptionAppearedInList;
        public string SubscriptionId = null!;

        async Task Because()
        {
            SubscriptionId = $"integration-test-subscription-{Guid.NewGuid():N}";
            AddResult = await RunCliAsync(
                "chronicle",
                "event-store-subscriptions",
                "add",
                SubscriptionId,
                "system",
                "Integration.Test.EventType",
                "--event-store",
                "system");

            var listResult = await RunCliAsync("chronicle", "event-store-subscriptions", "list", "--event-store", "system");
            var subscriptions = JsonDocument.Parse(listResult.StandardOutput).RootElement;
            var testSubscription = subscriptions.EnumerateArray()
                .FirstOrDefault(subscription => subscription.GetProperty("identifier").GetString() == SubscriptionId);
            SubscriptionAppearedInList = testSubscription.ValueKind != JsonValueKind.Undefined;

            if (SubscriptionAppearedInList)
            {
                RemoveResult = await RunCliAsync("chronicle", "event-store-subscriptions", "remove", SubscriptionId, "--event-store", "system");
            }
        }
    }

    [Fact] void should_return_success_for_add() => Context.AddResult.ExitCode.ShouldEqual(ExitCodes.Success);

    [Fact] void should_contain_added_message() => Context.AddResult.StandardOutput.ShouldContain("added");

    [Fact] void should_show_subscription_in_list() => Context.SubscriptionAppearedInList.ShouldBeTrue();

    [Fact] void should_return_success_for_remove() => Context.RemoveResult.ExitCode.ShouldEqual(ExitCodes.Success);

    [Fact] void should_contain_removed_message() => Context.RemoveResult.StandardOutput.ShouldContain("removed");
}
