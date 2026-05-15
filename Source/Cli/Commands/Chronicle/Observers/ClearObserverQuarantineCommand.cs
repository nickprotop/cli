// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.Observers;

/// <summary>
/// Clears quarantine for an observer.
/// </summary>
[LlmDescription("Clears quarantine for a quarantined observer so it can resume processing. Prompts for confirmation unless --yes is specified.")]
[CliCommand("clear-quarantine", "Clear quarantine for an observer", Branch = typeof(ChronicleBranch.Observers), DynamicCompletion = "observers")]
[CliExample("chronicle", "observers", "clear-quarantine", "550e8400-e29b-41d4-a716-446655440000")]
[LlmOption("<OBSERVER_ID>", "string", "Observer identifier (from 'cratis observers list') (positional)")]
public class ClearObserverQuarantineCommand : ChronicleCommand<ObserverCommandSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteCommandAsync(IServices services, ObserverCommandSettings settings, string format)
    {
        if (!ConfirmationHelper.ShouldProceed(settings, $"Are you sure you want to clear quarantine for observer '{settings.ObserverId}'?"))
        {
            OutputFormatter.WriteMessage(format, "Aborted.");
            return ExitCodes.Success;
        }

        var cleared = await ClearObserverQuarantineInvoker.TryClear(
            services.Observers,
            settings.ResolveEventStore(),
            settings.ResolveNamespace(),
            settings.ObserverId,
            settings.EventSequenceId);

        if (!cleared)
        {
            OutputFormatter.WriteError(
                format,
                "Connected Chronicle contracts do not support clearing observer quarantine.",
                "Upgrade Cratis.Chronicle.Contracts and Cratis.Chronicle.Connections to a version that includes observer quarantine clear support.",
                ExitCodes.ValidationErrorCode);
            return ExitCodes.ValidationError;
        }

        OutputFormatter.WriteMessage(format, $"Quarantine cleared for observer '{settings.ObserverId}'.");
        return ExitCodes.Success;
    }
}

static class ClearObserverQuarantineInvoker
{
    public static async Task<bool> TryClear(object observers, string eventStore, string @namespace, string observerId, string eventSequenceId)
    {
        var method = observers.GetType()
            .GetMethods()
            .FirstOrDefault(_ => _.Name == "ClearObserverQuarantine" && _.GetParameters().Length > 0);

        if (method is null)
        {
            return false;
        }

        var parameters = method.GetParameters();
        var command = Activator.CreateInstance(parameters[0].ParameterType);
        if (command is null)
        {
            return false;
        }

        if (!SetPropertyIfPresent(command, "EventStore", eventStore) ||
            !SetPropertyIfPresent(command, "Namespace", @namespace) ||
            !SetPropertyIfPresent(command, "ObserverId", observerId))
        {
            return false;
        }

        SetPropertyIfPresent(command, "EventSequenceId", eventSequenceId);

        var arguments = new object?[parameters.Length];
        arguments[0] = command;
        for (var i = 1; i < parameters.Length; i++)
        {
            if (parameters[i].HasDefaultValue)
            {
                arguments[i] = parameters[i].DefaultValue;
            }
            else if (parameters[i].ParameterType.IsValueType)
            {
                arguments[i] = Activator.CreateInstance(parameters[i].ParameterType);
            }
        }

        if (method.Invoke(observers, arguments) is not Task task)
        {
            return false;
        }

        await task;
        return true;
    }

    static bool SetPropertyIfPresent(object instance, string propertyName, string value)
    {
        var property = instance.GetType().GetProperty(propertyName);
        if (property?.CanWrite != true)
        {
            return false;
        }

        property.SetValue(instance, value);
        return true;
    }
}
