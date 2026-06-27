// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Namespaces tab — a filterable table of namespaces in the current event store, with an active
/// indicator and a detail pane. Switching (Enter, double-click, or the Switch action) makes the
/// selected namespace active.
/// </summary>
public class NamespacesView : SwitchableNameView
{
    /// <inheritdoc/>
    protected override string? PageTitle => "NAMESPACES";

    /// <inheritdoc/>
    protected override string DetailPanelHeader => "NAMESPACE";

    /// <inheritdoc/>
    protected override string ColumnHeader => "Namespace";

    /// <inheritdoc/>
    protected override string Noun => "namespace";

    /// <inheritdoc/>
    protected override (IReadOnlyList<string> Names, string Active) Source(WorkbenchData data) =>
        (data.NamespaceNames, data.Namespace);

    /// <inheritdoc/>
    protected override IEnumerable<string> ExtraDetailLines(NamedActiveRow item, WorkbenchData? data, string mut) =>
    [
        $"[{mut}]Store[/]      {data?.EventStore ?? string.Empty}",
        $"[{mut}]Available[/]  {data?.NamespaceNames.Count ?? 0} namespace(s)"
    ];
}
