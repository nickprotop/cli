// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Nodes;
using Grpc.Core;

namespace Cratis.Cli.Commands.Chronicle.ReadModels;

/// <summary>
/// Lists read model instances with pagination.
/// </summary>
[LlmDescription("Lists all current instances of a read model type as key-value pairs. Use -o plain for large datasets. Use to inspect the current state of all projected entities.")]
[CliCommand("instances", "List read model instances", Branch = typeof(ChronicleBranch.ReadModels), DynamicCompletion = "read-models")]
[CliExample("chronicle", "read-models", "instances", "MyReadModel")]
[CliExample("chronicle", "read-models", "instances", "MyReadModel", "--page", "2")]
[LlmOutputAdvice("plain", "Both formats are comparable; use plain for consistency.")]
[LlmOption("<READ_MODEL>", "string", "Read model container name (positional)")]
[LlmOption("--page", "int", "Page number, 0-based (default: 0)")]
[LlmOption("--page-size", "int", "Items per page (default: 20)")]
public class GetReadModelInstancesCommand : ChronicleCommand<GetReadModelInstancesSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteCommandAsync(IServices services, GetReadModelInstancesSettings settings, string format)
    {
        GetInstancesResponse response;
        try
        {
            response = await services.ReadModels.GetInstances(new GetInstancesRequest
            {
                EventStore = settings.ResolveEventStore(),
                Namespace = settings.ResolveNamespace(),
                ReadModel = settings.ReadModel,
                Page = settings.Page,
                PageSize = settings.PageSize
            });
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            OutputFormatter.WriteError(
                format,
                $"Read model '{settings.ReadModel}' not found",
                "Use 'cratis chronicle read-models list' to see available read models",
                ExitCodes.NotFoundCode);
            return ExitCodes.NotFound;
        }
        catch (RpcException ex) when (ex.Status.Detail.Contains("NullReferenceException", StringComparison.Ordinal))
        {
            OutputFormatter.WriteError(
                format,
                $"Read model '{settings.ReadModel}' is client-owned. Its state is stored by the client application and cannot be retrieved through the Chronicle server.",
                "Client-owned read models (Owner: Client) do not store state server-side. Use 'cratis chronicle read-models list' to see the Owner column.",
                ExitCodes.ServerErrorCode);
            return ExitCodes.ServerError;
        }

        var cleanedInstances = (response.Instances ?? [])
            .Select(ReadModelJsonCleaner.CleanInstance)
            .Where(obj => obj is not null)
            .ToList();

        if (string.Equals(format, OutputFormats.Json, StringComparison.Ordinal) || string.Equals(format, OutputFormats.JsonCompact, StringComparison.Ordinal))
        {
            OutputFormatter.WriteObject(
                format,
                new
                {
                    response.TotalCount,
                    response.Page,
                    response.PageSize,
                    Instances = cleanedInstances
                });
        }
        else if (cleanedInstances.Count == 0)
        {
            Console.WriteLine($"Total: {response.TotalCount} | Page: {response.Page} | PageSize: {response.PageSize}");
            Console.WriteLine("(no instances)");
        }
        else
        {
            {
                var columns = cleanedInstances[0]!.Select(p => p.Key).ToArray();

                OutputFormatter.Write(
                    format,
                    cleanedInstances,
                    columns,
                    element => [.. columns.Select(col =>
                    {
                        var node = element![col];
                        if (node is null) return string.Empty;
                        return node is JsonValue v && v.TryGetValue<string>(out var str)
                            ? str
                            : node.ToJsonString();
                    })]);
            }
        }

        return ExitCodes.Success;
    }
}
