// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SColor = SharpConsoleUI.Color;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// SharpConsoleUI-compatible color constants for the workbench — midnight-blue palette with vibrant accents.
/// </summary>
public static class WorkbenchColors
{
    /// <summary>Very deep midnight-navy background — the base window color.</summary>
    public static readonly SColor Background = new(8, 12, 28, 255);

    /// <summary>Deep navy surface — content pane background, slightly lighter than the window.</summary>
    public static readonly SColor Surface = new(16, 22, 46, 255);

    /// <summary>Primary foreground — blue-tinted white, easy on the eyes on dark backgrounds.</summary>
    public static readonly SColor Foreground = new(220, 225, 250, 255);

    /// <summary>Primary accent — electric blue, used for highlights, selected items, and borders.</summary>
    public static readonly SColor Accent = new(100, 180, 255, 255);

    /// <summary>Navigation pane selection background — rich royal blue.</summary>
    public static readonly SColor SelectedBg = new(35, 70, 150, 255);

    /// <summary>Muted secondary text — perceptible but not distracting.</summary>
    public static readonly SColor Muted = new(90, 110, 160, 255);

    /// <summary>Success / healthy state — vibrant mint green.</summary>
    public static readonly SColor Success = new(100, 220, 130, 255);

    /// <summary>Warning state — warm amber, used for the OBSERVATION nav section.</summary>
    public static readonly SColor Warning = new(240, 190, 80, 255);

    /// <summary>Danger / error state — bright coral-pink for failed partitions and errors.</summary>
    public static readonly SColor Danger = new(255, 100, 130, 255);

    /// <summary>Teal / cyan accent — used for the EVENTS nav section and event-type borders.</summary>
    public static readonly SColor Teal = new(60, 220, 200, 255);

    /// <summary>Mauve / violet accent — used for the PROJECTIONS nav section.</summary>
    public static readonly SColor Mauve = new(200, 155, 255, 255);

    /// <summary>Content pane border — subtle blue-violet, visible but not distracting.</summary>
    public static readonly SColor ContentBorder = new(45, 65, 115, 255);

    /// <summary>Dim table/panel border color — very subtle dark-blue chrome.</summary>
    public static readonly SColor ChromeBorder = new(30, 40, 80, 255);
}
