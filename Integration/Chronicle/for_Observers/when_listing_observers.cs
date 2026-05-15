// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using context = Cratis.Cli.Integration.Chronicle.for_Observers.when_listing_observers.context;

namespace Cratis.Cli.Integration.Chronicle.for_Observers;

[Collection(ChronicleCollection.Name)]
public class when_listing_observers(context context) : CliGiven<context>(context)
{
    public class context : given.a_connected_cli
    {
        public CliCommandResult Result = null!;

        async Task Because() => Result = await RunCliAsync("chronicle", "observers", "list", "--event-store", "system");
    }

    [Fact] void should_return_success_exit_code() => Context.Result.ExitCode.ShouldEqual(ExitCodes.Success);

    [Fact] void should_have_output() => (Context.Result.StandardOutput.Length > 0).ShouldBeTrue();

    [Fact]
    void should_include_is_quarantined_field()
    {
        var first = JsonDocument.Parse(Context.Result.StandardOutput).RootElement.EnumerateArray().First();
        first.TryGetProperty("isQuarantined", out _).ShouldBeTrue();
    }

    [Fact] void should_have_no_errors() => Context.Result.StandardError.ShouldEqual(string.Empty);
}
