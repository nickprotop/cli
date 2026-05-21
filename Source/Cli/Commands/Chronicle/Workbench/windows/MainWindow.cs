// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Jobs;
using Cratis.Chronicle.Contracts.Observation;
using Cratis.Chronicle.Contracts.Recommendations;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Helpers;
using SColor = SharpConsoleUI.Color;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// The main full-screen workbench window: navigation side pane, content area, and status bar.
/// Serves as the composition root — delegates action confirmation to <see cref="WorkbenchActionHandler"/>
/// and navigation building to <see cref="WorkbenchNavigation"/>.
/// </summary>
/// <param name="windowSystem">The SharpConsoleUI window system.</param>
/// <param name="dataService">The Chronicle data service.</param>
/// <param name="settings">The workbench settings.</param>
/// <param name="services">The Chronicle gRPC service clients.</param>
/// <param name="initialData">Pre-fetched snapshot used to populate all views before the first frame is rendered.</param>
/// <param name="state">Persisted workbench state from the previous session (last nav index, interval).</param>
public class MainWindow(
    ConsoleWindowSystem windowSystem,
    WorkbenchDataService dataService,
    WorkbenchSettings settings,
    IServices services,
    WorkbenchData initialData,
    WorkbenchState state)
{
    /// <summary>Width of the help overlay window in columns.</summary>
    const int HelpOverlayWidth = 72;

    /// <summary>Height of the help overlay window in rows.</summary>
    const int HelpOverlayHeight = 40;

    /// <summary>Width of the command palette window in columns.</summary>
    const int CommandPaletteWidth = 80;

    /// <summary>Height of the command palette window in rows.</summary>
    const int CommandPaletteHeight = 18;

    /// <summary>Maximum number of results shown in the command palette.</summary>
    const int MaxCommandPaletteResults = 10;

    /// <summary>
    /// View instances — created once, reused across refreshes.
    /// Order must match the <c>WorkbenchNavigation.IndexXxx</c> constants.
    /// </summary>
    readonly IWorkbenchView[] _views =
    [
        new OverviewView(),         // 0 Overview
        new ObserversView(),        // 1 Observers
        new FailedPartitionsView(), // 2 Failures
        new JobsView(),             // 3 Jobs
        new RecommendationsView(),  // 4 Recommendations
        new EventSequencesView(),   // 5 Event Sequences
        new EventTypesView(),       // 6 Event Types
        new ProjectionsView(),      // 7 Projections
        new ReadModelsView(),       // 8 Read Models
        new EventStoresView(),      // 9 Event Stores
        new NamespacesView(),       // 10 Namespaces
        new ApplicationsView(),     // 11 Applications
        new UsersView(),            // 12 Users
        new IdentitiesView(),       // 13 Identities
        new SubscriptionsView(),    // 14 Subscriptions
    ];

    readonly object _dataLock = new();
    string? _activeEventStore;
    string? _activeNamespace;
    WorkbenchData? _currentData;
    bool _wasDisconnected;
    Window? _window;
    StatusBarControl? _statusBar;
    MarkupControl? _titleBar;

    WorkbenchActionHandler? _actionHandler;
    WorkbenchNavigation? _navigation;
    bool _sidebarExpanded = true;

    /// <summary>
    /// Builds the main window with all controls and the async update thread.
    /// </summary>
    /// <returns>The constructed <see cref="Window"/>.</returns>
    public Window Build()
    {
        _actionHandler = new WorkbenchActionHandler(text =>
        {
            if (string.IsNullOrEmpty(text))
            {
                UpdateStatusBar();
            }
            else
            {
                UpdateStatusRight(text);
            }
        });

        _navigation = new WorkbenchNavigation(
            windowSystem,
            _views,
            settings,
            () => _activeEventStore,
            () => _activeNamespace,
            storeName =>
            {
                _activeEventStore = storeName;
                _activeNamespace = null;
                SwitchToOverview();
                _ = Task.Run(() => FetchAndUpdate(CancellationToken.None));
            },
            nsName =>
            {
                _activeNamespace = nsName;
                _ = Task.Run(() => FetchAndUpdate(CancellationToken.None));
            },
            () => _ = Task.Run(() => FetchAndUpdate(CancellationToken.None)),
            () =>
            {
                lock (_dataLock)
                {
                    return _currentData;
                }
            });

        WireViewCallbacks();

        var navView = _navigation.BuildNavigationView();
        var statusBar = BuildStatusBar();

        _currentData = initialData;
        PushDataToViews(initialData);
        UpdateStatusBar(initialData);
        _navigation.UpdateNavBadges(initialData);

        var builtWindow = new WindowBuilder(windowSystem)
            .WithTitle(string.Empty)
            .Maximized()
            .WithColors(WorkbenchColors.Foreground, WorkbenchColors.Background)
            .Borderless() // cspell:ignore Borderless
            .HideTitle()
            .HideCloseButton()
            .AddControl(BuildTitleBar())
            .AddControl(navView)
            .AddControl(statusBar)
            .OnKeyPressed((_, e) => HandleKeyPress(e))
            .WithAsyncWindowThread(RunDataRefreshLoop)
            .Build();

        _window = builtWindow;

        // Restore the last active navigation item from the previous session.
        if (_navigation?.NavView is not null && state.LastNavIndex > 0 && state.LastNavIndex < _views.Length)
            _navigation.NavView.SelectedIndex = state.LastNavIndex;

        return builtWindow;
    }

    static string TruncateId(string s) => s.Length <= 40 ? s : s[..37] + "…";

    /// <summary>
    /// Extracts the host:port portion from a Chronicle connection string.
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

    void WireViewCallbacks()
    {
        if (_views[WorkbenchNavigation.IndexObservers] is ObserversView ov)
        {
            ov.OnReplay = obs => _actionHandler!.ExecuteAction(
                $"Replay observer '{TruncateId(obs.Id)}'",
                () => services.Observers.Replay(new Replay
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    ObserverId = obs.Id,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }));

            ov.OnReplayAll = observers => _actionHandler!.ConfirmThenExecuteAll(
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

        if (_views[WorkbenchNavigation.IndexFailures] is FailedPartitionsView fv)
        {
            fv.OnRetryPartition = fp => _actionHandler!.ExecuteAction(
                $"Retry partition '{fp.Partition}'",
                () => services.Observers.RetryPartition(new RetryPartition
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    ObserverId = fp.ObserverId,
                    Partition = fp.Partition,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }));

            fv.OnReplayPartition = fp => _actionHandler!.ExecuteAction(
                $"Replay partition '{fp.Partition}'",
                () => services.Observers.ReplayPartition(new ReplayPartition
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    ObserverId = fp.ObserverId,
                    Partition = fp.Partition,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }));

            fv.OnRetryAll = partitions => _actionHandler!.ConfirmThenExecuteAll(
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

            fv.OnReplayAll = partitions => _actionHandler!.ConfirmThenExecuteAll(
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

        if (_views[WorkbenchNavigation.IndexJobs] is JobsView jv)
        {
            jv.OnStopJob = job => _actionHandler!.ExecuteAction(
                $"Stop job '{TruncateId(job.Type ?? job.Id.ToString())}'",
                () => services.Jobs.Stop(new StopJob
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    JobId = job.Id
                }));

            jv.OnResumeJob = job => _actionHandler!.ExecuteAction(
                $"Resume job '{TruncateId(job.Type ?? job.Id.ToString())}'",
                () => services.Jobs.Resume(new ResumeJob
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    JobId = job.Id
                }));

            jv.OnStopAll = jobs => _actionHandler!.ConfirmThenExecuteAll(
                $"Stop {jobs.Count} job{(jobs.Count == 1 ? string.Empty : "s")}",
                jobs,
                job => services.Jobs.Stop(new StopJob
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    JobId = job.Id
                }));

            jv.OnResumeAll = jobs => _actionHandler!.ConfirmThenExecuteAll(
                $"Resume {jobs.Count} job{(jobs.Count == 1 ? string.Empty : "s")}",
                jobs,
                job => services.Jobs.Resume(new ResumeJob
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    JobId = job.Id
                }));
        }

        if (_views[WorkbenchNavigation.IndexRecommendations] is RecommendationsView rv)
        {
            rv.OnApply = rec => _actionHandler!.ExecuteAction(
                $"Apply recommendation '{TruncateId(rec.Name ?? rec.Id.ToString())}'",
                () => services.Recommendations.Perform(new Perform
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    RecommendationId = rec.Id
                }));

            rv.OnIgnore = rec => _actionHandler!.ExecuteAction(
                $"Ignore recommendation '{TruncateId(rec.Name ?? rec.Id.ToString())}'",
                () => services.Recommendations.Ignore(new Perform
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    RecommendationId = rec.Id
                }));

            rv.OnApplyAll = recs => _actionHandler!.ConfirmThenExecuteAll(
                $"Apply {recs.Count} recommendation{(recs.Count == 1 ? string.Empty : "s")}",
                recs,
                rec => services.Recommendations.Perform(new Perform
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    RecommendationId = rec.Id
                }));

            rv.OnIgnoreAll = recs => _actionHandler!.ConfirmThenExecuteAll(
                $"Ignore {recs.Count} recommendation{(recs.Count == 1 ? string.Empty : "s")}",
                recs,
                rec => services.Recommendations.Ignore(new Perform
                {
                    EventStore = _activeEventStore ?? settings.ResolveEventStore(),
                    Namespace = _activeNamespace ?? settings.ResolveNamespace(),
                    RecommendationId = rec.Id
                }));
        }

        if (_views[WorkbenchNavigation.IndexReadModels] is ReadModelsView rmv)
        {
            rmv.OnFetchInstances = async (containerName, ct) =>
                await dataService.FetchAsync(
                    _activeEventStore,
                    _activeNamespace,
                    readModelContainerName: containerName,
                    ct);
        }

        if (_views[WorkbenchNavigation.IndexEventStores] is EventStoresView esv)
        {
            esv.OnSwitch = storeName =>
            {
                _activeEventStore = storeName;
                _activeNamespace = null;
                SwitchToOverview();
                _ = Task.Run(() => FetchAndUpdate(CancellationToken.None));
            };
        }

        if (_views[WorkbenchNavigation.IndexNamespaces] is NamespacesView nsv)
        {
            nsv.OnSwitch = nsName =>
            {
                _activeNamespace = nsName;
                SwitchToOverview();
                _ = Task.Run(() => FetchAndUpdate(CancellationToken.None));
            };
        }

        if (_views[WorkbenchNavigation.IndexEventSequences] is EventSequencesView seqView)
        {
            seqView.OnViewEventTypeDefinition = evt =>
            {
                _navigation?.NavigateTo(WorkbenchNavigation.IndexEventTypes);
                if (_views[WorkbenchNavigation.IndexEventTypes] is EventTypesView etv)
                {
                    etv.SetFilter(evt.Context.EventType.Id);
                }
            };

            seqView.OnViewObserversForType = evt =>
            {
                _navigation?.NavigateTo(WorkbenchNavigation.IndexObservers);
                if (_views[WorkbenchNavigation.IndexObservers] is ObserversView ov)
                {
                    ov.SetFilter($"event:{evt.Context.EventType.Id}");
                }
            };
        }

        if (_views[WorkbenchNavigation.IndexEventTypes] is EventTypesView etView)
        {
            etView.OnViewObservers = reg =>
            {
                _navigation?.NavigateTo(WorkbenchNavigation.IndexObservers);
                if (_views[WorkbenchNavigation.IndexObservers] is ObserversView ov)
                {
                    ov.SetFilter($"event:{reg.Type.Id}");
                }
            };
        }

        foreach (var view in _views)
        {
            view.OnFilterFocusChanged = focused => _actionHandler!.TextInputFocused = focused;
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

    StatusBarControl BuildStatusBar()
    {
        var statusBar = Controls.StatusBar()
            .WithName("StatusBar")
            .StickyBottom()
            .AddLeft("F", "Filter", null)
            .AddLeft("?", "Help", () => OpenHelpOverlay())
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
            UpdateStatusRight($"[{WorkbenchColors.Muted.ToMarkup()}]↻ refreshing…[/]");

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
            _navigation?.UpdateNavBadges(data);

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
            // Swallow — connectivity errors shown in status bar via IsConnected.
        }
    }

    void PushDataToViews(WorkbenchData data)
    {
        foreach (var view in _views)
        {
            view.UpdateData(data);
        }
    }

    void UpdateStatusBar(WorkbenchData? data = null)
    {
        if (_statusBar is null)
        {
            return;
        }

        data ??= _currentData;
        if (data is null)
        {
            return;
        }

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
            $"{connDot}{seqText}  [{acc}]{eventStore}[/] [{mut}]/[/] [{acc}]{ns}[/]  [{mut}]↻{settings.Interval}s[/]",
            null);
    }

    void HandleKeyPress(KeyPressedEventArgs e)
    {
        if (_navigation?.NavView is null)
        {
            return;
        }

        if (_actionHandler!.HandlePendingKeyPress(e.KeyInfo, () => UpdateStatusBar()))
        {
            e.Handled = true;
            return;
        }

        if (_actionHandler!.TextInputFocused)
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                var idx = _navigation.CurrentViewIndex;
                if (idx >= 0 && idx < _views.Length)
                {
                    _views[idx].ClearFilter();
                }

                e.Handled = true;
            }

            return;
        }

        var navView = _navigation.NavView;
        switch (e.KeyInfo.Key)
        {
            case ConsoleKey.LeftArrow:
                FocusNavigation();
                e.Handled = true;
                break;

            case ConsoleKey.RightArrow:
                FocusContent();
                e.Handled = true;
                break;

            case ConsoleKey.B when e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control):
                ToggleSidebar();
                e.Handled = true;
                break;

            case ConsoleKey.Oem5 when e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control):
                ToggleDetailPane();
                e.Handled = true;
                break;

            case ConsoleKey.E when e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control):
                _navigation.OpenEventStorePicker();
                e.Handled = true;
                break;

            case ConsoleKey.N when e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control):
                _navigation.OpenNamespacePicker();
                e.Handled = true;
                break;

            case ConsoleKey.C when e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control):
                CopyDetailToClipboard();
                e.Handled = true;
                break;

            case ConsoleKey.Enter when _navigation.CurrentViewIndex == WorkbenchNavigation.IndexReadModels:
                OpenReadModelDetail();
                e.Handled = true;
                break;

            case ConsoleKey.D when _navigation.CurrentViewIndex == WorkbenchNavigation.IndexEventSequences:
                TriggerEventSequenceAction(seqView => seqView.OnViewEventTypeDefinition, seqView => seqView.SelectedEvent);
                e.Handled = true;
                break;

            case ConsoleKey.V when _navigation.CurrentViewIndex == WorkbenchNavigation.IndexEventSequences:
                TriggerEventSequenceAction(seqView => seqView.OnViewObserversForType, seqView => seqView.SelectedEvent);
                e.Handled = true;
                break;

            case ConsoleKey.V when _navigation.CurrentViewIndex == WorkbenchNavigation.IndexEventTypes:
                TriggerEventTypeAction(etv => etv.OnViewObservers, etv => etv.SelectedEventType);
                e.Handled = true;
                break;

            case ConsoleKey.Oem2 when e.KeyInfo.Modifiers == ConsoleModifiers.Shift:
                OpenHelpOverlay();
                e.Handled = true;
                break;

            case ConsoleKey.P when e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control):
                OpenCommandPalette();
                e.Handled = true;
                break;

            case ConsoleKey.Oem6: // ] — next page (Mac-friendly alternative to PageDown)
            case ConsoleKey.PageDown:
            {
                var idx = _navigation.CurrentViewIndex;
                if (idx >= 0 && idx < _views.Length) _views[idx].NextPage();
                e.Handled = true;
                break;
            }

            case ConsoleKey.Oem4: // [ — previous page (Mac-friendly alternative to PageUp)
            case ConsoleKey.PageUp:
            {
                var idx = _navigation.CurrentViewIndex;
                if (idx >= 0 && idx < _views.Length) _views[idx].PreviousPage();
                e.Handled = true;
                break;
            }

            case ConsoleKey.R when _navigation.CurrentViewIndex == WorkbenchNavigation.IndexObservers:
                TriggerSelectedObserverReplay();
                e.Handled = true;
                break;

            case ConsoleKey.T when _navigation.CurrentViewIndex == WorkbenchNavigation.IndexFailures:
                TriggerSelectedPartitionRetry();
                e.Handled = true;
                break;

            case ConsoleKey.P when _navigation.CurrentViewIndex == WorkbenchNavigation.IndexFailures:
                TriggerSelectedPartitionReplay();
                e.Handled = true;
                break;

            case ConsoleKey.S when _navigation.CurrentViewIndex == WorkbenchNavigation.IndexJobs:
                TriggerSelectedJobStop();
                e.Handled = true;
                break;

            case ConsoleKey.U when _navigation.CurrentViewIndex == WorkbenchNavigation.IndexJobs:
                TriggerSelectedJobResume();
                e.Handled = true;
                break;

            case ConsoleKey.A when _navigation.CurrentViewIndex == WorkbenchNavigation.IndexRecommendations:
                TriggerSelectedRecommendationApply();
                e.Handled = true;
                break;

            case ConsoleKey.I when _navigation.CurrentViewIndex == WorkbenchNavigation.IndexRecommendations:
                TriggerSelectedRecommendationIgnore();
                e.Handled = true;
                break;

            case ConsoleKey.F:
                ActivateCurrentFilter();
                e.Handled = true;
                break;

            case ConsoleKey.Q:
                state.Interval = settings.Interval;
                state.LastNavIndex = _navigation?.CurrentViewIndex ?? 0;
                state.Save();
                Environment.Exit(0);
                break;
        }
    }

    void OpenHelpOverlay()
    {
        var mut = WorkbenchColors.Muted.ToMarkup();
        var acc = WorkbenchColors.Accent.ToMarkup();

        var currentViewHelp = string.Empty;
        var activeIdx = _navigation?.CurrentViewIndex ?? -1;
        if (activeIdx >= 0 && activeIdx < _views.Length && !string.IsNullOrEmpty(_views[activeIdx].ViewHelp))
        {
            currentViewHelp =
                $"[bold {acc}]THIS VIEW[/]\n" +
                string.Join('\n', _views[activeIdx].ViewHelp.Split('\n').Select(l => $"  {l}")) +
                "\n\n";
        }

        var helpText =
            currentViewHelp +
            $"[bold {acc}]NAVIGATION[/]\n" +
            $"  [{mut}]↑ ↓[/]           Move selection\n" +
            $"  [{mut}]← / →[/]         Sidebar ↔ Content\n" +
            $"  [{mut}]Ctrl+B[/]         Toggle sidebar expand / compact\n" +
            $"  [{mut}]Ctrl+\\[/]         Toggle detail pane\n" +
            $"  [{mut}]Enter[/]          Open detail overlay\n" +
            $"  [{mut}]Esc[/]            Close overlay\n" +
            "\n" +
            $"[bold {acc}]PAGING[/]\n" +
            $"  [{mut}][ / ][/]          Previous / next page\n" +
            $"  [{mut}]◄ ► buttons[/]    Click to change page\n" +
            "\n" +
            $"[bold {acc}]QUICK SWITCH[/]\n" +
            $"  [{mut}]Ctrl+E[/]         Switch event store\n" +
            $"  [{mut}]Ctrl+N[/]         Switch namespace\n" +
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
            $"  [{mut}]D[/]              View event type definition (Event Sequences)\n" +
            $"  [{mut}]V[/]              View observers for event type\n" +
            $"  [{mut}]Y / N[/]          Confirm / Cancel action\n" +
            "\n" +
            $"[bold {acc}]CLIPBOARD[/]\n" +
            $"  [{mut}]Ctrl+C[/]         Copy detail pane content to clipboard\n" +
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
            .WithSize(HelpOverlayWidth, HelpOverlayHeight)
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

    void TriggerEventSequenceAction(Func<EventSequencesView, Action<AppendedEvent>?> getCallback, Func<EventSequencesView, AppendedEvent?> getItem)
    {
        if (_views[WorkbenchNavigation.IndexEventSequences] is not EventSequencesView esv)
        {
            return;
        }

        var item = getItem(esv);
        if (item is null)
        {
            ShowNoSelectionHint();
            return;
        }

        getCallback(esv)?.Invoke(item);
    }

    void TriggerEventTypeAction(Func<EventTypesView, Action<EventTypeRegistration>?> getCallback, Func<EventTypesView, EventTypeRegistration?> getItem)
    {
        if (_views[WorkbenchNavigation.IndexEventTypes] is not EventTypesView etv)
        {
            return;
        }

        var item = getItem(etv);
        if (item is null)
        {
            ShowNoSelectionHint();
            return;
        }

        getCallback(etv)?.Invoke(item);
    }

    void ShowNoSelectionHint()
    {
        UpdateStatusRight($"[{WorkbenchColors.Muted.ToMarkup()}]Select a row first[/]");
        _ = Task.Delay(2000).ContinueWith(_ => UpdateStatusBar(_currentData), TaskScheduler.Default);
    }

    void TriggerSelectedObserverReplay()
    {
        if (_views[WorkbenchNavigation.IndexObservers] is not ObserversView ov) return;
        var selected = ov.SelectedObserver;
        if (selected is null)
        {
            ShowNoSelectionHint();
            return;
        }
        ov.OnReplay?.Invoke(selected);
    }

    void TriggerSelectedPartitionRetry()
    {
        if (_views[WorkbenchNavigation.IndexFailures] is not FailedPartitionsView fv) return;
        var selected = fv.SelectedPartition;
        if (selected is null)
        {
            ShowNoSelectionHint();
            return;
        }
        fv.OnRetryPartition?.Invoke(selected);
    }

    void TriggerSelectedPartitionReplay()
    {
        if (_views[WorkbenchNavigation.IndexFailures] is not FailedPartitionsView fv) return;
        var selected = fv.SelectedPartition;
        if (selected is null)
        {
            ShowNoSelectionHint();
            return;
        }
        fv.OnReplayPartition?.Invoke(selected);
    }

    void TriggerSelectedJobStop()
    {
        if (_views[WorkbenchNavigation.IndexJobs] is not JobsView jv) return;
        var selected = jv.SelectedJob;
        if (selected is null)
        {
            ShowNoSelectionHint();
            return;
        }
        jv.OnStopJob?.Invoke(selected);
    }

    void TriggerSelectedJobResume()
    {
        if (_views[WorkbenchNavigation.IndexJobs] is not JobsView jv) return;
        var selected = jv.SelectedJob;
        if (selected is null)
        {
            ShowNoSelectionHint();
            return;
        }
        jv.OnResumeJob?.Invoke(selected);
    }

    void TriggerSelectedRecommendationApply()
    {
        if (_views[WorkbenchNavigation.IndexRecommendations] is not RecommendationsView rv) return;
        var selected = rv.SelectedRecommendation;
        if (selected is null)
        {
            ShowNoSelectionHint();
            return;
        }
        rv.OnApply?.Invoke(selected);
    }

    void TriggerSelectedRecommendationIgnore()
    {
        if (_views[WorkbenchNavigation.IndexRecommendations] is not RecommendationsView rv) return;
        var selected = rv.SelectedRecommendation;
        if (selected is null)
        {
            ShowNoSelectionHint();
            return;
        }
        rv.OnIgnore?.Invoke(selected);
    }

    void NavigateObservers() => _navigation?.NavigateTo(WorkbenchNavigation.IndexObservers);

    void NavigateEventTypes() => _navigation?.NavigateTo(WorkbenchNavigation.IndexEventTypes);

    void NavigateProjections() => _navigation?.NavigateTo(WorkbenchNavigation.IndexProjections);

    void NavigateReadModels() => _navigation?.NavigateTo(WorkbenchNavigation.IndexReadModels);

    void NavigateFailures() => _navigation?.NavigateTo(WorkbenchNavigation.IndexFailures);

    void OpenCommandPalette()
    {
        WorkbenchData? snapshot;
        lock (_dataLock)
        {
            snapshot = _currentData;
        }

        if (snapshot is null)
        {
            return;
        }

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
            .OnGotFocus((_, _) => _actionHandler!.TextInputFocused = true)
            .OnLostFocus((_, _) => _actionHandler!.TextInputFocused = false)
            .Build();

        void PopulateResults(string query)
        {
            resultsTable.ClearRows();

            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

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

            foreach (var (kind, label, navigate) in matches.Take(MaxCommandPaletteResults))
            {
                resultsTable.AddRow(new UITableRow([kind, label]) { Tag = navigate });
            }
        }

        searchPrompt.InputChanged += (_, text) => PopulateResults(text ?? string.Empty);

        Window? paletteWindow = null;
        paletteWindow = new WindowBuilder(windowSystem)
            .WithTitle(" Command Palette ")
            .WithColors(WorkbenchColors.Foreground, WorkbenchColors.Background)
            .WithSize(CommandPaletteWidth, CommandPaletteHeight)
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
        if (_window is null || _navigation?.NavView is null)
        {
            return;
        }

        _window.FocusControl(_navigation.NavView);
    }

    void FocusContent()
    {
        if (_window is null) return;

        var idx = _navigation?.CurrentViewIndex ?? -1;
        if (idx >= 0 && idx < _views.Length && _views[idx].PrimaryFocusTarget is IInteractiveControl ic)
        {
            _window.FocusControl(ic);
        }
        else
        {
            // No specific focus target for this view — do nothing.
        }
    }

    void ToggleSidebar()
    {
        if (_navigation?.NavView is null)
        {
            return;
        }

        _sidebarExpanded = !_sidebarExpanded;
        _navigation.NavView.PaneDisplayMode = _sidebarExpanded
            ? NavigationViewDisplayMode.Expanded
            : NavigationViewDisplayMode.Compact;
    }

    void ToggleDetailPane()
    {
        var idx = _navigation?.CurrentViewIndex ?? -1;
        if (idx >= 0 && idx < _views.Length)
        {
            _views[idx].ToggleDetailPane();
        }
    }

    void ActivateCurrentFilter()
    {
        var idx = _navigation?.CurrentViewIndex ?? -1;
        if (idx < 0 || idx >= _views.Length)
        {
            return;
        }

        var window = windowSystem.GetWindowAtPoint(new System.Drawing.Point(0, 0));
        if (window is null)
        {
            return;
        }

        _views[idx].ActivateFilter(window);
    }

    void CopyDetailToClipboard()
    {
        var idx = _navigation?.CurrentViewIndex ?? -1;
        if (idx < 0 || idx >= _views.Length)
        {
            return;
        }

        var content = _views[idx].DetailContent;
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        var plain = Markup.Remove(content);
        ClipboardHelper.SetText(plain);
        UpdateStatusRight($"[{WorkbenchColors.Success.ToMarkup()}]✓ Copied[/]");
        _ = Task.Delay(2000).ContinueWith(_ => UpdateStatusBar(_currentData), TaskScheduler.Default);
    }

    void SwitchToOverview() => _navigation?.NavigateTo(WorkbenchNavigation.IndexOverview);

    void OpenReadModelDetail()
    {
        if (_views[WorkbenchNavigation.IndexReadModels] is not ReadModelsView rmv)
        {
            return;
        }

        rmv.OpenSelectedDetailOverlay();
    }

    void UpdateStatusRight(string text)
    {
        if (_statusBar is null)
        {
            return;
        }

        _statusBar.ClearRight();
        _statusBar.AddRightText(text, null);
    }
}
