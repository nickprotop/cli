// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Themes;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Severity of a workbench dialog. Drives the border and rule color so destructive and cautionary
/// dialogs read distinctly from informational ones.
/// </summary>
public enum DialogSeverity
{
    /// <summary>Informational — a dimmed-accent border that follows the theme.</summary>
    Default,

    /// <summary>Cautionary — a warning-colored border.</summary>
    Warning,

    /// <summary>Destructive — a danger-colored border.</summary>
    Danger
}

/// <summary>
/// One action button on a workbench dialog toolbar.
/// </summary>
/// <param name="Label">The button caption.</param>
/// <param name="Role">The semantic color role used to style the button.</param>
/// <param name="OnClick">Invoked when the button is clicked (after the dialog closes, unless <see cref="DialogOptions.CloseOnAction"/> is false).</param>
public readonly record struct DialogButton(string Label, ColorRole Role, Action OnClick);

/// <summary>
/// Optional configuration for <see cref="WorkbenchUi.BuildDialog(ConsoleWindowSystem, WorkbenchTheme, string, System.Collections.Generic.IReadOnlyList{IWindowControl}, System.Collections.Generic.IReadOnlyList{DialogButton}, DialogOptions?)"/>.
/// </summary>
public sealed class DialogOptions
{
    /// <summary>
    /// Gets the dialog severity, which drives the border and rule color. Defaults to
    /// <see cref="DialogSeverity.Default"/>.
    /// </summary>
    public DialogSeverity Severity { get; init; } = DialogSeverity.Default;

    /// <summary>
    /// Gets a value indicating whether the body fills the available height (for controls that manage
    /// their own scrolling, e.g. a tab control or editor) instead of being hosted in a scrollable
    /// panel. Defaults to <see langword="false"/> (scrollable body).
    /// </summary>
    public bool FillBody { get; init; }

    /// <summary>
    /// Gets a value indicating whether a trailing "Close" button is appended to the toolbar. Defaults
    /// to <see langword="true"/>. Set to <see langword="false"/> for dialogs that supply their own
    /// explicit Cancel/dismiss button.
    /// </summary>
    public bool ShowCloseButton { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether clicking an action button automatically closes the dialog
    /// before invoking its callback. Defaults to <see langword="true"/>.
    /// </summary>
    public bool CloseOnAction { get; init; } = true;

    /// <summary>
    /// Gets an optional custom key handler invoked before the default Escape-to-close handling. Return
    /// <see langword="true"/> to mark the key handled and suppress the default behavior. Receives the
    /// key and a close callback. Used for dialogs with a bespoke key contract (e.g. Y/N confirmation).
    /// </summary>
    public Func<ConsoleKeyInfo, Action, bool>? OnKey { get; init; }

    /// <summary>
    /// Gets an optional explicit width override. When null, the width is derived from the terminal size.
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Gets an optional explicit height override. When null, the height is derived from the terminal size.
    /// </summary>
    public int? Height { get; init; }
}
