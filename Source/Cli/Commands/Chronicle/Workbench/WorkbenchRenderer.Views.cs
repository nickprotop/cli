// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Jobs;
using Spectre.Console.Rendering;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

public static partial class WorkbenchRenderer
{
    const int EventLogPageSize = 50;

    static Rows BuildOverview(WorkbenchData data)
    {
        var topRow = new Table().HideHeaders().NoBorder().Expand()
            .AddColumn(new TableColumn(string.Empty))
            .AddColumn(new TableColumn(string.Empty));
        topRow.AddRow(BuildServerPanel(data), BuildObserverStatsPanel(data));

        var contextPanel = new Panel(new Rows(
                new Markup($"  [{_mut}]event store[/]  [bold {_acc}]{data.EventStore.EscapeMarkup()}[/]   [{_mut}][[ E ]][/] change"),
                new Markup($"  [{_mut}]namespace[/]     [bold {_acc}]{data.Namespace.EscapeMarkup()}[/]   [{_mut}][[ N ]][/] change")))
            .Header($"[{_acc}] Active Context [/]")
            .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted).Expand();

        var sections = new List<IRenderable> { topRow, contextPanel };
        var attentionPanel = BuildAttentionPanel(data);
        if (attentionPanel is not null) sections.Add(attentionPanel);
        return new Rows(sections);
    }

    static Panel BuildServerPanel(WorkbenchData data)
    {
        var connLine = data.IsConnected
            ? $"  [{_suc}]✓[/]  connected  [{_mut}]{(data.ServerVersion ?? "—").EscapeMarkup()}[/]"
            : $"  [{_dan}]✗[/]  [{_dan}]disconnected[/]";
        var storesLine = $"  [{_mut}]·[/]  [{_mut}]{data.EventStoreNames.Count} event store{(data.EventStoreNames.Count == 1 ? string.Empty : "s")}[/]";
        var tailLine = data.TailSequenceNumber.HasValue
            ? $"  [{_mut}]·[/]  [bold]{data.TailSequenceNumber.Value:N0}[/]  [{_mut}]events in log[/]"
            : $"  [{_mut}]·  event log unavailable[/]";

        return new Panel(new Rows(new Markup(connLine), new Markup(storesLine), new Markup(tailLine)))
            .Header($"[{_acc}] Server [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(OutputFormatter.Muted);
    }

    static Panel BuildObserverStatsPanel(WorkbenchData data)
    {
        var total = data.Observers.Count;
        var line1 = $"  [{_suc}]●[/]  [bold {_suc}]{data.ActiveObservers,3}[/]  [{_suc}]Active[/]     [{_war}]▲[/]  [bold {_war}]{data.ReplayingObservers,3}[/]  [{_war}]Replaying[/]";
        var line2 = $"  [{_mut}]○[/]  [bold {_mut}]{data.SuspendedObservers,3}[/]  [{_mut}]Suspended[/]  [{_mut}]⊘[/]  [bold {_mut}]{data.DisconnectedObservers,3}[/]  [{_mut}]Disconnected[/]";
        var failureLine = data.FailedPartitions.Count > 0
            ? $"  [{_dan}]✗[/]  [{_dan}]{data.FailedPartitions.Count} failed partition{(data.FailedPartitions.Count == 1 ? string.Empty : "s")}[/]"
            : $"  [{_suc}]✓[/]  [{_suc}]no failed partitions[/]";

        return new Panel(new Rows(new Markup(line1), new Markup(line2), new Markup(failureLine)))
            .Header($"[{_acc}] Observers ({total}) [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(OutputFormatter.Muted);
    }

    static Panel? BuildAttentionPanel(WorkbenchData data)
    {
        var items = new List<string>();
        foreach (var fp in data.FailedPartitions.Take(5))
        {
            var lastAttempt = fp.Attempts.MaxBy(a => (DateTimeOffset?)a.Occurred ?? DateTimeOffset.MinValue);
            var msg = (lastAttempt?.Messages.FirstOrDefault() ?? "unknown error").EscapeMarkup();
            if (msg.Length > 80) msg = msg[..80] + "…";
            var attempts = fp.Attempts.Count();
            items.Add($"  [{_dan}]✗[/]  {fp.ObserverId.EscapeMarkup()}  [{_mut}]on[/] {fp.Partition.EscapeMarkup()}  [{_dan}]{msg}[/]  [{_mut}]({attempts} attempt{(attempts == 1 ? string.Empty : "s")})[/]");
        }

        foreach (var rec in data.Recommendations.Take(3))
        {
            var desc = (rec.Description ?? string.Empty).EscapeMarkup();
            if (desc.Length > 80) desc = desc[..80] + "…";
            items.Add($"  [{_war}]▲[/]  [{_war}]{(rec.Name ?? string.Empty).EscapeMarkup()}[/]  [{_mut}]{desc}[/]");
        }

        if (items.Count == 0) return null;

        var borderColor = data.FailedPartitions.Count > 0 ? OutputFormatter.Danger : OutputFormatter.Warning;
        var headerColor = data.FailedPartitions.Count > 0 ? _dan : _war;
        return new Panel(new Rows(items.ConvertAll(i => (IRenderable)new Markup(i))))
            .Header($"[{headerColor}] Attention Needed ({items.Count}) [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(borderColor);
    }

    static Rows BuildObserversView(WorkbenchData data, int selectedIndex, string filterText)
    {
        var allObservers = data.Observers.OrderBy(o => StateOrder(o.RunningState)).ThenBy(o => o.Id).ToList();
        var observers = string.IsNullOrEmpty(filterText)
            ? allObservers
            : [.. allObservers.Where(o => o.Id.Contains(filterText, StringComparison.OrdinalIgnoreCase))];
        var effectiveIndex = observers.Count > 0 ? Math.Min(selectedIndex, observers.Count - 1) : -1;

        var table = new Table()
            .Border(TableBorder.Rounded).BorderColor(OutputFormatter.Muted).Expand()
            .AddColumn(new TableColumn(string.Empty).Width(2))
            .AddColumn(new TableColumn("[bold]Observer[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Type[/]").Padding(1, 0).Width(20).NoWrap())
            .AddColumn(new TableColumn("[bold]State[/]").Padding(1, 0).Width(18).NoWrap());

        AddListRows(table, observers, effectiveIndex, data.TailSequenceNumber, 4);

        var header = string.IsNullOrEmpty(filterText)
            ? $"[{_acc}] Observers ({data.Observers.Count}) [/]"
            : $"[{_acc}] Observers ({observers.Count}/{data.Observers.Count}) [/]";
        var sections = new List<IRenderable>
        {
            new Panel(table).Header(header).BorderColor(OutputFormatter.Accent).NoBorder()
        };

        if (effectiveIndex >= 0 && observers.Count > 0)
            sections.Add(BuildObserverMiniDetail(observers[effectiveIndex], data.TailSequenceNumber));

        return new Rows(sections);
    }

    static void AddListRows(Table table, List<ObserverInformation> observers, int effectiveIndex, ulong? tail, int colCount)
    {
        if (observers.Count == 0)
        {
            var empty = Enumerable.Range(0, colCount).Select(i => i == 1 ? (IRenderable)new Markup($"[{_mut}](none)[/]") : new Markup(string.Empty)).ToArray();
            table.AddRow(empty);
            return;
        }

        var (winStart, winEnd) = ListWindow(observers.Count, effectiveIndex < 0 ? 0 : effectiveIndex);
        if (winStart > 0)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↑ {winStart} more above[/]"), new Markup(string.Empty), new Markup(string.Empty));

        for (var i = winStart; i <= winEnd; i++)
        {
            var obs = observers[i];
            var isSelected = i == effectiveIndex;
            var pointer = isSelected ? $"[bold {_acc}]▶[/]" : string.Empty;
            table.AddRow(
                new Markup(pointer),
                new Markup(isSelected ? $"[bold {_acc}]{obs.Id.EscapeMarkup()}[/]" : obs.Id.EscapeMarkup()),
                new Markup($"[{_mut}]{obs.Type.ToString().EscapeMarkup()}[/]"),
                new Markup($"{StateIcon(obs.RunningState)} {StateName(obs.RunningState)}"));
        }

        if (winEnd < observers.Count - 1)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↓ {observers.Count - 1 - winEnd} more below[/]"), new Markup(string.Empty), new Markup(string.Empty));
    }

    static Panel BuildObserverMiniDetail(ObserverInformation obs, ulong? tail)
    {
        var line1 = $"  [{_mut}]type[/]  [bold]{obs.Type.ToString().EscapeMarkup()}[/]  [{_mut}]state[/]  {StateIcon(obs.RunningState)} {StateName(obs.RunningState)}";
        var line2 = $"  [{_mut}]next[/]  {FormatSeq(obs.NextEventSequenceNumber)}  [{_mut}]handled[/]  {FormatSeq(obs.LastHandledEventSequenceNumber)}";
        var line3 = $"\n  [{_acc}][[ Enter ]][/] Full detail  [{_acc}][[ R ]][/] Replay";
        return new Panel(new Rows(new Markup(line1), new Markup(line2), new Markup(line3)))
            .Header($"[{_acc}] {obs.Id.EscapeMarkup()} [/]")
            .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Accent).Expand();
    }

    static Rows BuildFailedView(WorkbenchData data, int selectedIndex)
    {
        if (data.FailedPartitions.Count == 0)
        {
            return new Rows(new Panel(new Markup($"\n  [{_suc}]✓  No failed partitions.[/]\n"))
                .Header($"[{_acc}] Failures [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Success));
        }

        var fps = data.FailedPartitions.OrderByDescending(fp => fp.Attempts.Count()).ToList();
        var effectiveIndex = Math.Min(selectedIndex, fps.Count - 1);
        var table = new Table().Border(TableBorder.Rounded).BorderColor(OutputFormatter.Danger).Expand()
            .AddColumn(new TableColumn(string.Empty).Width(2))
            .AddColumn(new TableColumn("[bold]Observer[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Partition[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Attempts[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Last Failed[/]").Padding(1, 0));

        var (winStart, winEnd) = ListWindow(fps.Count, effectiveIndex);
        if (winStart > 0)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↑ {winStart} more above[/]"), new Markup(string.Empty), new Markup(string.Empty), new Markup(string.Empty));
        for (var i = winStart; i <= winEnd; i++)
        {
            var fp = fps[i];
            var isSelected = i == effectiveIndex;
            var lastAttempt = fp.Attempts.MaxBy(a => (DateTimeOffset?)a.Occurred ?? DateTimeOffset.MinValue);
            var obsId = fp.ObserverId.EscapeMarkup();
            table.AddRow(
                new Markup(isSelected ? $"[bold {_acc}]▶[/]" : string.Empty),
                new Markup(isSelected ? $"[bold {_acc}]{obsId}[/]" : $"[{_dan}]{obsId}[/]"),
                new Markup(fp.Partition.EscapeMarkup()),
                new Markup(fp.Attempts.Count().ToString()),
                new Markup($"[{_dan}]{(lastAttempt is not null ? lastAttempt.Occurred.ToString() : "—").EscapeMarkup()}[/]"));
        }

        if (winEnd < fps.Count - 1)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↓ {fps.Count - 1 - winEnd} more below[/]"), new Markup(string.Empty), new Markup(string.Empty), new Markup(string.Empty));

        var selected = fps[effectiveIndex];
        var selLastAttempt = selected.Attempts.MaxBy(a => (DateTimeOffset?)a.Occurred ?? DateTimeOffset.MinValue);
        var msg = (selLastAttempt?.Messages.FirstOrDefault() ?? "—").EscapeMarkup();
        if (msg.Length > 80) msg = msg[..80] + "…";
        var miniDetail = new Panel(new Rows(
                new Markup($"  [{_mut}]partition[/]  {selected.Partition.EscapeMarkup()}  [{_mut}]attempts[/]  [{_dan}]{selected.Attempts.Count()}[/]  [{_mut}]last failed[/]  [{_dan}]{(selLastAttempt is not null ? selLastAttempt.Occurred.ToString() : "—").EscapeMarkup()}[/]"),
                new Markup($"  [{_dan}]{msg}[/]"),
                new Markup($"\n  [{_acc}][[ Enter ]][/] Full detail  [{_acc}][[ T ]][/] Retry  [{_acc}][[ P ]][/] Replay partition")))
            .Header($"[{_dan}] Failure Detail [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Danger).Expand();

        return new Rows(
            new Panel(table).Header($"[{_dan}] Failures ({fps.Count}) [/]").BorderColor(OutputFormatter.Danger).NoBorder(),
            miniDetail);
    }

    static Rows BuildJobsView(WorkbenchData data, int selectedIndex)
    {
        if (data.Jobs.Count == 0)
        {
            return new Rows(new Panel(new Markup($"\n  [{_mut}]No background jobs.[/]\n"))
                .Header($"[{_acc}] Jobs [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted));
        }

        var jobs = data.Jobs.OrderBy(j => j.Status.ToString()).ToList();
        var effectiveIndex = Math.Min(selectedIndex, jobs.Count - 1);
        var table = new Table().Border(TableBorder.Rounded).BorderColor(OutputFormatter.Muted).Expand()
            .AddColumn(new TableColumn(string.Empty).Width(2))
            .AddColumn(new TableColumn("[bold]Type[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Status[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Progress[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Created[/]").Padding(1, 0));

        var (winStart, winEnd) = ListWindow(jobs.Count, effectiveIndex);
        if (winStart > 0)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↑ {winStart} more above[/]"), new Markup(string.Empty), new Markup(string.Empty), new Markup(string.Empty));
        for (var i = winStart; i <= winEnd; i++)
        {
            var job = jobs[i];
            var isSelected = i == effectiveIndex;
            var statusStr = job.Status.ToString();
            string statusColor;
            if (statusStr.Contains("Running", StringComparison.OrdinalIgnoreCase)) statusColor = _suc;
            else if (statusStr.Contains("Failed", StringComparison.OrdinalIgnoreCase)) statusColor = _dan;
            else statusColor = _mut;
            string progressCell;
            if (job.Progress?.TotalSteps > 0)
            {
                var bar = ProgressBar(job.Progress.SuccessfulSteps, job.Progress.TotalSteps);
                var pct = (int)Math.Min(100, (long)job.Progress.SuccessfulSteps * 100 / job.Progress.TotalSteps);
                progressCell = $"{bar} {job.Progress.SuccessfulSteps:N0}/{job.Progress.TotalSteps:N0} [{_mut}]{pct}%[/]";
            }
            else
            {
                progressCell = $"[{_mut}]{(job.Progress?.Message ?? "—").EscapeMarkup()}[/]";
            }

            table.AddRow(
                new Markup(isSelected ? $"[bold {_acc}]▶[/]" : string.Empty),
                new Markup(isSelected ? $"[bold {_acc}]{(job.Type ?? "—").EscapeMarkup()}[/]" : (job.Type ?? "—").EscapeMarkup()),
                new Markup($"[{statusColor}]{statusStr.EscapeMarkup()}[/]"),
                new Markup(progressCell),
                new Markup(job.Created.ToString().EscapeMarkup()));
        }

        if (winEnd < jobs.Count - 1)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↓ {jobs.Count - 1 - winEnd} more below[/]"), new Markup(string.Empty), new Markup(string.Empty), new Markup(string.Empty));

        return new Rows(new Panel(table).Header($"[{_acc}] Jobs ({jobs.Count}) [/]").BorderColor(OutputFormatter.Accent).NoBorder());
    }

    static Rows BuildRecommendationsView(WorkbenchData data, int selectedIndex)
    {
        if (data.Recommendations.Count == 0)
        {
            return new Rows(new Panel(new Markup($"\n  [{_suc}]✓  No pending recommendations.[/]\n"))
                .Header($"[{_acc}] Recommendations [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Success));
        }

        var recs = data.Recommendations.ToList();
        var effectiveIndex = Math.Min(selectedIndex, recs.Count - 1);
        var table = new Table().Border(TableBorder.Rounded).BorderColor(OutputFormatter.Warning).Expand()
            .AddColumn(new TableColumn(string.Empty).Width(2))
            .AddColumn(new TableColumn("[bold]Recommendation[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Type[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Occurred[/]").Padding(1, 0));

        var (winStart, winEnd) = ListWindow(recs.Count, effectiveIndex);
        if (winStart > 0)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↑ {winStart} more above[/]"), new Markup(string.Empty), new Markup(string.Empty));
        for (var i = winStart; i <= winEnd; i++)
        {
            var rec = recs[i];
            var isSelected = i == effectiveIndex;
            table.AddRow(
                new Markup(isSelected ? $"[bold {_acc}]▶[/]" : string.Empty),
                new Markup(isSelected ? $"[bold {_acc}]{(rec.Name ?? "—").EscapeMarkup()}[/]" : $"[{_war}]{(rec.Name ?? "—").EscapeMarkup()}[/]"),
                new Markup($"[{_mut}]{(rec.Type ?? "—").EscapeMarkup()}[/]"),
                new Markup($"[{_mut}]{rec.Occurred.ToString().EscapeMarkup()}[/]"));
        }

        if (winEnd < recs.Count - 1)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↓ {recs.Count - 1 - winEnd} more below[/]"), new Markup(string.Empty), new Markup(string.Empty));

        var selRec = recs[effectiveIndex];
        var desc = (selRec.Description ?? string.Empty).EscapeMarkup();
        if (desc.Length > 100) desc = desc[..100] + "…";
        var detail = new Panel(new Rows(
                new Markup($"  [{_mut}]type[/]  [{_war}]{(selRec.Type ?? "—").EscapeMarkup()}[/]  [{_mut}]occurred[/]  [{_mut}]{selRec.Occurred.ToString().EscapeMarkup()}[/]"),
                new Markup($"  [{_mut}]{desc}[/]"),
                new Markup($"\n  [{_acc}][[ A ]][/] Apply  [{_acc}][[ I ]][/] Ignore")))
            .Header($"[{_war}] {(selRec.Name ?? "—").EscapeMarkup()} [/]")
            .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Warning).Expand();

        return new Rows(new Panel(table).Header($"[{_war}] Recommendations ({recs.Count}) [/]").BorderColor(OutputFormatter.Warning).NoBorder(), detail);
    }

    static Rows BuildEventLogView(WorkbenchData data, int selectedIndex, string filterText, bool ascending, int page)
    {
        if (data.RecentEvents.Count == 0)
        {
            return new Rows(new Panel(new Markup($"\n  [{_mut}]No events in the event log yet.[/]\n"))
                .Header($"[{_acc}] Event Log [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted));
        }

        // Sort the full event window, then filter, then page.
        var sortedEvents = ascending
            ? [.. data.RecentEvents.OrderBy(e => e.Context.SequenceNumber)]
            : data.RecentEvents.ToList();

        var filteredEvents = string.IsNullOrEmpty(filterText)
            ? sortedEvents
            : [.. sortedEvents.Where(e => (e.Context.EventType?.Id ?? string.Empty).Contains(filterText, StringComparison.OrdinalIgnoreCase))];

        if (filteredEvents.Count == 0)
        {
            return new Rows(new Panel(new Markup($"\n  [{_mut}]No events matching '{filterText.EscapeMarkup()}'.[/]\n"))
                .Header($"[{_acc}] Event Log (0/{sortedEvents.Count}) [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted));
        }

        var totalPages = Math.Max(1, (filteredEvents.Count + EventLogPageSize - 1) / EventLogPageSize);
        var effectivePage = Math.Min(page, totalPages - 1);
        var pageStart = effectivePage * EventLogPageSize;
        var pageEvents = filteredEvents.Skip(pageStart).Take(EventLogPageSize).ToList();
        var effectiveIndex = Math.Min(selectedIndex, Math.Max(0, pageEvents.Count - 1));

        var sortIndicator = ascending ? $"[{_mut}]↑ oldest first[/]" : $"[{_mut}]↓ newest first[/]";
        var pageIndicator = totalPages > 1
            ? $"  [{_mut}]page {effectivePage + 1}/{totalPages}[/]"
            : string.Empty;

        var table = new Table().Border(TableBorder.Rounded).BorderColor(OutputFormatter.Muted).Expand()
            .AddColumn(new TableColumn(string.Empty).Width(2))
            .AddColumn(new TableColumn("[bold]Seq#[/]").Padding(1, 0).RightAligned())
            .AddColumn(new TableColumn("[bold]EventType[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]EventSourceId[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Occurred[/]").Padding(1, 0));

        var (winStart, winEnd) = ListWindow(pageEvents.Count, effectiveIndex);
        if (winStart > 0)
            table.AddRow(new Markup(string.Empty), new Markup(string.Empty), new Markup($"[{_mut}]↑ {winStart} more above[/]"), new Markup(string.Empty), new Markup(string.Empty));
        for (var i = winStart; i <= winEnd; i++)
        {
            var evt = pageEvents[i];
            var ctx = evt.Context;
            var isSelected = i == effectiveIndex;
            var typeId = ctx.EventType?.Id ?? "—";
            table.AddRow(
                new Markup(isSelected ? $"[bold {_acc}]▶[/]" : string.Empty),
                new Markup($"[{_mut}]{ctx.SequenceNumber:N0}[/]"),
                new Markup(isSelected ? $"[bold {_acc}]{typeId.EscapeMarkup()}[/]" : typeId.EscapeMarkup()),
                new Markup((ctx.EventSourceId ?? string.Empty).EscapeMarkup()),
                new Markup($"[{_mut}]{ctx.Occurred.ToString().EscapeMarkup()}[/]"));
        }

        if (winEnd < pageEvents.Count - 1)
            table.AddRow(new Markup(string.Empty), new Markup(string.Empty), new Markup($"[{_mut}]↓ {pageEvents.Count - 1 - winEnd} more below[/]"), new Markup(string.Empty), new Markup(string.Empty));

        var selEvt = pageEvents[effectiveIndex];
        var selCtx = selEvt.Context;
        var miniDetail = new Panel(new Markup(
                $"  [{_mut}]seq#[/]  [bold]{selCtx.SequenceNumber:N0}[/]  [{_mut}]eventSource[/]  {(selCtx.EventSourceId ?? "—").EscapeMarkup()}  [{_mut}]occurred[/]  [{_mut}]{selCtx.Occurred.ToString().EscapeMarkup()}[/]\n" +
                $"\n  [{_acc}][[ Enter ]][/] Full event  [{_acc}][[ T ]][/] View event type"))
            .Header($"[{_acc}] {(selCtx.EventType?.Id ?? "—").EscapeMarkup()} [/]")
            .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Accent).Expand();

        var filterSuffix = string.IsNullOrEmpty(filterText)
            ? string.Empty
            : $" ({filteredEvents.Count}/{sortedEvents.Count} matching)";
        var logHeader = $"[{_acc}] Event Log{filterSuffix}  {sortIndicator}{pageIndicator}  [[PgDn/PgUp]] page [/]";
        return new Rows(
            new Panel(table).Header(logHeader).BorderColor(OutputFormatter.Accent).NoBorder(),
            miniDetail);
    }

    static Rows BuildEventTypesView(WorkbenchData data, int selectedIndex, string filterText)
    {
        if (data.EventTypeRegistrations.Count == 0)
        {
            return new Rows(new Panel(new Markup($"\n  [{_mut}]No event types registered.[/]\n"))
                .Header($"[{_acc}] Event Types [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted));
        }

        var allTypes = data.EventTypeRegistrations.OrderBy(r => r.Type.Id, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Type.Generation).ToList();
        var types = string.IsNullOrEmpty(filterText)
            ? allTypes
            : [.. allTypes.Where(r => r.Type.Id.Contains(filterText, StringComparison.OrdinalIgnoreCase))];

        if (types.Count == 0)
        {
            return new Rows(new Panel(new Markup($"\n  [{_mut}]No event types matching '{filterText.EscapeMarkup()}'.[/]\n"))
                .Header($"[{_acc}] Event Types (0/{allTypes.Count}) [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted));
        }

        var effectiveIndex = Math.Min(selectedIndex, types.Count - 1);
        var table = new Table().Border(TableBorder.Rounded).BorderColor(OutputFormatter.Muted).Expand()
            .AddColumn(new TableColumn(string.Empty).Width(2))
            .AddColumn(new TableColumn("[bold]EventType[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Gen[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Owner[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Source[/]").Padding(1, 0));

        var (winStart, winEnd) = ListWindow(types.Count, effectiveIndex);
        if (winStart > 0)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↑ {winStart} more above[/]"), new Markup(string.Empty), new Markup(string.Empty), new Markup(string.Empty));
        for (var i = winStart; i <= winEnd; i++)
        {
            var reg = types[i];
            var isSelected = i == effectiveIndex;
            table.AddRow(
                new Markup(isSelected ? $"[bold {_acc}]▶[/]" : string.Empty),
                new Markup(isSelected ? $"[bold {_acc}]{reg.Type.Id.EscapeMarkup()}[/]" : reg.Type.Id.EscapeMarkup()),
                new Markup($"[{_mut}]{reg.Type.Generation}[/]"),
                new Markup($"[{_mut}]{reg.Owner.ToString().EscapeMarkup()}[/]"),
                new Markup($"[{_mut}]{reg.Source.ToString().EscapeMarkup()}[/]"));
        }

        if (winEnd < types.Count - 1)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↓ {types.Count - 1 - winEnd} more below[/]"), new Markup(string.Empty), new Markup(string.Empty), new Markup(string.Empty));

        var selType = types[effectiveIndex];
        var miniDetail = new Panel(new Markup(
                $"  [{_mut}]owner[/]  {selType.Owner.ToString().EscapeMarkup()}  [{_mut}]source[/]  {selType.Source.ToString().EscapeMarkup()}  [{_mut}]tombstone[/]  {selType.Type.Tombstone}\n" +
                $"\n  [{_acc}][[ Enter ]][/] View schema"))
            .Header($"[{_acc}] {selType.Type.Id.EscapeMarkup()} +{selType.Type.Generation} [/]")
            .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Accent).Expand();

        var typesHeader = string.IsNullOrEmpty(filterText)
            ? $"[{_acc}] Event Types ({allTypes.Count}) [/]"
            : $"[{_acc}] Event Types ({types.Count}/{allTypes.Count}) [/]";
        return new Rows(
            new Panel(table).Header(typesHeader).BorderColor(OutputFormatter.Accent).NoBorder(),
            miniDetail);
    }

    static Rows BuildProjectionsView(WorkbenchData data, int selectedIndex, string filterText)
    {
        if (data.ProjectionDefinitions.Count == 0)
        {
            return new Rows(new Panel(new Markup($"\n  [{_mut}]No projections registered.[/]\n"))
                .Header($"[{_acc}] Projections [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted));
        }

        var allProjections = data.ProjectionDefinitions.OrderBy(d => d.Identifier, StringComparer.OrdinalIgnoreCase).ToList();
        var projections = string.IsNullOrEmpty(filterText)
            ? allProjections
            : [.. allProjections.Where(d => d.Identifier.Contains(filterText, StringComparison.OrdinalIgnoreCase))];

        if (projections.Count == 0)
        {
            return new Rows(new Panel(new Markup($"\n  [{_mut}]No projections matching '{filterText.EscapeMarkup()}'.[/]\n"))
                .Header($"[{_acc}] Projections (0/{allProjections.Count}) [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted));
        }

        var effectiveIndex = Math.Min(selectedIndex, projections.Count - 1);
        var table = new Table().Border(TableBorder.Rounded).BorderColor(OutputFormatter.Muted).Expand()
            .AddColumn(new TableColumn(string.Empty).Width(2))
            .AddColumn(new TableColumn("[bold]Identifier[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]ReadModel[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Active[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Rewindable[/]").Padding(1, 0));

        var (winStart, winEnd) = ListWindow(projections.Count, effectiveIndex);
        if (winStart > 0)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↑ {winStart} more above[/]"), new Markup(string.Empty), new Markup(string.Empty), new Markup(string.Empty));
        for (var i = winStart; i <= winEnd; i++)
        {
            var def = projections[i];
            var isSelected = i == effectiveIndex;
            var activeIcon = def.IsActive ? $"[{_suc}]✓[/]" : $"[{_mut}]✗[/]";
            var rewindIcon = def.IsRewindable ? $"[{_suc}]✓[/]" : $"[{_mut}]✗[/]";
            table.AddRow(
                new Markup(isSelected ? $"[bold {_acc}]▶[/]" : string.Empty),
                new Markup(isSelected ? $"[bold {_acc}]{def.Identifier.EscapeMarkup()}[/]" : def.Identifier.EscapeMarkup()),
                new Markup($"[{_mut}]{def.ReadModel.EscapeMarkup()}[/]"),
                new Markup(activeIcon),
                new Markup(rewindIcon));
        }

        if (winEnd < projections.Count - 1)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↓ {projections.Count - 1 - winEnd} more below[/]"), new Markup(string.Empty), new Markup(string.Empty), new Markup(string.Empty));

        var selDef = projections[effectiveIndex];
        var miniDetail = new Panel(new Markup(
                $"  [{_mut}]readModel[/]  {selDef.ReadModel.EscapeMarkup()}  [{_mut}]active[/]  {(selDef.IsActive ? $"[{_suc}]yes[/]" : $"[{_mut}]no[/]")}  [{_mut}]rewindable[/]  {(selDef.IsRewindable ? $"[{_suc}]yes[/]" : $"[{_mut}]no[/]")}\n" +
                $"\n  [{_acc}][[ Enter ]][/] View declaration"))
            .Header($"[{_acc}] {selDef.Identifier.EscapeMarkup()} [/]")
            .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Accent).Expand();

        var projHeader = string.IsNullOrEmpty(filterText)
            ? $"[{_acc}] Projections ({allProjections.Count}) [/]"
            : $"[{_acc}] Projections ({projections.Count}/{allProjections.Count}) [/]";
        return new Rows(
            new Panel(table).Header(projHeader).BorderColor(OutputFormatter.Accent).NoBorder(),
            miniDetail);
    }

    static Rows BuildReadModelsView(WorkbenchData data, int selectedIndex, string filterText)
    {
        if (data.ReadModelDefinitions.Count == 0)
        {
            return new Rows(new Panel(new Markup($"\n  [{_mut}]No read models registered.[/]\n"))
                .Header($"[{_acc}] Read Models [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted));
        }

        var allModels = data.ReadModelDefinitions.OrderBy(d => d.ContainerName, StringComparer.OrdinalIgnoreCase).ToList();
        var models = string.IsNullOrEmpty(filterText)
            ? allModels
            : [.. allModels.Where(d => d.ContainerName.Contains(filterText, StringComparison.OrdinalIgnoreCase)
                || d.DisplayName.Contains(filterText, StringComparison.OrdinalIgnoreCase))];

        if (models.Count == 0)
        {
            return new Rows(new Panel(new Markup($"\n  [{_mut}]No read models matching '{filterText.EscapeMarkup()}'.[/]\n"))
                .Header($"[{_acc}] Read Models (0/{allModels.Count}) [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted));
        }

        var effectiveIndex = Math.Min(selectedIndex, models.Count - 1);
        var table = new Table().Border(TableBorder.Rounded).BorderColor(OutputFormatter.Muted).Expand()
            .AddColumn(new TableColumn(string.Empty).Width(2))
            .AddColumn(new TableColumn("[bold]Container[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Display Name[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Owner[/]").Padding(1, 0))
            .AddColumn(new TableColumn("[bold]Queryable[/]").Padding(1, 0));

        var (winStart, winEnd) = ListWindow(models.Count, effectiveIndex);
        if (winStart > 0)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↑ {winStart} more above[/]"), new Markup(string.Empty), new Markup(string.Empty), new Markup(string.Empty));
        for (var i = winStart; i <= winEnd; i++)
        {
            var rm = models[i];
            var isSelected = i == effectiveIndex;
            var queryIcon = rm.IsQueryable ? $"[{_suc}]✓[/]" : $"[{_mut}]✗[/]";
            table.AddRow(
                new Markup(isSelected ? $"[bold {_acc}]▶[/]" : string.Empty),
                new Markup(isSelected ? $"[bold {_acc}]{rm.ContainerName.EscapeMarkup()}[/]" : rm.ContainerName.EscapeMarkup()),
                new Markup($"[{_mut}]{rm.DisplayName.EscapeMarkup()}[/]"),
                new Markup($"[{_mut}]{rm.Owner.EscapeMarkup()}[/]"),
                new Markup(queryIcon));
        }

        if (winEnd < models.Count - 1)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↓ {models.Count - 1 - winEnd} more below[/]"), new Markup(string.Empty), new Markup(string.Empty), new Markup(string.Empty));

        var selModel = models[effectiveIndex];
        var miniDetail = new Panel(new Markup(
                $"  [{_mut}]owner[/]  {selModel.Owner.EscapeMarkup()}  [{_mut}]source[/]  {selModel.Source.EscapeMarkup()}  [{_mut}]queryable[/]  {(selModel.IsQueryable ? $"[{_suc}]yes[/]" : $"[{_mut}]no[/]")}\n" +
                $"  [{_mut}]identifier[/]  {selModel.Identifier.EscapeMarkup()}\n" +
                (selModel.IsQueryable ? $"\n  [{_acc}][[ Enter ]][/] View instances" : $"\n  [{_mut}](client-owned — instances are not stored on the server)[/]")))
            .Header($"[{_acc}] {selModel.ContainerName.EscapeMarkup()} [/]")
            .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Accent).Expand();

        var rmHeader = string.IsNullOrEmpty(filterText)
            ? $"[{_acc}] Read Models ({allModels.Count}) [/]"
            : $"[{_acc}] Read Models ({models.Count}/{allModels.Count}) [/]";
        return new Rows(
            new Panel(table).Header(rmHeader).BorderColor(OutputFormatter.Accent).NoBorder(),
            miniDetail);
    }

    static Rows BuildEventStoresView(WorkbenchData data, int selectedIndex)
    {
        var stores = data.EventStoreNames.Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (stores.Count == 0)
        {
            return new Rows(new Panel(new Markup($"\n  [{_mut}]No event stores found.[/]\n"))
                .Header($"[{_acc}] Event Stores [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted));
        }

        var effectiveIndex = Math.Min(selectedIndex, stores.Count - 1);
        var table = new Table().Border(TableBorder.Rounded).BorderColor(OutputFormatter.Muted).Expand()
            .AddColumn(new TableColumn(string.Empty).Width(2))
            .AddColumn(new TableColumn("[bold]Event Store[/]").Padding(1, 0));

        var (winStart, winEnd) = ListWindow(stores.Count, effectiveIndex);
        if (winStart > 0)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↑ {winStart} more above[/]"));
        for (var i = winStart; i <= winEnd; i++)
        {
            var store = stores[i];
            var isSelected = i == effectiveIndex;
            var isActive = string.Equals(store, data.EventStore, StringComparison.Ordinal);
            var label = isActive ? $"[bold]{store.EscapeMarkup()}[/]  [{_suc}]← active[/]" : store.EscapeMarkup();
            table.AddRow(
                new Markup(isSelected ? $"[bold {_acc}]▶[/]" : string.Empty),
                new Markup(isSelected ? $"[bold {_acc}]{store.EscapeMarkup()}[/]" : label));
        }

        if (winEnd < stores.Count - 1)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↓ {stores.Count - 1 - winEnd} more below[/]"));

        var hint = new Panel(new Markup($"  [{_mut}]Press[/]  [{_acc}][[ Enter ]][/]  [{_mut}]to switch to the selected event store.[/]"))
            .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted);

        return new Rows(
            new Panel(table).Header($"[{_acc}] Event Stores ({stores.Count})  active: {data.EventStore.EscapeMarkup()} [/]").BorderColor(OutputFormatter.Accent).NoBorder(),
            hint);
    }

    static Rows BuildNamespacesView(WorkbenchData data, int selectedIndex)
    {
        if (data.NamespaceNames.Count == 0)
        {
            return new Rows(new Panel(new Markup($"\n  [{_mut}]No namespaces found (or data not yet loaded).[/]\n"))
                .Header($"[{_acc}] Namespaces [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted));
        }

        var nsList = data.NamespaceNames.Order(StringComparer.OrdinalIgnoreCase).ToList();
        var effectiveIndex = Math.Min(selectedIndex, nsList.Count - 1);
        var table = new Table().Border(TableBorder.Rounded).BorderColor(OutputFormatter.Muted).Expand()
            .AddColumn(new TableColumn(string.Empty).Width(2))
            .AddColumn(new TableColumn("[bold]Namespace[/]").Padding(1, 0));

        var (winStart, winEnd) = ListWindow(nsList.Count, effectiveIndex);
        if (winStart > 0)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↑ {winStart} more above[/]"));
        for (var i = winStart; i <= winEnd; i++)
        {
            var ns = nsList[i];
            var isSelected = i == effectiveIndex;
            var isActive = string.Equals(ns, data.Namespace, StringComparison.Ordinal);
            var label = isActive ? $"[bold]{ns.EscapeMarkup()}[/]  [{_suc}]← active[/]" : ns.EscapeMarkup();
            table.AddRow(
                new Markup(isSelected ? $"[bold {_acc}]▶[/]" : string.Empty),
                new Markup(isSelected ? $"[bold {_acc}]{ns.EscapeMarkup()}[/]" : label));
        }

        if (winEnd < nsList.Count - 1)
            table.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↓ {nsList.Count - 1 - winEnd} more below[/]"));

        var hint = new Panel(new Markup($"  [{_mut}]Press[/]  [{_acc}][[ Enter ]][/]  [{_mut}]to switch to the selected namespace.[/]"))
            .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted);

        return new Rows(
            new Panel(table).Header($"[{_acc}] Namespaces ({nsList.Count})  active: {data.Namespace.EscapeMarkup()} [/]").BorderColor(OutputFormatter.Accent).NoBorder(),
            hint);
    }
}
