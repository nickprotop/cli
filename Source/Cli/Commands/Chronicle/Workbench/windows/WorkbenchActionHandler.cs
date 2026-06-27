// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Themes;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Shows centered confirmation dialogs for destructive workbench actions and executes them on confirmation.
/// </summary>
/// <param name="windowSystem">The SharpConsoleUI window system — used to open the confirmation modal.</param>
/// <param name="updateStatus">Callback invoked to display progress / success / error messages in the top panel.</param>
public class WorkbenchActionHandler(ConsoleWindowSystem windowSystem, Action<string> updateStatus)
{
    /// <summary>
    /// Maximum number of affected items listed individually in the danger modal before collapsing
    /// the remainder into a "+K more" line.
    /// </summary>
    const int MaxAffectedListed = 8;

    readonly WorkbenchTheme _theme = new(windowSystem);

    /// <summary>
    /// Gets or sets a value indicating whether a text input control currently has keyboard focus.
    /// When <see langword="true"/>, most shortcut keys are suppressed so the user can type freely.
    /// </summary>
    public bool TextInputFocused { get; set; }

    /// <summary>
    /// Opens the danger-confirmation modal for a single-target <paramref name="description"/>.
    /// Executes <paramref name="action"/> when the user confirms (explicit Y); dismisses on cancel.
    /// </summary>
    /// <param name="description">Short human-readable description of the action.</param>
    /// <param name="action">Async delegate that performs the action when confirmed.</param>
    public void ExecuteAction(string description, Func<Task> action) =>
        ConfirmDanger(description, [], action);

    /// <summary>
    /// Opens a danger-confirmation dialog for a bulk operation over <paramref name="items"/>.
    /// Calls <paramref name="perItem"/> for each item in sequence when confirmed. The modal lists the
    /// affected items (capped, then "+K more") when a <paramref name="labelFor"/> selector is supplied.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="description">Short human-readable description of the bulk action.</param>
    /// <param name="items">The collection of items to act on.</param>
    /// <param name="perItem">Async delegate called once per item.</param>
    /// <param name="labelFor">Optional selector rendering each item for the affected list. When null, no list is shown.</param>
    public void ConfirmThenExecuteAll<T>(string description, IReadOnlyList<T> items, Func<T, Task> perItem, Func<T, string>? labelFor = null)
    {
        List<string> affected = labelFor is null ? [] : [.. items.Select(labelFor)];
        ConfirmDanger(description, affected, async () =>
        {
            foreach (var item in items)
            {
                await perItem(item);
            }
        });
    }

    /// <summary>
    /// Shows the danger-confirmation modal: a <see cref="ColorRole.Danger"/>-bordered, content-sized
    /// window with a warning title, the action message, an optional affected-item list, and a
    /// Cancel-focused key contract (Enter / Escape / N cancel; only an explicit Y confirms) so a
    /// reflexive Enter never triggers the destructive action.
    /// </summary>
    /// <param name="description">The action message.</param>
    /// <param name="affected">Affected-item labels to list (capped at <see cref="MaxAffectedListed"/>); empty for single-target actions.</param>
    /// <param name="action">Async delegate that performs the action when confirmed.</param>
    void ConfirmDanger(string description, List<string> affected, Func<Task> action)
    {
        var mut = _theme.Muted.ToMarkup();
        var dan = _theme.Danger.ToMarkup();
        var acc = _theme.Accent.ToMarkup();

        var markup = Controls.Markup()
            .AddEmptyLine()
            .AddLine($"  [{dan}]⚠[/]  {description}")
            .AddEmptyLine();

        if (affected.Count > 0)
        {
            markup.AddLine($"  [{mut}]Affected:[/]");
            foreach (var label in affected.Take(MaxAffectedListed))
            {
                markup.AddLine($"    [{mut}]•[/] {label}");
            }

            if (affected.Count > MaxAffectedListed)
            {
                markup.AddLine($"    [{mut}]+{affected.Count - MaxAffectedListed} more[/]");
            }

            markup.AddEmptyLine();
        }

        var body = markup
            .AddLine($"  [{mut}]This action cannot be undone.[/]")
            .AddEmptyLine()
            .AddLine($"  [bold {acc}]Y[/] [{mut}]confirm[/]   [bold {acc}]Esc[/] [{mut}]/[/] [bold {acc}]Enter[/] [{mut}]cancel[/]")
            .AddEmptyLine()
            .Build();

        // Content-sized height: chrome (border, title, rule + toolbar) plus body lines.
        var listLines = affected.Count == 0 ? 0 : Math.Min(affected.Count, MaxAffectedListed) + (affected.Count > MaxAffectedListed ? 1 : 0) + 2;
        var height = 12 + listLines;

        var dialog = WorkbenchUi.BuildDialog(
            windowSystem,
            _theme,
            "⚠ Confirm",
            [body],
            [new DialogButton("Confirm", ColorRole.Danger, () => RunAction(description, action))],
            new DialogOptions
            {
                Severity = DialogSeverity.Danger,
                Width = 64,
                Height = height,

                // Cancel-focused safety contract: only an explicit Y confirms; Enter / Esc / N cancel.
                // The toolbar Confirm button and the Y key both run the action; "Close" is the cancel.
                OnKey = (key, close) =>
                {
                    switch (key.Key)
                    {
                        case ConsoleKey.Y:
                            close();
                            RunAction(description, action);
                            return true;

                        case ConsoleKey.Enter:
                        case ConsoleKey.N:
                            close();
                            return true;

                        default:
                            return false;
                    }
                }
            });

        windowSystem.AddWindow(dialog, activateWindow: true);
    }

    void RunAction(string description, Func<Task> action)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                updateStatus($"[spinner] {description}…");
                await action();
                updateStatus("✓ Done");
                windowSystem.EnqueueOnUIThread(() =>
                    windowSystem.ToastService.Show($"{description} — done", SharpConsoleUI.Core.NotificationSeverity.Success));
                await Task.Delay(3000);
                updateStatus(string.Empty);
            }
            catch (Exception ex)
            {
                var msg = ex.Message.Length > 80 ? ex.Message[..80] : ex.Message;
                updateStatus($"✗ {msg}");
                windowSystem.EnqueueOnUIThread(() =>
                    windowSystem.ToastService.Show($"{description} failed: {msg}", SharpConsoleUI.Core.NotificationSeverity.Danger));
            }
        });
    }
}
