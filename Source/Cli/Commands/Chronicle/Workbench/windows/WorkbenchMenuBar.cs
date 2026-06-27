// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Builds the horizontal sticky menu bar displayed at the top of the workbench window.
/// </summary>
/// <param name="navigation">Navigation — wired to Ctrl+E / Ctrl+N quick-switch actions.</param>
/// <param name="overlays">Overlays — wired to Help menu items.</param>
/// <param name="windowSystem">The window system — used for theme switching.</param>
/// <param name="onQuit">The graceful quit action, shared with the Quit shortcut and status-bar hint.</param>
public class WorkbenchMenuBar(
    WorkbenchNavigation navigation,
    WorkbenchOverlays overlays,
    ConsoleWindowSystem windowSystem,
    Action onQuit)
{
    /// <summary>
    /// Builds and returns the configured horizontal sticky <see cref="MenuControl"/>.
    /// </summary>
    /// <returns>The fully wired menu bar control.</returns>
    public MenuControl Build()
    {
        var menu = Controls.Menu()
            .Horizontal()
            .Sticky()
            .WithName("WorkbenchMenuBar")
            .AddItem("File", m => m
                .AddItem("Switch Event Store", "Ctrl+E", () => navigation.OpenEventStorePicker())
                .AddItem("Switch Namespace", "Ctrl+N", () => navigation.OpenNamespacePicker())
                .AddSeparator()
                .AddItem("Quit", "Q", onQuit))
            .AddItem("Help", BuildHelpMenu)
            .Build();

        menu.StickyPosition = StickyPosition.Top;
        return menu;
    }

    void BuildHelpMenu(MenuItemBuilder help)
    {
        help.AddItem("Keyboard Shortcuts", "?", overlays.OpenHelpOverlay)
            .AddSeparator();

        var slots = WorkbenchThemes.GetPrimarySlots(windowSystem);
        string[] shortcuts = ["F9", "F10", "F11"];
        for (var i = 0; i < slots.Count && i < shortcuts.Length; i++)
        {
            var slot = slots[i];
            help.AddItem($"Theme: {slot.Label}", shortcuts[i], slot.Apply);
        }

        help.AddItem("More Themes", BuildThemeSubmenu);
    }

    void BuildThemeSubmenu(MenuItemBuilder submenu)
    {
        foreach (var name in WorkbenchThemes.GetAvailableThemeNames(windowSystem))
        {
            submenu.AddItem(name, () => WorkbenchThemes.Apply(windowSystem, name));
        }
    }
}
