// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json;
using Cratis.Chronicle.Contracts.Jobs;
using Cratis.Cli.Commands.Chronicle.ReadModels;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Interactive TUI workbench — a live-updating dashboard with full drill-down navigation and in-place
/// actions for observers, failed partitions, jobs, recommendations, event log, event types, and projections.
/// </summary>
[LlmDescription("Opens an interactive full-screen TUI workbench for the Chronicle server. Navigate with ← → or 1–8, drill into items with Enter, go back with Escape. Not suitable for scripting.")]
[CliCommand("workbench", "Open the interactive Chronicle workbench (live TUI dashboard)", Branch = typeof(ChronicleBranch))]
[CliExample("chronicle", "workbench")]
[CliExample("chronicle", "workbench", "--interval", "10")]
[CliExample("chronicle", "workbench", "-e", "my-event-store")]
public class WorkbenchCommand : ChronicleCommand<WorkbenchSettings>
{
    /// <summary>Number of primary <see cref="WorkbenchView"/> values (values &lt; 100) — must be kept in sync with the enum.</summary>
    const int ViewCount = 11;

    /// <summary>Number of events displayed per page in the Event Log view.</summary>
    const int EventLogPageSize = 50;

    /// <summary>Total events fetched from the server for the Event Log — determines how many pages are available.</summary>
    const int EventLogFetchWindow = 500;

    static readonly JsonSerializerOptions _instanceJsonOptions = new() { WriteIndented = true };

    /// <summary>Nav stack — only touched by the input task (HandleKey). Render loop uses snapshot fields.</summary>
    readonly Stack<NavFrame> _navStack = new();

    volatile int _currentView;
    volatile int _selectedIndex;
    volatile int _actionState;
    volatile int _scrollOffset;
    volatile int _isActionError;
    volatile int _filterInputMode;
    volatile int _eventLogAscending;
    volatile int _eventLogPage;

    /// <summary>
    /// Fired by the input task whenever the user presses a key; wakes the render loop immediately
    /// rather than waiting for the full refresh interval to elapse.
    /// </summary>
    TaskCompletionSource _keyPressSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    PendingAction? _pendingAction;
    string _actionResult = string.Empty;
    string _focusedId = string.Empty;
    string _filter = string.Empty;
    string? _activeEventStore;
    string? _activeNamespace;
    IReadOnlyList<string> _breadcrumb = [];
    IServices? _services;
    WorkbenchData? _lastData;

    /// <inheritdoc/>
    protected override bool UseStatusSpinner => false;

    /// <inheritdoc/>
    protected override async Task<int> ExecuteCommandAsync(IServices services, WorkbenchSettings settings, string format)
    {
        if (!string.Equals(format, OutputFormats.Table, StringComparison.Ordinal))
        {
            OutputFormatter.WriteError(
                format,
                "The workbench requires table output format",
                "Remove -o/--output or use --output table",
                ExitCodes.ValidationErrorCode);
            return ExitCodes.ValidationError;
        }

        _services = services;

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var inputTask = Task.Run(
            async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var pressedKey = Console.ReadKey(intercept: true);
                        HandleKey(pressedKey, settings, cts);
                    }

                    try
                    {
                        await Task.Delay(50, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            },
            cts.Token);

        var data = WorkbenchData.Loading(settings);

        await AnsiConsole.Live(WorkbenchRenderer.Build(data, LoadingState(settings)))
            .StartAsync(async ctx =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Interlocked.Exchange(ref _keyPressSignal, signal);

                    ctx.UpdateTarget(WorkbenchRenderer.Build(data, RenderState(settings, isRefreshing: true)));
                    ctx.Refresh();

                    data = await FetchData(services, settings, _activeEventStore, _activeNamespace, (WorkbenchView)_currentView, _focusedId, cts.Token);
                    _lastData = data;

                    ctx.UpdateTarget(WorkbenchRenderer.Build(data, RenderState(settings, isRefreshing: false)));
                    ctx.Refresh();

                    try
                    {
                        await Task.WhenAny(
                            Task.Delay(TimeSpan.FromSeconds(settings.Interval), cts.Token),
                            signal.Task);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (cts.Token.IsCancellationRequested) break;

                    ctx.UpdateTarget(WorkbenchRenderer.Build(data, RenderState(settings, isRefreshing: true)));
                    ctx.Refresh();
                }
            });

        try
        {
            await inputTask;
        }
        catch (OperationCanceledException)
        {
        }

