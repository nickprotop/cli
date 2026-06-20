// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Reflection;
using SharpConsoleUI;
using SharpConsoleUI.Themes;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// A primary theme exposed on an F-key / top-level menu slot: the label to display and the action
/// that applies it. Resolved per running SharpConsoleUI version so the slots stay valid on both.
/// </summary>
/// <param name="Label">The display label, for example <c>Modern Gray</c>.</param>
/// <param name="Apply">Applies the theme to the window system.</param>
public record WorkbenchThemeSlot(string Label, Action Apply);

/// <summary>
/// Bridges the two SharpConsoleUI theme APIs via reflection so the workbench compiles and runs
/// against both the published 2.4.78 package and the upcoming 2.4.79.
/// <para>
/// 2.4.78 exposes a process-global static <c>SharpConsoleUI.Themes.ThemeRegistry</c> and ships
/// <c>ClassicTheme</c> plus a <c>DevDarkTheme</c>; 2.4.79 removes those, moves the developer theme
/// out of the library, and replaces the static registry with a per-instance
/// <c>windowSystem.ThemeRegistryService</c>. Referencing any of those types directly would break
/// compilation against the other version, so everything here is resolved at runtime.
/// </para>
/// <para>
/// Theme application goes through the version-stable <c>ThemeStateService.SwitchTheme(name)</c>
/// (present in both versions; an unknown name is a safe no-op). The three primary slots preserve the
/// original 2.4.78 behaviour — Modern Gray / Classic / Dev Dark — and map to Modern Gray / Forest /
/// Crimson on 2.4.79 where Classic and Dev Dark no longer exist.
/// </para>
/// <para>
/// This reflection bridge is temporary: it exists only to span the 2.4.78 -> 2.4.79 transition. Once
/// the SharpConsoleUI dependency is pinned to 2.4.79+ and 2.4.78 is no longer supported, it can be
/// simplified to direct <c>windowSystem.ThemeRegistryService</c> calls and the static-registry and
/// Dev Dark fallbacks removed.
/// </para>
/// </summary>
public static class WorkbenchThemes
{
    /// <summary>
    /// Returns the three primary theme slots for the F9 / F10 / F11 shortcuts and top-level menu items,
    /// resolved for the running SharpConsoleUI version.
    /// </summary>
    /// <param name="windowSystem">The window system the slots apply themes to.</param>
    /// <returns>Exactly three slots, in F9, F10, F11 order.</returns>
    public static IReadOnlyList<WorkbenchThemeSlot> GetPrimarySlots(ConsoleWindowSystem windowSystem)
    {
        if (HasInstanceRegistry(windowSystem))
        {
            // 2.4.79: Classic and Dev Dark are gone; offer dark palette themes from the catalogue.
            return
            [
                new WorkbenchThemeSlot("Modern Gray", () => Apply(windowSystem, "ModernGray")),
                new WorkbenchThemeSlot("Forest", () => Apply(windowSystem, "Forest")),
                new WorkbenchThemeSlot("Crimson", () => Apply(windowSystem, "Crimson"))
            ];
        }

        // 2.4.78: preserve the original behaviour — Modern Gray / Classic / Dev Dark.
        return
        [
            new WorkbenchThemeSlot("Modern Gray", () => Apply(windowSystem, "ModernGray")),
            new WorkbenchThemeSlot("Classic", () => Apply(windowSystem, "Classic")),
            new WorkbenchThemeSlot("Dev Dark", () => ApplyType(windowSystem, "SharpConsoleUI.Plugins.DeveloperTools.DevDarkTheme, SharpConsoleUI"))
        ];
    }

    /// <summary>
    /// Returns the names of every theme registered with the running SharpConsoleUI version, resolving
    /// the per-instance registry (2.4.79) first and falling back to the static registry (2.4.78).
    /// Returns an empty list if neither is present.
    /// </summary>
    /// <param name="windowSystem">The window system whose per-instance registry is preferred.</param>
    /// <returns>The available theme names, or an empty list when no registry can be resolved.</returns>
    public static IReadOnlyList<string> GetAvailableThemeNames(ConsoleWindowSystem windowSystem)
    {
        var registryService = GetInstanceRegistry(windowSystem);
        if (registryService is not null &&
            InvokeGetAvailableThemeNames(registryService.GetType(), registryService) is { } instanceNames)
        {
            return instanceNames;
        }

        var staticRegistry = Type.GetType("SharpConsoleUI.Themes.ThemeRegistry, SharpConsoleUI");
        if (staticRegistry is not null &&
            InvokeGetAvailableThemeNames(staticRegistry, target: null) is { } staticNames)
        {
            return staticNames;
        }

        return [];
    }

    /// <summary>
    /// Applies a theme by name through the version-stable <c>SwitchTheme</c> API. Unknown names are a
    /// safe no-op (the library returns <see langword="false"/>), so callers can offer names that only
    /// exist on a subset of supported versions without guarding each one.
    /// </summary>
    /// <param name="windowSystem">The window system whose theme state service performs the switch.</param>
    /// <param name="themeName">The registered theme name to apply.</param>
    public static void Apply(ConsoleWindowSystem windowSystem, string themeName) =>
        windowSystem.ThemeStateService.SwitchTheme(themeName);

    static bool HasInstanceRegistry(ConsoleWindowSystem windowSystem) =>
        GetInstanceRegistry(windowSystem) is not null;

    static object? GetInstanceRegistry(ConsoleWindowSystem windowSystem) =>
        windowSystem.GetType()
            .GetProperty("ThemeRegistryService", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(windowSystem);

    static void ApplyType(ConsoleWindowSystem windowSystem, string assemblyQualifiedTypeName)
    {
        var type = Type.GetType(assemblyQualifiedTypeName);
        if (type is not null && Activator.CreateInstance(type) is ITheme theme)
        {
            windowSystem.ThemeStateService.SetTheme(theme);
        }
    }

    static IReadOnlyList<string>? InvokeGetAvailableThemeNames(Type registryType, object? target)
    {
        var result = registryType
            .GetMethod("GetAvailableThemeNames", BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            ?.Invoke(target, parameters: null);

        if (result is IEnumerable names)
        {
            return [.. names.Cast<object>().Select(n => n?.ToString() ?? string.Empty)];
        }

        return null;
    }
}
