// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;

namespace Cratis.Cli.Commands.Chronicle.Diagnose;

/// <summary>
/// Runs a battery of health checks against the Chronicle server and renders a diagnostic report.
/// Supports a live --watch mode that refreshes the report on a configurable interval.
/// </summary>
[LlmDescription("Runs a health check against the Chronicle server and returns a diagnostic report covering connectivity, event store health, and configuration. Use to debug connection or server issues.")]
[CliCommand("diagnose", "Run a health check against the Chronicle server and show a diagnostic report", Branch = typeof(ChronicleBranch))]
[CliExample("chronicle", "diagnose")]
[CliExample("chronicle", "diagnose", "-o", "json")]
[CliExample("chronicle", "diagnose", "--watch")]
[CliExample("chronicle", "diagnose", "--watch", "--interval", "2")]
public partial class DiagnoseCommand : ChronicleCommand<DiagnoseSettings>
{
    [GeneratedRegex("://(?<user>[^:@/]+):[^@/]+@", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    static partial Regex ConnectionStringCredentialsRegex { get; }

    /// <inheritdoc/>
    protected override async Task<int> ExecuteCommandAsync(IServices services, DiagnoseSettings settings, string format)
    {
        if (settings.Watch)
        {
            if (format is not OutputFormats.Table)
            {
                OutputFormatter.WriteError(format, "--watch requires text output format", "Remove -o/--output or use --output table", ExitCodes.ValidationErrorCode);
                return ExitCodes.ValidationError;
            }

            if (settings.Interval < 1)
            {
                OutputFormatter.WriteError(format, "--interval must be at least 1 second", errorCode: ExitCodes.ValidationErrorCode);
                return ExitCodes.ValidationError;
            }

            return await RunWatch(services, settings);
        }

        var data = await Gather(services, settings);
        Render(format, data);
        return data.IsHealthy ? ExitCodes.Success : ExitCodes.ServerError;
    }

    static string RedactConnectionString(string connectionString) =>
        ConnectionStringCredentialsRegex.Replace(connectionString, "://${user}:***@");

    static async Task<DiagnoseData> Gather(IServices services, DiagnoseSettings settings)
    {
        var eventStore = settings.ResolveEventStore();
        var ns = settings.ResolveNamespace();
        var connectionString = settings.ResolveConnectionString();

        // Server version
        string? serverVersion = null;
        var serverReachable = true;

        try
        {
            var versionInfo = await services.Server.GetVersionInfo();
            serverVersion = versionInfo.Version;
        }
        catch
        {
            serverReachable = false;
        }

        // Latest server version from package feed (non-blocking, best-effort)
        string? latestServerVersion = null;
        if (serverVersion is not null)
        {
            try
            {
                using var updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                latestServerVersion = await UpdateChecker.CheckForUpdate(UpdateChecker.ServerPackageId, serverVersion, updateCts.Token);
            }
            catch { }
        }

        // Event stores
        var eventStores = new List<string>();
        try
        {
            eventStores = [.. await services.EventStores.GetEventStores()];
        }
        catch { }

        // Observers
        int activeObservers = 0, replayingObservers = 0, disconnectedObservers = 0, suspendedObservers = 0, totalObservers = 0;
        try
        {
            var observers = (await services.Observers.GetObservers(new AllObserversRequest
            {
                EventStore = eventStore,
                Namespace = ns
            })).ToList();

            totalObservers = observers.Count;
            activeObservers = observers.Count(o => o.RunningState == ObserverRunningState.Active);
            replayingObservers = observers.Count(o => o.RunningState == ObserverRunningState.Replaying);
            suspendedObservers = observers.Count(o => o.RunningState == ObserverRunningState.Suspended);
            disconnectedObservers = observers.Count(o => o.RunningState == ObserverRunningState.Disconnected);
        }
        catch { }

        // Failed partitions
        var failedPartitions = 0;
        try
        {
            var fps = (await services.FailedPartitions.GetFailedPartitions(new GetFailedPartitionsRequest
            {
                EventStore = eventStore,
                Namespace = ns
            })).ToList();

            failedPartitions = fps.Count;
        }
        catch { }

        // Pending recommendations
        var pendingRecommendations = 0;
        try
        {
            var recs = (await services.Recommendations.GetRecommendations(new GetRecommendationsRequest
            {
                EventStore = eventStore,
                Namespace = ns
            })).ToList();

            pendingRecommendations = recs.Count;
        }
        catch { }

        // Event sequence tail
        ulong? eventSequenceTail = null;
        try
        {
            var tail = await services.EventSequences.GetTailSequenceNumber(new GetTailSequenceNumberRequest
            {
                EventStore = eventStore,
                Namespace = ns,
                EventSequenceId = CliDefaults.DefaultEventSequenceId
            });

            eventSequenceTail = tail.SequenceNumber == ulong.MaxValue ? null : tail.SequenceNumber;
        }
        catch { }

        return new DiagnoseData(
            ConnectionString: connectionString,
            EventStore: eventStore,
            Namespace: ns,
            ServerReachable: serverReachable,
            ServerVersion: serverVersion,
            LatestServerVersion: latestServerVersion,
            EventStores: eventStores,
            ActiveObservers: activeObservers,
            ReplayingObservers: replayingObservers,
            SuspendedObservers: suspendedObservers,
            DisconnectedObservers: disconnectedObservers,
            FailedPartitions: failedPartitions,
            PendingRecommendations: pendingRecommendations,
            EventSequenceTail: eventSequenceTail,
            CapturedAt: DateTimeOffset.Now);
    }

    static void Render(string format, DiagnoseData data)
    {
        if (string.Equals(format, OutputFormats.Json, StringComparison.Ordinal) || string.Equals(format, OutputFormats.JsonCompact, StringComparison.Ordinal))
        {
            OutputFormatter.WriteObject(format, new
            {
                capturedAt = data.CapturedAt,
                healthy = data.IsHealthy,
                connection = new
                {
                    server = RedactConnectionString(data.ConnectionString),
                    reachable = data.ServerReachable
                },
                version = new
                {
                    server = data.ServerVersion,
                    latestServer = data.LatestServerVersion
                },
                eventStores = data.EventStores,
                observers = new
                {
                    total = data.TotalObservers,
                    active = data.ActiveObservers,
                    replaying = data.ReplayingObservers,
                    suspended = data.SuspendedObservers,
                    disconnected = data.DisconnectedObservers
                },
                failedPartitions = data.FailedPartitions,
                pendingRecommendations = data.PendingRecommendations,
                eventSequenceTail = data.EventSequenceTail
            });
            return;
        }

        if (string.Equals(format, OutputFormats.Plain, StringComparison.Ordinal))
        {
            RenderPlain(data);
            return;
        }

        RenderText(data);
    }

    static void RenderText(DiagnoseData data)
    {
        var redactedConnectionString = RedactConnectionString(data.ConnectionString);
        var serverText = data.ServerReachable
            ? $"[bold]{redactedConnectionString.EscapeMarkup()}[/]"
            : $"[{OutputFormatter.Danger.ToMarkup()}]{redactedConnectionString.EscapeMarkup()} (unreachable)[/]";

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold]Chronicle Diagnostics[/]  [{OutputFormatter.Muted.ToMarkup()}]{data.CapturedAt:HH:mm:ss}[/]")
            .RuleStyle(new Style(OutputFormatter.Muted))
            .LeftJustified());

        AnsiConsole.MarkupLine($"  [{OutputFormatter.Muted.ToMarkup()}]server:[/]      {serverText}");
        AnsiConsole.MarkupLine($"  [{OutputFormatter.Muted.ToMarkup()}]event store:[/] {data.EventStore.EscapeMarkup()}  [{OutputFormatter.Muted.ToMarkup()}]/[/]  {data.Namespace.EscapeMarkup()}");
        AnsiConsole.WriteLine();

        WriteCheck(data.ServerReachable, "Connection", data.ServerReachable ? "connected" : "unreachable");

        if (data.ServerReachable)
        {
            if (data.LatestServerVersion is not null)
            {
                var serverVersionText = (data.ServerVersion ?? "unknown").EscapeMarkup();
                var latestVersionText = data.LatestServerVersion.EscapeMarkup();
                WriteCheck(false, "Server version", $"{serverVersionText}  [{OutputFormatter.Warning.ToMarkup()}]update available: {latestVersionText}[/]  [{OutputFormatter.Muted.ToMarkup()}]→ upgrade the Chronicle server[/]", isWarning: true);
            }
            else
            {
                WriteCheck(true, "Server version", data.ServerVersion ?? "unknown");
            }
        }

        var eventStoreStatus = data.EventStores.Count switch
        {
            0 => $"[{OutputFormatter.Muted.ToMarkup()}]none found[/]",
            1 => $"{data.EventStores[0].EscapeMarkup()}",
            _ => $"{data.EventStores.Count} stores: {string.Join(", ", data.EventStores.Select(e => e.EscapeMarkup()))}"
        };
        WriteCheck(data.EventStores.Count > 0, "Event stores", eventStoreStatus);

        var observerStatus = data.TotalObservers == 0
            ? $"[{OutputFormatter.Muted.ToMarkup()}]none[/]"
            : BuildObserverStatus(data);
        WriteCheck(data.ActiveObservers > 0, "Observers", observerStatus);

        var failedPartitionStatus = data.FailedPartitions == 0
            ? $"[{OutputFormatter.Success.ToMarkup()}]none[/]"
            : $"[{OutputFormatter.Danger.ToMarkup()}]{data.FailedPartitions} need attention[/]  [{OutputFormatter.Muted.ToMarkup()}]→ cratis chronicle failed-partitions list[/]";
        WriteCheck(data.FailedPartitions == 0, "Failed partitions", failedPartitionStatus);

        var recsStatus = data.PendingRecommendations == 0
            ? $"[{OutputFormatter.Success.ToMarkup()}]none[/]"
            : $"[{OutputFormatter.Warning.ToMarkup()}]{data.PendingRecommendations} pending[/]  [{OutputFormatter.Muted.ToMarkup()}]→ cratis chronicle recommendations list[/]";
        WriteCheck(data.PendingRecommendations == 0, "Recommendations", recsStatus);

        var tailStatus = data.EventSequenceTail.HasValue
            ? $"tail: {data.EventSequenceTail.Value:N0}"
            : $"[{OutputFormatter.Muted.ToMarkup()}]unavailable[/]";
        WriteCheck(data.EventSequenceTail.HasValue, "Event sequence", tailStatus, isInfo: true);

        AnsiConsole.WriteLine();

        if (data.IsHealthy)
        {
            AnsiConsole.MarkupLine($"  [{OutputFormatter.Success.ToMarkup()}]✓ System is healthy[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"  [{OutputFormatter.Danger.ToMarkup()}]✗ Issues detected — review items above[/]");
        }

        AnsiConsole.WriteLine();
    }

    static string BuildObserverStatus(DiagnoseData data)
    {
        var parts = new List<string>();

        if (data.ActiveObservers > 0)
        {
            parts.Add($"[{OutputFormatter.Success.ToMarkup()}]{data.ActiveObservers} active[/]");
        }

        if (data.ReplayingObservers > 0)
        {
            parts.Add($"[{OutputFormatter.Success.ToMarkup()}]{data.ReplayingObservers} replaying[/]");
        }

        if (data.SuspendedObservers > 0)
        {
            parts.Add($"[{OutputFormatter.Muted.ToMarkup()}]{data.SuspendedObservers} suspended[/]");
        }

        if (data.DisconnectedObservers > 0)
        {
            parts.Add($"[{OutputFormatter.Warning.ToMarkup()}]{data.DisconnectedObservers} disconnected[/]");
        }

        return string.Join("  ", parts);
    }

    static void WriteCheck(bool ok, string label, string detail, bool isInfo = false, bool isWarning = false)
    {
        string icon;
        if (isWarning)
        {
            icon = $"[{OutputFormatter.Warning.ToMarkup()}]▲[/]";
        }
        else if (ok)
        {
            icon = $"[{OutputFormatter.Success.ToMarkup()}]✓[/]";
        }
        else if (isInfo)
        {
            icon = $"[{OutputFormatter.Muted.ToMarkup()}]·[/]";
        }
        else
        {
            icon = $"[{OutputFormatter.Danger.ToMarkup()}]✗[/]";
        }

        AnsiConsole.MarkupLine($"  {icon}  [{OutputFormatter.Accent.ToMarkup()}]{label.PadRight(20).EscapeMarkup()}[/]  {detail}");
    }

    static void RenderPlain(DiagnoseData data)
    {
        Console.WriteLine($"healthy={data.IsHealthy}");
        Console.WriteLine($"server={RedactConnectionString(data.ConnectionString)}");
        Console.WriteLine($"reachable={data.ServerReachable}");
        Console.WriteLine($"server_version={data.ServerVersion ?? string.Empty}");
        Console.WriteLine($"server_version_latest={data.LatestServerVersion ?? string.Empty}");
        Console.WriteLine($"event_stores={data.EventStores.Count}");
        Console.WriteLine($"observers_active={data.ActiveObservers}");
        Console.WriteLine($"observers_replaying={data.ReplayingObservers}");
        Console.WriteLine($"observers_suspended={data.SuspendedObservers}");
        Console.WriteLine($"observers_disconnected={data.DisconnectedObservers}");
        Console.WriteLine($"failed_partitions={data.FailedPartitions}");
        Console.WriteLine($"pending_recommendations={data.PendingRecommendations}");
        Console.WriteLine($"event_sequence_tail={data.EventSequenceTail?.ToString() ?? string.Empty}");
    }

    static async Task<int> RunWatch(IServices services, DiagnoseSettings settings)
    {
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var interval = settings.Interval;
        var initialData = new DiagnoseData(
            ConnectionString: settings.ResolveConnectionString(),
            EventStore: settings.ResolveEventStore(),
            Namespace: settings.ResolveNamespace(),
            ServerReachable: false,
            ServerVersion: null,
            LatestServerVersion: null,
            EventStores: [],
            ActiveObservers: 0,
            ReplayingObservers: 0,
            SuspendedObservers: 0,
            DisconnectedObservers: 0,
            FailedPartitions: 0,
            PendingRecommendations: 0,
            EventSequenceTail: null,
            CapturedAt: DateTimeOffset.Now);

        await AnsiConsole.Live(BuildLiveTable(initialData, interval))
            .StartAsync(async ctx =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var data = await Gather(services, settings);
                    ctx.UpdateTarget(BuildLiveTable(data, interval));
                    ctx.Refresh();

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(settings.Interval), cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            });

        AnsiConsole.MarkupLine($"  [{OutputFormatter.Muted.ToMarkup()}]Watch stopped.[/]");
        return ExitCodes.Success;
    }

