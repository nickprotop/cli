// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Builds and manages the navigation side pane, badge counts, and the event store / namespace picker overlays.
/// </summary>
/// <param name="windowSystem">The SharpConsoleUI window system.</param>
/// <param name="views">The ordered array of <see cref="IWorkbenchView"/> instances, one per navigation item.</param>
/// <param name="settings">Workbench settings — used to resolve the active event store and namespace.</param>
/// <param name="getActiveEventStore">Returns the currently active event store name, or <see langword="null"/> for the default.</param>
/// <param name="getActiveNamespace">Returns the currently active namespace name, or <see langword="null"/> for the default.</param>
/// <param name="onStoreSwitch">Invoked with the newly selected event store name when the user picks one from the picker overlay.</param>
/// <param name="onNamespaceSwitch">Invoked with the newly selected namespace name when the user picks one from the picker overlay.</param>
/// <param name="onDataNeeded">Invoked when navigation changes require a data refresh.</param>
/// <param name="getLatestData">Returns the latest cached <see cref="WorkbenchData"/> snapshot, or <see langword="null"/> if none available yet.</param>
public class WorkbenchNavigation(
    ConsoleWindowSystem windowSystem,
    IWorkbenchView[] views,
    WorkbenchSettings settings,
    Func<string?> getActiveEventStore,
    Func<string?> getActiveNamespace,
    Action<string> onStoreSwitch,
    Action<string> onNamespaceSwitch,
    Action onDataNeeded,
    Func<WorkbenchData?> getLatestData)
{
    /// <summary>Navigation item index for Overview.</summary>
    public const int IndexOverview = 0;

    /// <summary>Navigation item index for Observers.</summary>
    public const int IndexObservers = 1;

    /// <summary>Navigation item index for Failures.</summary>
    public const int IndexFailures = 2;

    /// <summary>Navigation item index for Jobs.</summary>
    public const int IndexJobs = 3;

    /// <summary>Navigation item index for Recommendations.</summary>
    public const int IndexRecommendations = 4;

    /// <summary>Navigation item index for Event Sequences.</summary>
    public const int IndexEventSequences = 5;

    /// <summary>Navigation item index for Event Types.</summary>
    public const int IndexEventTypes = 6;

    /// <summary>Navigation item index for Projections.</summary>
    public const int IndexProjections = 7;

    /// <summary>Navigation item index for Read Models.</summary>
    public const int IndexReadModels = 8;

    /// <summary>Navigation item index for Event Stores.</summary>
    public const int IndexEventStores = 9;

    /// <summary>Navigation item index for Namespaces.</summary>
    public const int IndexNamespaces = 10;

    /// <summary>Width of the picker overlay window in columns.</summary>
    const int PickerOverlayWidth = 54;

    /// <summary>Maximum height of the picker overlay window in rows.</summary>
    const int MaxPickerOverlayHeight = 24;

    /// <summary>Extra rows added to item count to account for picker window chrome (borders, title, padding).</summary>
    const int PickerOverlayHeightPadding = 6;

    NavigationItem? _observersItem;
    NavigationItem? _failuresItem;
    NavigationItem? _recommendationsItem;
    NavigationView? _navView;
    int _currentViewIndex;

    /// <summary>
    /// Gets the built <see cref="NavigationView"/> control.
    /// Only available after <see cref="BuildNavigationView"/> has been called.
    /// </summary>
    public NavigationView? NavView => _navView;

    /// <summary>
    /// Gets the zero-based item index of the currently active view, sourced from
    /// <c>OnSelectedItemChanged</c>'s <c>NewIndex</c> — the same index used to activate views
    /// and guaranteed to match the <c>IndexXxx</c> constants regardless of how
    /// <see cref="NavigationView.SelectedIndex"/> counts headers internally.
    /// </summary>
    public int CurrentViewIndex => _currentViewIndex;

    /// <summary>
    /// Gets the Observers navigation item (used to set badge counts).
    /// Only available after <see cref="BuildNavigationView"/> has been called.
    /// </summary>
    public NavigationItem? ObserversItem => _observersItem;

    /// <summary>
    /// Gets the Failures navigation item (used to set badge counts).
    /// Only available after <see cref="BuildNavigationView"/> has been called.
    /// </summary>
    public NavigationItem? FailuresItem => _failuresItem;

    /// <summary>
    /// Gets the Recommendations navigation item (used to set badge counts).
    /// Only available after <see cref="BuildNavigationView"/> has been called.
    /// </summary>
    public NavigationItem? RecommendationsItem => _recommendationsItem;

    /// <summary>
    /// Builds the navigation view with all headers and items, wires the selection-changed callback,
    /// and captures the badge item references.
    /// </summary>
    /// <returns>The fully configured <see cref="NavigationView"/>.</returns>
    public NavigationView BuildNavigationView()
    {
        var selectedBg = new SharpConsoleUI.Color(49, 50, 68, 255);
        var selectedFg = WorkbenchColors.Accent;

        var navView = Controls.NavigationView()
            .WithNavWidth(26)
            .WithPaneHeader($"[bold {WorkbenchColors.Accent.ToMarkup()}] CHRONICLE [/]")
            .WithSelectedColors(selectedFg, selectedBg)
            .WithPaneDisplayMode(NavigationViewDisplayMode.Expanded)
            .WithName("MainNav")
            .Fill()
            .AddHeader("OVERVIEW", h =>
                h.AddItem("Overview", "◆", null, panel => panel.AddControl(views[0].BuildContent(windowSystem))))
            .AddHeader("OBSERVATION", h =>
                h.AddItem("Observers", "o", null, panel => panel.AddControl(views[1].BuildContent(windowSystem)))
                    .AddItem("Failures", "!", null, panel => panel.AddControl(views[2].BuildContent(windowSystem)))
                    .AddItem("Jobs", "~", null, panel => panel.AddControl(views[3].BuildContent(windowSystem)))
                    .AddItem("Recommendations", "*", null, panel => panel.AddControl(views[4].BuildContent(windowSystem))))
            .AddHeader("EVENTS", h =>
                h.AddItem("Event Sequences", "-", null, panel => panel.AddControl(views[5].BuildContent(windowSystem)))
                    .AddItem("Event Types", "#", null, panel => panel.AddControl(views[6].BuildContent(windowSystem))))
            .AddHeader("PROJECTIONS", h =>
                h.AddItem("Projections", ">", null, panel => panel.AddControl(views[7].BuildContent(windowSystem)))
                    .AddItem("Read Models", "=", null, panel => panel.AddControl(views[8].BuildContent(windowSystem))))
            .AddHeader("SERVER", h =>
                h.AddItem("Event Stores", "+", null, panel => panel.AddControl(views[9].BuildContent(windowSystem)))
                    .AddItem("Namespaces", "@", null, panel => panel.AddControl(views[10].BuildContent(windowSystem)))
                    .AddItem("Applications", "A", null, panel => panel.AddControl(views[11].BuildContent(windowSystem)))
                    .AddItem("Users", "U", null, panel => panel.AddControl(views[12].BuildContent(windowSystem)))
                    .AddItem("Identities", "I", null, panel => panel.AddControl(views[13].BuildContent(windowSystem)))
                    .AddItem("Subscriptions", "S", null, panel => panel.AddControl(views[14].BuildContent(windowSystem))))
            .OnSelectedItemChanged((_, e) =>
            {
                // Deactivate the previous view so background refreshes resume rebuilding it.
                if (e.OldIndex >= 0 && e.OldIndex < views.Length)
                    views[e.OldIndex].IsActive = false;

                var idx = e.NewIndex;
                _currentViewIndex = idx;

                if (idx < 0 || idx >= views.Length)
                {
                    return;
                }

                // Push latest data to the newly selected view (IsActive still false → will rebuild).
                var snapshot = getLatestData();
                if (snapshot is not null)
                {
                    views[idx].UpdateData(snapshot);
                }
                else
                {
                    onDataNeeded();
                }

                // Mark as active AFTER the rebuild so future interval refreshes preserve state.
                views[idx].IsActive = true;
            })
            .Build();

        var items = navView.Items;
        _observersItem = FindItemByText(items, "Observers");
        _failuresItem = FindItemByText(items, "Failures");
        _recommendationsItem = FindItemByText(items, "Recommendations");

        _navView = navView;
        return navView;
    }

    /// <summary>
    /// Navigates to the specified view by index. No-op when the index is out of range.
    /// </summary>
    /// <param name="viewIndex">Zero-based index of the target view (use <c>IndexXxx</c> constants).</param>
    public void NavigateTo(int viewIndex)
    {
        if (_navView is null || viewIndex < 0 || viewIndex >= views.Length)
        {
            return;
        }

        _navView.SelectedIndex = viewIndex;
    }

    /// <summary>
    /// Updates the badge subtitles on the Observers, Failures, and Recommendations navigation items
    /// to reflect the latest counts from <paramref name="data"/>.
    /// </summary>
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

    /// <summary>
    /// Opens a modal picker overlay that lets the user select a different event store.
    /// Calls the store-switch callback when a selection is confirmed.
    /// </summary>
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

    /// <summary>
    /// Opens a modal picker overlay that lets the user select a different namespace.
    /// Calls the namespace-switch callback when a selection is confirmed.
    /// </summary>
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

    /// <summary>
    /// Opens a modal, keyboard-navigable picker overlay presenting a list of strings.
    /// Highlights the currently active item with an arrow indicator; calls <paramref name="onSelected"/>
    /// with the chosen string when a row is activated or Enter is pressed.
    /// </summary>
    /// <param name="title">The window title shown in the overlay border.</param>
    /// <param name="columnHeader">The header text for the single picker column.</param>
    /// <param name="tableName">The SharpConsoleUI control name for the picker table (used for test/automation).</param>
    /// <param name="items">The ordered list of choices to display.</param>
    /// <param name="activeItem">The item that is currently selected; shown with a ► prefix.</param>
    /// <param name="onSelected">Invoked with the chosen item name when the user confirms a selection.</param>
    void ShowStringPickerOverlay(
        string title,
        string columnHeader,
        string tableName,
        List<string> items,
        string activeItem,
        Action<string> onSelected)
    {
        var acc = WorkbenchColors.Accent.ToMarkup();

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
            .WithColors(WorkbenchColors.Foreground, WorkbenchColors.Background)
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
