// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Manages the periodic data refresh loop, pushes snapshots to views, and keeps the top status panel current.
/// </summary>
/// <param name="dataService">The Chronicle data service.</param>
/// <param name="settings">Workbench settings — controls the refresh interval and connection string.</param>
/// <param name="views">All view instances — each receives every fresh snapshot.</param>
/// <param name="navigation">Navigation — used to update badge counts after each refresh.</param>
/// <param name="windowSystem">The window system — used to write to the top panel.</param>
/// <param name="getActiveEventStore">Returns the currently active event store name, or <see langword="null"/> for the default.</param>
/// <param name="getActiveNamespace">Returns the currently active namespace name, or <see langword="null"/> for the default.</param>
public class WorkbenchRefreshLoop(
    WorkbenchDataService dataService,
    WorkbenchSettings settings,
    IWorkbenchView[] views,
    WorkbenchNavigation navigation,
    ConsoleWindowSystem windowSystem,
    Func<string?> getActiveEventStore,
    Func<string?> getActiveNamespace)
{
    readonly object _dataLock = new();
    WorkbenchData? _currentData;
    bool _wasDisconnected;

    /// <summary>
    /// Gets the most recently fetched snapshot, or <see langword="null"/> if no fetch has completed yet.
    /// Thread-safe — acquires the internal data lock on every access.
    /// </summary>
    public WorkbenchData? CurrentData
    {
        get
        {
            lock (_dataLock)
            {
                return _currentData;
            }
        }
    }

    /// <summary>
    /// Seeds the loop with a pre-fetched snapshot: pushes it to all views, updates the top panel, and
    /// updates navigation badge counts. Call once, before the window is shown.
    /// </summary>
    /// <param name="data">The pre-fetched initial snapshot.</param>
    public void Initialize(WorkbenchData data)
    {
        lock (_dataLock)
        {
            _currentData = data;
        }

        PushDataToViews(data);
        UpdateTopPanel(data);
        navigation.UpdateNavBadges(data);
    }

    /// <summary>
    /// Runs the periodic refresh loop as a SharpConsoleUI async window thread.
    /// Performs an immediate fetch on entry, then repeats on the configured interval until cancellation.
    /// </summary>
    /// <param name="window">The host window (unused — required by the window-thread delegate signature).</param>
    /// <param name="ct">Cancellation token that signals the window is closing.</param>
    public async Task RunAsync(Window window, CancellationToken ct)
    {
        await FetchAndUpdate(ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.Interval), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await FetchAndUpdate(ct);
        }
    }

    /// <summary>
    /// Fetches a fresh snapshot and distributes it to all views, the top panel, and navigation badges.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task FetchAndUpdate(CancellationToken ct)
    {
        try
        {
            SetPanelText("↻ refreshing…");

            var data = await dataService.FetchAsync(
                getActiveEventStore(),
                getActiveNamespace(),
                readModelContainerName: null,
                ct);

            lock (_dataLock)
            {
                _currentData = data;
            }

            PushDataToViews(data);

            if (_wasDisconnected && data.IsConnected)
            {
                _ = Task.Run(
                    async () =>
                    {
                        SetPanelText("✓ Reconnected");
                        await Task.Delay(3000, ct);
                        UpdateTopPanel(_currentData);
                    },
                    ct);
            }

            _wasDisconnected = !data.IsConnected;

            UpdateTopPanel(data);
            navigation.UpdateNavBadges(data);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Swallow — connectivity errors are surfaced via IsConnected on the next successful fetch.
        }
    }

    /// <summary>
    /// Displays a temporary message in the top panel, then resets to the normal connection summary after two seconds.
    /// </summary>
    /// <param name="text">The temporary text to display.</param>
    public void ShowTemporaryMessage(string text)
    {
        SetPanelText(text);
        _ = Task.Delay(2000).ContinueWith(_ => UpdateTopPanel(CurrentData), TaskScheduler.Default);
    }

    /// <summary>
    /// Updates the top panel with the current connection / store / namespace summary.
    /// Falls back to <see cref="CurrentData"/> when <paramref name="data"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="data">Optional snapshot to use; falls back to <see cref="CurrentData"/> when omitted.</param>
    public void UpdateTopPanel(WorkbenchData? data = null)
    {
        data ??= CurrentData;
        if (data is null)
        {
            return;
        }

        var host = ExtractHostFromConnectionString(settings.ResolveConnectionString());
        var eventStore = getActiveEventStore() ?? settings.ResolveEventStore();
        var ns = getActiveNamespace() ?? settings.ResolveNamespace();
        var connDot = data.IsConnected ? "●" : "○";
        var seqText = data.TailSequenceNumber.HasValue ? $"  seq#{data.TailSequenceNumber.Value:N0}" : string.Empty;

        windowSystem.PanelStateService.TopStatus =
            $"◆ CHRONICLE WORKBENCH  ·  {host}  ·  {eventStore}/{ns}  ·  ↻{settings.Interval}s{seqText}  {connDot}";
    }

    static string ExtractHostFromConnectionString(string connectionString)
    {
        const string scheme = "chronicle://";
        if (!connectionString.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var afterScheme = connectionString[scheme.Length..];

        var queryStart = afterScheme.IndexOf('?');
        if (queryStart >= 0)
        {
            afterScheme = afterScheme[..queryStart];
        }

        var atSign = afterScheme.IndexOf('@');
        if (atSign >= 0)
        {
            afterScheme = afterScheme[(atSign + 1)..];
        }

        return $"chronicle://{afterScheme}";
    }

    void PushDataToViews(WorkbenchData data)
    {
        foreach (var view in views)
        {
            view.UpdateData(data);
        }
    }

    void SetPanelText(string text) =>
        windowSystem.PanelStateService.TopStatus = text;
}
