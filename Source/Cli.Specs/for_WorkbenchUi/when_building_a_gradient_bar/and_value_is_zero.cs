// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Cli.Commands.Chronicle.Workbench;

namespace Cratis.Cli.for_WorkbenchUi.when_building_a_gradient_bar;

[Collection(CliSpecsCollection.Name)]
public class and_value_is_zero : Specification
{
    string _result;

    void Because() => _result = WorkbenchUi.GradientBar(0, 10, 24);

    [Fact] void should_render_no_filled_cells() => _result.ShouldNotContain("█");

    [Fact] void should_fill_all_cells_with_the_empty_character() => _result.Count(c => c == '░').ShouldEqual(24);
}
