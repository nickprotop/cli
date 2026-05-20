// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Builds a modal overlay window for displaying item detail in tabbed panes with optional action buttons.
/// </summary>
public class DetailOverlayWindow
{
    Window? _window;

    /// <summary>
    /// Builds a detail overlay window with the specified title, tabbed content, and action buttons.
    /// </summary>
    /// <param name="windowSystem">The SharpConsoleUI window system used to close the window on Escape.</param>
    /// <param name="title">The overlay window title.</param>
    /// <param name="tabs">
    /// A list of <c>(TabName, Content)</c> tuples. Each tab displays its markup content in a scrollable panel.
    /// </param>
    /// <param name="actions">
    /// A list of <c>(Label, Execute)</c> tuples. Each entry produces a toolbar button that invokes the callback.
    /// </param>
    /// <returns>A configured <see cref="Window"/> ready to be passed to <c>windowSystem.AddWindow</c>.</returns>
    public Window Build(
        ConsoleWindowSystem windowSystem,
        string title,
        IReadOnlyList<(string TabName, string Content)> tabs,
        IReadOnlyList<(string Label, Action Execute)> actions)
    {
        var tabBuilder = Controls.TabControl();

        foreach (var (tabName, content) in tabs)
        {
            var markup = new MarkupControl([content]) { Wrap = true };
            var scrollPane = Controls.ScrollablePanel()
                .AddControl(markup)
                .WithVerticalScroll(ScrollMode.Scroll)
                .WithPadding(1, 1, 1, 1)
                .Build();

            tabBuilder.AddTab(tabName, scrollPane);
        }

        var tabControl = tabBuilder.Fill().Build();

        var toolbarBuilder = Controls.Toolbar()
            .WithBelowLine(false)
            .WithAboveLine(true)
            .WithAboveLineColor(WorkbenchColors.Muted);

        foreach (var (label, execute) in actions)
        {
            toolbarBuilder.AddButton(label, (_, _) => execute());
        }

        var toolbar = toolbarBuilder.Build();

        _window = new WindowBuilder(windowSystem)
            .WithTitle(title)
            .WithColors(WorkbenchColors.Foreground, WorkbenchColors.Background)
            .Centered()
            .WithSize(120, 35)
            .AddControl(tabControl)
            .AddControl(toolbar)
            .OnKeyPressed(HandleKeyPress)
            .Build();

        return _window;

        void HandleKeyPress(object? sender, KeyPressedEventArgs e)
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape && _window is not null)
            {
                windowSystem.CloseWindow(_window, activateParent: true, force: false);
                e.Handled = true;
            }
        }
    }
}
