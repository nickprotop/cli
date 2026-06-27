// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Themes;
using SColor = SharpConsoleUI.Color;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Identifies a navigation section whose accent is derived from the active theme.
/// </summary>
public enum WorkbenchSectionAccent
{
    /// <summary>
    /// Overview section.
    /// </summary>
    Overview,

    /// <summary>
    /// Observation section (observers, failures, jobs, recommendations).
    /// </summary>
    Observation,

    /// <summary>
    /// Events section.
    /// </summary>
    Events,

    /// <summary>
    /// Projections section.
    /// </summary>
    Projections,

    /// <summary>
    /// Server section.
    /// </summary>
    Server
}

/// <summary>
/// Theme-aware color accessor for the workbench. Chrome colors read from the active
/// <see cref="ITheme"/>; semantic colors resolve from <see cref="ColorRole"/>; nav section
/// accents are derived from the theme accent. Re-derives and raises <see cref="Changed"/>
/// when the window system's theme changes.
/// </summary>
public sealed class WorkbenchTheme
{
    readonly ConsoleWindowSystem _windowSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkbenchTheme"/> class and subscribes to
    /// theme changes on the window system.
    /// </summary>
    /// <param name="windowSystem">The window system that owns the active theme.</param>
    public WorkbenchTheme(ConsoleWindowSystem windowSystem)
    {
        _windowSystem = windowSystem;
        _windowSystem.ThemeStateService.ThemeChanged += (_, _) => Changed?.Invoke();
    }

    /// <summary>
    /// Occurs after the active theme changes; accessors already reflect the new theme.
    /// </summary>
    public event Action? Changed;

    /// <summary>Window background — from the active theme.</summary>
    public SColor Background => Theme.WindowBackgroundColor;

    /// <summary>Content / surface background — from the active theme's modal/surface color.</summary>
    public SColor Surface => Theme.ModalBackgroundColor ?? Background;

    /// <summary>Primary foreground — from the active theme.</summary>
    public SColor Foreground => Theme.WindowForegroundColor;

    /// <summary>Navigation selection background — from the active theme's selected-button color.</summary>
    public SColor SelectedBg => Theme.ButtonSelectedBackgroundColor ?? Accent.Shade(0.35);

    /// <summary>Content pane border — from the active theme's active border color.</summary>
    public SColor ContentBorder => Theme.ActiveBorderForegroundColor ?? Accent.Shade(0.45);

    /// <summary>Dim chrome border — a darker shade of the content border.</summary>
    public SColor ChromeBorder => ContentBorder.Shade(0.35);

    /// <summary>Muted secondary text — a low-contrast blend toward the background.</summary>
    public SColor Muted => Foreground.Mix(Background, 0.55);

    /// <summary>Primary accent — resolved from <see cref="ColorRole.Primary"/>.</summary>
    public SColor Accent => Role(ColorRole.Primary);

    /// <summary>
    /// A dimmed accent — the accent blended toward the background so it reads as quiet chrome
    /// (e.g. the main window border) rather than a bright highlight. Follows the theme accent.
    /// </summary>
    public SColor DimAccent => Accent.Mix(Background, 0.6);

    /// <summary>Success / healthy state — resolved from <see cref="ColorRole.Success"/>.</summary>
    public SColor Success => Role(ColorRole.Success);

    /// <summary>Warning state — resolved from <see cref="ColorRole.Warning"/>.</summary>
    public SColor Warning => Role(ColorRole.Warning);

    /// <summary>Danger / error state — resolved from <see cref="ColorRole.Danger"/>.</summary>
    public SColor Danger => Role(ColorRole.Danger);

    /// <summary>Informational accent — resolved from <see cref="ColorRole.Info"/> (was Teal).</summary>
    public SColor Teal => Role(ColorRole.Info);

    /// <summary>Secondary accent (was Mauve) — derived from the accent, hue-shifted via a blend.</summary>
    public SColor Mauve => Accent.Mix(Danger, 0.4);

    ITheme Theme => _windowSystem.Theme;

    /// <summary>
    /// Derives the accent for a navigation section from the active theme accent so the rail keeps
    /// its per-section character under any theme. The result is contrast-checked against the
    /// background so it stays legible.
    /// </summary>
    /// <param name="kind">The section to derive an accent for.</param>
    /// <returns>A theme-derived, legible accent color for the section.</returns>
    public SColor SectionAccent(WorkbenchSectionAccent kind)
    {
        var baseColor = kind switch
        {
            WorkbenchSectionAccent.Overview => Accent,
            WorkbenchSectionAccent.Observation => Accent.Mix(Warning, 0.6),
            WorkbenchSectionAccent.Events => Accent.Mix(Teal, 0.7),
            WorkbenchSectionAccent.Projections => Mauve,
            WorkbenchSectionAccent.Server => Accent.Shade(0.25),
            _ => Accent
        };

        return PaletteColors.EnsureContrast(baseColor, Background);
    }

    SColor Role(ColorRole role) => ColorRoleResolver.Resolve(role, Theme).Text;
}
