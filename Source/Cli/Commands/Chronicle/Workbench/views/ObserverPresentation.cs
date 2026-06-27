// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Observation;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Shared presentation helpers for observer state — a single source of the sort order, status glyph,
/// and theme color used wherever observers are rendered (the Observers view and the observers-for-event-type overlay).
/// </summary>
public static class ObserverPresentation
{
    /// <summary>
    /// Returns the sort rank for an observer's running state — problem states first (disconnected,
    /// replaying) so they surface at the top of a list.
    /// </summary>
    /// <param name="observer">The observer.</param>
    /// <returns>A sort rank (lower sorts first).</returns>
    public static int SortOrder(ObserverInformation observer) => observer.RunningState switch
    {
        ObserverRunningState.Disconnected => 0,
        ObserverRunningState.Replaying => 1,
        ObserverRunningState.Active => 2,
        ObserverRunningState.Suspended => 3,
        _ => 4
    };

    /// <summary>
    /// Returns the status glyph for an observer's running state.
    /// </summary>
    /// <param name="observer">The observer.</param>
    /// <returns>A single-character status glyph.</returns>
    public static string Icon(ObserverInformation observer) => observer.RunningState switch
    {
        ObserverRunningState.Active => "●",
        ObserverRunningState.Replaying => "▲",
        ObserverRunningState.Disconnected => "⊘",
        _ => "○"
    };

    /// <summary>
    /// Returns the theme color markup for an observer's running state.
    /// </summary>
    /// <param name="observer">The observer.</param>
    /// <param name="theme">The workbench theme to resolve colors from.</param>
    /// <returns>A color markup string for use in markup-rendered content.</returns>
    public static string StateColor(ObserverInformation observer, WorkbenchTheme theme) => observer.RunningState switch
    {
        ObserverRunningState.Active => theme.Success.ToMarkup(),
        ObserverRunningState.Replaying => theme.Warning.ToMarkup(),
        ObserverRunningState.Disconnected => theme.Danger.ToMarkup(),
        _ => theme.Muted.ToMarkup()
    };
}
