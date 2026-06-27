// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Themes;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Builds a modal overlay window for displaying item detail in tabbed panes with optional action buttons.
/// </summary>
public class DetailOverlayWindow
{
    readonly Dictionary<string, MultilineEditControl> _tabEditors = [];
    Window? _window;

    /// <summary>
    /// Gets the read-only editors backing each tab, keyed by tab name. Lets callers update a tab's
    /// content after <see cref="Build"/> (for example, when a tab is populated by an async fetch).
    /// </summary>
    public IReadOnlyDictionary<string, MultilineEditControl> TabEditors => _tabEditors;

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
        var theme = new WorkbenchTheme(windowSystem);
        var tabBuilder = Controls.TabControl();

        foreach (var (tabName, content) in tabs)
        {
            // Use a read-only MultilineEdit so text can be selected with mouse/keyboard and copied.
            // Markup is stripped to plain text — colors are not rendered in editable controls.
            var plainText = Markup.Remove(content);
            var editor = Controls.MultilineEdit(plainText)
                .AsReadOnly(true)
                .WrapWords()
                .WithVerticalScrollbar(ScrollbarVisibility.Auto)
                .WithSelectionColors(theme.Accent, theme.Background)
                .WithFocusedColors(theme.Foreground, theme.Background)
                .Build();

            tabBuilder.AddTab(tabName, editor);
            _tabEditors[tabName] = editor;
        }

        var tabControl = tabBuilder.Fill().Build();

        var dialogButtons = actions
            .Select(a => new DialogButton(a.Label, ColorRole.Primary, a.Execute))
            .ToList();

        _window = WorkbenchUi.BuildDialog(
            windowSystem,
            theme,
            title,
            [tabControl],
            dialogButtons,
            new DialogOptions
            {
                FillBody = true,
                CloseOnAction = false,
                Width = Math.Min(120, Console.WindowWidth - 4),
                Height = Math.Min(35, Console.WindowHeight - 4)
            });

        return _window;
    }
}
