// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.for_FilterableTableView.when_activating_a_row;

/// <summary>
/// Verifies the base reading-default contract: activating a row (Enter / double-click) on a view that
/// does not override the behavior opens a read-only detail overlay, so no table is ever inert.
/// </summary>
[Collection(CliSpecsCollection.Name)]
public class it_opens_a_reading_modal : given.a_populated_table_view
{
    int _windowsBefore;

    void Because()
    {
        _windowsBefore = _windowSystem.Windows.Count;
        _view.Activate("alpha");
    }

    [Fact] void should_open_one_additional_window() => _windowSystem.Windows.Count.ShouldEqual(_windowsBefore + 1);
}
