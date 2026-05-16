// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.for_ArcSettings.given;

public class a_temp_arc_directory : Specification, IDisposable
{
    protected string _tempDirectory = string.Empty;
    string? _previousCurrentDirectory;
    string? _previousArcUrl;

    void Establish()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"cratis-cli-arc-specs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _previousCurrentDirectory = Directory.GetCurrentDirectory();
        _previousArcUrl = Environment.GetEnvironmentVariable(ArcDefaults.UrlEnvVar);

        Environment.SetEnvironmentVariable(ArcDefaults.UrlEnvVar, null);
        Directory.SetCurrentDirectory(_tempDirectory);
    }

    protected void WriteLaunchSettings(string folderName, string applicationUrl)
    {
        var propertiesPath = Path.Combine(_tempDirectory, folderName);
        Directory.CreateDirectory(propertiesPath);

        var content =
            "{\n" +
            "  \"profiles\": {\n" +
            "    \"Core\": {\n" +
            "      \"commandName\": \"Project\",\n" +
            $"      \"applicationUrl\": \"{applicationUrl}\"\n" +
            "    }\n" +
            "  }\n" +
            "}\n";
        File.WriteAllText(Path.Combine(propertiesPath, "launchSettings.json"), content);
    }

    /// <inheritdoc/>
#pragma warning disable CA1033
    void IDisposable.Dispose()
#pragma warning restore CA1033
    {
        CleanUp();
    }

    protected virtual void CleanUp()
    {
        Directory.SetCurrentDirectory(_previousCurrentDirectory ?? Environment.CurrentDirectory);
        Environment.SetEnvironmentVariable(ArcDefaults.UrlEnvVar, _previousArcUrl);

        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
