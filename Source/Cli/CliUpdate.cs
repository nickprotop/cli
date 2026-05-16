// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace Cratis.Cli;

/// <summary>
/// Represents the update strategy for the current CLI installation.
/// </summary>
public enum CliUpdateStrategy
{
    /// <summary>
    /// Update by running <c>dotnet tool update -g Cratis.Cli</c>.
    /// </summary>
    DotNetTool,

    /// <summary>
    /// Update by running <c>brew upgrade cratis</c>.
    /// </summary>
    Homebrew,

    /// <summary>
    /// Update manually by downloading and replacing the Linux native binary.
    /// </summary>
    ManualLinux,

    /// <summary>
    /// Update manually using the installation mechanism used by the user.
    /// </summary>
    Manual
}

/// <summary>
/// Provides update-strategy detection and update-related command guidance.
/// </summary>
public static class CliUpdate
{
#if CRATIS_NATIVE
    const bool IsNativeBuild = true;
#else
    const bool IsNativeBuild = false;
#endif

    const string PackageId = "Cratis.Cli";

    /// <summary>
    /// Detects how this CLI instance should be updated.
    /// </summary>
    /// <returns>The update strategy.</returns>
    public static CliUpdateStrategy DetectStrategy()
    {
        var processPath = GetEffectiveProcessPath();
        var baseDirectory = AppContext.BaseDirectory;
        return DetectStrategy(
            processPath,
            baseDirectory,
            IsNativeBuild,
            OperatingSystem.IsMacOS(),
            OperatingSystem.IsLinux());
    }

    /// <summary>
    /// Creates process launch settings that should run before the main update command.
    /// </summary>
    /// <param name="strategy">The detected update strategy.</param>
    /// <param name="targetVersion">Optional target version.</param>
    /// <returns>A process start info for supported auto-update paths, otherwise null.</returns>
    public static ProcessStartInfo? CreatePreUpdateProcessStartInfo(CliUpdateStrategy strategy, string? targetVersion = null)
    {
        if (strategy != CliUpdateStrategy.Homebrew || !string.IsNullOrWhiteSpace(targetVersion))
        {
            return null;
        }

        return new ProcessStartInfo
        {
            FileName = "brew",
            Arguments = "update",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    /// <summary>
    /// Creates the process launch settings for automatic update, when available.
    /// </summary>
    /// <param name="strategy">The detected update strategy.</param>
    /// <param name="targetVersion">Optional target version.</param>
    /// <returns>A process start info for supported auto-update paths, otherwise null.</returns>
    public static ProcessStartInfo? CreateUpdateProcessStartInfo(CliUpdateStrategy strategy, string? targetVersion = null)
    {
        if (strategy == CliUpdateStrategy.DotNetTool)
        {
            var arguments = $"tool update -g {PackageId}";
            if (!string.IsNullOrWhiteSpace(targetVersion))
            {
                arguments += $" --version {targetVersion}";
            }

            return new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        if (strategy == CliUpdateStrategy.Homebrew)
        {
            if (!string.IsNullOrWhiteSpace(targetVersion))
            {
                return null;
            }

            return new ProcessStartInfo
            {
                FileName = "brew",
                Arguments = "upgrade cratis",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        return null;
    }

    /// <summary>
    /// Gets manual update guidance for non-automatic update strategies.
    /// </summary>
    /// <param name="strategy">The detected strategy.</param>
    /// <returns>Manual guidance text, or null when auto-update is supported.</returns>
    public static string? GetManualUpdateInstructions(CliUpdateStrategy strategy) =>
        strategy switch
        {
            CliUpdateStrategy.ManualLinux =>
                "Manual update (Linux):\n" +
                "curl -Lo cratis.tar.gz https://github.com/Cratis/cli/releases/latest/download/cratis-linux-x64.tar.gz\n" +
                "# arm64: curl -Lo cratis.tar.gz https://github.com/Cratis/cli/releases/latest/download/cratis-linux-arm64.tar.gz\n" +
                "tar -xzf cratis.tar.gz\n" +
                "sudo mv cratis /usr/local/bin/cratis",
            CliUpdateStrategy.Manual => "This installation method cannot be auto-updated by the CLI. Please upgrade using the same method you used to install it.",
            _ => null
        };

    /// <summary>
    /// Gets update hint text for interactive output when a newer version is available.
    /// </summary>
    /// <param name="strategy">The detected strategy.</param>
    /// <param name="currentVersion">Current version.</param>
    /// <param name="latestVersion">Latest available version.</param>
    /// <returns>A user-facing hint message.</returns>
    public static string GetUpdateHint(CliUpdateStrategy strategy, string currentVersion, string latestVersion)
    {
        var baseMessage = $"Update available: {currentVersion} -> {latestVersion}";
        return strategy switch
        {
            CliUpdateStrategy.ManualLinux => $"{baseMessage} — manual update required (download the latest Linux tarball and replace the 'cratis' binary)",
            CliUpdateStrategy.Manual => $"{baseMessage} — manual update required (use your original installation method)",
            _ => $"{baseMessage} — run 'cratis update'"
        };
    }

    internal static CliUpdateStrategy DetectStrategy(
        string? processPath,
        string? baseDirectory,
        bool isNativeBuild,
        bool isMacOS,
        bool isLinux)
    {
        if (IsDotNetToolPath(processPath) || IsDotNetToolPath(baseDirectory))
        {
            return CliUpdateStrategy.DotNetTool;
        }

        if (!isNativeBuild)
        {
            return CliUpdateStrategy.DotNetTool;
        }

        if (isMacOS && IsHomebrewPath(processPath))
        {
            return CliUpdateStrategy.Homebrew;
        }

        if (isLinux)
        {
            return CliUpdateStrategy.ManualLinux;
        }

        return CliUpdateStrategy.Manual;
    }

    static bool IsDotNetToolPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (path.Contains(".dotnet/tools", StringComparison.OrdinalIgnoreCase) ||
         path.Contains(".dotnet\\tools", StringComparison.OrdinalIgnoreCase));

    static bool IsHomebrewPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (path.Contains("/Cellar/cratis/", StringComparison.OrdinalIgnoreCase) ||
         path.Contains("/Homebrew/Cellar/cratis/", StringComparison.OrdinalIgnoreCase));

    static string? GetEffectiveProcessPath()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var currentPath = path;
        for (var i = 0; i < 8; i++)
        {
            try
            {
                var linkTarget = File.ResolveLinkTarget(currentPath, false);
                if (linkTarget is null)
                {
                    return currentPath;
                }

                currentPath = linkTarget.FullName;
            }
            catch
            {
                return currentPath;
            }
        }

        return currentPath;
    }
}
