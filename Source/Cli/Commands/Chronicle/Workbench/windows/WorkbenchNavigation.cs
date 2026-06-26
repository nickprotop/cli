// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Builds and manages the navigation side pane, badge counts, and the event store / namespace picker overlays.
/// All navigation items are driven by <see cref="WorkbenchViewRegistry"/> — add a view there and it appears here automatically.
/// </summary>
/// <param name="windowSystem">The SharpConsoleUI window system.</param>
/// <param name="theme">The workbench theme used to resolve chrome and section accent colors.</param>
/// <param name="views">The ordered array of <see cref="IWorkbenchView"/> instances — must match <see cref="WorkbenchViewRegistry.All"/> order.</param>
/// <param name="settings">Workbench settings — used to resolve the active event store and namespace.</param>
/// <param name="getActiveEventStore">Returns the currently active event store name, or <see langword="null"/> for the default.</param>
/// <param name="getActiveNamespace">Returns the currently active namespace name, or <see langword="null"/> for the default.</param>
/// <param name="onStoreSwitch">Invoked with the newly selected event store name when the user picks one from the picker overlay.</param>
/// <param name="onNamespaceSwitch">Invoked with the newly selected namespace name when the user picks one from the picker overlay.</param>
/// <param name="onDataNeeded">Invoked when navigation changes require a data refresh.</param>
/// <param name="getLatestData">Returns the latest cached <see cref="WorkbenchData"/> snapshot, or <see langword="null"/> if none available yet.</param>
public class WorkbenchNavigation(
    ConsoleWindowSystem windowSystem,
    WorkbenchTheme theme,
    IWorkbenchView[] views,
    WorkbenchSettings settings,
    Func<string?> getActiveEventStore,
    Func<string?> getActiveNamespace,
    Action<string> onStoreSwitch,
    Action<string> onNamespaceSwitch,
    Action onDataNeeded,
    Func<WorkbenchData?> getLatestData)
{
    // ── View index constants ───────────────────────────────────────────────────
    // Derived from WorkbenchViewRegistry — the registry position IS the index.
    // Never hardcode these values; never pass them to NavigationView.SelectedIndex directly.
    // Always navigate via NavigateTo(IndexXxx) which converts to the header-inclusive index.

    /// <summary>View index for Overview.</summary>
    public static readonly int IndexOverview = WorkbenchViewRegistry.IndexOf<OverviewView>();

    /// <summary>View index for Observers.</summary>
    public static readonly int IndexObservers = WorkbenchViewRegistry.IndexOf<ObserversView>();

    /// <summary>View index for Failures.</summary>
    public static readonly int IndexFailures = WorkbenchViewRegistry.IndexOf<FailedPartitionsView>();

    /// <summary>View index for Jobs.</summary>
    public static readonly int IndexJobs = WorkbenchViewRegistry.IndexOf<JobsView>();

    /// <summary>View index for Recommendations.</summary>
    public static readonly int IndexRecommendations = WorkbenchViewRegistry.IndexOf<RecommendationsView>();

    /// <summary>View index for Event Sequences.</summary>
    public static readonly int IndexEventSequences = WorkbenchViewRegistry.IndexOf<EventSequencesView>();

    /// <summary>View index for Event Types.</summary>
    public static readonly int IndexEventTypes = WorkbenchViewRegistry.IndexOf<EventTypesView>();

    /// <summary>View index for Projections.</summary>
    public static readonly int IndexProjections = WorkbenchViewRegistry.IndexOf<ProjectionsView>();

    /// <summary>View index for Read Models.</summary>
    public static readonly int IndexReadModels = WorkbenchViewRegistry.IndexOf<ReadModelsView>();

    /// <summary>View index for Event Stores.</summary>
    public static readonly int IndexEventStores = WorkbenchViewRegistry.IndexOf<EventStoresView>();

    /// <summary>View index for Namespaces.</summary>
    public static readonly int IndexNamespaces = WorkbenchViewRegistry.IndexOf<NamespacesView>();

    const int PickerOverlayWidth = 54;
    const int MaxPickerOverlayHeight = 24;
    const int PickerOverlayHeightPadding = 6;
    const int NavExpandedThreshold = 90;
    const int NavCompactThreshold = 40;

    /// <summary>Header items paired with their section accent, used to re-apply colors on theme change.</summary>
    readonly List<(NavigationItem Header, WorkbenchSectionAccent Accent)> _sectionHeaders = [];

    NavigationItem? _observersItem;
    NavigationItem? _failuresItem;
    NavigationItem? _recommendationsItem;
    NavigationView? _navView;
    int _currentViewIndex;

    /// <summary>Gets the built <see cref="NavigationView"/> control. Only available after <see cref="BuildNavigationView"/> has been called.</summary>
    public NavigationView? NavView => _navView;

    /// <summary>
    /// Gets the zero-based item-only index of the currently active view.
    /// Excludes header entries, aligns with <c>IndexXxx</c> constants, and can be used directly to index into <c>views[]</c>.
    /// </summary>
    public int CurrentViewIndex => _currentViewIndex;

    /// <summary>Gets the Observers navigation item (used to set badge counts). Only available after <see cref="BuildNavigationView"/>.</summary>
    public NavigationItem? ObserversItem => _observersItem;

    /// <summary>Gets the Failures navigation item (used to set badge counts). Only available after <see cref="BuildNavigationView"/>.</summary>
    public NavigationItem? FailuresItem => _failuresItem;

    /// <summary>Gets the Recommendations navigation item (used to set badge counts). Only available after <see cref="BuildNavigationView"/>.</summary>
    public NavigationItem? RecommendationsItem => _recommendationsItem;

    /// <summary>
    /// Builds the navigation view from <see cref="WorkbenchViewRegistry"/> — headers and items are derived automatically.
    /// Wires the selection-changed callback and captures the badge item references.
    /// </summary>
    /// <returns>The fully configured <see cref="NavigationView"/>.</returns>
    public NavigationView BuildNavigationView()
    {
        // Declare navView before the builder chain so the lambda can capture it.
        // It will be null when OnSelectedItemChanged fires during the initial build-time
        // auto-selection; the guard below handles that case gracefully.
        NavigationView? navView = null;

        navView = Controls.NavigationView()
            .WithNavWidth(28)
            .WithPaneHeader($"[bold {theme.Accent.ToMarkup()}] ◆ CHRONICLE[/]")
            .WithContentBorder(BorderStyle.Rounded)
            .WithContentPadding(1, 0, 1, 0)
            .WithPaneDisplayMode(NavigationViewDisplayMode.Auto)
            .WithExpandedThreshold(NavExpandedThreshold)
            .WithCompactThreshold(NavCompactThreshold)
            .WithName("MainNav")
            .Fill()
            .OnSelectedItemChanged((_, e) =>
            {
                // navView may be null while Build() is still executing (first auto-selection).
                // In that case _currentViewIndex stays at its default of 0 (Overview), which is correct.
                if (navView is null)
                {
                    return;
                }

                var oldViewIdx = ToViewIndex(navView, e.OldIndex);
                if (oldViewIdx >= 0 && oldViewIdx < views.Length)
                {
                    views[oldViewIdx].IsActive = false;
                }

                var idx = ToViewIndex(navView, e.NewIndex);
                _currentViewIndex = idx >= 0 ? idx : 0;

                if (idx < 0 || idx >= views.Length)
                {
                    return;
                }

                var snapshot = getLatestData();
                if (snapshot is not null)
                {
                    views[idx].UpdateData(snapshot);
                }
                else
                {
                    onDataNeeded();
                }

                views[idx].IsActive = true;
            })
            .Build();

        // Add headers and items driven entirely by the registry.
        // Adding a new view to WorkbenchViewRegistry.All automatically adds it here.
        WorkbenchSection? lastSection = null;
        NavigationItem? currentHeader = null;

        for (var i = 0; i < WorkbenchViewRegistry.All.Count; i++)
        {
            var def = WorkbenchViewRegistry.All[i];
            var viewIndex = i;

            if (!ReferenceEquals(def.Section, lastSection))
            {
                currentHeader = navView!.AddHeader(def.Section.Title, theme.SectionAccent(def.Section.Accent));
                _sectionHeaders.Add((currentHeader, def.Section.Accent));
                lastSection = def.Section;
            }

            var navItem = navView!.AddItemToHeader(currentHeader!, def.NavText, def.NavIcon, def.NavSubtitle);
            navView.SetItemContent(navItem, panel => views[viewIndex].PopulateContent(panel, windowSystem));
        }

        var allItems = navView!.Items;
        _observersItem = FindItemByText(allItems, WorkbenchViewRegistry.All[IndexObservers].NavText);
        _failuresItem = FindItemByText(allItems, WorkbenchViewRegistry.All[IndexFailures].NavText);
        _recommendationsItem = FindItemByText(allItems, WorkbenchViewRegistry.All[IndexRecommendations].NavText);

        _navView = navView;

        theme.Changed += ApplyThemeColors;

        // Guard: registry count must equal views.Length — they're both built from the same registry.
        var nonHeaderCount = allItems.Count(i => i.ItemType != NavigationItemType.Header);
        System.Diagnostics.Debug.Assert(
            nonHeaderCount == views.Length,
            $"Nav has {nonHeaderCount} selectable items but _views has {views.Length}. Both must match {nameof(WorkbenchViewRegistry)}.");

        return navView;
    }

    /// <summary>Navigates to the specified view by index. No-op when the index is out of range.</summary>
    /// <param name="viewIndex">Zero-based item-only view index (use <c>IndexXxx</c> constants).</param>
    public void NavigateTo(int viewIndex)
    {
        if (_navView is null || viewIndex < 0 || viewIndex >= views.Length)
        {
            return;
        }

        var navIndex = ToNavIndex(_navView, viewIndex);
        if (navIndex >= 0)
        {
            _navView.SelectedIndex = navIndex;
        }
    }

    /// <summary>Updates the badge subtitles on the Observers, Failures, and Recommendations nav items.</summary>
    /// <param name="data">The latest workbench data snapshot.</param>
    public void UpdateNavBadges(WorkbenchData data)
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

    /// <summary>Opens a modal picker that lets the user select a different event store.</summary>
    public void OpenEventStorePicker()
    {
        var snapshot = getLatestData();
        if (snapshot is null)
        {
            return;
        }

        ShowStringPickerOverlay(
            " Switch Event Store ",
            "Event Store",
            "EventStorePickerTable",
            [.. snapshot.EventStoreNames.Order()],
            getActiveEventStore() ?? settings.ResolveEventStore(),
            onStoreSwitch);
    }

    /// <summary>Opens a modal picker that lets the user select a different namespace.</summary>
    public void OpenNamespacePicker()
    {
        var snapshot = getLatestData();
        if (snapshot is null)
        {
            return;
        }

        ShowStringPickerOverlay(
            " Switch Namespace ",
            "Namespace",
            "NamespacePickerTable",
            [.. snapshot.NamespaceNames.Order()],
            getActiveNamespace() ?? settings.ResolveNamespace(),
            onNamespaceSwitch);
    }

    /// <summary>
    /// Converts a header-inclusive NavigationView item index to a zero-based view-only index.
    /// Takes the nav view directly so it works inside lambdas before <c>_navView</c> is assigned.
    /// Returns -1 for header entries or out-of-range indices.
    /// </summary>
    /// <param name="nav">The NavigationView to query.</param>
    /// <param name="navIndex">The header-inclusive index from the NavigationView.</param>
    /// <returns>The zero-based view-only index, or -1 if not applicable.</returns>
    static int ToViewIndex(NavigationView nav, int navIndex)
    {
        if (navIndex < 0)
        {
            return -1;
        }

        var items = nav.Items;
        if (navIndex >= items.Count || items[navIndex].ItemType == NavigationItemType.Header)
        {
            return -1;
        }

        var viewIdx = 0;
        for (var i = 0; i < navIndex; i++)
        {
            if (items[i].ItemType != NavigationItemType.Header)
            {
                viewIdx++;
            }
        }

        return viewIdx;
    }

    /// <summary>
    /// Converts a zero-based view-only index to the header-inclusive NavigationView item index
    /// required by <see cref="NavigationView.SelectedIndex"/>.
    /// Returns -1 when the view index is not found.
    /// </summary>
    /// <param name="nav">The NavigationView to query.</param>
    /// <param name="viewIndex">The zero-based view-only index.</param>
    /// <returns>The header-inclusive NavigationView index, or -1 if not found.</returns>
    static int ToNavIndex(NavigationView nav, int viewIndex)
    {
        if (viewIndex < 0)
        {
            return -1;
        }

        var items = nav.Items;
        var itemCount = 0;
        for (var navIdx = 0; navIdx < items.Count; navIdx++)
        {
            if (items[navIdx].ItemType != NavigationItemType.Header)
            {
                if (itemCount == viewIndex)
                {
                    return navIdx;
                }

                itemCount++;
            }
        }

        return -1;
    }

    static NavigationItem? FindItemByText(IReadOnlyList<NavigationItem> items, string text)
    {
        foreach (var item in items)
        {
            if (item.Text == text)
            {
                return item;
            }
        }

        return null;
    }

    void ApplyThemeColors()
    {
        foreach (var (header, accent) in _sectionHeaders)
        {
            header.HeaderColor = theme.SectionAccent(accent);
        }

        _navView?.Invalidate();
    }

    void ShowStringPickerOverlay(
        string title,
        string columnHeader,
        string tableName,
        List<string> items,
        string activeItem,
        Action<string> onSelected)
    {
        var acc = theme.Accent.ToMarkup();

        var pickerTable = Controls.Table()
            .AddColumn(columnHeader, SharpConsoleUI.Layout.TextJustification.Left, null)
            .Interactive()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithName(tableName)
            .Build();

        foreach (var name in items)
        {
            var label = name == activeItem ? $"[{acc}]► {name}[/]" : name;
            pickerTable.AddRow(new UITableRow([label]) { Tag = name });
        }

        Window? picker = null;
        var height = Math.Min(items.Count + PickerOverlayHeightPadding, MaxPickerOverlayHeight);
        picker = new WindowBuilder(windowSystem)
            .WithTitle(title)
            .WithSize(PickerOverlayWidth, height)
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
            if (pickerTable.SelectedRow?.Tag is string selected)
            {
                windowSystem.CloseWindow(picker, activateParent: true, force: false);
                onSelected(selected);
            }
        };

        windowSystem.AddWindow(picker, activateWindow: true);
    }
}
