// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Jobs;
using Cratis.Chronicle.Contracts.Observation;
using Cratis.Chronicle.Contracts.Recommendations;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SColor = SharpConsoleUI.Color;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// The main full-screen workbench window: navigation side pane, content area, live event log strip, and status bar.
/// </summary>
/// <param name="windowSystem">The SharpConsoleUI window system.</param>
/// <param name="dataService">The Chronicle data service.</param>
/// <param name="settings">The workbench settings.</param>
/// <param name="services">The Chronicle gRPC service clients.</param>
/// <param name="initialData">Pre-fetched snapshot used to populate all views before the first frame is rendered.</param>
public class MainWindow(
    ConsoleWindowSystem windowSystem,
    WorkbenchDataService dataService,
    WorkbenchSettings settings,
    IServices services,
    WorkbenchData initialData)
{
    /// <summary>Navigation item index for Overview.</summary>
    const int IndexOverview = 0;

    /// <summary>Navigation item index for Observers.</summary>
    const int IndexObservers = 1;

    /// <summary>Navigation item index for Failures.</summary>
    const int IndexFailures = 2;

    /// <summary>Navigation item index for Jobs.</summary>
    const int IndexJobs = 3;

    /// <summary>Navigation item index for Recommendations.</summary>
    const int IndexRecommendations = 4;

    /// <summary>Navigation item index for Event Types.</summary>
    const int IndexEventTypes = 5;

    /// <summary>Navigation item index for Projections.</summary>
    const int IndexProjections = 6;

    /// <summary>Navigation item index for Read Models.</summary>
    const int IndexReadModels = 7;

    /// <summary>Navigation item index for Event Log.</summary>
    const int IndexEventLog = 8;

    /// <summary>Navigation item index for Event Stores.</summary>
    const int IndexEventStores = 9;

    /// <summary>Navigation item index for Namespaces.</summary>
    const int IndexNamespaces = 10;

    /// <summary>Maximum number of raw event stream lines retained in the ring buffer for filter replay.</summary>
    const int StreamBufferCapacity = 500;

    /// <summary>Color palette for coloring event type names in the live stream by a hash of their ID.</summary>
    static readonly SColor[] _streamPalette =
    [
        new(122, 162, 247, 255), // electric blue (Accent)
        new(115, 218, 118, 255), // vivid green (Success)
        new(224, 175, 104, 255), // amber (Warning)
        new(187, 154, 247, 255), // mauve/purple
        new(42, 195, 222, 255),  // teal/cyan
        new(247, 118, 142, 255), // coral-red (Danger)
        new(255, 199, 119, 255), // gold
        new(137, 220, 235, 255), // sky blue
        new(166, 209, 137, 255), // sage green
        new(238, 153, 160, 255), // rose
    ];

    /// <summary>
    /// View instances — created once, reused across refreshes.
    /// Order must match the nav index constants above.
    /// </summary>
    readonly IWorkbenchView[] _views =
    [
        new OverviewView(),
        new ObserversView(),
        new FailedPartitionsView(),
        new JobsView(),
        new RecommendationsView(),
        new EventTypesView(),
        new ProjectionsView(),
        new ReadModelsView(),
        new EventLogView(),
        new EventStoresView(),
        new NamespacesView()
    ];

    readonly object _dataLock = new();
    readonly Queue<string> _eventStreamBuffer = new();
    string? _activeEventStore;
    string? _activeNamespace;
    WorkbenchData? _currentData;
    bool _eventStreamVisible = true;
    ulong _lastEventStreamSeq;
    bool _wasDisconnected;
    string _streamFilter = string.Empty;
    (string Description, Func<Task> Execute)? _pendingAction;
    bool _textInputFocused;
    NavigationView? _navView;
    StatusBarControl? _statusBar;
    MarkupControl? _titleBar;
    LogViewerControl? _eventStreamLog;
    NavigationItem? _observersItem;
    NavigationItem? _failuresItem;
    NavigationItem? _recommendationsItem;

    /// <summary>
    /// Builds the main window with all controls and the async update thread.
    /// </summary>
    /// <returns>The constructed <see cref="Window"/>.</returns>
    public Window Build()
    {
        WireViewCallbacks();

        var navView = BuildNavigationView();
        var logViewer = BuildEventStreamLogViewer();
        var streamFilterPrompt = BuildStreamFilterPrompt();
        var statusBar = BuildStatusBar();

        var splitterBar = Controls.HorizontalSplitter()
            .WithMinHeightAbove(6)
            .WithMinHeightBelow(4)
            .WithControls(navView, logViewer)
            .Build();

        // Populate all views with the pre-fetched snapshot before the first frame is rendered,
        // so every navigation pane has real data from the moment the window opens.
        _currentData = initialData;
        PushDataToViews(initialData);
        UpdateStatusBar(initialData);
        UpdateNavBadges(initialData);

        return new WindowBuilder(windowSystem)
            .WithTitle("Chronicle Workbench")
            .Maximized()
            .WithColors(WorkbenchColors.Foreground, WorkbenchColors.Background)
            .Borderless() // cspell:ignore Borderless
            .HideTitle()
            .HideCloseButton()
            .AddControl(BuildTitleBar())
            .AddControl(navView)
            .AddControl(splitterBar)
            .AddControl(streamFilterPrompt)
            .AddControl(logViewer)
            .AddControl(statusBar)
            .OnKeyPressed((_, e) => HandleKeyPress(e))
            .WithAsyncWindowThread(RunDataRefreshLoop)
            .Build();
    }

    static string TruncateId(string s) => s.Length <= 40 ? s : s[..37] + "…";

    static SColor EventTypeColor(string eventTypeId)
    {
        var hash = Math.Abs(eventTypeId.GetHashCode());
        return _streamPalette[hash % _streamPalette.Length];
    }

    /// <summary>
    /// Extracts the host:port portion from a Chronicle connection string,
    /// stripping credentials and query parameters so the title bar shows only the endpoint.
    /// </summary>
    /// <param name="connectionString">The raw connection string, possibly including credentials.</param>
    /// <returns>A clean <c>chronicle://host:port</c> string, or the original if parsing fails.</returns>
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

    static NavigationItem? FindItemByText(IReadOnlyList<NavigationItem> items, string text)
    {
        foreach (var item in items)
        {
            if (item.Text == text) return item;
        }

        return null;
    }

    void WireViewCallbacks()
    {
        if (_views[IndexObservers] is ObserversView ov)
        {
            ov.OnReplay = obs => ExecuteAction(
                $"Replay observer '{TruncateId(obs.Id)}'",
                () => services.Observers.Replay(new Replay
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    ObserverId = obs.Id,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }));

            ov.OnReplayAll = observers => ConfirmThenExecuteAll(
                $"Replay {observers.Count} observer{(observers.Count == 1 ? string.Empty : "s")}",
                observers,
                obs => services.Observers.Replay(new Replay
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    ObserverId = obs.Id,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }));
        }

        if (_views[IndexFailures] is FailedPartitionsView fv)
        {
            fv.OnRetryPartition = fp => ExecuteAction(
                $"Retry partition '{fp.Partition}'",
                () => services.Observers.RetryPartition(new RetryPartition
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    ObserverId = fp.ObserverId,
                    Partition = fp.Partition,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }));

            fv.OnReplayPartition = fp => ExecuteAction(
                $"Replay partition '{fp.Partition}'",
                () => services.Observers.ReplayPartition(new ReplayPartition
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    ObserverId = fp.ObserverId,
                    Partition = fp.Partition,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }));

            fv.OnRetryAll = partitions => ConfirmThenExecuteAll(
                $"Retry {partitions.Count} partition{(partitions.Count == 1 ? string.Empty : "s")}",
                partitions,
                fp => services.Observers.RetryPartition(new RetryPartition
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    ObserverId = fp.ObserverId,
                    Partition = fp.Partition,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }));

            fv.OnReplayAll = partitions => ConfirmThenExecuteAll(
                $"Replay {partitions.Count} partition{(partitions.Count == 1 ? string.Empty : "s")}",
                partitions,
                fp => services.Observers.ReplayPartition(new ReplayPartition
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    ObserverId = fp.ObserverId,
                    Partition = fp.Partition,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }));
        }

        if (_views[IndexJobs] is JobsView jv)
        {
            jv.OnStopJob = job => ExecuteAction(
                $"Stop job '{TruncateId(job.Type ?? job.Id.ToString())}'",
                () => services.Jobs.Stop(new StopJob
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    JobId = job.Id
                }));

            jv.OnResumeJob = job => ExecuteAction(
                $"Resume job '{TruncateId(job.Type ?? job.Id.ToString())}'",
                () => services.Jobs.Resume(new ResumeJob
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    JobId = job.Id
                }));

            jv.OnStopAll = jobs => ConfirmThenExecuteAll(
                $"Stop {jobs.Count} job{(jobs.Count == 1 ? string.Empty : "s")}",
                jobs,
                job => services.Jobs.Stop(new StopJob
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    JobId = job.Id
                }));

            jv.OnResumeAll = jobs => ConfirmThenExecuteAll(
                $"Resume {jobs.Count} job{(jobs.Count == 1 ? string.Empty : "s")}",
                jobs,
                job => services.Jobs.Resume(new ResumeJob
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    JobId = job.Id
                }));
        }

        if (_views[IndexRecommendations] is RecommendationsView rv)
        {
            rv.OnApply = rec => ExecuteAction(
                $"Apply recommendation '{TruncateId(rec.Name ?? rec.Id.ToString())}'",
                () => services.Recommendations.Perform(new Perform
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    RecommendationId = rec.Id
                }));

            rv.OnIgnore = rec => ExecuteAction(
                $"Ignore recommendation '{TruncateId(rec.Name ?? rec.Id.ToString())}'",
                () => services.Recommendations.Ignore(new Perform
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    RecommendationId = rec.Id
                }));

            rv.OnApplyAll = recs => ConfirmThenExecuteAll(
                $"Apply {recs.Count} recommendation{(recs.Count == 1 ? string.Empty : "s")}",
                recs,
                rec => services.Recommendations.Perform(new Perform
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    RecommendationId = rec.Id
                }));

            rv.OnIgnoreAll = recs => ConfirmThenExecuteAll(
                $"Ignore {recs.Count} recommendation{(recs.Count == 1 ? string.Empty : "s")}",
                recs,
                rec => services.Recommendations.Ignore(new Perform
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    RecommendationId = rec.Id
                }));
        }

        if (_views[IndexReadModels] is ReadModelsView rmv)
        {
            rmv.OnFetchInstances = async (containerName, ct) =>
                await dataService.FetchAsync(
                    _activeEventStore,
                    _activeNamespace,
                    readModelContainerName: containerName,
                    ct);
        }

        if (_views[IndexEventStores] is EventStoresView esv)
        {
            esv.OnSwitch = storeName =>
            {
                _activeEventStore = storeName;
                _activeNamespace = null;
                SwitchToOverview();
            };
        }

        if (_views[IndexNamespaces] is NamespacesView nsv)
        {
            nsv.OnSwitch = nsName =>
            {
                _activeNamespace = nsName;
                SwitchToOverview();
            };
        }

        foreach (var view in _views)
        {
            view.OnFilterFocusChanged = focused => _textInputFocused = focused;
        }
    }

    string BuildTitleContent()
    {
        var acc = WorkbenchColors.Accent.ToMarkup();
        var mut = WorkbenchColors.Muted.ToMarkup();
        var suc = WorkbenchColors.Success.ToMarkup();
        var host = ExtractHostFromConnectionString(settings.ResolveConnectionString());
        var eventStore = _activeEventStore ?? settings.ResolveEventStore();
        var ns = _activeNamespace ?? settings.ResolveNamespace();
        return $"  [bold {acc}]◆ CHRONICLE WORKBENCH[/]" +
               $"   [{mut}]{host}[/]" +
               $"   [{suc}]●[/] [{mut}]{eventStore} / {ns}[/]" +
               $"   [{mut}]↻ {settings.Interval}s[/]";
    }

    MarkupControl BuildTitleBar()
    {
        var control = new MarkupControl([BuildTitleContent()])
        {
            Name = "TitleBar"
        };
        _titleBar = control;
        return control;
    }

    NavigationView BuildNavigationView()
    {
        var selectedBg = new SColor(49, 50, 68, 255);
        var selectedFg = WorkbenchColors.Accent;

        var navView = Controls.NavigationView()
            .WithNavWidth(22)
            .WithPaneHeader($"[bold {WorkbenchColors.Accent.ToMarkup()}] CHRONICLE [/]")
            .WithSelectedColors(selectedFg, selectedBg)
            .WithPaneDisplayMode(NavigationViewDisplayMode.Expanded)
            .WithName("MainNav")
            .Fill()
            .AddHeader("DASHBOARD", h =>
                h.AddItem("Overview", "◆", null, panel =>
                    panel.AddControl(_views[IndexOverview].BuildContent(windowSystem))))
            .AddHeader("OBSERVATION", h =>
                h.AddItem("Observers", "o", null, panel =>
                        panel.AddControl(_views[IndexObservers].BuildContent(windowSystem)))
                    .AddItem("Failures", "!", null, panel =>
                        panel.AddControl(_views[IndexFailures].BuildContent(windowSystem))))
            .AddHeader("OPERATIONS", h =>
                h.AddItem("Jobs", "~", null, panel =>
                        panel.AddControl(_views[IndexJobs].BuildContent(windowSystem)))
                    .AddItem("Recommendations", "*", null, panel =>
                        panel.AddControl(_views[IndexRecommendations].BuildContent(windowSystem))))
            .AddHeader("SCHEMA", h =>
                h.AddItem("Event Types", "#", null, panel =>
                        panel.AddControl(_views[IndexEventTypes].BuildContent(windowSystem)))
                    .AddItem("Projections", ">", null, panel =>
                        panel.AddControl(_views[IndexProjections].BuildContent(windowSystem)))
                    .AddItem("Read Models", "=", null, panel =>
                        panel.AddControl(_views[IndexReadModels].BuildContent(windowSystem))))
            .AddHeader("DATA", h =>
                h.AddItem("Event Log", "-", null, panel =>
                        panel.AddControl(_views[IndexEventLog].BuildContent(windowSystem)))
                    .AddItem("Event Stores", "+", null, panel =>
                        panel.AddControl(_views[IndexEventStores].BuildContent(windowSystem)))
                    .AddItem("Namespaces", "@", null, panel =>
                        panel.AddControl(_views[IndexNamespaces].BuildContent(windowSystem))))
            .OnSelectedItemChanged((_, e) =>
            {
                var idx = e.NewIndex;
                if (idx < 0 || idx >= _views.Length) return;

                WorkbenchData? snapshot;
                lock (_dataLock)
                {
                    snapshot = _currentData;
                }

                if (snapshot is not null)
                {
                    _views[idx].UpdateData(snapshot);
                }
                else
                {
                    _ = Task.Run(() => FetchAndUpdate(CancellationToken.None));
                }
            })
            .Build();

        var items = navView.Items;
        _observersItem = FindItemByText(items, "Observers");
        _failuresItem = FindItemByText(items, "Failures");
        _recommendationsItem = FindItemByText(items, "Recommendations");

        _navView = navView;
        return navView;
    }

    LogViewerControl BuildEventStreamLogViewer()
    {
        var logViewer = new LogViewerControl(windowSystem.LogService)
        {
            Title = "Live Event Stream",
            AutoScroll = true,
            Name = "EventStream"
        };
        _eventStreamLog = logViewer;
        return logViewer;
    }

    PromptControl BuildStreamFilterPrompt()
    {
        var mut = WorkbenchColors.Muted.ToMarkup();
        return Controls.Prompt($"[{mut}]Stream: [/]")
            .WithName("StreamFilter")
            .OnInputChanged((_, text) => ApplyStreamFilter(text ?? string.Empty))
            .OnGotFocus((_, _) => _textInputFocused = true)
            .OnLostFocus((_, _) => _textInputFocused = false)
            .Build();
    }

    StatusBarControl BuildStatusBar()
    {
        var statusBar = Controls.StatusBar()
            .WithName("StatusBar")
            .StickyBottom()
            .AddLeft("1-0", "Jump", null)
            .AddLeft("<-", "Sidebar", null)
            .AddLeft("T", "Toggle Log", ToggleEventStream)
            .AddLeft("C", "Clear Log", ClearEventStream)
            .AddLeft("Q", "Quit", null)
            .AddRight(string.Empty, "Connecting...", null)
            .Build();

        _statusBar = statusBar;
        return statusBar;
    }

    async Task RunDataRefreshLoop(Window window, CancellationToken ct)
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

    async Task FetchAndUpdate(CancellationToken ct)
    {
        try
        {
            var data = await dataService.FetchAsync(
                _activeEventStore,
                _activeNamespace,
                readModelContainerName: null,
                ct);

            lock (_dataLock)
            {
                _currentData = data;
            }

            PushDataToViews(data);
            FetchNewEvents(data, ct);

            if (_wasDisconnected && data.IsConnected)
            {
                var suc = WorkbenchColors.Success.ToMarkup();
                _ = Task.Run(
                    async () =>
                    {
                        UpdateStatusRight($"[{suc}]✓ Reconnected[/]");
                        await Task.Delay(3000, ct);
                        UpdateStatusBar(_currentData);
                    },
                    ct);
            }

            _wasDisconnected = !data.IsConnected;

            UpdateStatusBar(data);
            UpdateNavBadges(data);

            if (_titleBar is MarkupControl titleBar)
            {
                titleBar.Text = BuildTitleContent();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Swallow — connectivity errors shown in status bar via IsConnected
        }
    }

    void PushDataToViews(WorkbenchData data)
    {
        foreach (var view in _views)
        {
            view.UpdateData(data);
        }
    }

    void FetchNewEvents(WorkbenchData data, CancellationToken ct)
    {
        if (data.TailSequenceNumber is null || _eventStreamLog is null) return;

        var afterSeq = _lastEventStreamSeq;
        if (data.TailSequenceNumber.Value <= afterSeq) return;

        _ = Task.Run(
            async () =>
            {
                try
                {
                    var newEvents = await dataService.FetchNewEventsAsync(
                        afterSeq,
                        _activeEventStore,
                        _activeNamespace,
                        ct);

                    foreach (var evt in newEvents)
                    {
                        var typeColor = EventTypeColor(evt.Context.EventType.Id).ToMarkup();
                        var line =
                            $"[{WorkbenchColors.Muted.ToMarkup()}]{evt.Context.Occurred}[/]  [{typeColor}]{evt.Context.EventType.Id}[/]  [{WorkbenchColors.Muted.ToMarkup()}]{evt.Context.EventSourceId ?? string.Empty}[/]  [bold]#{evt.Context.SequenceNumber:N0}[/]";

                        AppendToStreamBuffer(line);
                    }

                    if (newEvents.Count > 0)
                    {
                        _lastEventStreamSeq = newEvents[^1].Context.SequenceNumber;
                    }

                    if (_views[IndexOverview] is OverviewView ov)
                    {
                        ov.UpdateEventDelta(newEvents.Count);
                    }
                }
                catch
                {
                    // Best-effort display — ignore fetch errors for live stream
                }
            },
            ct);
    }

    void AppendToStreamBuffer(string line)
    {
        lock (_eventStreamBuffer)
        {
            while (_eventStreamBuffer.Count >= StreamBufferCapacity)
            {
                _eventStreamBuffer.Dequeue();
            }

            _eventStreamBuffer.Enqueue(line);
        }

        if (string.IsNullOrEmpty(_streamFilter) ||
            line.Contains(_streamFilter, StringComparison.OrdinalIgnoreCase))
        {
            windowSystem.LogService.LogInfo(line, "events");
        }
    }

    void ApplyStreamFilter(string filter)
    {
        _streamFilter = filter;

        windowSystem.LogService.ClearLogs();

        string[] buffered;
        lock (_eventStreamBuffer)
        {
            buffered = [.. _eventStreamBuffer];
        }

        foreach (var line in buffered)
        {
            if (string.IsNullOrEmpty(filter) ||
                line.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                windowSystem.LogService.LogInfo(line, "events");
            }
        }
    }

    void UpdateStatusBar(WorkbenchData? data = null)
    {
        if (_statusBar is null) return;

        data ??= _currentData;
        if (data is null) return;

        _statusBar.ClearRight();

        var connDot = data.IsConnected
            ? $"[{WorkbenchColors.Success.ToMarkup()}]●[/] connected"
            : $"[{WorkbenchColors.Danger.ToMarkup()}]●[/] disconnected";

        var seqText = data.TailSequenceNumber.HasValue
            ? $"  seq# {data.TailSequenceNumber.Value:N0}"
            : string.Empty;

        var mut = WorkbenchColors.Muted.ToMarkup();
        var acc = WorkbenchColors.Accent.ToMarkup();

        var eventStore = _activeEventStore ?? settings.ResolveEventStore();
        var ns = _activeNamespace ?? settings.ResolveNamespace();

        _statusBar.AddRightText(
            $"{connDot}{seqText}   [bold {acc}][E][/] [{mut}]{eventStore}[/]   [bold {acc}][N][/] [{mut}]{ns}[/]   ↻{settings.Interval}s",
            null);
    }

    void UpdateNavBadges(WorkbenchData data)
    {
        var problemCount = data.DisconnectedObservers + data.ReplayingObservers;

        if (_observersItem is NavigationItem observersItem)
        {
            observersItem.Subtitle = problemCount > 0 ? $"⚠{problemCount}" : string.Empty;
        }

        if (_failuresItem is NavigationItem failuresItem)
        {
            failuresItem.Subtitle = data.FailedPartitions.Count > 0
                ? data.FailedPartitions.Count.ToString()
                : string.Empty;
        }

        if (_recommendationsItem is NavigationItem recommendationsItem)
        {
            recommendationsItem.Subtitle = data.Recommendations.Count > 0
                ? data.Recommendations.Count.ToString()
                : string.Empty;
        }

        _navView?.Invalidate();
    }

    void ToggleEventStream()
    {
        _eventStreamVisible = !_eventStreamVisible;
        if (_eventStreamLog is LogViewerControl log)
        {
            log.Visible = _eventStreamVisible;
        }
    }

    void ClearEventStream() => windowSystem.LogService.ClearLogs();

    void HandleKeyPress(KeyPressedEventArgs e)
    {
        if (_navView is null) return;

        // When a destructive action is pending, intercept Y/N/Escape for confirmation.
        if (_pendingAction is not null)
        {
            switch (e.KeyInfo.Key)
            {
                case ConsoleKey.Y:
                    var pending = _pendingAction.Value;
                    _pendingAction = null;
                    RunPendingAction(pending.Description, pending.Execute);
                    e.Handled = true;
                    return;

                case ConsoleKey.N:
                case ConsoleKey.Escape:
                    _pendingAction = null;
                    UpdateStatusBar();
                    e.Handled = true;
                    return;
            }

            // Swallow all other keys while confirmation is shown.
            e.Handled = true;
            return;
        }

        // When a text input has focus, suppress all global shortcuts except Escape (to clear filter).
        if (_textInputFocused)
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                var idx = _navView.SelectedIndex;
                if (idx >= 0 && idx < _views.Length)
                {
                    _views[idx].ClearFilter();
                }

                e.Handled = true;
            }

            return;
        }

        switch (e.KeyInfo.Key)
        {
            case ConsoleKey.D1: _navView.SelectedIndex = IndexOverview; e.Handled = true; break;
            case ConsoleKey.D2: _navView.SelectedIndex = IndexObservers; e.Handled = true; break;
            case ConsoleKey.D3: _navView.SelectedIndex = IndexFailures; e.Handled = true; break;
            case ConsoleKey.D4: _navView.SelectedIndex = IndexJobs; e.Handled = true; break;
            case ConsoleKey.D5: _navView.SelectedIndex = IndexRecommendations; e.Handled = true; break;
            case ConsoleKey.D6: _navView.SelectedIndex = IndexEventTypes; e.Handled = true; break;
            case ConsoleKey.D7: _navView.SelectedIndex = IndexProjections; e.Handled = true; break;
            case ConsoleKey.D8: _navView.SelectedIndex = IndexReadModels; e.Handled = true; break;
            case ConsoleKey.D9: _navView.SelectedIndex = IndexEventLog; e.Handled = true; break;
            case ConsoleKey.D0: _navView.SelectedIndex = IndexEventStores; e.Handled = true; break;
            case ConsoleKey.LeftArrow: FocusNavigation(); e.Handled = true; break;
            case ConsoleKey.E: OpenEventStorePicker(); e.Handled = true; break;
            case ConsoleKey.N: OpenNamespacePicker(); e.Handled = true; break;
            case ConsoleKey.Enter when _navView.SelectedIndex == IndexReadModels:
                OpenReadModelDetail();
                e.Handled = true;
                break;
            case ConsoleKey.Oem2 when e.KeyInfo.Modifiers == ConsoleModifiers.Shift:
                OpenHelpOverlay();
                e.Handled = true;
                break;
            case ConsoleKey.T: ToggleEventStream(); e.Handled = true; break;
            case ConsoleKey.C: ClearEventStream(); e.Handled = true; break;
            case ConsoleKey.P when e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control):
                OpenCommandPalette();
                e.Handled = true;
                break;
            case ConsoleKey.F:
                ActivateCurrentFilter();
                e.Handled = true;
                break;
            case ConsoleKey.Q: Environment.Exit(0); break;
        }
    }

    void OpenHelpOverlay()
    {
        var mut = WorkbenchColors.Muted.ToMarkup();
        var acc = WorkbenchColors.Accent.ToMarkup();

        var helpText =
            $"[bold {acc}]NAVIGATION[/]\n" +
            $"  [{mut}]1–9/0[/]         Jump to section\n" +
            $"  [{mut}]↑ ↓[/]           Move selection\n" +
            $"  [{mut}]← / →[/]         Sidebar ↔ Content\n" +
            $"  [{mut}]Enter[/]          Open detail overlay\n" +
            $"  [{mut}]Esc[/]            Close overlay\n" +
            "\n" +
            $"[bold {acc}]QUICK SWITCH[/]\n" +
            $"  [{mut}]E[/]              Switch event store\n" +
            $"  [{mut}]N[/]              Switch namespace\n" +
            "\n" +
            $"[bold {acc}]FILTER[/]\n" +
            $"  [{mut}]F[/]              Focus filter prompt for current view\n" +
            $"  [{mut}]Escape[/]         Clear filter and return focus to table\n" +
            "\n" +
            $"[bold {acc}]ACTIONS (when row selected)[/]\n" +
            $"  [{mut}]R[/]              Replay observer\n" +
            $"  [{mut}]T[/]              Retry partition\n" +
            $"  [{mut}]P[/]              Replay partition\n" +
            $"  [{mut}]S / U[/]          Stop / Resume job\n" +
            $"  [{mut}]A / I[/]          Apply / Ignore recommendation\n" +
            $"  [{mut}]Y / N[/]          Confirm / Cancel action\n" +
            "\n" +
            $"[bold {acc}]LIVE STREAM[/]\n" +
            $"  [{mut}]T[/]              Toggle event stream\n" +
            $"  [{mut}]C[/]              Clear event stream\n" +
            "\n" +
            $"[bold {acc}]GENERAL[/]\n" +
            $"  [{mut}]+  /  -[/]        Increase / decrease refresh interval\n" +
            $"  [{mut}]Ctrl+P[/]         Command palette\n" +
            $"  [{mut}]?[/]              This help screen\n" +
            $"  [{mut}]Q[/]              Quit";

        var markup = new MarkupControl([helpText]) { Wrap = true };
        var content = Controls.ScrollablePanel()
            .AddControl(markup)
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithPadding(2, 1, 2, 1)
            .Build();

        Window? helpWindow = null;
        helpWindow = new WindowBuilder(windowSystem)
            .WithTitle(" Keyboard Shortcuts ")
            .WithColors(WorkbenchColors.Foreground, WorkbenchColors.Background)
            .WithSize(70, 35)
            .Centered()
            .AddControl(content)
            .OnKeyPressed((_, e) =>
            {
                if (e.KeyInfo.Key == ConsoleKey.Escape ||
                    (e.KeyInfo.Key == ConsoleKey.Oem2 && e.KeyInfo.Modifiers == ConsoleModifiers.Shift))
                {
                    windowSystem.CloseWindow(helpWindow, activateParent: true, force: false);
                }

                e.Handled = true;
            })
            .Build();

        windowSystem.AddWindow(helpWindow, activateWindow: true);
    }

    void NavigateObservers()
    {
        if (_navView is NavigationView nav) nav.SelectedIndex = IndexObservers;
    }

    void NavigateEventTypes()
    {
        if (_navView is NavigationView nav) nav.SelectedIndex = IndexEventTypes;
    }

    void NavigateProjections()
    {
        if (_navView is NavigationView nav) nav.SelectedIndex = IndexProjections;
    }

    void NavigateReadModels()
    {
        if (_navView is NavigationView nav) nav.SelectedIndex = IndexReadModels;
    }

    void NavigateFailures()
    {
        if (_navView is NavigationView nav) nav.SelectedIndex = IndexFailures;
    }

    void OpenCommandPalette()
    {
        WorkbenchData? snapshot;
        lock (_dataLock)
        {
            snapshot = _currentData;
        }

        if (snapshot is null) return;

        var mut = WorkbenchColors.Muted.ToMarkup();
        var acc = WorkbenchColors.Accent.ToMarkup();

        var resultsTable = Controls.Table()
            .AddColumn("Kind", SharpConsoleUI.Layout.TextJustification.Left, 20)
            .AddColumn("Name", SharpConsoleUI.Layout.TextJustification.Left, null)
            .Interactive()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithName("CommandPaletteResults")
            .Build();

        var searchPrompt = Controls.Prompt($"[{acc}]>[/] ")
            .WithName("CommandPaletteSearch")
            .OnGotFocus((_, _) => _textInputFocused = true)
            .OnLostFocus((_, _) => _textInputFocused = false)
            .Build();

        void PopulateResults(string query)
        {
            resultsTable.ClearRows();

            if (string.IsNullOrWhiteSpace(query)) return;

            var matches = new List<(string Kind, string Label, Action Navigate)>();

            foreach (var obs in snapshot.Observers)
            {
                if (obs.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(("Observer", $"{obs.Id} [{obs.RunningState}]", NavigateObservers));
                }
            }

            foreach (var et in snapshot.EventTypeRegistrations)
            {
                if (et.Type.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(("Event Type", $"{et.Type.Id} gen {et.Type.Generation}", NavigateEventTypes));
                }
            }

            foreach (var pd in snapshot.ProjectionDefinitions)
            {
                if (pd.Identifier.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(("Projection", pd.Identifier, NavigateProjections));
                }
            }

            foreach (var rm in snapshot.ReadModelDefinitions)
            {
                if (rm.ContainerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    rm.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(("Read Model", rm.DisplayName.Length > 0 ? rm.DisplayName : rm.ContainerName, NavigateReadModels));
                }
            }

            foreach (var fp in snapshot.FailedPartitions)
            {
                if (fp.ObserverId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    fp.Partition.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(("Failure", $"{fp.ObserverId}/{fp.Partition}", NavigateFailures));
                }
            }

            foreach (var (kind, label, navigate) in matches.Take(10))
            {
                resultsTable.AddRow(new SharpConsoleUI.Controls.TableRow([kind, label]) { Tag = navigate });
            }
        }

        searchPrompt.InputChanged += (_, text) => PopulateResults(text ?? string.Empty);

        Window? paletteWindow = null;
        paletteWindow = new WindowBuilder(windowSystem)
            .WithTitle(" Command Palette ")
            .WithColors(WorkbenchColors.Foreground, WorkbenchColors.Background)
            .WithSize(80, 16)
            .Centered()
            .AddControl(searchPrompt)
            .AddControl(resultsTable)
            .OnKeyPressed((_, e) =>
            {
                if (e.KeyInfo.Key == ConsoleKey.Escape)
                {
                    windowSystem.CloseWindow(paletteWindow, activateParent: true, force: false);
                    e.Handled = true;
                    return;
                }

                if (e.KeyInfo.Key == ConsoleKey.Enter)
                {
                    var selectedRow = resultsTable.SelectedRow;
                    if (selectedRow?.Tag is Action navigate)
                    {
                        windowSystem.CloseWindow(paletteWindow, activateParent: true, force: false);
                        navigate();
                    }

                    e.Handled = true;
                }
            })
            .Build();

        windowSystem.AddWindow(paletteWindow, activateWindow: true);
    }

    void FocusNavigation()
    {
        if (_navView is null) return;

        var window = windowSystem.GetWindowAtPoint(new System.Drawing.Point(0, 0));
        window?.FocusControl(_navView);
    }

    void ActivateCurrentFilter()
    {
        var idx = _navView?.SelectedIndex ?? -1;
        if (idx < 0 || idx >= _views.Length) return;

        var window = windowSystem.GetWindowAtPoint(new System.Drawing.Point(0, 0));
        if (window is null) return;

        _views[idx].ActivateFilter(window);
    }

    void OpenEventStorePicker()
    {
        WorkbenchData? snapshot;
        lock (_dataLock)
        {
            snapshot = _currentData;
        }

        if (snapshot is null) return;

        var stores = snapshot.EventStoreNames.Order().ToList();
        var active = _activeEventStore ?? settings.ResolveEventStore();
        var acc = WorkbenchColors.Accent.ToMarkup();

        var pickerTable = Controls.Table()
            .AddColumn("Event Store", SharpConsoleUI.Layout.TextJustification.Left, null)
            .Interactive()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithName("EventStorePickerTable")
            .Build();

        foreach (var name in stores)
        {
            var label = name == active ? $"[{acc}]► {name}[/]" : name;
            pickerTable.AddRow(new SharpConsoleUI.Controls.TableRow([label]) { Tag = name });
        }

        Window? picker = null;
        var height = Math.Min(stores.Count + 4, 20);
        picker = new WindowBuilder(windowSystem)
            .WithTitle(" Switch Event Store ")
            .WithColors(WorkbenchColors.Foreground, WorkbenchColors.Background)
            .WithSize(50, height)
            .Centered()
            .AddControl(pickerTable)
            .OnKeyPressed((_, ke) =>
            {
                if (ke.KeyInfo.Key == ConsoleKey.Escape)
                {
                    windowSystem.CloseWindow(picker, activateParent: true, force: false);
                    ke.Handled = true;
                }
            })
            .Build();

        pickerTable.RowActivated += (_, _) =>
        {
            if (pickerTable.SelectedRow?.Tag is string storeName)
            {
                _activeEventStore = storeName;
                _activeNamespace = null;
                windowSystem.CloseWindow(picker, activateParent: true, force: false);
                _ = Task.Run(() => FetchAndUpdate(CancellationToken.None));
            }
        };

        windowSystem.AddWindow(picker, activateWindow: true);
    }

    void OpenNamespacePicker()
    {
        WorkbenchData? snapshot;
        lock (_dataLock)
        {
            snapshot = _currentData;
        }

        if (snapshot is null) return;

        var namespaces = snapshot.NamespaceNames.Order().ToList();
        var active = _activeNamespace ?? settings.ResolveNamespace();
        var acc = WorkbenchColors.Accent.ToMarkup();

        var pickerTable = Controls.Table()
            .AddColumn("Namespace", SharpConsoleUI.Layout.TextJustification.Left, null)
            .Interactive()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithName("NamespacePickerTable")
            .Build();

        foreach (var name in namespaces)
        {
            var label = name == active ? $"[{acc}]► {name}[/]" : name;
            pickerTable.AddRow(new SharpConsoleUI.Controls.TableRow([label]) { Tag = name });
        }

        Window? picker = null;
        var height = Math.Min(namespaces.Count + 4, 20);
        picker = new WindowBuilder(windowSystem)
            .WithTitle(" Switch Namespace ")
            .WithColors(WorkbenchColors.Foreground, WorkbenchColors.Background)
            .WithSize(50, height)
            .Centered()
            .AddControl(pickerTable)
            .OnKeyPressed((_, ke) =>
            {
                if (ke.KeyInfo.Key == ConsoleKey.Escape)
                {
                    windowSystem.CloseWindow(picker, activateParent: true, force: false);
                    ke.Handled = true;
                }
            })
            .Build();

        pickerTable.RowActivated += (_, _) =>
        {
            if (pickerTable.SelectedRow?.Tag is string nsName)
            {
                _activeNamespace = nsName;
                windowSystem.CloseWindow(picker, activateParent: true, force: false);
                _ = Task.Run(() => FetchAndUpdate(CancellationToken.None));
            }
        };

        windowSystem.AddWindow(picker, activateWindow: true);
    }

    void SwitchToOverview()
    {
        if (_navView is null) return;
        _navView.SelectedIndex = IndexOverview;
    }

    void OpenReadModelDetail()
    {
        if (_views[IndexReadModels] is not ReadModelsView rmv) return;

        // Retrieve the selected read model from the view's table via the public overlay method.
        // The view owns the table reference, so we delegate entirely to it.
        rmv.OpenSelectedDetailOverlay();
    }

    void ExecuteAction(string description, Func<Task> action)
    {
        _pendingAction = (description, action);
        var war = WorkbenchColors.Warning.ToMarkup();
        var acc = WorkbenchColors.Accent.ToMarkup();
        var mut = WorkbenchColors.Muted.ToMarkup();
        UpdateStatusRight(
            $"[{war}]⚡ {description}?[/]   [bold {acc}][Y][/] [{mut}]Confirm[/]   [bold {acc}][N][/] [{mut}]Cancel[/]");
    }

    void ConfirmThenExecuteAll<T>(string description, IReadOnlyList<T> items, Func<T, Task> perItem)
    {
        ExecuteAction(description, async () =>
        {
            foreach (var item in items)
            {
                await perItem(item);
            }
        });
    }

    void RunPendingAction(string description, Func<Task> action)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                UpdateStatusRight($"[{WorkbenchColors.Warning.ToMarkup()}]⟳ {description}...[/]");
                await action();
                UpdateStatusRight($"[{WorkbenchColors.Success.ToMarkup()}]✓ Done[/]");

                await Task.Delay(3000);
                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                var msg = ex.Message.Length > 60 ? ex.Message[..60] : ex.Message;
                UpdateStatusRight($"[{WorkbenchColors.Danger.ToMarkup()}]✗ {msg}[/]");
            }
        });
    }

    void UpdateStatusRight(string text)
    {
        if (_statusBar is null) return;
        _statusBar.ClearRight();
        _statusBar.AddRightText(text, null);
    }
}
