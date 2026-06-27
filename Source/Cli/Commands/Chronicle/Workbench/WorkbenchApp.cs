// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Panel;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Bootstraps and runs the Chronicle Workbench TUI application using SharpConsoleUI.
/// </summary>
/// <param name="dataService">The data service for fetching workbench snapshots.</param>
/// <param name="settings">The workbench command settings.</param>
/// <param name="services">The Chronicle gRPC service clients.</param>
/// <param name="initialData">Pre-fetched data snapshot to populate all views before the first frame is rendered.</param>
/// <param name="state">Persisted workbench state from the previous session.</param>
public class WorkbenchApp(WorkbenchDataService dataService, WorkbenchSettings settings, IServices services, WorkbenchData initialData, WorkbenchState state)
{
    /// <summary>
    /// Runs the workbench TUI and blocks until the user exits.
    /// </summary>
    /// <returns>The exit code.</returns>
    public int Run()
    {
        // The bottom panel is hidden — the WorkbenchStatusBar control now carries the key hints and
        // live status at the bottom, so the system bottom panel would be redundant. The top panel is
        // kept for the CHRONICLE WORKBENCH summary line.
        var windowSystem = new ConsoleWindowSystem(
            new NetConsoleDriver(RenderMode.Buffer),
            options: new ConsoleWindowSystemOptions(
                TopPanelConfig: panel => panel.Left(Elements.StatusText(string.Empty)),
                ShowBottomPanel: false));

        windowSystem.PanelStateService.TopStatus = "◆ CHRONICLE WORKBENCH";

        var mainWindow = new MainWindow(windowSystem, dataService, settings, services, initialData, state);
        windowSystem.AddWindow(mainWindow.Build(), activateWindow: true);

        return windowSystem.Run();
    }
}
