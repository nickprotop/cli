// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Manages destructive-action confirmation for the workbench: queuing a pending action, waiting for
/// Y/N confirmation, and executing the action while streaming status messages back to the caller.
/// </summary>
/// <param name="updateStatus">Callback invoked to display a status message in the workbench status bar right segment.</param>
public class WorkbenchActionHandler(Action<string> updateStatus)
{
    (string Description, Func<Task> Execute)? _pendingAction;

    /// <summary>
    /// Gets a value indicating whether there is a destructive action waiting for Y/N confirmation.
    /// </summary>
    public bool IsPendingAction => _pendingAction is not null;

    /// <summary>
    /// Gets or sets a value indicating whether a text input control currently has keyboard focus.
    /// When <see langword="true"/>, most shortcut keys are suppressed so the user can type freely.
    /// </summary>
    public bool TextInputFocused { get; set; }

    /// <summary>
    /// Queues a destructive action for confirmation and shows a Y/N prompt in the status bar.
    /// </summary>
    /// <param name="description">Short human-readable description of the action.</param>
    /// <param name="action">Async delegate that performs the action when confirmed.</param>
    public void ExecuteAction(string description, Func<Task> action)
    {
        _pendingAction = (description, action);
        var war = WorkbenchColors.Warning.ToMarkup();
        var acc = WorkbenchColors.Accent.ToMarkup();
        var mut = WorkbenchColors.Muted.ToMarkup();
        updateStatus(
            $"[{war}]⚡ {description}?[/]   [bold {acc}][Y][/] [{mut}]Confirm[/]   [bold {acc}][N][/] [{mut}]Cancel[/]");
    }

    /// <summary>
    /// Queues a bulk action that iterates over <paramref name="items"/> and calls <paramref name="perItem"/> for each.
    /// The action is queued with a Y/N confirmation prompt before anything is executed.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="description">Short human-readable description of the bulk action.</param>
    /// <param name="items">The collection of items to act on.</param>
    /// <param name="perItem">Async delegate called once per item.</param>
    public void ConfirmThenExecuteAll<T>(string description, IReadOnlyList<T> items, Func<T, Task> perItem)
    {
        ExecuteAction(description, async () =>
        {
            foreach (var item in items)
            {
                await perItem(item);
            }
        });
    }

    /// <summary>
    /// Handles a key press while a pending action is queued.
    /// Returns <see langword="true"/> if the key was consumed (whether confirmed, cancelled, or a no-op
    /// because a confirmation is already in progress).
    /// </summary>
    /// <param name="keyInfo">The key that was pressed.</param>
    /// <param name="onCancelled">Invoked after cancellation so the caller can refresh the status bar.</param>
    /// <returns><see langword="true"/> if the key was consumed by the pending-action handler.</returns>
    public bool HandlePendingKeyPress(ConsoleKeyInfo keyInfo, Action onCancelled)
    {
        if (_pendingAction is null)
        {
            return false;
        }

        switch (keyInfo.Key)
        {
            case ConsoleKey.Y:
                var pending = _pendingAction.Value;
                _pendingAction = null;
                RunPendingAction(pending.Description, pending.Execute);
                return true;

            case ConsoleKey.N:
            case ConsoleKey.Escape:
                _pendingAction = null;
                onCancelled();
                return true;
        }

        // Any other key while a confirmation is pending — consume it.
        return true;
    }

    void RunPendingAction(string description, Func<Task> action)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                updateStatus($"[{WorkbenchColors.Warning.ToMarkup()}]⟳ {description}...[/]");
                await action();
                updateStatus($"[{WorkbenchColors.Success.ToMarkup()}]✓ Done[/]");

                await Task.Delay(3000);
                updateStatus(string.Empty);
            }
            catch (Exception ex)
            {
                var msg = ex.Message.Length > 60 ? ex.Message[..60] : ex.Message;
                updateStatus($"[{WorkbenchColors.Danger.ToMarkup()}]✗ {msg}[/]");
            }
        });
    }
}
