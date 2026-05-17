// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// The current action lifecycle state of the workbench.
/// </summary>
public enum WorkbenchActionState
{
    /// <summary>Normal operation — no action pending.</summary>
    None,

    /// <summary>An action is queued and awaiting user confirmation.</summary>
    AwaitingConfirmation,

    /// <summary>An action is being executed against the server.</summary>
    Executing,

    /// <summary>An action has completed; the result is available.</summary>
    Completed
}

/// <summary>
/// Describes a pending in-place action that requires user confirmation before execution.
/// </summary>
/// <param name="Description">Human-readable label shown in the confirmation prompt.</param>
/// <param name="SuccessMessage">Message displayed after successful execution.</param>
/// <param name="Execute">The gRPC call to invoke when the user confirms.</param>
public record PendingAction(
    string Description,
    string SuccessMessage,
    Func<CancellationToken, Task> Execute);

/// <summary>
/// Carries all render-relevant state for a single frame of the workbench, keeping the Build() signature stable.
/// </summary>
/// <param name="View">The currently active view (primary or detail).</param>
/// <param name="SelectedIndex">The selected list-item index within the current view.</param>
/// <param name="Interval">The current refresh interval in seconds.</param>
/// <param name="IsRefreshing">Whether a data fetch is currently in progress.</param>
/// <param name="ActionState">The current action lifecycle state.</param>
/// <param name="PendingActionDescription">Description of the action awaiting confirmation, or <see langword="null"/>.</param>
/// <param name="ActionResult">Result message after action completion, or <see langword="null"/>.</param>
/// <param name="IsActionError">Whether the most recent action completed with an error.</param>
/// <param name="FocusedId">The identifier of the item shown in a detail view.</param>
/// <param name="ScrollOffset">The content scroll offset (line number) in detail views.</param>
/// <param name="Breadcrumb">Navigation breadcrumb path, e.g. ["Observers", "SomeProjection"].</param>
/// <param name="FilterText">The active inline filter string, or empty when no filter is applied.</param>
/// <param name="FilterInputMode">Whether the user is actively typing a filter (entered via '/').</param>
/// <param name="EventLogAscending">Whether the event log is sorted ascending (oldest first) instead of the default descending (newest first).</param>
/// <param name="EventLogPage">The current zero-based page index within the event log (50 events per page).</param>
public record WorkbenchRenderState(
    WorkbenchView View,
    int SelectedIndex,
    int Interval,
    bool IsRefreshing,
    WorkbenchActionState ActionState,
    string? PendingActionDescription,
    string? ActionResult,
    bool IsActionError = false,
    string FocusedId = "",
    int ScrollOffset = 0,
    IReadOnlyList<string>? Breadcrumb = null,
    string FilterText = "",
    bool FilterInputMode = false,
    bool EventLogAscending = false,
    int EventLogPage = 0);
