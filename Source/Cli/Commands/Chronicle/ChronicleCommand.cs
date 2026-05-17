// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Grpc.Core;

namespace Cratis.Cli.Commands.Chronicle;

/// <summary>
/// Base class for all CLI commands that need a Chronicle connection.
/// </summary>
/// <typeparam name="TSettings">The settings type for this command.</typeparam>
public abstract partial class ChronicleCommand<TSettings> : AsyncCommand<TSettings>
    where TSettings : ChronicleSettings
{
    /// <summary>
    /// Gets a value indicating whether this command wraps execution in an <see cref="AnsiConsole.Status"/> spinner.
    /// Override and return <see langword="false"/> for commands that manage their own interactive display (e.g. live dashboards).
    /// </summary>
    protected virtual bool UseStatusSpinner => true;

    [GeneratedRegex("://(?<user>[^:@/]+):[^@/]+@", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    static partial Regex ConnectionStringCredentialsRegex { get; }

    /// <inheritdoc/>
    protected sealed override async Task<int> ExecuteAsync(CommandContext context, TSettings settings, CancellationToken cancellationToken)
    {
        var format = settings.ResolveOutputFormat();

        if (settings.Debug)
        {
            WriteDebugInfo(settings);
        }

        var tokenRefreshAttempted = false;
        while (true)
        {
            try
            {
                var connectionString = new ChronicleConnectionString(settings.ResolveConnectionString());
                var managementPort = settings.ResolveManagementPort();
                using var client = await CliChronicleConnection.Connect(connectionString, managementPort, cancellationToken);

                int exitCode;
                var sw = settings.Debug ? Stopwatch.StartNew() : null;

                if (string.Equals(format, OutputFormats.Table, StringComparison.Ordinal) && UseStatusSpinner)
                {
                    exitCode = await AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .SpinnerStyle(new Style(OutputFormatter.Accent))
                        .StartAsync("Connecting...", async _ =>
                            await ExecuteCommandAsync(client.Services, settings, format));
                }
                else
                {
                    exitCode = await ExecuteCommandAsync(client.Services, settings, format);
                }

                if (sw is not null)
                {
                    sw.Stop();
                    await Console.Error.WriteLineAsync($"[debug] command completed in {sw.ElapsedMilliseconds}ms, exit code {exitCode}");
                }

                return exitCode;
            }
            catch (RpcException ex) when (!tokenRefreshAttempted && IsHttpUnauthorized(ex))
            {
                // Cached token was rejected — clear it and retry once with a fresh token.
                tokenRefreshAttempted = true;
                var config = CliConfiguration.Load();
                var cs = new ChronicleConnectionString(settings.ResolveConnectionString());
                CliChronicleConnection.ClearTokenCache(config.ActiveContextName, cs.Username ?? string.Empty);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable || IsNetworkException(ex.InnerException))
            {
                OutputFormatter.WriteError(format, CliDefaults.CannotConnectMessage, BuildConnectionHint(format, settings), ExitCodes.ConnectionErrorCode);
                return ExitCodes.ConnectionError;
            }
            catch (RpcException ex) when (ex.Status.Detail.Contains("disposed", StringComparison.OrdinalIgnoreCase))
            {
                OutputFormatter.WriteError(format, "Server error", $"{ex.Status.Detail}", ExitCodes.ServerErrorCode);
                return ExitCodes.ServerError;
            }
            catch (RpcException ex)
            {
                OutputFormatter.WriteError(format, $"Server error: {ex.Status.Detail}", errorCode: ExitCodes.ServerErrorCode);
                return ExitCodes.ServerError;
            }
            catch (ObjectDisposedException)
            {
                OutputFormatter.WriteError(format, CliDefaults.CannotConnectMessage, BuildConnectionHint(format, settings), ExitCodes.ConnectionErrorCode);
                return ExitCodes.ConnectionError;
            }
            catch (HttpRequestException ex)
            {
                var message = ex.InnerException is SocketException
                    ? $"Connection refused ({RedactConnectionString(settings.ResolveConnectionString())})"
                    : ex.Message;
                OutputFormatter.WriteError(format, message, BuildConnectionHint(format, settings), ExitCodes.ConnectionErrorCode);
                return ExitCodes.ConnectionError;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                OutputFormatter.WriteError(format, ex.Message, errorCode: ExitCodes.ServerErrorCode);
                return ExitCodes.ServerError;
            }
        }
    }

    /// <summary>
    /// Executes the command logic with gRPC services.
    /// </summary>
    /// <param name="services">The gRPC service proxies.</param>
    /// <param name="settings">The command settings.</param>
    /// <param name="format">The resolved output format.</param>
    /// <returns>The exit code.</returns>
    protected abstract Task<int> ExecuteCommandAsync(IServices services, TSettings settings, string format);

    static void WriteDebugInfo(ChronicleSettings settings)
    {
        var configPath = CliConfiguration.GetConfigPath();
        var config = CliConfiguration.Load();
        var connectionString = settings.ResolveConnectionString();
        var managementPort = settings.ResolveManagementPort();

        Console.Error.WriteLine($"[debug] config:          {configPath}");
        Console.Error.WriteLine($"[debug] context:         {config.ActiveContextName}");
        Console.Error.WriteLine($"[debug] server:          {RedactConnectionString(connectionString)}");
        Console.Error.WriteLine($"[debug] management-port: {managementPort}");
        Console.Error.WriteLine($"[debug] output:          {settings.ResolveOutputFormat()}");

        if (settings is EventStoreSettings ess)
        {
            Console.Error.WriteLine($"[debug] event-store:     {ess.ResolveEventStore()}");
            Console.Error.WriteLine($"[debug] namespace:       {ess.ResolveNamespace()}");
        }
    }

    static string BuildConnectionHint(string format, ChronicleSettings settings)
    {
        // In JSON/machine-readable formats only include the minimal connection info, not multi-line hints.
        var connectionString = RedactConnectionString(settings.ResolveConnectionString());
        if (string.Equals(format, OutputFormats.Json, StringComparison.Ordinal) ||
            string.Equals(format, OutputFormats.JsonCompact, StringComparison.Ordinal))
        {
            return $"Verify the server is running and reachable. Connection: {connectionString}";
        }

        var config = CliConfiguration.Load();
        var contextName = config.ActiveContextName;

        return $"Context: {contextName} → {connectionString}\n" +
               "To update: cratis context set-value server <new-url>\n" +
               "To create a new context: cratis context create <name> --server <url>";
    }

    static string RedactConnectionString(string connectionString) =>
        ConnectionStringCredentialsRegex.Replace(connectionString, "://${user}:***@");

    static bool IsHttpUnauthorized(RpcException ex) =>
        ex.Status.Detail.Contains("HTTP status code: 401", StringComparison.Ordinal);

    static bool IsNetworkException(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is HttpRequestException or SocketException)
            {
                return true;
            }

            ex = ex.InnerException;
        }

        return false;
    }
}
