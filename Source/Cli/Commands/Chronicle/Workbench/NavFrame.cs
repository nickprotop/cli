// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// A frame on the workbench navigation stack, capturing view state before drilling into a detail view.
/// </summary>
/// <param name="View">The view enum value (stored as int to avoid volatile boxing).</param>
/// <param name="SelectedIndex">The list cursor position in that view.</param>
/// <param name="FocusedId">The focused item identifier in that view.</param>
public readonly record struct NavFrame(int View, int SelectedIndex, string FocusedId);
