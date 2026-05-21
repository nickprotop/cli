// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Interactive TUI workbench — a live-updating dashboard with full drill-down navigation and in-place
/// actions for observers, failed partitions, jobs, recommendations, event log, event types, and projections.
/// </summary>
[LlmDescription("Opens an interactive full-screen TUI workbench for the Chronicle server. Navigate with number keys 1–9 to switch tabs. Not suitable for scripting.")]
[CliCommand("workbench", "Open the interactive Chronicle workbench (live TUI dashboard)", Branch = typeof(ChronicleBranch))]
[CliExample("chronicle", "workbench")]
[CliExample("chronicle", "workbench", "--interval", "10")]
[CliExample("chronicle", "workbench", "-e", "my-event-store")]
public class WorkbenchCommand : ChronicleCommand<WorkbenchSettings>
{
    /// <inheritdoc/>
    protected override bool UseStatusSpinner => false;

    /// <inheritdoc/>
    protected override async Task<int> ExecuteCommandAsync(IServices services, WorkbenchSettings settings, string format)
    {
        if (!string.Equals(format, OutputFormats.Table, StringComparison.Ordinal))
        {
            OutputFormatter.WriteError(
                format,
                "The workbench requires table output format",
                "Remove -o/--output or use --output table",
                ExitCodes.ValidationErrorCode);
            return ExitCodes.ValidationError;
        }

        // Restore persisted state from the previous session.
        var state = WorkbenchState.Load();
        if (settings.Interval == 5)
        {
            // Only apply saved interval when the user hasn't explicitly set one via --interval.
            settings.Interval = state.Interval;
        }

        var dataService = new WorkbenchDataService(services, settings);

        // Pre-fetch before launching the window so every view has real data from the first frame —
        // mirroring the old render-loop approach where data was fetched before the first render.
        var initialData = await dataService.FetchAsync(null, null, null, CancellationToken.None).ConfigureAwait(false);

        var app = new WorkbenchApp(dataService, settings, services, initialData, state);
        app.Run();

        // Persist final state so next session restores the same context.
        state.Interval = settings.Interval;
        state.Save();

        AnsiConsole.MarkupLine($"  [{OutputFormatter.Muted.ToMarkup()}]Workbench closed.[/]");
        return ExitCodes.Success;
    }
}
