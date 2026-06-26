// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Controls;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Translates raw key-press events from the main window into workbench actions.
/// <para>
/// Wired to <c>Window.KeyPressed</c> (fires after the focused control processes each key).
/// All shortcuts live here because <c>MenuControl</c> only handles arrow keys, Enter, Escape,
/// Home and End — it does NOT intercept letters or Ctrl+letter combinations, so those always
/// bubble to this handler regardless of whether the menu bar currently has focus.
/// </para>
/// <para>
/// Left and Right arrows use an <see cref="KeyPressedEventArgs.AlreadyHandled"/> guard so the
/// NavigationView nav pane (collapse/expand headers) and menu bar (navigate items) can handle
/// them without our focus-switch logic interfering.
/// </para>
/// </summary>
/// <param name="navigation">Navigation — used for view jumping, sidebar mode, and picker overlays.</param>
/// <param name="views">All view instances — used for jumping, filter, and view-action dispatch.</param>
/// <param name="actionHandler">Action handler — owns the text-input focus state flag.</param>
/// <param name="windowSystem">The window system — used for theme switching.</param>
/// <param name="overlays">Overlays — invoked for help, command palette, read model detail, and clipboard copy.</param>
/// <param name="settings">Workbench settings — used by the Quit action to persist the refresh interval.</param>
/// <param name="state">Workbench state — used by the Quit action to persist the last active navigation index.</param>
/// <param name="getWindow">Returns the main <see cref="Window"/>, used for focus control targeting.</param>
/// <param name="onIntervalChanged">Invoked after the refresh interval changes so the status bar can refresh immediately.</param>
public class WorkbenchKeyDispatcher(
    WorkbenchNavigation navigation,
    IWorkbenchView[] views,
    WorkbenchActionHandler actionHandler,
    ConsoleWindowSystem windowSystem,
    WorkbenchOverlays overlays,
    WorkbenchSettings settings,
    WorkbenchState state,
    Func<Window?> getWindow,
    Action onIntervalChanged)
{
    const int MinInterval = 1;
    const int MaxInterval = 60;

    bool _sidebarExpanded = true;

    /// <summary>
    /// Dispatches a key press to the appropriate workbench action.
    /// Wired to <c>Window.KeyPressed</c> which fires after the focused control has processed the key.
    /// </summary>
    /// <param name="e">The key-press event.</param>
    public void Dispatch(KeyPressedEventArgs e)
    {
        if (navigation.NavView is null)
        {
            return;
        }

        // Suppress all shortcuts while the filter prompt is active so the user can type freely.
        // Escape is the only exception — it exits filter mode.
        if (actionHandler.TextInputFocused)
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                var filterIdx = navigation.CurrentViewIndex;
                if (filterIdx >= 0 && filterIdx < views.Length)
                {
                    views[filterIdx].ClearFilter();
                }

                e.Handled = true;
            }

            return;
        }

        var idx = navigation.CurrentViewIndex;

        switch (e.KeyInfo.Key)
        {
            // Left/Right: only act when the focused control did NOT already handle them.
            // The NavigationView nav pane handles Left (collapse header) and the menu bar handles
            // Left/Right (navigate items). We skip FocusNavigation/FocusContent in those cases.
            case ConsoleKey.LeftArrow:
                if (!e.AlreadyHandled)
                {
                    FocusNavigation();
                    e.Handled = true;
                }

                break;

            case ConsoleKey.RightArrow:
                if (!e.AlreadyHandled)
                {
                    FocusContent();
                    e.Handled = true;
                }

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
                navigation.OpenEventStorePicker();
                e.Handled = true;
                break;

            case ConsoleKey.N when e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control):
                navigation.OpenNamespacePicker();
                e.Handled = true;
                break;

            case ConsoleKey.C when e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control):
                overlays.CopyDetailToClipboard();
                e.Handled = true;
                break;

            case ConsoleKey.Enter when idx == WorkbenchNavigation.IndexReadModels:
                overlays.OpenReadModelDetail();
                e.Handled = true;
                break;

            case ConsoleKey.Oem2 when e.KeyInfo.Modifiers == ConsoleModifiers.Shift:
                if (!e.AlreadyHandled)
                {
                    overlays.OpenHelpOverlay();
                    e.Handled = true;
                }

                break;

            case ConsoleKey.P when e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control):
                overlays.OpenCommandPalette();
                e.Handled = true;
                break;

            // Theme switching
            case ConsoleKey.F9:
                ApplyThemeSlot(0);
                e.Handled = true;
                break;

            case ConsoleKey.F10:
                ApplyThemeSlot(1);
                e.Handled = true;
                break;

            case ConsoleKey.F11:
                ApplyThemeSlot(2);
                e.Handled = true;
                break;

            // Refresh interval — OemPlus/Add increase, OemMinus/Subtract decrease (clamped).
            // OemPlus is the '=' / '+' key (terminals report '+' as Shift+OemPlus → OemPlus here).
            case ConsoleKey.OemPlus:
            case ConsoleKey.Add:
                if (!e.AlreadyHandled)
                {
                    AdjustInterval(+1);
                    e.Handled = true;
                }

                break;

            case ConsoleKey.OemMinus:
            case ConsoleKey.Subtract:
                if (!e.AlreadyHandled)
                {
                    AdjustInterval(-1);
                    e.Handled = true;
                }

                break;

            // Page navigation
            case ConsoleKey.Oem6: // ]
                if (idx >= 0 && idx < views.Length) views[idx].NextPage();
                e.Handled = true;
                break;

            case ConsoleKey.Oem4: // [
                if (idx >= 0 && idx < views.Length) views[idx].PreviousPage();
                e.Handled = true;
                break;

            // Row jumping — guarded so Shift+G and Home in the filter prompt aren't intercepted.
            case ConsoleKey.G when e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift):
                if (!e.AlreadyHandled)
                {
                    if (idx >= 0 && idx < views.Length) views[idx].JumpToLastRow();
                    e.Handled = true;
                }

                break;

            case ConsoleKey.Home:
                if (!e.AlreadyHandled)
                {
                    if (idx >= 0 && idx < views.Length) views[idx].JumpToFirstRow();
                    e.Handled = true;
                }

                break;

            // Single-character shortcuts: only act when the focused control did NOT already consume
            // the key. When the filter prompt is active and the user types 'f', 'q', 'v', etc.,
            // the prompt's ProcessKey returns true (alreadyHandled = true) and we skip these cases,
            // so the character is inserted into the filter instead of triggering a shortcut.
            case ConsoleKey.F:
                if (!e.AlreadyHandled)
                {
                    ActivateCurrentFilter();
                    e.Handled = true;
                }

                break;

            case ConsoleKey.Q:
                if (!e.AlreadyHandled)
                {
                    state.Interval = settings.Interval;
                    state.LastNavIndex = navigation.CurrentViewIndex;
                    state.Save();
                    Environment.Exit(0);
                }

                break;

            default:
                // OCP: view-scoped action keys (R, V, D, T, P, S, U, A, I, etc.)
                // Also guarded by AlreadyHandled — when the filter prompt is active,
                // it consumes every printable key, so these action shortcuts are naturally
                // suppressed without needing any separate TextInputFocused flag.
                if (!e.AlreadyHandled && DispatchCurrentViewAction(e.KeyInfo.Key, e.KeyInfo.Modifiers))
                {
                    e.Handled = true;
                }

                break;
        }
    }

    void FocusNavigation()
    {
        var window = getWindow();
        if (window is null || navigation.NavView is null)
        {
            return;
        }

        window.FocusControl(navigation.NavView);
    }

    void FocusContent()
    {
        var window = getWindow();
        if (window is null)
        {
            return;
        }

        var idx = navigation.CurrentViewIndex;
        if (idx >= 0 && idx < views.Length && views[idx].PrimaryFocusTarget is IInteractiveControl ic)
        {
            window.FocusControl(ic);
        }
    }

    void ToggleSidebar()
    {
        if (navigation.NavView is null)
        {
            return;
        }

        _sidebarExpanded = !_sidebarExpanded;
        navigation.NavView.PaneDisplayMode = _sidebarExpanded
            ? NavigationViewDisplayMode.Expanded
            : NavigationViewDisplayMode.Compact;
    }

    void ToggleDetailPane()
    {
        var idx = navigation.CurrentViewIndex;
        if (idx >= 0 && idx < views.Length)
        {
            views[idx].ToggleDetailPane();
        }
    }

    /// <summary>
    /// Adjusts the refresh interval by <paramref name="delta"/> seconds, clamped to
    /// <see cref="MinInterval"/>..<see cref="MaxInterval"/>, and refreshes the status bar immediately.
    /// The refresh loop re-reads the interval on its next delay, so the new cadence applies from there.
    /// </summary>
    /// <param name="delta">The amount to add to the current interval in seconds. Positive values increase the interval (slower refresh); negative values decrease it (faster refresh).</param>
    void AdjustInterval(int delta)
    {
        var updated = Math.Clamp(settings.Interval + delta, MinInterval, MaxInterval);
        if (updated == settings.Interval)
        {
            return;
        }

        settings.Interval = updated;
        onIntervalChanged();
    }

    void ActivateCurrentFilter()
    {
        var idx = navigation.CurrentViewIndex;
        var window = getWindow();
        if (idx < 0 || idx >= views.Length || window is null)
        {
            return;
        }

        views[idx].ActivateFilter(window);
    }

    bool DispatchCurrentViewAction(ConsoleKey key, ConsoleModifiers modifiers)
    {
        var idx = navigation.CurrentViewIndex;
        if (idx < 0 || idx >= views.Length)
        {
            return false;
        }

        var match = views[idx].ViewActions.FirstOrDefault(
            a => a.TriggerKey == key && a.TriggerModifiers == modifiers && a.Enabled);

        if (match is null)
        {
            return false;
        }

        match.Execute();
        return true;
    }

    void ApplyThemeSlot(int index)
    {
        var slots = WorkbenchThemes.GetPrimarySlots(windowSystem);
        if (index >= 0 && index < slots.Count)
        {
            slots[index].Apply();
        }
    }
}
