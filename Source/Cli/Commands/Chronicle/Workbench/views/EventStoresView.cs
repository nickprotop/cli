// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Event Stores tab — a filterable table of available event stores with an active indicator and a
/// detail pane. Switching (Enter, double-click, or the Switch action) makes the selected store active.
/// </summary>
public class EventStoresView : SwitchableNameView
{
    /// <inheritdoc/>
    protected override string? PageTitle => "EVENT STORES";

    /// <inheritdoc/>
    protected override string DetailPanelHeader => "EVENT STORE";

    /// <inheritdoc/>
    protected override string ColumnHeader => "Event Store";

    /// <inheritdoc/>
    protected override string Noun => "event store";

    /// <inheritdoc/>
    protected override (IReadOnlyList<string> Names, string Active) Source(WorkbenchData data) =>
        (data.EventStoreNames, data.EventStore);
}
