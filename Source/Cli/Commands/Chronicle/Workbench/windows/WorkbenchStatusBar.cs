// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Themes;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Builds and owns the live bottom status bar for the Chronicle workbench.
/// The left zone contains static key hints; the right zone contains live status items
/// (connection, server version, refresh interval, and active context) updated each data tick.
/// </summary>
public class WorkbenchStatusBar
{
    const int MaxContextLength = 40;

    readonly StatusBarItem _connectionItem;
    readonly StatusBarItem _versionItem;
    readonly StatusBarItem _intervalItem;
    readonly StatusBarItem _contextItem;

    readonly WorkbenchTheme _theme;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkbenchStatusBar"/> class and builds the
    /// <see cref="StatusBarControl"/>.
    /// </summary>
    /// <param name="theme">The workbench theme, used to color the connection indicator by state.</param>
    public WorkbenchStatusBar(WorkbenchTheme theme)
    {
        _theme = theme;
        _connectionItem = new StatusBarItem { Label = "● Connecting…" };
        _versionItem = new StatusBarItem { Label = string.Empty };
        _intervalItem = new StatusBarItem { Label = string.Empty };
        _contextItem = new StatusBarItem { Label = string.Empty };

        Control = Controls.StatusBar()
            .AddLeft("Q", "Quit")
            .AddLeft("Ctrl+P", "Palette")
            .AddLeftSeparator()
            .AddLeft("?", "Help")
            .AddLeft("F", "Filter")
            .AddLeftSeparator()
            .AddLeft("F9/F10/F11", "Theme")
            .AddRight(_connectionItem)
            .AddRightSeparator()
            .AddRight(_versionItem)
            .AddRightSeparator()
            .AddRight(_intervalItem)
            .AddRightSeparator()
            .AddRight(_contextItem)
            .WithColorRole(ColorRole.Default)
            .WithAboveLine()
            .StickyBottom()
            .Build();
    }

    /// <summary>
    /// Gets the built <see cref="StatusBarControl"/> to be added to the window.
    /// </summary>
    public StatusBarControl Control { get; }

    /// <summary>
    /// Updates the live right-zone items from the latest workbench snapshot and settings.
    /// Call this on every data tick from <see cref="WorkbenchRefreshLoop"/>.
    /// </summary>
    /// <param name="data">The freshly fetched workbench data snapshot.</param>
    /// <param name="settings">The workbench settings (provides the refresh interval).</param>
    /// <param name="getActiveEventStore">Returns the currently active event store name, or <see langword="null"/> for the default.</param>
    /// <param name="getActiveNamespace">Returns the currently active namespace name, or <see langword="null"/> for the default.</param>
    public void Update(
        WorkbenchData data,
        WorkbenchSettings settings,
        Func<string?> getActiveEventStore,
        Func<string?> getActiveNamespace)
    {
        // Color the connection indicator by state (re-read each tick so it follows theme changes):
        // green/Success when connected, red/Danger when not.
        if (data.IsConnected)
        {
            _connectionItem.Label = "● Connected";
            _connectionItem.LabelForeground = _theme.Success;
        }
        else
        {
            _connectionItem.Label = "● Disconnected";
            _connectionItem.LabelForeground = _theme.Danger;
        }

        _versionItem.Label = data.ServerVersion is not null ? $"v{data.ServerVersion}" : string.Empty;
        _intervalItem.Label = $"↻ {settings.Interval}s";

        var store = getActiveEventStore() ?? settings.ResolveEventStore();
        var ns = getActiveNamespace() ?? settings.ResolveNamespace();
        var context = $"{store}/{ns}";
        _contextItem.Label = context.Length <= MaxContextLength
            ? context
            : context[..(MaxContextLength - 1)] + "…";
    }
}
