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
    /// Gets the primary focus target for this view (filter prompt or table).
    /// Returns <see langword="null"/> for views that do not support keyboard focus routing.
    /// </summary>
    IWindowControl? PrimaryFocusTarget => null;

    /// <summary>
    /// Gets or sets whether this view is currently visible to the user.
    /// When <see langword="true"/>, background data refreshes update the internal cache but do not
    /// rebuild the table — preserving the user's sort order, selection, and scroll position.
    /// </summary>
    bool IsActive { get; set; }

    /// <summary>
    /// Gets the current content of the detail panel, or <see langword="null"/> if no detail is shown.
    /// Returns <see langword="null"/> for views that do not have a detail panel.
    /// </summary>
    string? DetailContent => null;

    /// <summary>
    /// Gets the per-view help text shown in the help overlay — a brief description plus key shortcuts.
    /// Returns an empty string when the view provides no extra help.
    /// </summary>
    string ViewHelp => string.Empty;

    /// <summary>
    /// Gets the actions available for the currently selected row.
    /// Consumed by the keyboard dispatcher, right-click context menu, and the Actions menu bar item.
    /// May include currently-unavailable actions with <see cref="ViewAction.Enabled"/> set to
    /// <see langword="false"/>; consumers must check <see cref="ViewAction.Enabled"/> before invoking.
    /// </summary>
    IReadOnlyList<ViewAction> ViewActions => [];

    /// <summary>
    /// Moves the selection one row toward the end of the table. No-op for views without a table.
    /// </summary>
    void MoveSelectionDown()
    {
    }

    /// <summary>
    /// Moves the selection one row toward the top of the table. No-op for views without a table.
    /// </summary>
    void MoveSelectionUp()
    {
    }

    /// <summary>
    /// Jumps the selection to the first row in the table. No-op for views without a table.
    /// </summary>
    void JumpToFirstRow()
    {
    }

    /// <summary>
    /// Jumps the selection to the last row in the table. No-op for views without a table.
    /// </summary>
    void JumpToLastRow()
    {
    }

    /// <summary>
    /// Toggles the detail pane open or closed with an animation.
    /// Views without a detail pane may leave this as a no-op.
    /// </summary>
    void ToggleDetailPane()
    {
    }

    /// <summary>
    /// Populates the framework navigation panel with this view's controls.
    /// Called each time the user navigates to this view; implementations must call
    /// <see cref="SharpConsoleUI.Controls.ScrollablePanelControl.ClearContents"/> first and then
    /// add all controls directly to <paramref name="panel"/> so the framework can propagate a
    /// bounded height to fill-aligned children.
    /// </summary>
    /// <param name="panel">The framework <see cref="SharpConsoleUI.Controls.ScrollablePanelControl"/> provided by the navigation view.</param>
    /// <param name="windowSystem">The SharpConsoleUI window system.</param>
    void PopulateContent(SharpConsoleUI.Controls.ScrollablePanelControl panel, ConsoleWindowSystem windowSystem);

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

    /// <summary>
    /// Applies the given filter string and rebuilds the table rows. No-op for views without a filter.
    /// </summary>
    /// <param name="filter">The filter text to apply.</param>
    void SetFilter(string filter)
    {
    }

    /// <summary>Advances to the next page of results. No-op for views without pagination.</summary>
    void NextPage()
    {
    }

    /// <summary>Goes back to the previous page of results. No-op for views without pagination.</summary>
    void PreviousPage()
    {
    }
}