    static Table BuildLiveTable(DiagnoseData data, int intervalSeconds = 5)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(OutputFormatter.Muted)
            .AddColumn(new TableColumn(string.Empty).Width(3).NoWrap())
            .AddColumn(new TableColumn($"[bold]{data.EventStore.EscapeMarkup()}[/]  [{OutputFormatter.Muted.ToMarkup()}]/{data.Namespace.EscapeMarkup()}[/]").NoWrap())
            .AddColumn(new TableColumn($"[{OutputFormatter.Muted.ToMarkup()}]{data.CapturedAt:HH:mm:ss}  (every {intervalSeconds}s)[/]").RightAligned());

        table.AddRow(
            data.ServerReachable ? $"[{OutputFormatter.Success.ToMarkup()}]✓[/]" : $"[{OutputFormatter.Danger.ToMarkup()}]✗[/]",
            $"[{OutputFormatter.Accent.ToMarkup()}]Connection[/]",
            data.ServerReachable ? "connected" : $"[{OutputFormatter.Danger.ToMarkup()}]unreachable[/]");

        if (data.ServerReachable)
        {
            var serverVersionCell = data.LatestServerVersion is not null
                ? $"{(data.ServerVersion ?? "unknown").EscapeMarkup()}  [{OutputFormatter.Warning.ToMarkup()}]↑ {data.LatestServerVersion.EscapeMarkup()}[/]"
                : (data.ServerVersion ?? "unknown").EscapeMarkup();
            var serverVersionIcon = data.LatestServerVersion is not null
                ? $"[{OutputFormatter.Warning.ToMarkup()}]▲[/]"
                : $"[{OutputFormatter.Muted.ToMarkup()}]·[/]";
            table.AddRow(serverVersionIcon, $"[{OutputFormatter.Accent.ToMarkup()}]Server version[/]", serverVersionCell);
        }

