// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Builds and shows the right-click context menu of view actions — a small cursor-anchored popup that
/// closes on selection, Escape, or losing focus. Extracted from the table view so the menu chrome lives
/// in one place.
/// </summary>
public static class WorkbenchContextMenu
{
    /// <summary>
    /// Shows a context menu of <paramref name="actions"/> at the given screen position, sized to its
    /// content and clamped to the terminal bounds. Each item runs its action and closes the menu.
    /// </summary>
    /// <param name="windowSystem">The window system that hosts the popup.</param>
    /// <param name="theme">The workbench theme for menu colors.</param>
    /// <param name="x">The desired left position in screen columns.</param>
    /// <param name="y">The desired top position in screen rows.</param>
    /// <param name="actions">The actions to list. Callers should pass only enabled actions.</param>
    public static void Show(ConsoleWindowSystem windowSystem, WorkbenchTheme theme, int x, int y, IReadOnlyList<ViewAction> actions)
    {
        if (actions.Count == 0)
        {
            return;
        }

        var menuBuilder = Controls.Menu().Vertical()
            .WithMenuBarColors(theme.Background, theme.Foreground, theme.Accent, theme.Background)
            .WithDropdownColors(theme.Background, theme.Foreground, theme.Accent, theme.Background);

        foreach (var action in actions)
        {
            menuBuilder.AddItem(action.Label, action.KeyHint ?? string.Empty, action.Execute);
        }

        var menu = menuBuilder.Build();
        Window? contextWindow = null;

        menu.ItemSelected += (_, _) => windowSystem.CloseWindow(contextWindow, activateParent: true, force: false);

        var width = Math.Max(20, Width(actions));
        var height = actions.Count + 2;
        var clampedX = Math.Max(0, Math.Min(x, Console.WindowWidth - width));
        var clampedY = Math.Max(0, Math.Min(y, Console.WindowHeight - height));

        contextWindow = new WindowBuilder(windowSystem)
            .WithTitle(string.Empty)
            .HideTitle()
            .HideCloseButton()
            .WithColors(theme.Foreground, theme.Background)
            .WithSize(width, height)
            .AtPosition(clampedX, clampedY)
            .WithCloseOnDeactivate(true)
            .AddControl(menu)
            .OnKeyPressed((_, ke) =>
            {
                if (ke.KeyInfo.Key == ConsoleKey.Escape)
                {
                    windowSystem.CloseWindow(contextWindow, activateParent: true, force: false);
                    ke.Handled = true;
                }
            })
            .Build();

        windowSystem.AddWindow(contextWindow, activateWindow: true);
    }

    /// <summary>
    /// Computes the menu width from the widest label plus the widest key hint.
    /// </summary>
    /// <param name="actions">The actions to size for.</param>
    /// <returns>The menu width in columns.</returns>
    static int Width(IReadOnlyList<ViewAction> actions)
    {
        var maxLabel = actions.Max(a => a.Label.Length);
        var maxHint = actions.Max(a => a.KeyHint?.Length ?? 0);
        return maxLabel + (maxHint > 0 ? maxHint + 4 : 0) + 4;
    }
}
