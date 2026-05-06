// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Nodes;
using Grpc.Core;

namespace Cratis.Cli.Commands.Chronicle.ReadModels;

/// <summary>
/// Gets a single read model instance by key.
/// </summary>
[LlmDescription("Gets a single read model instance by its key (entity ID). Returns the full projected state as JSON. Use -o json-compact.")]
[CliCommand("get", "Get a single read model instance by key", Branch = typeof(ChronicleBranch.ReadModels), DynamicCompletion = "read-models")]
[CliExample("chronicle", "read-models", "get", "MyReadModel", "abc-123")]
[CliExample("chronicle", "read-models", "get", "MyReadModel", "abc-123", "-o", "json")]
[LlmOutputAdvice("json", "JSON contains the full read model document. Use JSON for structured parsing.")]
[LlmOption("<READ_MODEL>", "string", "Read model container name (from 'cratis read-models list') (positional)")]
[LlmOption("<KEY>", "string", "Read model instance key (typically an event source ID) (positional)")]
public class GetReadModelByKeyCommand : ChronicleCommand<ReadModelKeySettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteCommandAsync(IServices services, ReadModelKeySettings settings, string format)
    {
        try
        {
            return await GetInstanceAsync(services, settings, format);
        }
        catch (RpcException ex) when (ex.Status.Detail.Contains("NotSupportedException", StringComparison.Ordinal))
        {
            OutputFormatter.WriteError(
                format,
                $"Read model '{settings.ReadModel}' is client-owned. Its state is stored by the client application and cannot be retrieved through the Chronicle server.",
                "Client-owned read models (Owner: Client) do not store state server-side. Use 'cratis chronicle read-models list' to see the Owner column.",
                ExitCodes.ServerErrorCode);
            return ExitCodes.ServerError;
        }
    }

    async Task<int> GetInstanceAsync(IServices services, ReadModelKeySettings settings, string format)
    {
        var response = await services.ReadModels.GetInstanceByKey(new GetInstanceByKeyRequest
        {
            EventStore = settings.ResolveEventStore(),
            Namespace = settings.ResolveNamespace(),
            ReadModelIdentifier = settings.ReadModel,
            EventSequenceId = settings.EventSequenceId,
            ReadModelKey = settings.Key
        });

        if (string.IsNullOrEmpty(response.ReadModel))
        {
            OutputFormatter.WriteError(format, $"No instance found for key '{settings.Key}' in read model '{settings.ReadModel}'", errorCode: ExitCodes.NotFoundCode);
            return ExitCodes.NotFound;
        }

        var cleanedReadModel = ReadModelJsonCleaner.CleanInstance(response.ReadModel);

        OutputFormatter.WriteObject(
            format,
            new
            {
                ReadModel = cleanedReadModel,
                response.ProjectedEventsCount,
                response.LastHandledEventSequenceNumber
            },
            data =>
            {
                AnsiConsole.MarkupLine($"[bold]ProjectedEvents:[/] {data.ProjectedEventsCount}");
                AnsiConsole.MarkupLine($"[bold]LastHandled#:[/]    {data.LastHandledEventSequenceNumber}");
                AnsiConsole.WriteLine();
                AnsiConsole.WriteLine(data.ReadModel is not null
                    ? JsonSerializer.Serialize(data.ReadModel, OutputFormatter.IndentedJsonSerializerOptions)
                    : response.ReadModel);
            });

        return ExitCodes.Success;
    }
}
