// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Describes a single user-triggerable action that a view exposes.
/// The same record is consumed by the keyboard dispatcher, the right-click context menu,
/// and the Actions menu bar item — defining the action once eliminates duplication across all three.
/// </summary>
/// <param name="Label">Display text shown in menus and the context menu.</param>
/// <param name="KeyHint">Short key hint shown alongside the label (e.g. "R"). Display only — not handled here.</param>
/// <param name="TriggerKey">The <see cref="ConsoleKey"/> that activates this action, or <see langword="null"/> for menu-only actions.</param>
/// <param name="TriggerModifiers">Required modifier keys. Use <see langword="default"/> for no modifiers.</param>
/// <param name="Execute">The delegate invoked when the action is activated.</param>
/// <param name="Enabled">Whether this action is currently available. Disabled actions are shown in the toolbar with neutral styling but cannot be triggered via keyboard or context menu.</param>
public record ViewAction(
    string Label,
    string? KeyHint,
    ConsoleKey? TriggerKey,
    ConsoleModifiers TriggerModifiers,
    Action Execute,
    bool Enabled = true);
