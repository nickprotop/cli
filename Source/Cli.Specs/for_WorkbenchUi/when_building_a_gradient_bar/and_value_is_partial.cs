// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Cli.Commands.Chronicle.Workbench;

namespace Cratis.Cli.for_WorkbenchUi.when_building_a_gradient_bar;

[Collection(CliSpecsCollection.Name)]
public class and_value_is_partial : Specification
{
    string _result;

    void Because() => _result = WorkbenchUi.GradientBar(7, 10, 24);

    [Fact] void should_render_the_filled_block_character() => _result.ShouldContain("█");

    [Fact] void should_render_the_empty_block_character() => _result.ShouldContain("░");

    [Fact] void should_fill_seventy_percent_of_the_cells() => _result.Count(c => c == '█').ShouldEqual(17);

    [Fact] void should_leave_the_remaining_cells_empty() => _result.Count(c => c == '░').ShouldEqual(7);
}
