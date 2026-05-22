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
    readonly IWorkbenchView[] _views = WorkbenchViewRegistry.CreateViews();

    string? _activeEventStore;
    string? _activeNamespace;
    Window? _window;

    WorkbenchActionHandler? _actionHandler;
    WorkbenchNavigation? _navigation;
    WorkbenchRefreshLoop? _refreshLoop;
    WorkbenchOverlays? _overlays;

    /// <summary>
    /// Builds the main window, composes all workbench subsystems, and returns the ready-to-show window.
    /// </summary>
    /// <returns>The fully configured <see cref="Window"/>.</returns>
    public Window Build()
    {
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

        _refreshLoop = new WorkbenchRefreshLoop(
            dataService,
            settings,
            _views,
            _navigation,
            windowSystem,
            () => _activeEventStore,
            () => _activeNamespace);

        WireViewCallbacks();

        _overlays = new WorkbenchOverlays(windowSystem, _views, _navigation, _actionHandler, _refreshLoop);
        var overlays = _overlays;
        var keyDispatcher = new WorkbenchKeyDispatcher(
            _navigation, _views, _actionHandler, windowSystem, overlays, settings, state, () => _window);
        var menuBar = new WorkbenchMenuBar(_navigation, overlays, windowSystem, settings, state).Build();
        var navView = _navigation.BuildNavigationView();

        _refreshLoop.Initialize(initialData);

        var builtWindow = new WindowBuilder(windowSystem)
            .WithTitle(string.Empty)
            .Maximized()
            .WithColors(WorkbenchColors.Foreground, WorkbenchColors.Background)
            .WithBackgroundGradient(
                ColorGradient.FromColors([
                    WorkbenchColors.Background,
                    WorkbenchColors.Surface,
                    WorkbenchColors.Background
                ]),
                GradientDirection.DiagonalDown)
            .Borderless() // cspell:ignore Borderless
            .HideTitle()
            .HideCloseButton()
            .AddControl(menuBar)
            .AddControl(navView)
            .OnKeyPressed((_, e) => keyDispatcher.Dispatch(e))
            .WithAsyncWindowThread(_refreshLoop.RunAsync)
            .Build();

        _window = builtWindow;

        // The menu bar is the first added control so the window system gives it initial focus.
        // Move focus to the nav view so arrow keys, action keys, and shortcuts work immediately.
        _window.FocusControl(navView);

        if (state.LastNavIndex > 0 && state.LastNavIndex < _views.Length)
        {
            _navigation.NavigateTo(state.LastNavIndex);
        }

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
                _navigation!.NavigateTo(WorkbenchNavigation.IndexOverview);
                _ = Task.Run(() => _refreshLoop!.FetchAndUpdate(CancellationToken.None));
            };
        }

        if (_views[WorkbenchNavigation.IndexNamespaces] is NamespacesView nsv)
        {
            nsv.OnSwitch = nsName =>
            {
                _activeNamespace = nsName;
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
