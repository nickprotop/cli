// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Controls;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Contract for a workbench view — a navigation-item content pane that can receive live data updates.
/// </summary>
public interface IWorkbenchView : IDisposable
{
    /// <summary>
    /// Gets or sets the callback invoked when the filter input gains or loses focus.
    /// <see langword="true"/> = filter focused; <see langword="false"/> = filter unfocused.
    /// Wired by <c>MainWindow</c> to gate global keyboard shortcuts.
    /// Views that do not have a filter bar may leave this as a no-op.
    /// </summary>
    Action<bool>? OnFilterFocusChanged
    {
        get => null;
        set { }
    }

    /// <summary>
    /// Builds the initial control hierarchy for this view. Called once during window construction.
    /// </summary>
    /// <param name="windowSystem">The SharpConsoleUI window system.</param>
    /// <returns>The root <see cref="IWindowControl"/> to embed in the navigation pane.</returns>
    IWindowControl BuildContent(ConsoleWindowSystem windowSystem);

    /// <summary>
    /// Called on the background refresh thread whenever new data arrives.
    /// Implementations must only update control properties — never create new controls here.
    /// </summary>
    /// <param name="data">The latest workbench data snapshot.</param>
    void UpdateData(WorkbenchData data);

    /// <summary>
    /// Activates the filter bar for this view, if the view supports filtering.
    /// Default implementation is a no-op for views that do not have a filter bar.
    /// </summary>
    /// <param name="window">The host <see cref="Window"/> used to set focus.</param>
    void ActivateFilter(Window window)
    {
    }

    /// <summary>
    /// Clears the current filter and returns focus to the table. No-op for views without a filter.
    /// </summary>
    void ClearFilter()
    {
    }
}
