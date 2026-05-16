// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.for_ArcSettings.when_resolving_url;

[Collection(CliSpecsCollection.Name)]
public class and_no_override_is_configured : given.a_temp_arc_directory
{
    ArcSettings _settings = null!;
    string _result = string.Empty;

    void Establish() => _settings = new ArcSettings();

    void Because() => _result = _settings.ResolveUrl();

    [Fact] void should_return_default_url() => _result.ShouldEqual(ArcDefaults.DefaultUrl);
}
