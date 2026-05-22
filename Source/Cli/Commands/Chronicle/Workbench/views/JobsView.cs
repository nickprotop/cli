// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Jobs;
using SharpConsoleUI.Layout;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Jobs tab — filterable table of background jobs with stop/resume actions in the detail pane.
/// </summary>
public class JobsView : FilterableTableView<Job>
{
    /// <summary>Gets the currently selected job, or <see langword="null"/> if none is selected.</summary>
    public Job? SelectedJob => SelectedItem;

    /// <inheritdoc/>
    public override string ViewHelp =>
        "Lists all background jobs and their current status.\n" +
        "  [S]  Stop the selected job\n" +
        "  [U]  Resume a stopped job\n" +
        "  [Space]  Check / uncheck row for bulk operations\n" +
        "  [S] / [U]  (with 2+ checked) Bulk stop / resume all checked jobs";

    /// <summary>
    /// Gets or sets the callback invoked when the user requests to stop a job.
    /// </summary>
    public Action<Job>? OnStopJob { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user requests to resume a job.
    /// </summary>
    public Action<Job>? OnResumeJob { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user requests a bulk stop of all checked jobs.
    /// </summary>
    public Action<IReadOnlyList<Job>>? OnStopAll { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user requests a bulk resume of all checked jobs.
    /// </summary>
    public Action<IReadOnlyList<Job>>? OnResumeAll { get; set; }

    /// <summary>
    /// Gets all jobs that are currently checked (checkbox mode).
    /// </summary>
    public IReadOnlyList<Job> Checked => CheckedItems;

    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns =>
    [
        ("Status", TextJustification.Left, 22),
        ("Type", TextJustification.Left, null),
        ("Progress", TextJustification.Right, 14)
    ];

    /// <inheritdoc/>
    protected override string DetailPanelHeader => "JOB";

    /// <inheritdoc/>
    protected override bool HasCheckboxMode => true;

    /// <inheritdoc/>
    protected override IReadOnlyList<ViewAction> GetAvailableActions(Job item)
    {
        List<ViewAction> actions = [];
        if (OnStopJob is not null)
        {
            actions.Add(new ViewAction("Stop job", "S", ConsoleKey.S, default, () => OnStopJob(item)));
        }

        if (OnResumeJob is not null)
        {
            actions.Add(new ViewAction("Resume job", "U", ConsoleKey.U, default, () => OnResumeJob(item)));
        }

        var checkedItems = Checked;
        if (OnStopAll is not null && checkedItems.Count > 1)
        {
            actions.Add(new ViewAction($"Stop {checkedItems.Count} checked", null, null, default, () => OnStopAll(checkedItems)));
        }

        if (OnResumeAll is not null && checkedItems.Count > 1)
        {
            actions.Add(new ViewAction($"Resume {checkedItems.Count} checked", null, null, default, () => OnResumeAll(checkedItems)));
        }

        return actions;
    }

    /// <inheritdoc/>
    protected override IEnumerable<Job> GetItems(WorkbenchData data) =>
        data.Jobs.OrderBy(j => j.Status.ToString());

    /// <inheritdoc/>
    protected override string GetKey(Job item) => item.Id.ToString();

    /// <inheritdoc/>
    protected override string[] BuildRow(Job item)
    {
        var statusColor = GetJobStatusColor(item.Status);
        return
        [
            $"[{statusColor}]{item.Status}[/]",
            item.Type ?? item.Id.ToString(),
            FormatProgress(item.Progress)
        ];
    }

    /// <inheritdoc/>
    protected override string RenderDetail(Job? item, WorkbenchData? data)
    {
        if (item is null)
        {
            return $"[{WorkbenchColors.Muted.ToMarkup()}]Select a job.[/]";
        }

        var mut = WorkbenchColors.Muted.ToMarkup();
        var statusColor = GetJobStatusColor(item.Status);

        var lines = new List<string>
        {
            $"[{mut}]Id[/]       {item.Id}",
            $"[{mut}]Type[/]     {item.Type ?? "—"}",
            $"[{mut}]Status[/]   [{statusColor}]{item.Status}[/]",
            $"[{mut}]Progress[/] {FormatProgress(item.Progress)}"
        };

        if (item.Progress is not null)
        {
            lines.Add($"[{mut}]Steps[/]    {item.Progress.SuccessfulSteps}/{item.Progress.TotalSteps}");
            if (item.Progress.FailedSteps > 0)
            {
                lines.Add($"[{WorkbenchColors.Danger.ToMarkup()}]Failed[/]   {item.Progress.FailedSteps}");
            }

            if (!string.IsNullOrEmpty(item.Progress.Message))
            {
                lines.Add($"[{mut}]Message[/]  {item.Progress.Message}");
            }
        }

        if (OnStopJob is not null || OnResumeJob is not null)
        {
            lines.Add(string.Empty);
            if (OnStopJob is not null)
            {
                lines.Add($"[{mut}]Press[/] [bold]S[/] [{mut}]to stop[/]");
            }

            if (OnResumeJob is not null)
            {
                lines.Add($"[{mut}]Press[/] [bold]U[/] [{mut}]to resume[/]");
            }
        }

        return string.Join('\n', lines);
    }

    /// <inheritdoc/>
    protected override bool MatchesFilter(Job item, string filter) =>
        (item.Type ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        item.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        item.Status.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase);

    static string GetJobStatusColor(JobStatus status) => status switch
    {
        JobStatus.Running => WorkbenchColors.Success.ToMarkup(),
        JobStatus.Failed => WorkbenchColors.Danger.ToMarkup(),
        JobStatus.Stopped => WorkbenchColors.Warning.ToMarkup(),
        JobStatus.CompletedWithFailures => WorkbenchColors.Warning.ToMarkup(),
        _ => WorkbenchColors.Muted.ToMarkup()
    };

    static string FormatProgress(JobProgress? p)
    {
        if (p is null || p.TotalSteps == 0)
        {
            return "—";
        }

        return $"{p.SuccessfulSteps}/{p.TotalSteps}";
    }
}
