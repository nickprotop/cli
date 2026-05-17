// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json;
using Spectre.Console.Rendering;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

public static partial class WorkbenchRenderer
{
    static readonly JsonSerializerOptions _prettyJson = new() { WriteIndented = true };

    static Rows BuildObserverDetailPage(WorkbenchData data, string focusedId, int selectedIndex)
    {
        var obs = data.Observers.FirstOrDefault(o => o.Id == focusedId);
        if (obs is null)
        {
            return new Rows(new Panel(new Markup($"[{_dan}]Observer '{focusedId.EscapeMarkup()}' not found.[/]"))
                .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Danger).Expand());
        }

        var lag = ComputeLag(obs, data.TailSequenceNumber);
        var lagText = lag switch { null => $"[{_mut}]—[/]", 0 => $"[{_suc}]caught up[/]", _ => $"[{_war}]{lag.Value:N0} events behind tail ({data.TailSequenceNumber?.ToString("N0") ?? "—"})[/]" };

        var infoPanel = new Panel(new Rows(
                new Markup($"  [{_mut}]type[/]  [bold]{obs.Type.ToString().EscapeMarkup()}[/]   [{_mut}]owner[/]  {obs.Owner.ToString().EscapeMarkup()}   [{_mut}]sequence[/]  [{_mut}]{obs.EventSequenceId.EscapeMarkup()}[/]   [{_mut}]replayable[/]  {(obs.IsReplayable ? $"[{_suc}]yes[/]" : $"[{_mut}]no[/]")}"),
                new Markup($"  [{_mut}]state[/]  {StateIcon(obs.RunningState)} {StateName(obs.RunningState)}   [{_mut}]subscribed[/]  {(obs.IsSubscribed ? $"[{_suc}]yes[/]" : $"[{_dan}]no[/]")}"),
                new Markup($"  [{_mut}]next seq[/]  {FormatSeq(obs.NextEventSequenceNumber)}   [{_mut}]last handled[/]  {FormatSeq(obs.LastHandledEventSequenceNumber)}   [{_mut}]lag[/]  {lagText}")))
            .Header($"[{_acc}] {obs.Id.EscapeMarkup()} [/]")
            .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Accent).Expand();

        var eventTypes = (obs.EventTypes ?? []).OrderBy(et => et.Id, StringComparer.OrdinalIgnoreCase).ToList();
        IRenderable eventTypesSection;
        if (eventTypes.Count == 0)
        {
            eventTypesSection = new Panel(new Markup($"  [{_mut}]No subscribed event types.[/]"))
                .Header($"[{_acc}] Subscribed Event Types [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted).Expand();
        }
        else
        {
            var etEffective = Math.Min(selectedIndex, eventTypes.Count - 1);
            var etTable = new Table().Border(TableBorder.Rounded).BorderColor(OutputFormatter.Muted).Expand()
                .AddColumn(new TableColumn(string.Empty).Width(2))
                .AddColumn(new TableColumn("[bold]EventType[/]").Padding(1, 0))
                .AddColumn(new TableColumn("[bold]Generation[/]").Padding(1, 0).Width(12).NoWrap());

            var (winStart, winEnd) = ListWindow(eventTypes.Count, etEffective);
            if (winStart > 0)
                etTable.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↑ {winStart} more above[/]"), new Markup(string.Empty));
            for (var i = winStart; i <= winEnd; i++)
            {
                var et = eventTypes[i];
                var isSel = i == etEffective;
                etTable.AddRow(
                    new Markup(isSel ? $"[bold {_acc}]▶[/]" : string.Empty),
                    new Markup(isSel ? $"[bold {_acc}]{et.Id.EscapeMarkup()}[/]" : et.Id.EscapeMarkup()),
                    new Markup($"[{_mut}]{et.Generation}[/]"));
            }

            if (winEnd < eventTypes.Count - 1)
                etTable.AddRow(new Markup(string.Empty), new Markup($"[{_mut}]↓ {eventTypes.Count - 1 - winEnd} more below[/]"), new Markup(string.Empty));

            eventTypesSection = new Panel(etTable)
                .Header($"[{_acc}] Subscribed Event Types ({eventTypes.Count})  ↑↓ select  [[ Enter ]] view schema [/]")
                .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Accent).Expand();
        }

        if (obs.Type.ToString().Contains("Projection", StringComparison.OrdinalIgnoreCase))
        {
            var projHint = new Panel(new Markup($"  [{_acc}][[ P ]][/]  [{_mut}]View this observer's projection definition[/]"))
                .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Muted).Expand();
            return new Rows(infoPanel, eventTypesSection, projHint);
        }

        return new Rows(infoPanel, eventTypesSection);
    }

    static Rows BuildFailedPartitionDetailPage(WorkbenchData data, string focusedId, int scrollOffset)
    {
        var sep = focusedId.IndexOf('/');
        FailedPartition? fp = null;
        if (sep >= 0)
        {
            var obsId = focusedId[..sep];
            var partition = focusedId[(sep + 1)..];
            fp = data.FailedPartitions.FirstOrDefault(f => f.ObserverId == obsId && f.Partition == partition);
        }

        if (fp is null)
        {
            return new Rows(new Panel(new Markup($"[{_dan}]Failed partition not found.[/]"))
                .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Danger).Expand());
        }

        var attempts = fp.Attempts.OrderByDescending(a => (DateTimeOffset?)a.Occurred ?? DateTimeOffset.MinValue).ToList();
        var lastAttempt = attempts.FirstOrDefault();
        var msg = (lastAttempt?.Messages.FirstOrDefault() ?? "—").EscapeMarkup();
        var stackTrace = lastAttempt?.StackTrace ?? string.Empty;

        var header = new Panel(new Markup(
                $"  [{_mut}]observer[/]  [{_dan}]{fp.ObserverId.EscapeMarkup()}[/]   [{_mut}]partition[/]  {fp.Partition.EscapeMarkup()}   [{_mut}]attempts[/]  [{_dan}]{attempts.Count}[/]\n" +
                $"  [{_mut}]last failed[/]  [{_dan}]{(lastAttempt is not null ? lastAttempt.Occurred.ToString() : "—").EscapeMarkup()}[/]"))
            .Header($"[{_dan}] Failure Detail [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Danger).Expand();

        var msgPanel = new Panel(new Markup($"  [{_dan}]{msg}[/]"))
            .Header($"[{_dan}] Last Error [/]").Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Danger).Expand();

        var stackLines = (string.IsNullOrEmpty(stackTrace) ? ["(no stack trace)"] : stackTrace.Split('\n').Select(l => $"  [{_mut}]{l.EscapeMarkup()}[/]").ToList())
            as IReadOnlyList<string>;
        var stackPanel = ScrollableText("Stack Trace", stackLines, scrollOffset, OutputFormatter.Muted);

        return new Rows(header, msgPanel, stackPanel);
    }

    static Rows BuildEventDetailPage(WorkbenchData data, string focusedId, int scrollOffset)
    {
        AppendedEvent? evt = null;
        if (ulong.TryParse(focusedId, out var seq))
            evt = data.RecentEvents.FirstOrDefault(e => e.Context.SequenceNumber == seq);

        if (evt is null)
        {
            return new Rows(new Panel(new Markup($"[{_dan}]Event not found (seq# {focusedId.EscapeMarkup()}).[/]"))
                .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Danger).Expand());
        }

        var ctx = evt.Context;
        var header = new Panel(new Rows(
                new Markup($"  [{_mut}]seq#[/]  [bold]{ctx.SequenceNumber:N0}[/]   [{_mut}]eventType[/]  [bold {_acc}]{(ctx.EventType?.Id ?? "—").EscapeMarkup()}[/]  [{_mut}]+{ctx.EventType?.Generation}[/]"),
                new Markup($"  [{_mut}]eventSourceId[/]  {(ctx.EventSourceId ?? "—").EscapeMarkup()}   [{_mut}]occurred[/]  [{_mut}]{ctx.Occurred.ToString().EscapeMarkup()}[/]"),
                new Markup($"  [{_mut}]correlationId[/]  [{_mut}]{ctx.CorrelationId.ToString().EscapeMarkup()}[/]")))
            .Header($"[{_acc}] Event seq# {ctx.SequenceNumber:N0} [/]")
            .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Accent).Expand();

        List<string> contentLines;
        if (string.IsNullOrWhiteSpace(evt.Content))
        {
            contentLines = [$"  [{_mut}](no content)[/]"];
        }
        else
        {
            try
            {
                var pretty = JsonSerializer.Serialize(
                    JsonSerializer.Deserialize<JsonElement>(evt.Content),
                    _prettyJson);
                contentLines = [.. pretty.Split('\n').Select(l => $"  [{_mut}]{l.EscapeMarkup()}[/]")];
            }
            catch
            {
                contentLines = [$"  [{_mut}]{evt.Content.EscapeMarkup()}[/]"];
            }
        }

        var contentPanel = ScrollableText("Content", contentLines, scrollOffset, OutputFormatter.Muted);
        return new Rows(header, contentPanel);
    }

    static Rows BuildEventTypeDetailPage(WorkbenchData data, string focusedId, int scrollOffset)
    {
        var parts = focusedId.Split('+');
        var typeId = parts[0];
        var gen = parts.Length > 1 ? parts[1] : string.Empty;
        var reg = data.EventTypeRegistrations
            .FirstOrDefault(r => string.Equals(r.Type.Id, typeId, StringComparison.OrdinalIgnoreCase)
                && r.Type.Generation.ToString() == gen);

        if (reg is null)
        {
            return new Rows(new Panel(new Markup($"[{_dan}]Event type '{focusedId.EscapeMarkup()}' not found.[/]"))
                .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Danger).Expand());
        }

        var header = new Panel(new Markup(
                $"  [{_mut}]id[/]  [bold]{reg.Type.Id.EscapeMarkup()}[/]   [{_mut}]generation[/]  {reg.Type.Generation}   [{_mut}]owner[/]  {reg.Owner.ToString().EscapeMarkup()}   [{_mut}]source[/]  {reg.Source.ToString().EscapeMarkup()}   [{_mut}]tombstone[/]  {reg.Type.Tombstone}"))
            .Header($"[{_acc}] {reg.Type.Id.EscapeMarkup()} +{reg.Type.Generation} [/]")
            .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Accent).Expand();

        List<string> schemaLines;
        if (string.IsNullOrWhiteSpace(reg.Schema))
        {
            schemaLines = [$"  [{_mut}](no schema)[/]"];
        }
        else
        {
            try
            {
                var pretty = JsonSerializer.Serialize(
                    JsonSerializer.Deserialize<JsonElement>(reg.Schema),
                    _prettyJson);
                schemaLines = [.. pretty.Split('\n').Select(l => $"  [{_mut}]{l.EscapeMarkup()}[/]")];
            }
            catch
            {
                schemaLines = [.. reg.Schema.Split('\n').Select(l => $"  [{_mut}]{l.EscapeMarkup()}[/]")];
            }
        }

        var schemaPanel = ScrollableText("JSON Schema", schemaLines, scrollOffset, OutputFormatter.Muted);
        return new Rows(header, schemaPanel);
    }

    static Rows BuildReadModelDetailPage(WorkbenchData data, string focusedId, int scrollOffset)
    {
        var def = data.ReadModelDefinitions
            .FirstOrDefault(d => string.Equals(d.ContainerName, focusedId, StringComparison.OrdinalIgnoreCase));

        var header = new Panel(new Markup(
                $"  [{_mut}]container[/]  [bold]{focusedId.EscapeMarkup()}[/]   [{_mut}]owner[/]  {(def?.Owner ?? "—").EscapeMarkup()}   [{_mut}]queryable[/]  {(def?.IsQueryable == true ? $"[{_suc}]yes[/]" : $"[{_mut}]no[/]")}   [{_mut}]total[/]  [bold]{data.ReadModelInstancesTotalCount:N0}[/]"))
            .Header($"[{_acc}] {focusedId.EscapeMarkup()} — Instances [/]")
            .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Accent).Expand();

        IReadOnlyList<string> lines;
        if (data.ReadModelInstancesError is not null)
        {
            lines = [$"  [{_dan}]Error fetching instances: {data.ReadModelInstancesError.EscapeMarkup()}[/]"];
        }
        else if (data.ReadModelInstances.Count == 0)
        {
            lines = [$"  [{_mut}](no instances — model may be client-owned or empty)[/]"];
        }
        else
        {
            var allLines = new List<string>();
            for (var i = 0; i < data.ReadModelInstances.Count; i++)
            {
                if (i > 0) allLines.Add($"  [{_mut}]─────────────────────────────────────────[/]");
                allLines.Add($"  [{_acc}]Instance {i + 1}[/]");
                allLines.AddRange(data.ReadModelInstances[i].Split('\n').Select(l => $"  [{_mut}]{l.EscapeMarkup()}[/]"));
            }
            if (data.ReadModelInstancesTotalCount > data.ReadModelInstances.Count)
                allLines.Add($"\n  [{_mut}]showing {data.ReadModelInstances.Count} of {data.ReadModelInstancesTotalCount:N0} total instances[/]");
            lines = allLines;
        }

        var instancesPanel = ScrollableText("Instances  (↑↓ scroll)", lines, scrollOffset, OutputFormatter.Muted);
        return new Rows(header, instancesPanel);
    }

    static Rows BuildProjectionDetailPage(WorkbenchData data, string focusedId, int scrollOffset)
    {
        var def = data.ProjectionDefinitions
            .FirstOrDefault(d => string.Equals(d.Identifier, focusedId, StringComparison.OrdinalIgnoreCase));

        if (def is null)
        {
            return new Rows(new Panel(new Markup($"[{_dan}]Projection '{focusedId.EscapeMarkup()}' not found.[/]"))
                .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Danger).Expand());
        }

        data.ProjectionDeclarations.TryGetValue(def.Identifier, out var declaration);

        var header = new Panel(new Markup(
                $"  [{_mut}]readModel[/]  {def.ReadModel.EscapeMarkup()}   [{_mut}]active[/]  {(def.IsActive ? $"[{_suc}]yes[/]" : $"[{_mut}]no[/]")}   [{_mut}]rewindable[/]  {(def.IsRewindable ? $"[{_suc}]yes[/]" : $"[{_mut}]no[/]")}   [{_mut}]autoMap[/]  {def.AutoMap.ToString().EscapeMarkup()}"))
            .Header($"[{_acc}] {def.Identifier.EscapeMarkup()} [/]")
            .Border(BoxBorder.Rounded).BorderColor(OutputFormatter.Accent).Expand();

        IReadOnlyList<string> declLines = string.IsNullOrWhiteSpace(declaration)
            ? [$"  [{_mut}](no declaration available)[/]"]
            : [.. declaration.Split('\n').Select(l => $"  [{_mut}]{l.EscapeMarkup()}[/]")];

        var declPanel = ScrollableText("Declaration", declLines, scrollOffset, OutputFormatter.Muted);
        return new Rows(header, declPanel);
    }
}
