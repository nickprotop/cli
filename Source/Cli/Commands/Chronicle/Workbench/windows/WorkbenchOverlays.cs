// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Themes;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Opens and manages modal popup windows: keyboard-shortcuts help, command palette, and read model detail.
/// Also owns clipboard copy, which is triggered from both the keyboard and the menu bar.
/// </summary>
/// <param name="windowSystem">The SharpConsoleUI window system.</param>
/// <param name="views">All view instances — used to read current-view help and selected items.</param>
/// <param name="navigation">Navigation — provides the current view index and navigate methods.</param>
/// <param name="actionHandler">Action handler — owns the <c>TextInputFocused</c> flag used by the command palette prompt.</param>
/// <param name="refreshLoop">Refresh loop — provides the latest data snapshot and temporary panel messages.</param>
public class WorkbenchOverlays(
    ConsoleWindowSystem windowSystem,
    IWorkbenchView[] views,
    WorkbenchNavigation navigation,
    WorkbenchActionHandler actionHandler,
    WorkbenchRefreshLoop refreshLoop)
{
    readonly WorkbenchTheme _theme = new(windowSystem);

    Window? _mainWindow;
    WorkbenchCommandPalette? _palettePortal;
    LayoutNode? _palettePortalNode;

    /// <summary>
    /// Registers the main <see cref="Window"/> so the command palette can use
    /// <see cref="Window.CreatePortal"/> and <see cref="Window.RemovePortal"/>, and hooks
    /// <see cref="Window.PreviewKeyPressed"/> to forward all keys to the palette while it is open.
    /// Must be called once from <see cref="MainWindow"/> immediately after the window is built.
    /// </summary>
    /// <param name="window">The main application window.</param>
    public void SetWindow(Window window)
    {
        _mainWindow = window;

        window.PreviewKeyPressed += (_, e) =>
        {
            if (_palettePortal is not null)
            {
                // Ctrl+P toggles the palette closed while it is open. The palette swallows all other
                // keys, so this toggle must be handled here before delegating to the palette.
                if (e.KeyInfo.Key == ConsoleKey.P && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    DismissPalette();
                }
                else
                {
                    _palettePortal.ProcessKey(e.KeyInfo);
                }

                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// Opens the keyboard-shortcuts help overlay. Shows a view-specific section at the top when the active
    /// view exposes <see cref="IWorkbenchView.ViewHelp"/> text.
    /// </summary>
    public void OpenHelpOverlay()
    {
        var mut = _theme.Muted.ToMarkup();
        var acc = _theme.Accent.ToMarkup();

        var themeLabels = string.Join(" / ", WorkbenchThemes.GetPrimarySlots(windowSystem).Select(s => s.Label));

        var activeIdx = navigation.CurrentViewIndex;
        var currentViewHelp = string.Empty;
        if (activeIdx >= 0 && activeIdx < views.Length && !string.IsNullOrEmpty(views[activeIdx].ViewHelp))
        {
            currentViewHelp =
                $"[bold {acc}]THIS VIEW[/]\n" +
                string.Join('\n', views[activeIdx].ViewHelp.Split('\n').Select(l => $"  {l}")) +
                "\n\n";
        }

        var helpText =
            currentViewHelp +
            $"[bold {acc}]NAVIGATION[/]\n" +
            $"  [{mut}]↑ ↓[/]           Move selection up / down\n" +
            $"  [{mut}]← / →[/]         Focus sidebar / content pane\n" +
            $"  [{mut}]Home / Shift+G[/] Jump to first / last row\n" +
            $"  [{mut}]1 – 9[/]          Jump to view by number\n" +
            $"  [{mut}]Ctrl+B[/]         Toggle sidebar expand / compact\n" +
            $"  [{mut}]Ctrl+\\[/]         Toggle detail pane\n" +
            $"  [{mut}]Enter / dbl-click[/] Inspect selected row (open detail)\n" +
            $"  [{mut}]Esc[/]            Close overlay\n" +
            "\n" +
            $"[bold {acc}]QUICK SWITCH[/]\n" +
            $"  [{mut}]Ctrl+E[/]         Switch event store\n" +
            $"  [{mut}]Ctrl+N[/]         Switch namespace\n" +
            "\n" +
            $"[bold {acc}]FILTER & SEARCH[/]\n" +
            $"  [{mut}]F[/]              Focus filter prompt for current view\n" +
            $"  [{mut}]Ctrl+P[/]         Open command palette\n" +
            $"  [{mut}]Escape[/]         Clear filter and return focus to table\n" +
            "\n" +
            $"[bold {acc}]VIEW ACTIONS (when row selected)[/]\n" +
            $"  [{mut}]?[/]              See this screen — actions vary per view\n" +
            $"  [{mut}]Y[/] [{mut}]/[/] [{mut}]Esc[/]        Confirm / cancel inside a confirmation dialog\n" +
            "\n" +
            $"[bold {acc}]CLIPBOARD[/]\n" +
            $"  [{mut}]Ctrl+C[/]         Copy detail pane content to clipboard\n" +
            "\n" +
            $"[bold {acc}]THEMES[/]\n" +
            $"  [{mut}]F9 / F10 / F11[/] {themeLabels} theme\n" +
            "\n" +
            $"[bold {acc}]GENERAL[/]\n" +
            $"  [{mut}]+  /  -[/]        Increase / decrease refresh interval\n" +
            $"  [{mut}]?[/]              This help screen\n" +
            $"  [{mut}]Q[/]              Quit";

        var markup = new MarkupControl([helpText]) { Wrap = true };
        var helpWindow = WorkbenchUi.BuildDialog(
            windowSystem,
            _theme,
            "Keyboard Shortcuts",
            [markup],
            []);

        windowSystem.AddWindow(helpWindow, activateWindow: true);
    }

    /// <summary>
    /// Opens the command palette overlay for searching observers, event types, projections, read models, and failures.
    /// Calling when the palette is already open toggles it closed (Ctrl+P again dismisses).
    /// </summary>
    public void OpenCommandPalette()
    {
        // Toggle: dismiss if already open.
        if (_palettePortal is not null)
        {
            DismissPalette();
            return;
        }

        var snapshot = refreshLoop.CurrentData;
        if (snapshot is null || _mainWindow is null || navigation.NavView is null)
        {
            return;
        }

        var matches = BuildPaletteItems(snapshot);

        var palette = new WorkbenchCommandPalette(matches, _theme, _mainWindow.Width, _mainWindow.Height)
        {
            Container = _mainWindow
        };

        _palettePortal = palette;
        _palettePortalNode = _mainWindow.CreatePortal(navigation.NavView, palette);

        // Route focus through the window's FocusManager (not just the portal-local focus) so the search
        // prompt's text cursor renders — the cursor seam reads FocusManager.FocusedControl. Mirrors how
        // NavigationView focuses its own portal content.
        var firstFocusable = palette.GetChildren()
            .OfType<IFocusableControl>()
            .FirstOrDefault(c => c.CanReceiveFocus);
        if (firstFocusable is not null)
        {
            _mainWindow.FocusManager.SetFocus(firstFocusable, FocusReason.Programmatic);
        }

        // Suppress global shortcuts while the palette search prompt has focus.
        actionHandler.TextInputFocused = true;

        palette.CommandChosen += (_, navigateAction) =>
        {
            DismissPalette();
            navigateAction?.Invoke();
        };

        // EscapeRequested: user pressed Esc inside the palette.
        palette.EscapeRequested += (_, _) => DismissPalette();

        // DismissRequested: framework-initiated dismissal (outside-click, debounce) via the base
        // PortalContentBase.DismissRequested event. Both paths must clean up the portal refs.
        palette.DismissRequested += (_, _) => DismissPalette();
    }

    /// <summary>
    /// Opens the detail overlay for the currently selected read model entry.
    /// No-op when the Read Models view is not active or nothing is selected.
    /// </summary>
    public void OpenReadModelDetail()
    {
        if (views[WorkbenchNavigation.IndexReadModels] is ReadModelsView rmv)
        {
            rmv.OpenSelectedDetailOverlay();
        }
    }

    /// <summary>
    /// Copies the detail pane content of the active view to the system clipboard.
    /// Shows a brief confirmation message in the top panel.
    /// No-op when no content is available.
    /// </summary>
    public void CopyDetailToClipboard()
    {
        var idx = navigation.CurrentViewIndex;
        if (idx < 0 || idx >= views.Length)
        {
            return;
        }

        var content = views[idx].DetailContent;
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        ClipboardHelper.SetText(Markup.Remove(content));
        refreshLoop.ShowTemporaryMessage("✓ Copied to clipboard");
    }

    /// <summary>
    /// Opens a modal overlay listing all <paramref name="matchingObservers"/> that subscribe to
    /// <paramref name="eventTypeId"/>. The user can inspect each observer's detail, press Enter to
    /// navigate directly to it in the Observers view, or Escape to close.
    /// </summary>
    /// <param name="eventTypeId">The event type identifier — shown in the window title.</param>
    /// <param name="matchingObservers">Pre-filtered list of observers subscribed to this event type.</param>
    /// <param name="navigateToObserver">Invoked with the selected observer when the user confirms navigation.</param>
    public void OpenObserversForEventType(
        string eventTypeId,
        IReadOnlyList<ObserverInformation> matchingObservers,
        Action<ObserverInformation> navigateToObserver)
    {
        var mut = _theme.Muted.ToMarkup();
        var acc = _theme.Accent.ToMarkup();
        var warn = _theme.Warning.ToMarkup();

        var table = Controls.Table()
            .AddColumn("State", SharpConsoleUI.Layout.TextJustification.Left, 18)
            .AddColumn("Observer", SharpConsoleUI.Layout.TextJustification.Left, null)
            .AddColumn("Type", SharpConsoleUI.Layout.TextJustification.Left, 14)
            .Interactive()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithName("ObserversForEventTypeTable")
            .Build();

        var detailPanel = Controls.Panel()
            .WithContent($"[{mut}]Select an observer.[/]")
            .WithHeader(" OBSERVER ")
            .Rounded()
            .WithColorRole(ColorRole.Warning)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("ObserversForEventTypeDetail")
            .Build();

        foreach (var obs in matchingObservers.OrderBy(ObserverSortOrder).ThenBy(o => o.Id))
        {
            var icon = ObserverIcon(obs);
            var color = ObserverStateColor(obs);
            table.AddRow(new UITableRow([$"[{color}]{icon} {obs.RunningState}[/]", obs.Id, obs.Type.ToString()]) { Tag = obs });
        }

        if (matchingObservers.Count == 0)
        {
            table.AddRow(new UITableRow([$"[{mut}]—[/]", $"[{mut}]No observers found for this event type[/]", string.Empty]));
        }

        table.SelectedRowChanged += (_, _) =>
        {
            if (table.SelectedRow?.Tag is ObserverInformation obs)
            {
                detailPanel.Content = RenderObserverDetail(obs, mut, acc, warn);
            }
        };

        // Select first row to populate the detail panel immediately
        if (table.Rows.Count > 0 && table.Rows[0].Tag is ObserverInformation first)
        {
            table.SelectedRowIndex = 0;
            detailPanel.Content = RenderObserverDetail(first, mut, acc, warn);
        }

        var layout = HorizontalGridControl.Create()
            .Column(c => c.Add(table))
            .WithSplitterAfter(0)
            .Column(c => c.Width(38).Add(detailPanel))
            .Build();
        layout.VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Fill;

        void OpenSelected()
        {
            if (table.SelectedRow?.Tag is ObserverInformation obs)
            {
                navigateToObserver(obs);
            }
        }

        var overlayWindow = WorkbenchUi.BuildDialog(
            windowSystem,
            _theme,
            $"Observers for {eventTypeId}",
            [layout],
            [new DialogButton("Open Observer", ColorRole.Warning, OpenSelected)],
            new DialogOptions
            {
                Severity = DialogSeverity.Warning,
                FillBody = true,
                OnKey = (key, close) =>
                {
                    if (key.Key == ConsoleKey.Enter && table.SelectedRow?.Tag is ObserverInformation obs)
                    {
                        close();
                        navigateToObserver(obs);
                        return true;
                    }

                    return false;
                }
            });

        windowSystem.AddWindow(overlayWindow, activateWindow: true);
    }

    /// <summary>
    /// Opens a modal overlay showing the full definition — name, generation, owner, and schema — for
    /// the event type identified by <paramref name="eventTypeId"/>.
    /// </summary>
    /// <param name="eventTypeId">The event type identifier to look up.</param>
    /// <param name="snapshot">The current data snapshot used to locate the registration.</param>
    public void OpenEventTypeDefinition(string eventTypeId, WorkbenchData? snapshot)
    {
        var mut = _theme.Muted.ToMarkup();
        var acc = _theme.Accent.ToMarkup();
        var teal = _theme.Teal.ToMarkup();

        EventTypeRegistration? reg = null;
        if (snapshot is not null)
        {
            reg = snapshot.EventTypeRegistrations
                .FirstOrDefault(r => string.Equals(r.Type.Id, eventTypeId, StringComparison.OrdinalIgnoreCase));
        }

        string content;
        if (reg is null)
        {
            content = $"[{mut}]No registration found for[/] {eventTypeId}";
        }
        else
        {
            var schemaContent = !string.IsNullOrEmpty(reg.Schema)
                ? JsonYamlFormatter.FormatAsYaml(reg.Schema, mut)
                : $"[{mut}](no schema)[/]";

            content = string.Join(
                '\n',
                [
                    $"[bold {teal}]{reg.Type.Id}[/]  [{mut}]gen {reg.Type.Generation}[/]",
                    string.Empty,
                    $"[{mut}]Owner[/]      {reg.Owner}",
                    $"[{mut}]Source[/]     {reg.Source}",
                    $"[{mut}]Tombstone[/]  {reg.Type.Tombstone}",
                    string.Empty,
                    $"[{acc}]Schema:[/]",
                    schemaContent
                ]);
        }

        var markup = new MarkupControl([content]) { Wrap = true };
        var defWindow = WorkbenchUi.BuildDialog(
            windowSystem,
            _theme,
            $"Event Type: {eventTypeId}",
            [markup],
            []);

        windowSystem.AddWindow(defWindow, activateWindow: true);
    }

    static int ObserverSortOrder(ObserverInformation o) => ObserverPresentation.SortOrder(o);

    static string ObserverIcon(ObserverInformation obs) => ObserverPresentation.Icon(obs);

    string ObserverStateColor(ObserverInformation obs) => ObserverPresentation.StateColor(obs, _theme);

    string RenderObserverDetail(ObserverInformation obs, string mut, string acc, string warn)
    {
        var color = ObserverStateColor(obs);
        var lastSeq = obs.LastHandledEventSequenceNumber == ulong.MaxValue
            ? "N/A"
            : obs.LastHandledEventSequenceNumber.ToString("N0");

        var lines = new List<string>
        {
            $"[{mut}]Id[/]      {obs.Id}",
            $"[{mut}]Type[/]    {obs.Type}",
            $"[{mut}]State[/]   [{color}]{obs.RunningState}[/]",
            $"[{mut}]Next #[/]  {obs.NextEventSequenceNumber:N0}",
            $"[{mut}]Last #[/]  {lastSeq}",
            string.Empty,
            $"[{acc}]Event Types:[/]"
        };

        foreach (var et in (obs.EventTypes ?? []).OrderBy(e => e.Id))
        {
            lines.Add($"  [{mut}]•[/] {et.Id} [{mut}]gen {et.Generation}[/]");
        }

        lines.Add(string.Empty);
        lines.Add($"[{mut}]Enter[/] → go to observer in main view");

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Dismisses the command palette portal if one is currently open. Safe to call more than
    /// once — subsequent calls are no-ops once the portal refs have been cleared.
    /// </summary>
    void DismissPalette()
    {
        if (_palettePortalNode is null)
        {
            return;
        }

        if (_mainWindow is not null && navigation.NavView is not null)
        {
            _mainWindow.RemovePortal(navigation.NavView, _palettePortalNode);
        }

        _palettePortal = null;
        _palettePortalNode = null;

        // Restore normal shortcut dispatch now that the palette prompt is gone.
        actionHandler.TextInputFocused = false;
    }

    /// <summary>
    /// Builds the full set of palette items from the current data snapshot. Items are produced for
    /// Observers, Event Types, Projections, Read Models, and Failed Partitions. No cap is applied
    /// here — the palette's <c>PopulateList</c> applies a display cap to the filtered results so
    /// that items beyond the first N raw entries are still reachable by typing a more specific query.
    /// </summary>
    /// <param name="snapshot">The current workbench data snapshot.</param>
    /// <returns>All candidate palette items.</returns>
    List<(string Kind, string Label, string SearchKey, Action Navigate)> BuildPaletteItems(WorkbenchData snapshot)
    {
        var matches = new List<(string Kind, string Label, string SearchKey, Action Navigate)>();

        foreach (var obs in snapshot.Observers)
        {
            var obsId = obs.Id;
            matches.Add(("Observer", $"{obs.Id} [{obs.RunningState}]", obs.Id, () =>
            {
                navigation.NavigateTo(WorkbenchNavigation.IndexObservers);
                NavigateAndFilter(WorkbenchNavigation.IndexObservers, obsId);
            }));
        }

        foreach (var et in snapshot.EventTypeRegistrations)
        {
            var typeId = et.Type.Id;
            matches.Add(("Event Type", $"{et.Type.Id} gen {et.Type.Generation}", et.Type.Id, () =>
            {
                navigation.NavigateTo(WorkbenchNavigation.IndexEventTypes);
                NavigateAndFilter(WorkbenchNavigation.IndexEventTypes, typeId);
            }));
        }

        foreach (var pd in snapshot.ProjectionDefinitions)
        {
            var id = pd.Identifier;
            matches.Add(("Projection", pd.Identifier, pd.Identifier, () =>
            {
                navigation.NavigateTo(WorkbenchNavigation.IndexProjections);
                NavigateAndFilter(WorkbenchNavigation.IndexProjections, id);
            }));
        }

        foreach (var rm in snapshot.ReadModelDefinitions)
        {
            var label = rm.DisplayName.Length > 0 ? rm.DisplayName : rm.ContainerName;
            matches.Add(("Read Model", label, $"{rm.ContainerName} {rm.DisplayName}", () =>
            {
                navigation.NavigateTo(WorkbenchNavigation.IndexReadModels);
                NavigateAndFilter(WorkbenchNavigation.IndexReadModels, label);
            }));
        }

        foreach (var fp in snapshot.FailedPartitions)
        {
            var obsId = fp.ObserverId;
            matches.Add(("Failure", $"{fp.ObserverId}/{fp.Partition}", $"{fp.ObserverId} {fp.Partition}", () =>
            {
                navigation.NavigateTo(WorkbenchNavigation.IndexFailures);
                NavigateAndFilter(WorkbenchNavigation.IndexFailures, obsId);
            }));
        }

        return matches;
    }

    void NavigateAndFilter(int viewIndex, string filter)
    {
        if (viewIndex >= 0 && viewIndex < views.Length)
        {
            views[viewIndex].SetFilter(filter);
        }
    }
}
