// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SColor = SharpConsoleUI.Color;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Provides SharpConsoleUI-compatible color constants using a vivid Tokyo Night Storm inspired palette.
/// </summary>
public static class WorkbenchColors
{
    /// <summary>
    /// The primary accent color — electric blue (Tokyo Night: Blue).
    /// </summary>
    public static readonly SColor Accent = new(122, 162, 247, 255);

    /// <summary>
    /// A muted blue-grey for secondary text.
    /// </summary>
    public static readonly SColor Muted = new(86, 95, 137, 255);

    /// <summary>
    /// The success color — vivid neon green (Tokyo Night: Green).
    /// </summary>
    public static readonly SColor Success = new(115, 218, 118, 255);

    /// <summary>
    /// The warning color — vivid amber (Tokyo Night: Warning).
    /// </summary>
    public static readonly SColor Warning = new(224, 175, 104, 255);

    /// <summary>
    /// The danger/error color — vivid coral-red (Tokyo Night: Red/Pink).
    /// </summary>
    public static readonly SColor Danger = new(247, 118, 142, 255);

    /// <summary>
    /// A very dark navy-black background color (GitHub dark background).
    /// </summary>
    public static readonly SColor Background = new(13, 17, 23, 255);

    /// <summary>
    /// A dark blue-grey surface color for panels.
    /// </summary>
    public static readonly SColor Surface = new(22, 27, 39, 255);

    /// <summary>
    /// The primary foreground text color — cold white-blue (Tokyo Night: Foreground).
    /// </summary>
    public static readonly SColor Foreground = new(192, 202, 245, 255);

    /// <summary>
    /// Mauve/purple accent for variety in stream palettes and indicators.
    /// </summary>
    public static readonly SColor Mauve = new(187, 154, 247, 255);

    /// <summary>
    /// Teal/cyan accent for variety in stream palettes and indicators.
    /// </summary>
    public static readonly SColor Teal = new(42, 195, 222, 255);
}
