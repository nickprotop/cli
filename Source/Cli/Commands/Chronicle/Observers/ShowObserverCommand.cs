// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.Observers;

/// <summary>
/// Shows detailed information about a specific observer.
/// </summary>
[LlmDescription("Shows detailed information about a specific observer including last handled sequence number, partition health, and type. Use -o json-compact.")]
[CliCommand("show", "Show detailed information about a specific observer", Branch = typeof(ChronicleBranch.Observers), DynamicCompletion = "observers")]
[CliExample("chronicle", "observers", "show", "550e8400-e29b-41d4-a716-446655440000")]
[LlmOutputAdvice("json", "JSON contains all observer fields. Use JSON for structured parsing, plain for quick overview.")]
[LlmOption("<OBSERVER_ID>", "string", "Observer identifier (from 'cratis observers list') (positional). Format varies: projections use dotted type names (e.g. Core.MyFeature.Listing.MyProjection), system observers use prefixed names.")]
public class ShowObserverCommand : ChronicleCommand<ObserverCommandSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteCommandAsync(IServices services, ObserverCommandSettings settings, string format)
    {
        var observers = await services.Observers.GetObservers(new AllObserversRequest
        {
            EventStore = settings.ResolveEventStore(),
            Namespace = settings.ResolveNamespace()
        });

        var (matched, exitCode) = IdentifierMatcher.Match(
            observers,
            settings.ObserverId,
            o => o.Id,
            format,
            "observer");

        if (matched is null)
        {
            return exitCode;
        }

        var info = await services.Observers.GetObserverInformation(new GetObserverInformationRequest
        {
            EventStore = settings.ResolveEventStore(),
            Namespace = settings.ResolveNamespace(),
            ObserverId = matched.Id,
            EventSequenceId = settings.EventSequenceId
        });

        var eventTypes = (info.EventTypes ?? []).Select(et => $"{et.Id}+{et.Generation}").ToList();
        var lastHandled = info.LastHandledEventSequenceNumber == ulong.MaxValue ? null : (ulong?)info.LastHandledEventSequenceNumber;

        OutputFormatter.WriteObject(
            format,
            new
            {
                id = info.Id,
                eventSequenceId = info.EventSequenceId,
                type = info.Type.ToString(),
                owner = info.Owner.ToString(),
                runningState = info.RunningState.ToString(),
                nextEventSequenceNumber = info.NextEventSequenceNumber,
                lastHandledEventSequenceNumber = lastHandled,
                isSubscribed = info.IsSubscribed,
                eventTypes
            },
            data =>
            {
                AnsiConsole.MarkupLine($"[bold]Observer:[/]     {data.id.EscapeMarkup()}");
                AnsiConsole.MarkupLine($"[bold]Sequence:[/]     {data.eventSequenceId.EscapeMarkup()}");
                AnsiConsole.MarkupLine($"[bold]Type:[/]         {data.type}");
                AnsiConsole.MarkupLine($"[bold]Owner:[/]        {data.owner}");
                AnsiConsole.MarkupLine($"[bold]State:[/]        {data.runningState}");
                AnsiConsole.MarkupLine($"[bold]Next#:[/]        {data.nextEventSequenceNumber}");
                AnsiConsole.MarkupLine($"[bold]LastHandled#:[/] {(lastHandled.HasValue ? lastHandled.Value.ToString() : "(never)")}");
                AnsiConsole.MarkupLine($"[bold]Subscribed:[/]   {data.isSubscribed}");
                AnsiConsole.MarkupLine($"[bold]EventTypes:[/]   {string.Join(", ", data.eventTypes)}");

                if (info.RunningState == ObserverRunningState.Disconnected)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"  [{OutputFormatter.Warning.ToMarkup()}]▲ Observer is disconnected — no client is currently subscribed.[/]");
                    AnsiConsole.MarkupLine($"  [{OutputFormatter.Muted.ToMarkup()}]The client application is likely offline or has not reconnected.[/]");
                    AnsiConsole.MarkupLine($"  [{OutputFormatter.Muted.ToMarkup()}]Disconnect reason and timestamp are not surfaced by the server API.[/]");
                }
            });

        return ExitCodes.Success;
    }
}
