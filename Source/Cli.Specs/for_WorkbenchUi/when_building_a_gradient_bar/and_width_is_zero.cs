// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Cli.Commands.Chronicle.Workbench;

namespace Cratis.Cli.for_WorkbenchUi.when_building_a_gradient_bar;

[Collection(CliSpecsCollection.Name)]
public class and_width_is_zero : Specification
{
    string _result;

    void Because() => _result = WorkbenchUi.GradientBar(5, 10, 0);

    [Fact] void should_render_nothing() => _result.ShouldBeEmpty();
}
