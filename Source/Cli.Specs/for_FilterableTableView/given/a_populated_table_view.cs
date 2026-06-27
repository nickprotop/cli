// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Cli.Commands.Chronicle.Workbench;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Layout;

namespace Cratis.Cli.for_FilterableTableView.given;

/// <summary>
/// A minimal concrete <see cref="FilterableTableView{TItem}"/> over plain strings, populated and hosted
/// in a headless window system so its row-activation behavior can be exercised.
/// </summary>
public class a_populated_table_view : Specification
{
    protected ConsoleWindowSystem _windowSystem;
    protected TestTableView _view;

    void Establish()
    {
        _windowSystem = new ConsoleWindowSystem(new HeadlessConsoleDriver(200, 50));
        _view = new TestTableView { Rows = ["alpha", "beta"] };

        var panel = Controls.ScrollablePanel().Build();
        _view.PopulateContent(panel, _windowSystem);
        _view.UpdateData(WorkbenchData.Loading(new WorkbenchSettings()));
    }

    /// <summary>
    /// A concrete table view over plain strings that exposes the protected activation path for specs.
    /// </summary>
    public class TestTableView : FilterableTableView<string>
    {
        /// <summary>Gets or sets the backing rows returned by <see cref="GetItems"/>.</summary>
        public IReadOnlyList<string> Rows { get; set; } = [];

        /// <summary>Activates the given row through the same path as Enter / double-click.</summary>
        /// <param name="item">The row to activate.</param>
        public void Activate(string item) => OnRowActivated(item);

        /// <inheritdoc/>
        protected override IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns =>
            [("Value", TextJustification.Left, null)];

        /// <inheritdoc/>
        protected override IEnumerable<string> GetItems(WorkbenchData data) => Rows;

        /// <inheritdoc/>
        protected override string GetKey(string item) => item;

        /// <inheritdoc/>
        protected override string[] BuildRow(string item) => [item];

        /// <inheritdoc/>
        protected override string RenderDetail(string? item, WorkbenchData? data) => item ?? "none";

        /// <inheritdoc/>
        protected override bool MatchesFilter(string item, string filter) =>
            item.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
