// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.Observers;

/// <summary>
/// Lists observers (reactors, reducers, projections) with optional type filtering.
/// </summary>
[LlmDescription("Lists all observers (projections, reactors, reducers, client observers) in the namespace. Supports filtering by type with --type. Use to see observer health and replay status.")]
[CliCommand("list", "List observers", Branch = typeof(ChronicleBranch.Observers))]
[CliExample("chronicle", "observers", "list")]
[CliExample("chronicle", "observers", "list", "--type", "reactor")]
[LlmOutputAdvice("plain", "When empty, JSON is smaller (2B vs 44B), but with data plain is comparable. Use plain for consistency.")]
[LlmOption("-t, --type", "string", "Filter by type: reactor, reducer, projection, or all. Invalid values return an error.")]
public class ListObserversCommand : ChronicleCommand<ListObserversSettings>
{
    const int QuarantinedRunningStateValue = 5;

    /// <summary>
    /// Filters observers by type name, returning all observers when the type is "all".
    /// </summary>
    /// <param name="observers">The observers to filter.</param>
    /// <param name="type">The type name to filter by (e.g. "reactor", "projection", "all").</param>
    /// <returns>Filtered observers matching the specified type.</returns>
    internal static IEnumerable<ObserverInformation> FilterByType(IEnumerable<ObserverInformation> observers, string type)
    {
        if (string.Equals(type, "all", StringComparison.OrdinalIgnoreCase))
        {
            return observers;
        }

        // Validation already passed in IsValidType, so TryParse is guaranteed to succeed.
        Enum.TryParse<ObserverType>(type, ignoreCase: true, out var parsed);
        return observers.Where(o => o.Type == parsed);
    }

    /// <summary>
    /// Validates whether the given type string is a recognized observer type or "all".
    /// </summary>
    /// <param name="type">The type string to validate.</param>
    /// <param name="errorMessage">When invalid, contains the error description.</param>
    /// <returns><see langword="true"/> if the type is valid; otherwise <see langword="false"/>.</returns>
    internal static bool IsValidType(string type, out string errorMessage)
    {
        if (string.Equals(type, "all", StringComparison.OrdinalIgnoreCase) ||
            Enum.TryParse<ObserverType>(type, ignoreCase: true, out _))
        {
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = $"Invalid observer type '{type}'";
        return false;
    }

    /// <summary>
    /// Determines whether an observer is quarantined.
    /// </summary>
    /// <param name="observer">The observer to inspect.</param>
    /// <returns><see langword="true"/> if quarantined; otherwise <see langword="false"/>.</returns>
    internal static bool IsQuarantined(ObserverInformation observer) =>
        string.Equals(observer.RunningState.ToString(), "Quarantined", StringComparison.OrdinalIgnoreCase) ||
        (int)observer.RunningState == QuarantinedRunningStateValue;

    /// <inheritdoc/>
    protected override async Task<int> ExecuteCommandAsync(IServices services, ListObserversSettings settings, string format)
    {
        if (!IsValidType(settings.Type, out var errorMessage))
        {
            OutputFormatter.WriteError(format, errorMessage, "Valid types: reactor, reducer, projection, all", ExitCodes.ValidationErrorCode);
            return ExitCodes.ValidationError;
        }

        var observers = await services.Observers.GetObservers(new AllObserversRequest
        {
            EventStore = settings.ResolveEventStore(),
            Namespace = settings.ResolveNamespace()
        });

        var filtered = FilterByType(observers, settings.Type).ToList();

        if (string.Equals(format, OutputFormats.Json, StringComparison.Ordinal) || string.Equals(format, OutputFormats.JsonCompact, StringComparison.Ordinal))
        {
            var projected = filtered.Select(obs => new
            {
                id = obs.Id,
                type = obs.Type.ToString(),
                runningState = obs.RunningState.ToString(),
                isQuarantined = IsQuarantined(obs),
                nextEventSequenceNumber = obs.NextEventSequenceNumber == ulong.MaxValue ? null : (ulong?)obs.NextEventSequenceNumber,
                lastHandledEventSequenceNumber = obs.LastHandledEventSequenceNumber == ulong.MaxValue ? null : (ulong?)obs.LastHandledEventSequenceNumber,
                isSubscribed = obs.IsSubscribed
            });
            OutputFormatter.WriteObject(format, projected);
        }
        else
        {
            OutputFormatter.Write(
                format,
                filtered,
                ["Id", "Type", "State", "Quarantined", "Next#", "LastHandled#", "Subscribed"],
                obs =>
                [
                    obs.Id,
                    obs.Type.ToString(),
                    obs.RunningState.ToString(),
                    IsQuarantined(obs).ToString(),
                    obs.NextEventSequenceNumber == ulong.MaxValue ? "(never)" : obs.NextEventSequenceNumber.ToString(),
                    obs.LastHandledEventSequenceNumber == ulong.MaxValue ? "(never)" : obs.LastHandledEventSequenceNumber.ToString(),
                    obs.IsSubscribed.ToString()
                ]);
        }

        return ExitCodes.Success;
    }
}
