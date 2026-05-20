// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Jobs;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Jobs tab — table of background jobs with stop/resume actions in the bordered detail pane.
/// </summary>
public class JobsView : IWorkbenchView
{
    TableControl? _table;
    PanelControl? _detailPanel;
    WorkbenchData? _pendingData;

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
    /// Returns all jobs that are currently checked (checkbox mode).
    /// </summary>
    /// <returns>A list of checked <see cref="Job"/> items.</returns>
    public IReadOnlyList<Job> GetCheckedItems() =>
        [.. (_table?.GetCheckedRows() ?? []).Select(r => r.Tag).OfType<Job>()];

    /// <inheritdoc/>
    public void Dispose()
    {
        _table?.Dispose();
        _detailPanel?.Dispose();
    }

    /// <inheritdoc/>
    public IWindowControl BuildContent(ConsoleWindowSystem windowSystem)
    {
        _table = Controls.Table()
            .AddColumn("Status", SharpConsoleUI.Layout.TextJustification.Left, 22)
            .AddColumn("Type", SharpConsoleUI.Layout.TextJustification.Left, null)
            .AddColumn("Progress", SharpConsoleUI.Layout.TextJustification.Right, 14)
            .Interactive()
            .WithCheckboxMode()
            .WithFiltering()
            .WithSorting()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .OnSelectedRowChanged((_, _) => RefreshDetail())
            .WithName("JobsTable")
            .Build();

        _detailPanel = Controls.Panel()
            .WithContent($"[{WorkbenchColors.Muted.ToMarkup()}]Select a job.[/]")
            .WithHeader(" JOB ")
            .Rounded()
            .WithBorderColor(WorkbenchColors.Accent)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName("JobDetailPanel")
            .Build();

        var root = HorizontalGridControl.Create()
            .Column(c => c.Add(_table))
            .WithSplitterAfter(0)
            .Column(c => c.Width(45).Add(_detailPanel))
            .Build();

        // Apply any data that arrived before controls were ready (NavigationView lazy init).
        if (_pendingData is not null)
            UpdateData(_pendingData);

        return root;
    }

    /// <inheritdoc/>
    public void UpdateData(WorkbenchData data)
    {
        _pendingData = data;
        if (_table is null) return;

        var selectedKey = (_table.SelectedRow?.Tag as Job)?.Id.ToString();

        _table.ClearRows();
        foreach (var job in data.Jobs.OrderBy(j => j.Status.ToString()))
        {
            var statusColor = GetJobStatusColor(job.Status);
            _table.AddRow(new UITableRow(
            [
                $"[{statusColor}]{job.Status}[/]",
                job.Type ?? job.Id.ToString(),
                FormatProgress(job.Progress)
            ])
            { Tag = job });
        }

        if (selectedKey is not null)
        {
            RestoreSelection(selectedKey);
        }

        RefreshDetail();
    }

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
        if (p is null || p.TotalSteps == 0) return "—";
        return $"{p.SuccessfulSteps}/{p.TotalSteps}";
    }

    void RestoreSelection(string key)
    {
        if (_table is null) return;

        for (var i = 0; i < _table.Rows.Count; i++)
        {
            if (_table.Rows[i].Tag is Job job && job.Id.ToString() == key)
            {
                _table.SelectedRowIndex = i;
                return;
            }
        }
    }

    void RefreshDetail()
    {
        if (_table is null || _detailPanel is null) return;

        if (_table.SelectedRow?.Tag is not Job job)
        {
            _detailPanel.Content = $"[{WorkbenchColors.Muted.ToMarkup()}]Select a job.[/]";
            return;
        }

        var mut = WorkbenchColors.Muted.ToMarkup();
        var statusColor = GetJobStatusColor(job.Status);

        var lines = new List<string>
        {
            $"[{mut}]Id[/]       {job.Id}",
            $"[{mut}]Type[/]     {job.Type ?? "—"}",
            $"[{mut}]Status[/]   [{statusColor}]{job.Status}[/]",
            $"[{mut}]Progress[/] {FormatProgress(job.Progress)}"
        };

        if (job.Progress is not null)
        {
            lines.Add($"[{mut}]Steps[/]    {job.Progress.SuccessfulSteps}/{job.Progress.TotalSteps}");
            if (job.Progress.FailedSteps > 0)
            {
                lines.Add($"[{WorkbenchColors.Danger.ToMarkup()}]Failed[/]   {job.Progress.FailedSteps}");
            }

            if (!string.IsNullOrEmpty(job.Progress.Message))
            {
                lines.Add($"[{mut}]Message[/]  {job.Progress.Message}");
            }
        }

        if (OnStopJob is not null || OnResumeJob is not null)
        {
            lines.Add(string.Empty);
            if (OnStopJob is not null) lines.Add($"[{mut}]Press[/] [bold]S[/] [{mut}]to stop[/]");
            if (OnResumeJob is not null) lines.Add($"[{mut}]Press[/] [bold]U[/] [{mut}]to resume[/]");
        }

        _detailPanel.Content = string.Join('\n', lines);
    }
}
