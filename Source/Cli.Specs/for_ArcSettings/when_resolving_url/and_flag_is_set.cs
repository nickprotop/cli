// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.for_ArcSettings.when_resolving_url;

[Collection(CliSpecsCollection.Name)]
public class and_flag_is_set : given.a_temp_arc_directory
{
    ArcSettings _settings = null!;
    string _result = string.Empty;

    void Establish() => _settings = new ArcSettings { Url = "http://localhost:6001/" };

    void Because() => _result = _settings.ResolveUrl();

    [Fact] void should_return_the_flag_value() => _result.ShouldEqual("http://localhost:6001");
}
