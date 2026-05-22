// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Themes;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Builds the horizontal sticky menu bar displayed at the top of the workbench window.
/// </summary>
/// <param name="navigation">Navigation — wired to Ctrl+E / Ctrl+N quick-switch actions.</param>
/// <param name="overlays">Overlays — wired to Help menu items.</param>
/// <param name="windowSystem">The window system — used for theme switching.</param>
/// <param name="settings">Workbench settings — used by the Quit action to persist the refresh interval.</param>
/// <param name="state">Workbench state — used by the Quit action to persist the last active navigation index.</param>
public class WorkbenchMenuBar(
    WorkbenchNavigation navigation,
    WorkbenchOverlays overlays,
    ConsoleWindowSystem windowSystem,
    WorkbenchSettings settings,
    WorkbenchState state)
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
                .AddItem("Quit", "Q", () =>
                {
                    state.Interval = settings.Interval;
                    state.LastNavIndex = navigation.CurrentViewIndex;
                    state.Save();
                    Environment.Exit(0);
                }))
            .AddItem("Help", m => m
                .AddItem("Keyboard Shortcuts", "?", () => overlays.OpenHelpOverlay())
                .AddSeparator()
                .AddItem("Theme: Modern Gray", "F9", () => ApplyTheme(new ModernGrayTheme()))
                .AddItem("Theme: Classic", "F10", () => ApplyTheme(new ClassicTheme()))
                .AddItem("Theme: Dev Dark", "F11", () => ApplyThemeByName("SharpConsoleUI.Plugins.DeveloperTools.DevDarkTheme, SharpConsoleUI")))
            .Build();

        menu.StickyPosition = StickyPosition.Top;
        return menu;
    }

    void ApplyTheme(ITheme theme) =>
        windowSystem.ThemeStateService.SetTheme(theme);

    void ApplyThemeByName(string typeName)
    {
        var type = Type.GetType(typeName);
        if (type is not null && Activator.CreateInstance(type) is ITheme theme)
        {
            ApplyTheme(theme);
        }
    }
}
