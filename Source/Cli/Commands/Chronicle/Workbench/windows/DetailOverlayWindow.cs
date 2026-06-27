// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Themes;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Builds a modal overlay window for displaying read-only item detail, with optional action buttons.
/// The detail is a single scrolling markup document that fills the dialog body.
/// </summary>
public class DetailOverlayWindow
{
    MarkupControl? _content;
    Window? _window;

    /// <summary>
    /// Gets the markup control backing the detail body. Lets callers replace the content after
    /// <see cref="Build"/> (for example, when part of the detail is populated by an async fetch) via
    /// <see cref="MarkupControl.SetContent"/>.
    /// </summary>
    public MarkupControl? Content => _content;

    /// <summary>
    /// Builds a detail overlay window with the specified title, markup content, and action buttons.
    /// </summary>
    /// <param name="windowSystem">The SharpConsoleUI window system used to close the window on Escape.</param>
    /// <param name="title">The overlay window title.</param>
    /// <param name="content">The detail content as markup (colors are rendered, not stripped).</param>
    /// <param name="actions">
    /// A list of <c>(Label, Key, Execute)</c> tuples. Each entry produces a button that invokes the
    /// callback; when <c>Key</c> is set, that key also triggers the action while the modal is open.
    /// </param>
    /// <returns>A configured <see cref="Window"/> ready to be passed to <c>windowSystem.AddWindow</c>.</returns>
    public Window Build(
        ConsoleWindowSystem windowSystem,
        string title,
        string content,
        IReadOnlyList<(string Label, ConsoleKey? Key, Action Execute)> actions)
    {
        var theme = new WorkbenchTheme(windowSystem);

        // A markup control (rendering colors) hosted in a vertically-filling scrollable panel: the panel
        // fills the dialog body and scrolls when the detail overflows.
        _content = Controls.Markup()
            .AddLines(content.Split('\n'))
            .WithMargin(1, 1, 1, 1)
            .Build();

        var panel = Controls.ScrollablePanel()
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithPadding(1, 0, 1, 0)
            .Build();
        panel.AddControl(_content);

        // Close the reading modal before running the action, so the action's own UI (a confirm dialog or
        // toast) is not stacked underneath it. CloseOnAction handles the button-click path; the key path
        // below closes explicitly before executing.
        var dialogButtons = actions
            .Select(a => new DialogButton(a.Label, ColorRole.Primary, a.Execute))
            .ToList();

        _window = WorkbenchUi.BuildDialog(
            windowSystem,
            theme,
            title,
            [panel],
            dialogButtons,
            new DialogOptions
            {
                FillBody = true,
                CloseOnAction = true,
                Width = Math.Min(120, Console.WindowWidth - 4),
                Height = Math.Min(35, Console.WindowHeight - 4),

                // Let the action shortcut keys fire while the modal is open — close first, then run, so a
                // button captioned "Replay (R)" responds to R exactly like its click, matching the view.
                OnKey = (keyInfo, close) =>
                {
                    foreach (var action in actions)
                    {
                        if (action.Key is { } key && keyInfo.Key == key)
                        {
                            close();
                            action.Execute();
                            return true;
                        }
                    }

                    return false;
                }
            });

        return _window;
    }
}
