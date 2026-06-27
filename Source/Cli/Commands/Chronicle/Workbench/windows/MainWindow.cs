// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Jobs;
using Cratis.Chronicle.Contracts.Observation;
using Cratis.Chronicle.Contracts.Recommendations;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Rendering;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Composition root for the Chronicle Workbench TUI — creates and wires all subsystems, then builds the main window.
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
    /// <summary>How much to darken the main window (0..1) while a modal dialog is open.</summary>
    const float ModalDimIntensity = 0.85f;

    readonly IWorkbenchView[] _views = WorkbenchViewRegistry.CreateViews();

    string? _activeEventStore;
    string? _activeNamespace;
    Window? _window;

    WorkbenchActionHandler? _actionHandler;
    WorkbenchNavigation? _navigation;
    WorkbenchRefreshLoop? _refreshLoop;
    WorkbenchOverlays? _overlays;
    WorkbenchKeyDispatcher? _keyDispatcher;

    /// <summary>Gets the active event store — the user-selected one, or the configured default.</summary>
    string ActiveEventStore => _activeEventStore ?? settings.ResolveEventStore();

    /// <summary>Gets the active namespace — the user-selected one, or the configured default.</summary>
    string ActiveNamespace => _activeNamespace ?? settings.ResolveNamespace();

    /// <summary>
    /// Builds the main window, composes all workbench subsystems, and returns the ready-to-show window.
    /// </summary>
    /// <returns>The fully configured <see cref="Window"/>.</returns>
    public Window Build()
    {
        var theme = new WorkbenchTheme(windowSystem);

        _actionHandler = new WorkbenchActionHandler(
            windowSystem,
            text =>
            {
                if (string.IsNullOrEmpty(text))
                    _refreshLoop?.UpdateTopPanel();
                else
                    windowSystem.PanelStateService.TopStatus = text;
            });

        _navigation = new WorkbenchNavigation(
            windowSystem,
            theme,
            _views,
            settings,
            () => _activeEventStore,
            () => _activeNamespace,
            storeName =>
            {
                _activeEventStore = storeName;
                _activeNamespace = null;
                _navigation!.NavigateTo(WorkbenchNavigation.IndexOverview);
                _ = Task.Run(() => _refreshLoop!.FetchAndUpdate(CancellationToken.None));
            },
            nsName =>
            {
                _activeNamespace = nsName;
                _navigation!.NavigateTo(WorkbenchNavigation.IndexOverview);
                _ = Task.Run(() => _refreshLoop!.FetchAndUpdate(CancellationToken.None));
            },
            () => _ = Task.Run(() => _refreshLoop!.FetchAndUpdate(CancellationToken.None)),
            () => _refreshLoop?.CurrentData);

        // The status-bar key hints are clickable. The actions resolve the overlays/dispatcher lazily
        // because those subsystems are constructed just below, after the status bar.
        var statusBarHelper = new WorkbenchStatusBar(
            theme,
            onQuit: () => _keyDispatcher?.Quit(),
            onPalette: () => _overlays?.OpenCommandPalette(),
            onHelp: () => _overlays?.OpenHelpOverlay(),
            onFilter: () => _keyDispatcher?.ActivateCurrentFilter());

        _refreshLoop = new WorkbenchRefreshLoop(
            dataService,
            settings,
            _views,
            _navigation,
            windowSystem,
            () => _activeEventStore,
            () => _activeNamespace,
            statusBarHelper);

        WireViewCallbacks();

        _overlays = new WorkbenchOverlays(windowSystem, _views, _navigation, _actionHandler, _refreshLoop);
        var overlays = _overlays;
        var keyDispatcher = new WorkbenchKeyDispatcher(
            _navigation,
            _views,
            _actionHandler,
            windowSystem,
            overlays,
            settings,
            state,
            () => _window,
            () => _refreshLoop?.UpdateStatusBar());
        _keyDispatcher = keyDispatcher;
        var menuBar = new WorkbenchMenuBar(_navigation, overlays, windowSystem, () => _keyDispatcher?.Quit()).Build();
        var navView = _navigation.BuildNavigationView();

        _refreshLoop.Initialize(initialData);

        var bg = theme.Background;
        var builtWindow = new WindowBuilder(windowSystem)
            .WithTitle(string.Empty)
            .Maximized()
            .WithBackgroundGradient(
                ColorGradient.FromColors([
                    bg.Tint(0.10),
                    bg,
                    bg.Shade(0.35)
                ]),
                GradientDirection.Vertical)
            .WithBorderStyle(BorderStyle.Rounded)
            .WithBorderColor(theme.DimAccent)
            .HideTitle()
            .HideTitleButtons()
            .HideCloseButton()
            .Movable(false)
            .Resizable(false)
            .Minimizable(false)
            .Maximizable(false)
            .AddControl(menuBar)
            .AddControl(navView)
            .AddControl(statusBarHelper.Control)
            .OnKeyPressed((_, e) => keyDispatcher.Dispatch(e))
            .WithAsyncWindowThread(_refreshLoop.RunAsync)
            .Build();

        // Keep the window border in sync with the theme (a dimmed accent), re-applying on theme change
        // since an explicit border color is pinned and would otherwise not follow F9/F10/F11.
        theme.Changed += () =>
        {
            builtWindow.ActiveBorderForegroundColor = theme.DimAccent;
            builtWindow.InactiveBorderForegroundColor = theme.DimAccent;
        };

        // Dim the main window while a modal dialog is open, so the dialog reads as the focus. The
        // post-paint hook darkens the rendered buffer; a modal open/close forces a repaint so the dim
        // appears and clears immediately.
        builtWindow.PostBufferPaint += (buffer, _, _) =>
        {
            if (windowSystem.ModalStateService.HasModals)
            {
                ColorBlendHelper.ApplyColorOverlay(buffer, SharpConsoleUI.Color.Black, ModalDimIntensity);
            }
        };

        windowSystem.ModalStateService.StateChanged += (_, _) => builtWindow.Invalidate(true);

        _window = builtWindow;

        // Register the main window with overlays so the command palette portal can use
        // CreatePortal/RemovePortal and PreviewKeyPressed for key interception.
        _overlays.SetWindow(builtWindow);

        // The menu bar is the first added control so the window system gives it initial focus.
        // Move focus to the nav view so arrow keys, action keys, and shortcuts work immediately.
        _window.FocusControl(navView);

        // Activate the saved view (Overview / index 0 included) so its content is populated on start;
        // fall back to Overview when the saved index is out of range.
        var startIndex = state.LastNavIndex >= 0 && state.LastNavIndex < _views.Length
            ? state.LastNavIndex
            : WorkbenchNavigation.IndexOverview;
        _navigation.NavigateTo(startIndex);

        return builtWindow;
    }

    static string TruncateId(string s) => s.Length <= 40 ? s : s[..37] + "…";

    void OpenObserversForEventTypeOverlay(string eventTypeId)
    {
        var snapshot = _refreshLoop?.CurrentData;
        if (snapshot is null || _overlays is null)
        {
            return;
        }

        var matching = snapshot.Observers
            .Where(o => (o.EventTypes ?? []).Any(et =>
                string.Equals(et.Id, eventTypeId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        _overlays.OpenObserversForEventType(
            eventTypeId,
            matching,
            obs =>
            {
                _navigation!.NavigateTo(WorkbenchNavigation.IndexObservers);
                if (_views[WorkbenchNavigation.IndexObservers] is ObserversView ov)
                {
                    ov.SetFilter(obs.Id);
                }
            });
    }

    void WireViewCallbacks()
    {
        if (_views[WorkbenchNavigation.IndexObservers] is ObserversView ov)
        {
            ov.OnReplay = obs => _actionHandler!.ExecuteAction(
                $"Replay observer '{TruncateId(obs.Id)}'",
                () => services.Observers.Replay(new Replay
                {
                    EventStore = ActiveEventStore,
                    Namespace = ActiveNamespace,
                    ObserverId = obs.Id,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }));

            ov.OnReplayAll = observers => _actionHandler!.ConfirmThenExecuteAll(
                $"Replay {observers.Count} observer{(observers.Count == 1 ? string.Empty : "s")}",
                observers,
                obs => services.Observers.Replay(new Replay
                {
                    EventStore = ActiveEventStore,
                    Namespace = ActiveNamespace,
                    ObserverId = obs.Id,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }),
                obs => obs.Id);
        }

        if (_views[WorkbenchNavigation.IndexFailures] is FailedPartitionsView fv)
        {
            fv.OnRetryPartition = fp => _actionHandler!.ExecuteAction(
                $"Retry partition '{fp.Partition}'",
                () => services.Observers.RetryPartition(new RetryPartition
                {
                    EventStore = ActiveEventStore,
                    Namespace = ActiveNamespace,
                    ObserverId = fp.ObserverId,
                    Partition = fp.Partition,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }));

            fv.OnReplayPartition = fp => _actionHandler!.ExecuteAction(
                $"Replay partition '{fp.Partition}'",
                () => services.Observers.ReplayPartition(new ReplayPartition
                {
                    EventStore = ActiveEventStore,
                    Namespace = ActiveNamespace,
                    ObserverId = fp.ObserverId,
                    Partition = fp.Partition,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }));

            fv.OnRetryAll = partitions => _actionHandler!.ConfirmThenExecuteAll(
                $"Retry {partitions.Count} partition{(partitions.Count == 1 ? string.Empty : "s")}",
                partitions,
                fp => services.Observers.RetryPartition(new RetryPartition
                {
                    EventStore = ActiveEventStore,
                    Namespace = ActiveNamespace,
                    ObserverId = fp.ObserverId,
                    Partition = fp.Partition,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }),
                fp => $"{fp.ObserverId}/{fp.Partition}");

            fv.OnReplayAll = partitions => _actionHandler!.ConfirmThenExecuteAll(
                $"Replay {partitions.Count} partition{(partitions.Count == 1 ? string.Empty : "s")}",
                partitions,
                fp => services.Observers.ReplayPartition(new ReplayPartition
                {
                    EventStore = ActiveEventStore,
                    Namespace = ActiveNamespace,
                    ObserverId = fp.ObserverId,
                    Partition = fp.Partition,
                    EventSequenceId = CliDefaults.DefaultEventSequenceId
                }),
                fp => $"{fp.ObserverId}/{fp.Partition}");
        }

        if (_views[WorkbenchNavigation.IndexJobs] is JobsView jv)
        {
            jv.OnStopJob = job => _actionHandler!.ExecuteAction(
                $"Stop job '{TruncateId(job.Type ?? job.Id.ToString())}'",
                () => services.Jobs.Stop(new StopJob
                {
                    EventStore = ActiveEventStore,
                    Namespace = ActiveNamespace,
                    JobId = job.Id
                }));

            jv.OnResumeJob = job => _actionHandler!.ExecuteAction(
                $"Resume job '{TruncateId(job.Type ?? job.Id.ToString())}'",
                () => services.Jobs.Resume(new ResumeJob
                {
                    EventStore = ActiveEventStore,
                    Namespace = ActiveNamespace,
                    JobId = job.Id
                }));

            jv.OnStopAll = jobs => _actionHandler!.ConfirmThenExecuteAll(
                $"Stop {jobs.Count} job{(jobs.Count == 1 ? string.Empty : "s")}",
                jobs,
                job => services.Jobs.Stop(new StopJob
                {
                    EventStore = ActiveEventStore,
                    Namespace = ActiveNamespace,
                    JobId = job.Id
                }),
                job => job.Type ?? job.Id.ToString());

            jv.OnResumeAll = jobs => _actionHandler!.ConfirmThenExecuteAll(
                $"Resume {jobs.Count} job{(jobs.Count == 1 ? string.Empty : "s")}",
                jobs,
                job => services.Jobs.Resume(new ResumeJob
                {
                    EventStore = ActiveEventStore,
                    Namespace = ActiveNamespace,
                    JobId = job.Id
                }),
                job => job.Type ?? job.Id.ToString());
        }

        if (_views[WorkbenchNavigation.IndexRecommendations] is RecommendationsView rv)
        {
            rv.OnApply = rec => _actionHandler!.ExecuteAction(
                $"Apply recommendation '{TruncateId(rec.Name ?? rec.Id.ToString())}'",
                () => services.Recommendations.Perform(new Perform
                {
                    EventStore = ActiveEventStore,
                    Namespace = ActiveNamespace,
                    RecommendationId = rec.Id
                }));

            rv.OnIgnore = rec => _actionHandler!.ExecuteAction(
                $"Ignore recommendation '{TruncateId(rec.Name ?? rec.Id.ToString())}'",
                () => services.Recommendations.Ignore(new Perform
                {
                    EventStore = ActiveEventStore,
                    Namespace = ActiveNamespace,
                    RecommendationId = rec.Id
                }));

            rv.OnApplyAll = recs => _actionHandler!.ConfirmThenExecuteAll(
                $"Apply {recs.Count} recommendation{(recs.Count == 1 ? string.Empty : "s")}",
                recs,
                rec => services.Recommendations.Perform(new Perform
                {
                    EventStore = ActiveEventStore,
                    Namespace = ActiveNamespace,
                    RecommendationId = rec.Id
                }),
                rec => rec.Name ?? rec.Id.ToString());

            rv.OnIgnoreAll = recs => _actionHandler!.ConfirmThenExecuteAll(
                $"Ignore {recs.Count} recommendation{(recs.Count == 1 ? string.Empty : "s")}",
                recs,
                rec => services.Recommendations.Ignore(new Perform
                {
                    EventStore = ActiveEventStore,
                    Namespace = ActiveNamespace,
                    RecommendationId = rec.Id
                }),
                rec => rec.Name ?? rec.Id.ToString());
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
                windowSystem.ToastService.Show($"Switched to event store '{storeName}'", SharpConsoleUI.Core.NotificationSeverity.Success);
                _navigation!.NavigateTo(WorkbenchNavigation.IndexOverview);
                _ = Task.Run(() => _refreshLoop!.FetchAndUpdate(CancellationToken.None));
            };
        }

        if (_views[WorkbenchNavigation.IndexNamespaces] is NamespacesView nsv)
        {
            nsv.OnSwitch = nsName =>
            {
                _activeNamespace = nsName;
                windowSystem.ToastService.Show($"Switched to namespace '{nsName}'", SharpConsoleUI.Core.NotificationSeverity.Success);
                _navigation!.NavigateTo(WorkbenchNavigation.IndexOverview);
                _ = Task.Run(() => _refreshLoop!.FetchAndUpdate(CancellationToken.None));
            };
        }

        if (_views[WorkbenchNavigation.IndexEventSequences] is EventSequencesView seqView)
        {
            seqView.OnViewEventTypeDefinition = evt =>
                _overlays?.OpenEventTypeDefinition(
                    evt.Context.EventType.Id,
                    _refreshLoop?.CurrentData);

            seqView.OnViewObserversForType = evt =>
                OpenObserversForEventTypeOverlay(evt.Context.EventType.Id);
        }

        if (_views[WorkbenchNavigation.IndexEventTypes] is EventTypesView etView)
        {
            etView.OnViewObservers = reg =>
                OpenObserversForEventTypeOverlay(reg.Type.Id);
        }

        foreach (var view in _views)
        {
            view.OnFilterFocusChanged = focused => _actionHandler!.TextInputFocused = focused;
        }
    }
}