        var observersIcon = $"[{OutputFormatter.Success.ToMarkup()}]✓[/]";
        var observersDetail = data.TotalObservers == 0 ? $"[{OutputFormatter.Muted.ToMarkup()}]none[/]" : BuildObserverStatus(data);
        table.AddRow(observersIcon, $"[{OutputFormatter.Accent.ToMarkup()}]Observers[/]", observersDetail);

        var failedIcon = data.FailedPartitions == 0 ? $"[{OutputFormatter.Success.ToMarkup()}]✓[/]" : $"[{OutputFormatter.Danger.ToMarkup()}]✗[/]";
        var failedDetail = data.FailedPartitions == 0
            ? $"[{OutputFormatter.Success.ToMarkup()}]none[/]"
            : $"[{OutputFormatter.Danger.ToMarkup()}]{data.FailedPartitions} need attention[/]";
        table.AddRow(failedIcon, $"[{OutputFormatter.Accent.ToMarkup()}]Failed partitions[/]", failedDetail);

        var recsIcon = data.PendingRecommendations == 0 ? $"[{OutputFormatter.Success.ToMarkup()}]✓[/]" : $"[{OutputFormatter.Warning.ToMarkup()}]▲[/]";
        var recsDetail = data.PendingRecommendations == 0
            ? $"[{OutputFormatter.Success.ToMarkup()}]none[/]"
            : $"[{OutputFormatter.Warning.ToMarkup()}]{data.PendingRecommendations} pending[/]";
        table.AddRow(recsIcon, $"[{OutputFormatter.Accent.ToMarkup()}]Recommendations[/]", recsDetail);

        var tailDetail = data.EventSequenceTail.HasValue
            ? $"{data.EventSequenceTail.Value:N0}"
            : $"[{OutputFormatter.Muted.ToMarkup()}]—[/]";
        table.AddRow($"[{OutputFormatter.Muted.ToMarkup()}]·[/]", $"[{OutputFormatter.Accent.ToMarkup()}]Event sequence tail[/]", tailDetail);

        return table;
    }
}
