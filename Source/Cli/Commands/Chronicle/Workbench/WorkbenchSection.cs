// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SColor = SharpConsoleUI.Color;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Identifies a navigation pane section header and its display color.
/// Instances are held as <c>static readonly</c> fields in <see cref="WorkbenchViewRegistry"/>
/// and compared by reference to detect section boundaries.
/// </summary>
/// <param name="Title">The all-caps section header text shown in the nav pane.</param>
/// <param name="Color">The color used for the section header and its items.</param>
public sealed record WorkbenchSection(string Title, SColor Color);