        AnsiConsole.MarkupLine($"  [{OutputFormatter.Muted.ToMarkup()}]Workbench closed.[/]");
        return ExitCodes.Success;
    }

    static async Task<WorkbenchData> FetchData(
        IServices services,
        WorkbenchSettings settings,
        string? activeEventStore,
        string? activeNamespace,
        WorkbenchView currentView,
        string focusedId,
        CancellationToken ct)
    {
        var eventStore = activeEventStore ?? settings.ResolveEventStore();
        var ns = activeNamespace ?? settings.ResolveNamespace();
        var connectionString = settings.ResolveConnectionString();

        string? serverVersion = null;
        var isConnected = true;

        try
        {
            var versionInfo = await services.Server.GetVersionInfo();
            serverVersion = versionInfo.Version;
        }
        catch
        {
            isConnected = false;
        }

        var eventStoreNames = new List<string>();
        try { eventStoreNames = [.. await services.EventStores.GetEventStores()]; }
        catch { }

        IReadOnlyList<ObserverInformation> observers = [];
        try
        {
            observers = [.. await services.Observers.GetObservers(new AllObserversRequest
            {
                EventStore = eventStore,
                Namespace = ns
            })];
        }
        catch { }

        IReadOnlyList<FailedPartition> failedPartitions = [];
        try
        {
            failedPartitions = [.. await services.FailedPartitions.GetFailedPartitions(new GetFailedPartitionsRequest
            {
                EventStore = eventStore,
                Namespace = ns
            })];
        }
        catch { }

        IReadOnlyList<Job> jobs = [];
        try
        {
            jobs = [.. (await services.Jobs.GetJobs(new GetJobsRequest
            {
                EventStore = eventStore,
                Namespace = ns
            })) ?? []];
        }
        catch { }

        IReadOnlyList<Recommendation> recommendations = [];
        try
        {
            recommendations = [.. await services.Recommendations.GetRecommendations(new GetRecommendationsRequest
            {
                EventStore = eventStore,
                Namespace = ns
            })];
        }
        catch { }

        ulong? tailSequenceNumber = null;
        try
        {
            var tail = await services.EventSequences.GetTailSequenceNumber(new GetTailSequenceNumberRequest
            {
                EventStore = eventStore,
                Namespace = ns,
                EventSequenceId = CliDefaults.DefaultEventSequenceId
            });
            tailSequenceNumber = tail.SequenceNumber == ulong.MaxValue ? null : tail.SequenceNumber;
        }
        catch { }

        IReadOnlyList<EventTypeRegistration> eventTypeRegistrations = [];
        try
        {
            eventTypeRegistrations = [.. await services.EventTypes.GetAllRegistrations(
                new GetAllEventTypesRequest { EventStore = eventStore })];
        }
        catch { }

        IReadOnlyList<ProjectionDefinition> projectionDefinitions = [];
        try
        {
            projectionDefinitions = [.. await services.Projections.GetAllDefinitions(
                new GetAllDefinitionsRequest { EventStore = eventStore })];
        }
        catch { }

        var projectionDeclarations = new Dictionary<string, string>();
        try
        {
            var declarations = await services.Projections.GetAllDeclarations(
                new GetAllDeclarationsRequest { EventStore = eventStore });
            foreach (var d in declarations)
            {
                projectionDeclarations[d.Identifier] = d.Declaration ?? string.Empty;
            }
        }
        catch { }

        IReadOnlyList<AppendedEvent> recentEvents = [];
        try
        {
            if (tailSequenceNumber > 0)
            {
                var fromSeq = tailSequenceNumber.Value >= EventLogFetchWindow
                    ? tailSequenceNumber.Value - EventLogFetchWindow + 1
                    : 0;
                var eventsResp = await services.EventSequences.GetEventsFromEventSequenceNumber(
                    new GetFromEventSequenceNumberRequest
                    {
                        EventStore = eventStore,
                        Namespace = ns,
                        EventSequenceId = CliDefaults.DefaultEventSequenceId,
                        FromEventSequenceNumber = fromSeq
                    });
                recentEvents = [.. eventsResp.Events.OrderByDescending(e => e.Context.SequenceNumber)];
            }
        }
        catch { }

        IReadOnlyList<WorkbenchReadModel> readModelDefinitions = [];
        try
        {
            var defs = await services.ReadModels.GetDefinitions(new GetDefinitionsRequest { EventStore = eventStore });
            readModelDefinitions = [.. defs.ReadModels.Select(rm => new WorkbenchReadModel(
                rm.ContainerName,
                rm.DisplayName,
                rm.Owner.ToString(),
                !string.Equals(rm.Owner.ToString(), "Client", StringComparison.Ordinal),
                rm.Source.ToString(),
                rm.Type?.Identifier ?? string.Empty))];
        }
        catch { }

        IReadOnlyList<string> namespaceNames = [];
        try { namespaceNames = [.. await services.Namespaces.GetNamespaces(new GetNamespacesRequest { EventStore = eventStore })]; }
        catch { }

        IReadOnlyList<string> readModelInstances = [];
        var readModelInstancesTotalCount = 0;
        string? readModelInstancesError = null;
        if (currentView == WorkbenchView.ReadModelDetail && !string.IsNullOrEmpty(focusedId))
        {
            try
            {
                var instResp = await services.ReadModels.GetInstances(new GetInstancesRequest
                {
                    EventStore = eventStore,
                    Namespace = ns,
                    ReadModel = focusedId,
                    Page = 0,
                    PageSize = 20
                });
                readModelInstancesTotalCount = (int)Math.Min(instResp.TotalCount, int.MaxValue);
                readModelInstances = [.. (instResp.Instances ?? [])
                    .Select(ReadModelJsonCleaner.CleanInstance)
                    .Where(o => o is not null)
                    .Select(o => JsonSerializer.Serialize(o, _instanceJsonOptions))];
            }
            catch (Exception ex)
            {
                readModelInstancesError = ex.Message;
            }
        }

        return new WorkbenchData(
            ConnectionString: connectionString,
            EventStore: eventStore,
            Namespace: ns,
            IsConnected: isConnected,
            ServerVersion: serverVersion,
            EventStoreNames: eventStoreNames,
            Observers: observers,
            FailedPartitions: failedPartitions,
            Jobs: jobs,
            Recommendations: recommendations,
            TailSequenceNumber: tailSequenceNumber,
            CapturedAt: DateTimeOffset.Now,
            FetchError: null,
            EventTypeRegistrations: eventTypeRegistrations,
            ProjectionDefinitions: projectionDefinitions,
            ProjectionDeclarations: projectionDeclarations,
            RecentEvents: recentEvents,
            ReadModelDefinitions: readModelDefinitions,
            NamespaceNames: namespaceNames,
            ReadModelInstances: readModelInstances,
            ReadModelInstancesTotalCount: readModelInstancesTotalCount,
            ReadModelInstancesError: readModelInstancesError);
    }

    static int ObserverSortOrder(ObserverInformation o) => o.RunningState switch
    {
        ObserverRunningState.Disconnected => 0,
        ObserverRunningState.Replaying => 1,
        ObserverRunningState.Active => 2,
        ObserverRunningState.Suspended => 3,
        _ => 4
    };

    static string GetViewLabel(WorkbenchView view) => view switch
    {
        WorkbenchView.Overview => "Overview",
        WorkbenchView.Observers => "Observers",
        WorkbenchView.FailedPartitions => "Failures",
        WorkbenchView.Jobs => "Jobs",
        WorkbenchView.Recommendations => "Recommendations",
        WorkbenchView.EventLog => "Event Log",
        WorkbenchView.EventTypes => "Event Types",
        WorkbenchView.Projections => "Projections",
        WorkbenchView.ReadModels => "Read Models",
        WorkbenchView.EventStores => "Event Stores",
        WorkbenchView.Namespaces => "Namespaces",
        WorkbenchView.ObserverDetail => "Observer",
        WorkbenchView.FailedPartitionDetail => "Failure",
        WorkbenchView.EventDetail => "Event",
        WorkbenchView.EventTypeDetail => "Event Type",
        WorkbenchView.ProjectionDetail => "Projection",
        WorkbenchView.ReadModelDetail => "Read Model",
        _ => string.Empty
    };

    static WorkbenchRenderState LoadingState(WorkbenchSettings settings) =>
        new(WorkbenchView.Overview, 0, settings.Interval, true, WorkbenchActionState.None, null, null);

    static string TruncateForPrompt(string s) => s.Length <= 60 ? s : s[..57] + "…";

    static bool HasSubNavigation(WorkbenchView view) => view == WorkbenchView.ObserverDetail;

    static bool IsFilterableView(WorkbenchView view) =>
        view is WorkbenchView.Observers or WorkbenchView.EventTypes
            or WorkbenchView.EventLog or WorkbenchView.Projections
            or WorkbenchView.ReadModels;

    int GetMaxSelectedIndex()
    {
        var view = (WorkbenchView)_currentView;
        var data = _lastData;
        if (data is null) return 0;
        if (view == WorkbenchView.EventLog)
        {
            var pageStart = _eventLogPage * EventLogPageSize;
            return Math.Max(0, Math.Min(EventLogPageSize, data.RecentEvents.Count - pageStart) - 1);
        }
        return Math.Max(0, view switch
        {
            WorkbenchView.Observers => data.Observers.Count - 1,
            WorkbenchView.FailedPartitions => data.FailedPartitions.Count - 1,
            WorkbenchView.Jobs => data.Jobs.Count - 1,
            WorkbenchView.Recommendations => data.Recommendations.Count - 1,
            WorkbenchView.EventTypes => data.EventTypeRegistrations.Count - 1,
            WorkbenchView.Projections => data.ProjectionDefinitions.Count - 1,
            WorkbenchView.ReadModels => data.ReadModelDefinitions.Count - 1,
            WorkbenchView.EventStores => data.EventStoreNames.Count - 1,
            WorkbenchView.Namespaces => data.NamespaceNames.Count - 1,
            WorkbenchView.ObserverDetail => Math.Max(0, (data.Observers
                .FirstOrDefault(o => o.Id == _focusedId)?.EventTypes?.Count() ?? 1) - 1),
            _ => 0
        });
    }

    WorkbenchRenderState RenderState(WorkbenchSettings settings, bool isRefreshing) =>
        new(
            View: (WorkbenchView)_currentView,
            SelectedIndex: _selectedIndex,
            Interval: settings.Interval,
            IsRefreshing: isRefreshing,
            ActionState: (WorkbenchActionState)_actionState,
            PendingActionDescription: _pendingAction?.Description,
            ActionResult: _actionResult,
            IsActionError: _isActionError != 0,
            FocusedId: _focusedId,
            ScrollOffset: _scrollOffset,
            Breadcrumb: _breadcrumb,
            FilterText: _filter,
            FilterInputMode: _filterInputMode != 0,
            EventLogAscending: _eventLogAscending != 0,
            EventLogPage: _eventLogPage);

    ObserverInformation? GetSelectedObserver() =>
        _lastData?.Observers
            .OrderBy(ObserverSortOrder)
            .ThenBy(o => o.Id)
            .Skip(_selectedIndex)
            .FirstOrDefault();

    ObserverInformation? FindObserverById(string id) =>
        _lastData?.Observers.FirstOrDefault(o => o.Id == id);

    FailedPartition? GetSelectedFailedPartition() =>
        _lastData?.FailedPartitions
            .OrderByDescending(fp => fp.Attempts.Count())
            .Skip(_selectedIndex)
            .FirstOrDefault();

    FailedPartition? FindFailedPartitionByFocusedId(string focusedId)
    {
        var sep = focusedId.IndexOf('/');
        if (sep < 0) return null;
        var obsId = focusedId[..sep];
        var partition = focusedId[(sep + 1)..];
        return _lastData?.FailedPartitions
            .FirstOrDefault(fp => fp.ObserverId == obsId && fp.Partition == partition);
    }

    Job? GetSelectedJob() =>
        _lastData?.Jobs
            .OrderBy(j => j.Status.ToString())
            .Skip(_selectedIndex)
            .FirstOrDefault();

    Recommendation? GetSelectedRecommendation() =>
        _lastData?.Recommendations
            .Skip(_selectedIndex)
            .FirstOrDefault();

    AppendedEvent? GetSelectedEvent() =>
        _lastData?.RecentEvents
            .Skip(_selectedIndex)
            .FirstOrDefault();

    EventTypeRegistration? GetSelectedEventTypeRegistration() =>
        _lastData?.EventTypeRegistrations
            .OrderBy(r => r.Type.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Type.Generation)
            .Skip(_selectedIndex)
            .FirstOrDefault();

    ProjectionDefinition? GetSelectedProjectionDefinition() =>
        _lastData?.ProjectionDefinitions
            .OrderBy(d => d.Identifier, StringComparer.OrdinalIgnoreCase)
            .Skip(_selectedIndex)
            .FirstOrDefault();

    WorkbenchReadModel? GetSelectedReadModel() =>
        _lastData?.ReadModelDefinitions
            .OrderBy(d => d.ContainerName, StringComparer.OrdinalIgnoreCase)
            .Skip(_selectedIndex)
            .FirstOrDefault();

    string? GetSelectedEventStoreName() =>
        _lastData?.EventStoreNames
            .Order(StringComparer.OrdinalIgnoreCase)
            .Skip(_selectedIndex)
            .FirstOrDefault();

    string? GetSelectedNamespaceName() =>
        _lastData?.NamespaceNames
            .Order(StringComparer.OrdinalIgnoreCase)
            .Skip(_selectedIndex)
            .FirstOrDefault();

    void PushNav()
    {
        _navStack.Push(new NavFrame(_currentView, _selectedIndex, _focusedId));
    }

    List<string> BuildBreadcrumb()
    {
        if (_navStack.Count == 0) return [];
        var path = new List<string>();
        foreach (var frame in _navStack.Reverse())
        {
            path.Add(GetViewLabel((WorkbenchView)frame.View));
        }

        if (!string.IsNullOrEmpty(_focusedId)) path.Add(TruncateForPrompt(_focusedId));
        return path;
    }

    void NavigateToDetail(WorkbenchView detailView, string focusedId)
    {
        PushNav();
        _focusedId = focusedId;
        _currentView = (int)detailView;
        _selectedIndex = 0;
        _scrollOffset = 0;
        _breadcrumb = BuildBreadcrumb();
    }

    void NavigateBack()
    {
        var frame = _navStack.Pop();
        _currentView = frame.View;
        _selectedIndex = frame.SelectedIndex;
        _focusedId = frame.FocusedId;
        _scrollOffset = 0;
        _breadcrumb = BuildBreadcrumb();
    }

    void SetPendingAction(PendingAction action)
    {
        _pendingAction = action;
        _actionState = (int)WorkbenchActionState.AwaitingConfirmation;
        _keyPressSignal.TrySetResult();
    }

    async Task ExecuteActionAsync(CancellationToken ct)
    {
        var action = _pendingAction;
        if (action is null) return;

        _isActionError = 0;
        _actionState = (int)WorkbenchActionState.Executing;
        _keyPressSignal.TrySetResult();

        try
        {
            await action.Execute(ct);
            _actionResult = action.SuccessMessage;
        }
        catch (OperationCanceledException)
        {
            _actionState = (int)WorkbenchActionState.None;
            return;
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            _actionResult = msg.Length > 100 ? msg[..100] + "…" : msg;
            _isActionError = 1;
        }

        _pendingAction = null;
        _actionState = (int)WorkbenchActionState.Completed;
        _keyPressSignal.TrySetResult();
    }

    void HandleKey(ConsoleKeyInfo keyInfo, WorkbenchSettings settings, CancellationTokenSource cts)
    {
        var key = keyInfo.Key;
        var actionState = (WorkbenchActionState)_actionState;
        var view = (WorkbenchView)_currentView;

        // When a completed action is showing its result, any key dismisses it.
        if (actionState == WorkbenchActionState.Completed)
        {
            _actionState = (int)WorkbenchActionState.None;
            _actionResult = string.Empty;
            _keyPressSignal.TrySetResult();
            return;
        }

        // While executing, ignore all input except quit.
        if (actionState == WorkbenchActionState.Executing)
        {
            if (key == ConsoleKey.Q) cts.Cancel();
            return;
        }

        // Confirmation prompt — only Y/N/Escape are valid.
        if (actionState == WorkbenchActionState.AwaitingConfirmation)
        {
            switch (key)
            {
                case ConsoleKey.Y:
                    _ = Task.Run(() => ExecuteActionAsync(cts.Token), cts.Token);
                    break;
                case ConsoleKey.N:
                case ConsoleKey.Escape:
                    _pendingAction = null;
                    _actionState = (int)WorkbenchActionState.None;
                    _keyPressSignal.TrySetResult();
                    break;
            }

            return;
        }

        var isDetail = (int)view >= 100;

        // Filter input mode — route all input to the filter until Enter or Escape exits it.
        if (_filterInputMode != 0)
        {
            switch (key)
            {
                case ConsoleKey.Escape:
                    _filterInputMode = 0;
                    _filter = string.Empty;
                    _selectedIndex = 0;
                    break;
                case ConsoleKey.Enter:
                    _filterInputMode = 0;
                    break;
                case ConsoleKey.Backspace:
                    if (!string.IsNullOrEmpty(_filter))
                        _filter = _filter[..^1];
                    break;
                case ConsoleKey.UpArrow:
                    _selectedIndex = Math.Max(0, _selectedIndex - 1);
                    break;
                case ConsoleKey.DownArrow:
                    _selectedIndex++;
                    break;
                default:
                    if (keyInfo.KeyChar >= ' ')
                    {
                        _filter += keyInfo.KeyChar;
                        _selectedIndex = 0;
                    }
                    break;
            }
            _keyPressSignal.TrySetResult();
            return;
        }

        switch (key)
        {
            // Primary view number keys — always clear the nav stack and jump directly.
            case ConsoleKey.D1:
                _navStack.Clear();
                _currentView = (int)WorkbenchView.Overview;
                _selectedIndex = 0;
                _scrollOffset = 0;
                _focusedId = string.Empty;
                _filter = string.Empty;
                _breadcrumb = [];
                break;
            case ConsoleKey.D2:
                _navStack.Clear();
                _currentView = (int)WorkbenchView.Observers;
                _selectedIndex = 0;
                _scrollOffset = 0;
                _focusedId = string.Empty;
                _filter = string.Empty;
                _breadcrumb = [];
                break;
            case ConsoleKey.D3:
                _navStack.Clear();
                _currentView = (int)WorkbenchView.FailedPartitions;
                _selectedIndex = 0;
                _scrollOffset = 0;
                _focusedId = string.Empty;
                _filter = string.Empty;
                _breadcrumb = [];
                break;
            case ConsoleKey.D4:
                _navStack.Clear();
                _currentView = (int)WorkbenchView.Jobs;
                _selectedIndex = 0;
                _scrollOffset = 0;
                _focusedId = string.Empty;
                _filter = string.Empty;
                _breadcrumb = [];
                break;
            case ConsoleKey.D5:
                _navStack.Clear();
                _currentView = (int)WorkbenchView.Recommendations;
                _selectedIndex = 0;
                _scrollOffset = 0;
                _focusedId = string.Empty;
                _filter = string.Empty;
                _breadcrumb = [];
                break;
            case ConsoleKey.D6:
                _navStack.Clear();
                _currentView = (int)WorkbenchView.EventLog;
                _selectedIndex = 0;
                _scrollOffset = 0;
                _focusedId = string.Empty;
                _filter = string.Empty;
                _eventLogPage = 0;
                _breadcrumb = [];
                break;
            case ConsoleKey.D7:
                _navStack.Clear();
                _currentView = (int)WorkbenchView.EventTypes;
                _selectedIndex = 0;
                _scrollOffset = 0;
                _focusedId = string.Empty;
                _filter = string.Empty;
                _breadcrumb = [];
                break;
            case ConsoleKey.D8:
                _navStack.Clear();
                _currentView = (int)WorkbenchView.Projections;
                _selectedIndex = 0;
                _scrollOffset = 0;
                _focusedId = string.Empty;
                _filter = string.Empty;
                _filterInputMode = 0;
                _breadcrumb = [];
                break;
            case ConsoleKey.D9:
                _navStack.Clear();
                _currentView = (int)WorkbenchView.ReadModels;
                _selectedIndex = 0;
                _scrollOffset = 0;
                _focusedId = string.Empty;
                _filter = string.Empty;
                _filterInputMode = 0;
                _breadcrumb = [];
                break;
            case ConsoleKey.D0:
                _navStack.Clear();
                _currentView = (int)WorkbenchView.EventStores;
                _selectedIndex = 0;
                _scrollOffset = 0;
                _focusedId = string.Empty;
                _filter = string.Empty;
                _filterInputMode = 0;
                _breadcrumb = [];
                break;

            // Left/right — cycle through primary views only (detail views excluded).
            case ConsoleKey.LeftArrow when !isDetail:
                _currentView = (_currentView - 1 + ViewCount) % ViewCount;
                _selectedIndex = 0;
                _scrollOffset = 0;
                _filter = string.Empty;
                _filterInputMode = 0;
                _eventLogPage = 0;
                break;
            case ConsoleKey.RightArrow when !isDetail:
                _currentView = (_currentView + 1) % ViewCount;
                _selectedIndex = 0;
                _scrollOffset = 0;
                _filter = string.Empty;
                _filterInputMode = 0;
                _eventLogPage = 0;
                break;

            // Up/down — navigate list in primary views and sub-navigation detail views; scroll in other detail views.
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                if (isDetail && !HasSubNavigation(view))
                    _scrollOffset = Math.Max(0, _scrollOffset - 1);
                else
                    _selectedIndex = Math.Max(0, _selectedIndex - 1);
                break;
            case ConsoleKey.DownArrow:
                if (isDetail && !HasSubNavigation(view))
                    _scrollOffset = Math.Min(2000, _scrollOffset + 1);
                else
                    _selectedIndex = Math.Min(GetMaxSelectedIndex(), _selectedIndex + 1);
                break;

            // Enter — drill into detail view for selected item.
            case ConsoleKey.Enter:
                HandleEnterKey(settings);
                return;  // HandleEnterKey fires signal

            // Escape — clear filter if active; otherwise pop nav stack or reset selection.
            case ConsoleKey.Escape:
                if (!string.IsNullOrEmpty(_filter))
                {
                    _filter = string.Empty;
                    _selectedIndex = 0;
                }
                else if (_navStack.Count > 0)
                {
                    NavigateBack();
                }
                else
                {
                    _selectedIndex = 0;
                    _scrollOffset = 0;
                }

                break;

            // Backspace — remove last filter character when a filter is active.
            case ConsoleKey.Backspace:
                if (!string.IsNullOrEmpty(_filter))
                {
                    _filter = _filter[..^1];
                    _selectedIndex = 0;
                }

                break;

            // Interval adjustment.
            case ConsoleKey.OemPlus:
            case ConsoleKey.Add:
                settings.Interval = Math.Min(60, settings.Interval + 1);
                break;
            case ConsoleKey.OemMinus:
            case ConsoleKey.Subtract:
                settings.Interval = Math.Max(1, settings.Interval - 1);
                break;

            case ConsoleKey.Q:
                cts.Cancel();
                return;

            // --- In-view action keys ---
            case ConsoleKey.R when view == WorkbenchView.Observers:
            case ConsoleKey.R when view == WorkbenchView.ObserverDetail:
            {
                var obs = view == WorkbenchView.ObserverDetail
                    ? FindObserverById(_focusedId)
                    : GetSelectedObserver();
                if (obs is not null)
                {
                    SetPendingAction(new PendingAction(
                        $"Replay observer '{TruncateForPrompt(obs.Id)}'",
                        $"Replay started for observer '{TruncateForPrompt(obs.Id)}'",
                        ct => _services!.Observers.Replay(new Replay
                        {
                            EventStore = settings.ResolveEventStore(),
                            Namespace = settings.ResolveNamespace(),
                            ObserverId = obs.Id,
                            EventSequenceId = CliDefaults.DefaultEventSequenceId
                        })));
                }

                return;
            }

            case ConsoleKey.T when view == WorkbenchView.FailedPartitions:
            case ConsoleKey.T when view == WorkbenchView.FailedPartitionDetail:
            {
                var fp = view == WorkbenchView.FailedPartitionDetail
                    ? FindFailedPartitionByFocusedId(_focusedId)
                    : GetSelectedFailedPartition();
                if (fp is not null)
                {
                    SetPendingAction(new PendingAction(
                        $"Retry partition '{fp.Partition}' of '{TruncateForPrompt(fp.ObserverId)}'",
                        $"Retry started for partition '{fp.Partition}'",
                        ct => _services!.Observers.RetryPartition(new RetryPartition
                        {
                            EventStore = settings.ResolveEventStore(),
                            Namespace = settings.ResolveNamespace(),
                            ObserverId = fp.ObserverId,
                            Partition = fp.Partition,
                            EventSequenceId = CliDefaults.DefaultEventSequenceId
                        })));
                }

                return;
            }

            case ConsoleKey.P when view == WorkbenchView.FailedPartitions:
            case ConsoleKey.P when view == WorkbenchView.FailedPartitionDetail:
            {
                var fp = view == WorkbenchView.FailedPartitionDetail
                    ? FindFailedPartitionByFocusedId(_focusedId)
                    : GetSelectedFailedPartition();
                if (fp is not null)
                {
                    SetPendingAction(new PendingAction(
                        $"Replay partition '{fp.Partition}' of '{TruncateForPrompt(fp.ObserverId)}'",
                        $"Replay started for partition '{fp.Partition}'",
                        ct => _services!.Observers.ReplayPartition(new ReplayPartition
                        {
                            EventStore = settings.ResolveEventStore(),
                            Namespace = settings.ResolveNamespace(),
                            ObserverId = fp.ObserverId,
                            Partition = fp.Partition,
                            EventSequenceId = CliDefaults.DefaultEventSequenceId
                        })));
                }

                return;
            }

            // In ObserverDetail, P navigates to the projection definition for projection-type observers.
            case ConsoleKey.P when view == WorkbenchView.ObserverDetail:
            {
                var obs = FindObserverById(_focusedId);
                if (obs is not null && _lastData is not null
                    && obs.Type.ToString().Contains("Projection", StringComparison.OrdinalIgnoreCase))
                {
                    var projDef = _lastData.ProjectionDefinitions
                        .FirstOrDefault(d => string.Equals(d.Identifier, obs.Id, StringComparison.OrdinalIgnoreCase));
                    if (projDef is not null)
                        NavigateToDetail(WorkbenchView.ProjectionDetail, projDef.Identifier);
                }

                break;
            }

            case ConsoleKey.S when view == WorkbenchView.Jobs:
            {
                var job = GetSelectedJob();
                if (job is not null)
                {
                    SetPendingAction(new PendingAction(
                        $"Stop job '{TruncateForPrompt(job.Type ?? job.Id.ToString())}'",
                        "Job stopped",
                        ct => _services!.Jobs.Stop(new StopJob
                        {
                            EventStore = settings.ResolveEventStore(),
                            Namespace = settings.ResolveNamespace(),
                            JobId = job.Id
                        })));
                }

                return;
            }

            case ConsoleKey.U when view == WorkbenchView.Jobs:
            {
                var job = GetSelectedJob();
                if (job is not null)
                {
                    SetPendingAction(new PendingAction(
                        $"Resume job '{TruncateForPrompt(job.Type ?? job.Id.ToString())}'",
                        "Job resumed",
                        ct => _services!.Jobs.Resume(new ResumeJob
                        {
                            EventStore = settings.ResolveEventStore(),
                            Namespace = settings.ResolveNamespace(),
                            JobId = job.Id
                        })));
                }

                return;
            }

            case ConsoleKey.A when view == WorkbenchView.Recommendations:
            {
                var rec = GetSelectedRecommendation();
                if (rec is not null)
                {
                    SetPendingAction(new PendingAction(
                        $"Apply recommendation '{TruncateForPrompt(rec.Name ?? rec.Id.ToString())}'",
                        "Recommendation applied",
                        ct => _services!.Recommendations.Perform(new Perform
                        {
                            EventStore = settings.ResolveEventStore(),
                            Namespace = settings.ResolveNamespace(),
                            RecommendationId = rec.Id
                        })));
                }

                return;
            }

            case ConsoleKey.I when view == WorkbenchView.Recommendations:
            {
                var rec = GetSelectedRecommendation();
                if (rec is not null)
                {
                    SetPendingAction(new PendingAction(
                        $"Ignore recommendation '{TruncateForPrompt(rec.Name ?? rec.Id.ToString())}'",
                        "Recommendation ignored",
                        ct => _services!.Recommendations.Ignore(new Perform
                        {
                            EventStore = settings.ResolveEventStore(),
                            Namespace = settings.ResolveNamespace(),
                            RecommendationId = rec.Id
                        })));
                }

                return;
            }

            // In EventDetail, T navigates to the event type detail view.
            case ConsoleKey.T when view == WorkbenchView.EventDetail:
                var evtForType = _lastData?.RecentEvents
                    .FirstOrDefault(e => e.Context.SequenceNumber.ToString() == _focusedId);
                var etToNav = evtForType?.Context.EventType;
                if (etToNav is not null)
                    NavigateToDetail(WorkbenchView.EventTypeDetail, $"{etToNav.Id}+{etToNav.Generation}");
                break;

            // Event Log sort order toggle — also resets to page 0.
            case ConsoleKey.S when view == WorkbenchView.EventLog:
                _eventLogAscending = 1 - _eventLogAscending;
                _eventLogPage = 0;
                _selectedIndex = 0;
                break;

            // Event Log paging — PageDown goes to older events, PageUp goes to newer.
            case ConsoleKey.PageDown when view == WorkbenchView.EventLog:
                var totalEvents = _lastData?.RecentEvents.Count ?? 0;
                var maxPage = Math.Max(0, (totalEvents - 1) / EventLogPageSize);
                _eventLogPage = Math.Min(maxPage, _eventLogPage + 1);
                _selectedIndex = 0;
                break;

            case ConsoleKey.PageUp when view == WorkbenchView.EventLog:
                _eventLogPage = Math.Max(0, _eventLogPage - 1);
                _selectedIndex = 0;
                break;

            // Quick navigation to context-switching views from any primary view.
            case ConsoleKey.E when !isDetail:
                _navStack.Clear();
                _currentView = (int)WorkbenchView.EventStores;
                _selectedIndex = 0;
                _scrollOffset = 0;
                _filter = string.Empty;
                _filterInputMode = 0;
                _focusedId = string.Empty;
                _breadcrumb = [];
                break;
            case ConsoleKey.N when !isDetail:
                _navStack.Clear();
                _currentView = (int)WorkbenchView.Namespaces;
                _selectedIndex = 0;
                _scrollOffset = 0;
                _filter = string.Empty;
                _filterInputMode = 0;
                _focusedId = string.Empty;
                _breadcrumb = [];
                break;

            // '/' activates inline filter mode in filterable views.
            default:
                if (!isDetail && IsFilterableView(view) && keyInfo.KeyChar == '/')
                {
                    _filterInputMode = 1;
                    _filter = string.Empty;
                    _selectedIndex = 0;
                }

                break;
        }

        _keyPressSignal.TrySetResult();
    }

    void HandleEnterKey(WorkbenchSettings settings)
    {
        _filter = string.Empty;

        switch ((WorkbenchView)_currentView)
        {
            case WorkbenchView.Observers:
                if (GetSelectedObserver() is { } obs)
                    NavigateToDetail(WorkbenchView.ObserverDetail, obs.Id);
                break;

            case WorkbenchView.ObserverDetail:
                var focusedObs = FindObserverById(_focusedId);
                var eventTypes = (focusedObs?.EventTypes ?? [])
                    .OrderBy(et => et.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (_selectedIndex < eventTypes.Count)
                {
                    var et = eventTypes[_selectedIndex];
                    NavigateToDetail(WorkbenchView.EventTypeDetail, $"{et.Id}+{et.Generation}");
                }

                break;

            case WorkbenchView.FailedPartitions:
                if (GetSelectedFailedPartition() is { } fp)
                    NavigateToDetail(WorkbenchView.FailedPartitionDetail, $"{fp.ObserverId}/{fp.Partition}");
                break;

            case WorkbenchView.EventLog:
                if (GetSelectedEvent() is { } evt)
                    NavigateToDetail(WorkbenchView.EventDetail, evt.Context.SequenceNumber.ToString());
                break;

            case WorkbenchView.EventTypes:
                if (GetSelectedEventTypeRegistration() is { } et2)
                    NavigateToDetail(WorkbenchView.EventTypeDetail, $"{et2.Type.Id}+{et2.Type.Generation}");
                break;

            case WorkbenchView.Projections:
                if (GetSelectedProjectionDefinition() is { } proj)
                    NavigateToDetail(WorkbenchView.ProjectionDetail, proj.Identifier);
                break;

            case WorkbenchView.ReadModels:
                if (GetSelectedReadModel() is { } rm)
                    NavigateToDetail(WorkbenchView.ReadModelDetail, rm.ContainerName);
                break;

            case WorkbenchView.EventStores:
                var storeName = GetSelectedEventStoreName();
                if (storeName is not null)
                {
                    _activeEventStore = storeName;
                    _activeNamespace = null;
                    _navStack.Clear();
                    _currentView = (int)WorkbenchView.Overview;
                    _selectedIndex = 0;
                    _filter = string.Empty;
                    _filterInputMode = 0;
                    _focusedId = string.Empty;
                    _breadcrumb = [];
                }
                break;

            case WorkbenchView.Namespaces:
                var nsName = GetSelectedNamespaceName();
                if (nsName is not null)
                {
                    _activeNamespace = nsName;
                    _navStack.Clear();
                    _currentView = (int)WorkbenchView.Overview;
                    _selectedIndex = 0;
                    _filter = string.Empty;
                    _filterInputMode = 0;
                    _focusedId = string.Empty;
                    _breadcrumb = [];
                }
                break;
        }

        _keyPressSignal.TrySetResult();
    }
}
