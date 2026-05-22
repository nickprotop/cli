// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Helpers;
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
    const int HelpOverlayWidth = 72;
    const int HelpOverlayHeight = 40;
    const int CommandPaletteWidth = 80;
    const int CommandPaletteHeight = 18;
    const int MaxCommandPaletteResults = 10;

    /// <summary>
    /// Opens the keyboard-shortcuts help overlay. Shows a view-specific section at the top when the active
    /// view exposes <see cref="IWorkbenchView.ViewHelp"/> text.
    /// </summary>
    public void OpenHelpOverlay()
    {
        var mut = WorkbenchColors.Muted.ToMarkup();
        var acc = WorkbenchColors.Accent.ToMarkup();

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
            $"  [{mut}]Ctrl+B[/]         Toggle sidebar expand / compact\n" +
            $"  [{mut}]Ctrl+\\[/]         Toggle detail pane\n" +
            $"  [{mut}]Enter[/]          Open detail overlay\n" +
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
            $"  [{mut}]Y / N[/]          Confirm / Cancel pending action\n" +
            "\n" +
            $"[bold {acc}]CLIPBOARD[/]\n" +
            $"  [{mut}]Ctrl+C[/]         Copy detail pane content to clipboard\n" +
            "\n" +
            $"[bold {acc}]THEMES[/]\n" +
            $"  [{mut}]F9 / F10 / F11[/] Modern Gray / Classic / Dev Dark theme\n" +
            "\n" +
            $"[bold {acc}]GENERAL[/]\n" +
            $"  [{mut}]+  /  -[/]        Increase / decrease refresh interval\n" +
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

    /// <summary>
    /// Opens the command palette overlay for searching observers, event types, projections, read models, and failures.
    /// </summary>
    public void OpenCommandPalette()
    {
        var snapshot = refreshLoop.CurrentData;
        if (snapshot is null)
        {
            return;
        }

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
            .OnGotFocus((_, _) => actionHandler.TextInputFocused = true)
            .OnLostFocus((_, _) => actionHandler.TextInputFocused = false)
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
                    var obsId = obs.Id;
                    matches.Add(("Observer", $"{obs.Id} [{obs.RunningState}]", () =>
                    {
                        navigation.NavigateTo(WorkbenchNavigation.IndexObservers);
                        NavigateAndFilter(WorkbenchNavigation.IndexObservers, obsId);
                    }));
                }
            }

            foreach (var et in snapshot.EventTypeRegistrations)
            {
                if (et.Type.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var typeId = et.Type.Id;
                    matches.Add(("Event Type", $"{et.Type.Id} gen {et.Type.Generation}", () =>
                    {
                        navigation.NavigateTo(WorkbenchNavigation.IndexEventTypes);
                        NavigateAndFilter(WorkbenchNavigation.IndexEventTypes, typeId);
                    }));
                }
            }

            foreach (var pd in snapshot.ProjectionDefinitions)
            {
                if (pd.Identifier.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var id = pd.Identifier;
                    matches.Add(("Projection", pd.Identifier, () =>
                    {
                        navigation.NavigateTo(WorkbenchNavigation.IndexProjections);
                        NavigateAndFilter(WorkbenchNavigation.IndexProjections, id);
                    }));
                }
            }

            foreach (var rm in snapshot.ReadModelDefinitions)
            {
                if (rm.ContainerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    rm.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var label = rm.DisplayName.Length > 0 ? rm.DisplayName : rm.ContainerName;
                    matches.Add(("Read Model", label, () =>
                    {
                        navigation.NavigateTo(WorkbenchNavigation.IndexReadModels);
                        NavigateAndFilter(WorkbenchNavigation.IndexReadModels, label);
                    }));
                }
            }

            foreach (var fp in snapshot.FailedPartitions)
            {
                if (fp.ObserverId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    fp.Partition.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var obsId = fp.ObserverId;
                    matches.Add(("Failure", $"{fp.ObserverId}/{fp.Partition}", () =>
                    {
                        navigation.NavigateTo(WorkbenchNavigation.IndexFailures);
                        NavigateAndFilter(WorkbenchNavigation.IndexFailures, obsId);
                    }));
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
        var mut = WorkbenchColors.Muted.ToMarkup();
        var acc = WorkbenchColors.Accent.ToMarkup();
        var warn = WorkbenchColors.Warning.ToMarkup();

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
            .WithBorderColor(WorkbenchColors.Warning)
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

        var width = Math.Min(96, Console.WindowWidth - 4);
        var height = Math.Min(28, Console.WindowHeight - 4);

        Window? overlayWindow = null;
        overlayWindow = new WindowBuilder(windowSystem)
            .WithTitle($" Observers for {eventTypeId} ")
            .WithColors(WorkbenchColors.Foreground, WorkbenchColors.Background)
            .WithSize(width, height)
            .Centered()
            .AddControl(layout)
            .OnKeyPressed((_, e) =>
            {
                if (e.KeyInfo.Key == ConsoleKey.Escape)
                {
                    windowSystem.CloseWindow(overlayWindow, activateParent: true, force: false);
                    e.Handled = true;
                    return;
                }

                if (e.KeyInfo.Key == ConsoleKey.Enter &&
                    table.SelectedRow?.Tag is ObserverInformation obs)
                {
                    windowSystem.CloseWindow(overlayWindow, activateParent: true, force: false);
                    navigateToObserver(obs);
                    e.Handled = true;
                }
            })
            .Build();

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
        var mut = WorkbenchColors.Muted.ToMarkup();
        var acc = WorkbenchColors.Accent.ToMarkup();
        var teal = WorkbenchColors.Teal.ToMarkup();

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
        var scrollable = Controls.ScrollablePanel()
            .AddControl(markup)
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithPadding(2, 1, 2, 1)
            .Build();

        var width = Math.Min(80, Console.WindowWidth - 4);
        var height = Math.Min(32, Console.WindowHeight - 4);

        Window? defWindow = null;
        defWindow = new WindowBuilder(windowSystem)
            .WithTitle($" Event Type: {eventTypeId} ")
            .WithColors(WorkbenchColors.Foreground, WorkbenchColors.Background)
            .WithSize(width, height)
            .Centered()
            .AddControl(scrollable)
            .OnKeyPressed((_, e) =>
            {
                if (e.KeyInfo.Key == ConsoleKey.Escape)
                {
                    windowSystem.CloseWindow(defWindow, activateParent: true, force: false);
                    e.Handled = true;
                }
            })
            .Build();

        windowSystem.AddWindow(defWindow, activateWindow: true);
    }

    static string RenderObserverDetail(ObserverInformation obs, string mut, string acc, string warn)
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

    static int ObserverSortOrder(ObserverInformation o) => o.RunningState switch
    {
        ObserverRunningState.Disconnected => 0,
        ObserverRunningState.Replaying => 1,
        ObserverRunningState.Active => 2,
        ObserverRunningState.Suspended => 3,
        _ => 4
    };

    static string ObserverStateColor(ObserverInformation obs) => obs.RunningState switch
    {
        ObserverRunningState.Active => WorkbenchColors.Success.ToMarkup(),
        ObserverRunningState.Replaying => WorkbenchColors.Warning.ToMarkup(),
        ObserverRunningState.Disconnected => WorkbenchColors.Danger.ToMarkup(),
        _ => WorkbenchColors.Muted.ToMarkup()
    };

    static string ObserverIcon(ObserverInformation obs) => obs.RunningState switch
    {
        ObserverRunningState.Active => "●",
        ObserverRunningState.Replaying => "▲",
        ObserverRunningState.Disconnected => "⊘",
        _ => "○"
    };

    void NavigateAndFilter(int viewIndex, string filter)
    {
        if (viewIndex >= 0 && viewIndex < views.Length)
        {
            views[viewIndex].SetFilter(filter);
        }
    }
}
