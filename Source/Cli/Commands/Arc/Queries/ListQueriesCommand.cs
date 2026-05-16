// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Arc.Queries;

/// <summary>
/// Lists all registered query endpoints in the connected Arc application.
/// </summary>
[LlmDescription("Lists all registered query endpoints in the Arc application. Returns name, namespace, HTTP route, fully qualified type, and documentation summary for each query.")]
[CliCommand("list", "List registered query endpoints", Branch = typeof(ArcBranch.Queries))]
[CliExample("arc", "queries", "list")]
[CliExample("arc", "queries", "list", "--url", "http://localhost:5000")]
[CliExample("arc", "queries", "list", "-o", "json")]
[LlmOutputAdvice("plain", "plain is significantly smaller than JSON for large query lists.")]
public class ListQueriesCommand : ArcCommand<ArcSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteCommandAsync(HttpClient httpClient, ArcSettings settings, string format, CancellationToken cancellationToken)
    {
        var url = settings.ResolveUrl();
        var response = await httpClient.GetAsync($"{url}/.cratis/queries", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            OutputFormatter.WriteError(format, $"Arc application returned {(int)response.StatusCode}", errorCode: ExitCodes.ServerErrorCode);
            return ExitCodes.ServerError;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var queries = JsonSerializer.Deserialize<List<QueryIntrospectionMetadata>>(json, OutputFormatter.JsonSerializerOptions) ?? [];

        OutputFormatter.Write(
            format,
            queries,
            ["Name", "Namespace", "Route", "Type", "Summary"],
            q =>
            [
                q.Name,
                q.Namespace,
                q.Route,
                q.Type,
                q.DocumentationSummary
            ]);

        return ExitCodes.Success;
    }
}
