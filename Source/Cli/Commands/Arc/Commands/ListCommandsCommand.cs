// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Arc.Commands;

/// <summary>
/// Lists all registered command endpoints in the connected Arc application.
/// </summary>
[LlmDescription("Lists all registered command endpoints in the Arc application. Returns name, namespace, HTTP route, type, and documentation summary for each command.")]
[CliCommand("list", "List registered command endpoints", Branch = typeof(ArcBranch.Commands))]
[CliExample("arc", "commands", "list")]
[CliExample("arc", "commands", "list", "--url", "http://localhost:5000")]
[CliExample("arc", "commands", "list", "-o", "json")]
[LlmOutputAdvice("plain", "plain is significantly smaller than JSON for large command lists.")]
public class ListCommandsCommand : ArcCommand<ArcSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteCommandAsync(HttpClient httpClient, ArcSettings settings, string format, CancellationToken cancellationToken)
    {
        var url = settings.ResolveUrl();
        var response = await httpClient.GetAsync($"{url}/.cratis/commands", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            OutputFormatter.WriteError(format, $"Arc application returned {(int)response.StatusCode}", errorCode: ExitCodes.ServerErrorCode);
            return ExitCodes.ServerError;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var commands = JsonSerializer.Deserialize<List<CommandIntrospectionMetadata>>(json, OutputFormatter.JsonSerializerOptions) ?? [];

        OutputFormatter.Write(
            format,
            commands,
            ["Name", "Namespace", "Route", "Summary"],
            cmd =>
            [
                cmd.Name,
                cmd.Namespace,
                cmd.Route,
                cmd.DocumentationSummary
            ]);

        return ExitCodes.Success;
    }
}
