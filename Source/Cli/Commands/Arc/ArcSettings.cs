// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json;

namespace Cratis.Cli.Commands.Arc;

/// <summary>
/// Settings shared by all commands that connect to a Cratis Arc application.
/// </summary>
public class ArcSettings : GlobalSettings
{
    /// <summary>
    /// Gets or sets the base URL of the Arc application.
    /// </summary>
    [CommandOption("--url <URL>")]
    [Description("Base URL of the Arc application (e.g. http://localhost:5000)")]
    public string? Url { get; set; }

    /// <summary>
    /// Resolves the effective base URL by checking the flag, environment variable, launch settings, then default.
    /// </summary>
    /// <returns>The resolved base URL string.</returns>
#pragma warning disable CA1055 // URI string return type — string is intentional here for consistency with connection helpers
    public string ResolveUrl()
#pragma warning restore CA1055
    {
        if (!string.IsNullOrWhiteSpace(Url))
        {
            return Url.TrimEnd('/');
        }

        var envVar = Environment.GetEnvironmentVariable(ArcDefaults.UrlEnvVar);
        if (!string.IsNullOrWhiteSpace(envVar))
        {
            return envVar.TrimEnd('/');
        }

        var launchSettingsUrl = TryGetApplicationUrlFromLaunchSettings();
        if (!string.IsNullOrWhiteSpace(launchSettingsUrl))
        {
            return launchSettingsUrl;
        }

        return ArcDefaults.DefaultUrl;
    }

    static string? TryGetApplicationUrlFromLaunchSettings()
    {
        var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDirectory is not null)
        {
            var upperCasePath = Path.Combine(currentDirectory.FullName, "Properties", "launchSettings.json");
            var upperCaseUrl = TryReadApplicationUrl(upperCasePath);
            if (!string.IsNullOrWhiteSpace(upperCaseUrl))
            {
                return upperCaseUrl;
            }

            var lowerCasePath = Path.Combine(currentDirectory.FullName, "properties", "launchSettings.json");
            var lowerCaseUrl = TryReadApplicationUrl(lowerCasePath);
            if (!string.IsNullOrWhiteSpace(lowerCaseUrl))
            {
                return lowerCaseUrl;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }

    static string? TryReadApplicationUrl(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var profile in profiles.EnumerateObject())
            {
                if (profile.Value.ValueKind != JsonValueKind.Object ||
                    !profile.Value.TryGetProperty("applicationUrl", out var applicationUrlProperty) ||
                    applicationUrlProperty.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var applicationUrl = applicationUrlProperty.GetString();
                if (string.IsNullOrWhiteSpace(applicationUrl))
                {
                    continue;
                }

                var firstUrl = applicationUrl.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(firstUrl))
                {
                    return firstUrl.TrimEnd('/');
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
