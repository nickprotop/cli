// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.for_ArcSettings.when_resolving_url;

[Collection(CliSpecsCollection.Name)]
public class and_environment_variable_is_set : given.a_temp_arc_directory
{
    ArcSettings _settings = null!;
    string _result = string.Empty;

    void Establish()
    {
        WriteLaunchSettings("Properties", "http://localhost:5100/");
        Environment.SetEnvironmentVariable(ArcDefaults.UrlEnvVar, "http://localhost:5300/");
        _settings = new ArcSettings();
    }

    void Because() => _result = _settings.ResolveUrl();

    [Fact] void should_return_environment_variable() => _result.ShouldEqual("http://localhost:5300");
}
