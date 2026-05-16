// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Cratis.Chronicle.Contracts.Host;
using Cratis.Cli.Commands.Chronicle;

namespace Cratis.Cli.Commands.Version;

/// <summary>
/// Command that displays CLI version and server version.
/// Does not require a running server — gracefully shows CLI-only info when unavailable.
/// </summary>
[LlmDescription("Shows the CLI version, Chronicle server version, and whether the API contracts are compatible. Use -o json to parse version information programmatically.")]
[CliCommand("version", "Show CLI and server version information and contracts compatibility")]
[CliExample("version")]
[CliExample("version", "-o", "json")]
[LlmOutputAdvice("json", "JSON contains CLI version, server version, contracts version, compatibility flag, and latest available versions from NuGet — ideal for programmatic checks.")]
public class VersionCommand : AsyncCommand<ChronicleSettings>
{
    /// <summary>
    /// Gets the CLI assembly version.
    /// </summary>
    /// <returns>The version string.</returns>
    internal static string GetCliVersion()
    {
        var assembly = typeof(CliApp).Assembly;

        return GetVersionFromAssembly(assembly);
    }

    /// <inheritdoc/>
    protected override async Task<int> ExecuteAsync(CommandContext context, ChronicleSettings settings, CancellationToken cancellationToken)
    {
        var format = settings.ResolveOutputFormat();
        var cliVersion = GetCliVersion();

        // Try to connect to the server — swallow all failures silently.
        ServerVersionInfo? serverInfo = null;

        try
        {
            var connectionString = new ChronicleConnectionString(settings.ResolveConnectionString());
            var managementPort = settings.ResolveManagementPort();
            using var client = await CliChronicleConnection.Connect(connectionString, managementPort, cancellationToken);
            serverInfo = await client.Services.Server.GetVersionInfo();
        }
        catch
        {
            // Server unavailable, misconfigured, or doesn't support GetVersionInfo — all fine.
        }

        // Check NuGet for newer versions — both fire in parallel and never block on failure.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var cliUpdateTask = UpdateChecker.CheckForUpdate(UpdateChecker.CliPackageId, cliVersion, cts.Token);
        var serverUpdateTask = serverInfo is not null
            ? UpdateChecker.CheckForUpdate(UpdateChecker.ServerPackageId, serverInfo.Version, cts.Token)
            : Task.FromResult<string?>(null);

        string? latestCli = null;
        string? latestServer = null;

        try
        {
            latestCli = await cliUpdateTask;
        }
        catch
        {
            // Non-critical.
        }

        try
        {
            latestServer = await serverUpdateTask;
        }
        catch
        {
            // Non-critical.
        }

        if (string.Equals(format, OutputFormats.Quiet, StringComparison.Ordinal))
        {
            Console.WriteLine(cliVersion);
            return ExitCodes.Success;
        }

        if (string.Equals(format, OutputFormats.Json, StringComparison.Ordinal) || string.Equals(format, OutputFormats.JsonCompact, StringComparison.Ordinal))
        {
            var result = new
            {
                Cli = new
                {
                    Version = cliVersion,
                    LatestVersion = latestCli
                },
                Server = serverInfo is not null
                    ? new
                    {
                        serverInfo.Version,
                        serverInfo.CommitSha,
                        LatestVersion = latestServer
                    }
                    : null,
                ServerAvailable = serverInfo is not null,
                Compatible = serverInfo is not null
            };

            OutputFormatter.WriteObject(format, result);
            return ExitCodes.Success;
        }

        AnsiConsole.MarkupLine($"[bold]CLI version:[/]   {cliVersion.EscapeMarkup()}");

        if (latestCli is not null)
        {
            AnsiConsole.MarkupLine($"[yellow]Update available:[/] {cliVersion.EscapeMarkup()} -> {latestCli.EscapeMarkup()} (run 'cratis update' to upgrade)");
        }

        if (serverInfo is not null)
        {
            AnsiConsole.MarkupLine($"[bold]Server version:[/] {serverInfo.Version.EscapeMarkup()}");

            if (!string.IsNullOrEmpty(serverInfo.CommitSha))
            {
                AnsiConsole.MarkupLine($"[bold]Server commit:[/]  {serverInfo.CommitSha.EscapeMarkup()}");
            }

            if (latestServer is not null)
            {
                AnsiConsole.MarkupLine($"[yellow]Server update available:[/] {latestServer.EscapeMarkup()}");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]Server:[/]         [dim]unavailable[/]");
        }

        return ExitCodes.Success;
    }

    static string GetVersionFromAssembly(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        if (informational is not null)
        {
            var version = informational.InformationalVersion;
            var plusIndex = version.IndexOf('+');

            return plusIndex > 0 ? version[..plusIndex] : version;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
