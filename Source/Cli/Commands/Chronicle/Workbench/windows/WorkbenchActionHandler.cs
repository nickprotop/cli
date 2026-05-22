// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Shows centered confirmation dialogs for destructive workbench actions and executes them on confirmation.
/// </summary>
/// <param name="windowSystem">The SharpConsoleUI window system — used to open the confirmation modal.</param>
/// <param name="updateStatus">Callback invoked to display progress / success / error messages in the top panel.</param>
public class WorkbenchActionHandler(ConsoleWindowSystem windowSystem, Action<string> updateStatus)
{
    /// <summary>
    /// Gets or sets a value indicating whether a text input control currently has keyboard focus.
    /// When <see langword="true"/>, most shortcut keys are suppressed so the user can type freely.
    /// </summary>
    public bool TextInputFocused { get; set; }

    /// <summary>
    /// Opens a centered confirmation dialog for <paramref name="description"/>.
    /// Executes <paramref name="action"/> when the user confirms; dismisses on cancel.
    /// </summary>
    /// <param name="description">Short human-readable description of the action.</param>
    /// <param name="action">Async delegate that performs the action when confirmed.</param>
    public void ExecuteAction(string description, Func<Task> action) =>
        ShowConfirmationDialog(description, action);

    /// <summary>
    /// Opens a confirmation dialog for a bulk operation over <paramref name="items"/>.
    /// Calls <paramref name="perItem"/> for each item in sequence when confirmed.
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

    void ShowConfirmationDialog(string description, Func<Task> action)
    {
        var mut = WorkbenchColors.Muted.ToMarkup();
        var warn = WorkbenchColors.Warning.ToMarkup();
        var acc = WorkbenchColors.Accent.ToMarkup();

        var body = Controls.Markup()
            .AddEmptyLine()
            .AddLine($"  [{warn}]⚡[/]  {description}")
            .AddEmptyLine()
            .AddLine($"  [{mut}]This action cannot be undone.[/]")
            .AddEmptyLine()
            .AddLine($"  [{mut}]Press[/] [bold {acc}]Enter[/] [{mut}]or[/] [bold {acc}]Y[/] [{mut}]to confirm, or[/] [bold {acc}]Escape[/] [{mut}]/ [bold {acc}]N[/] [{mut}]to cancel.[/]")
            .AddEmptyLine()
            .Build();

        Window? dialog = null;
        dialog = new WindowBuilder(windowSystem)
            .WithTitle(" Confirm Action ")
            .WithColors(WorkbenchColors.Foreground, WorkbenchColors.Surface)
            .WithSize(64, 10)
            .Centered()
            .AddControl(body)
            .OnKeyPressed((_, e) =>
            {
                switch (e.KeyInfo.Key)
                {
                    case ConsoleKey.Enter:
                    case ConsoleKey.Y:
                        windowSystem.CloseWindow(dialog, activateParent: true, force: false);
                        RunAction(description, action);
                        e.Handled = true;
                        break;

                    case ConsoleKey.Escape:
                    case ConsoleKey.N:
                        windowSystem.CloseWindow(dialog, activateParent: true, force: false);
                        e.Handled = true;
                        break;
                }
            })
            .Build();

        windowSystem.AddWindow(dialog, activateWindow: true);
    }

    void RunAction(string description, Func<Task> action)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                updateStatus($"⟳ {description}…");
                await action();
                updateStatus("✓ Done");
                await Task.Delay(3000);
                updateStatus(string.Empty);
            }
            catch (Exception ex)
            {
                var msg = ex.Message.Length > 80 ? ex.Message[..80] : ex.Message;
                updateStatus($"✗ {msg}");
            }
        });
    }
}
