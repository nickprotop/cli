// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.for_ArcSettings.when_resolving_url;

[Collection(CliSpecsCollection.Name)]
public class and_launch_settings_exists_in_lowercase_properties_folder : given.a_temp_arc_directory
{
    ArcSettings _settings = null!;
    string _result = string.Empty;

    void Establish()
    {
        WriteLaunchSettings("properties", "http://localhost:5200/");
        _settings = new ArcSettings();
    }

    void Because() => _result = _settings.ResolveUrl();

    [Fact] void should_return_the_application_url() => _result.ShouldEqual("http://localhost:5200");
}
