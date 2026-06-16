// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace Cratis.Cli.Commands.Version;

/// <summary>
/// Updates the Cratis CLI to the latest version using the detected installation method.
/// </summary>
[LlmDescription("Updates the cratis CLI to the latest version using the appropriate installation method (dotnet tool or Homebrew).")]
[CliCommand("update", "Update the Cratis CLI to the latest version")]
[CliExample("update")]
[CliExample("update", "--version", "1.2.3")]
[LlmOutputAdvice("json", "JSON contains previousVersion, currentVersion, and updated flag.")]
[LlmOption("--version", "string", "Specific version to install (default: latest)")]
public class SelfUpdateCommand : AsyncCommand<SelfUpdateSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteAsync(CommandContext context, SelfUpdateSettings settings, CancellationToken cancellationToken)
    {
        var format = ResolveFormat(settings.Output);
        var currentVersion = VersionCommand.GetCliVersion();
        var strategy = CliUpdate.DetectStrategy();

        // Check for available updates before performing the update
        var expectedNewVersion = settings.TargetVersion;
        if (string.IsNullOrWhiteSpace(expectedNewVersion))
        {
            using var updateCheckCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                expectedNewVersion = await UpdateChecker.CheckForUpdate(UpdateChecker.CliPackageId, currentVersion, updateCheckCts.Token);
            }
            catch
            {
                // If we can't check for updates, proceed anyway - the update tool will handle it
            }
        }

        if (string.Equals(format, OutputFormats.Table, StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine($"[bold]Updating Cratis CLI...[/] (current: {currentVersion.EscapeMarkup()})");
        }

        var preUpdateStartInfo = CliUpdate.CreatePreUpdateProcessStartInfo(strategy, settings.TargetVersion);
        if (preUpdateStartInfo is not null)
        {
            var preUpdateResult = await RunProcess(preUpdateStartInfo);
            if (preUpdateResult is not null)
            {
                return preUpdateResult.Value;
            }
        }

        var startInfo = CliUpdate.CreateUpdateProcessStartInfo(strategy, settings.TargetVersion);
        if (startInfo is null)
        {
            if (!string.IsNullOrWhiteSpace(settings.TargetVersion) && strategy == CliUpdateStrategy.Homebrew)
            {
                OutputFormatter.WriteError(format, "Target version is not supported for Homebrew updates", "Run 'cratis update' to upgrade to the latest Homebrew version", ExitCodes.ValidationErrorCode);
                return ExitCodes.ValidationError;
            }

            var instructions = CliUpdate.GetManualUpdateInstructions(strategy) ?? "Manual update required.";
            if (string.Equals(format, OutputFormats.Json, StringComparison.Ordinal) || string.Equals(format, OutputFormats.JsonCompact, StringComparison.Ordinal))
            {
                OutputFormatter.WriteObject(format, new
                {
                    PreviousVersion = currentVersion,
                    CurrentVersion = currentVersion,
                    Updated = false,
                    Strategy = strategy.ToString(),
                    Message = instructions
                });
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]{instructions.EscapeMarkup()}[/]");
            }
            return ExitCodes.Success;
        }

        var updateResult = await RunProcess(startInfo);
        if (updateResult is not null)
        {
            return updateResult.Value;
        }

        // Use the expected new version instead of checking the currently running assembly
        // The currently running process won't change version until it's restarted
        var newVersion = expectedNewVersion ?? currentVersion;
        var wasUpdated = !string.IsNullOrWhiteSpace(expectedNewVersion);

        if (string.Equals(format, OutputFormats.Json, StringComparison.Ordinal) || string.Equals(format, OutputFormats.JsonCompact, StringComparison.Ordinal))
        {
            OutputFormatter.WriteObject(format, new
            {
                PreviousVersion = currentVersion,
                CurrentVersion = newVersion,
                Updated = wasUpdated
            });
        }
        else if (wasUpdated)
        {
            AnsiConsole.MarkupLine($"[green]Updated from {currentVersion.EscapeMarkup()} to {newVersion.EscapeMarkup()}[/]");
            AnsiConsole.MarkupLine("[dim]The new version will be active when you run 'cratis' again.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]Already at the latest version ({currentVersion.EscapeMarkup()})[/]");
        }

        return ExitCodes.Success;

        async Task<int?> RunProcess(ProcessStartInfo processStartInfo)
        {
            using var process = Process.Start(processStartInfo);
            if (process is null)
            {
                var hint = strategy switch
                {
                    CliUpdateStrategy.DotNetTool => "Ensure the .NET SDK is installed and 'dotnet' is on your PATH",
                    CliUpdateStrategy.Homebrew => "Ensure Homebrew is installed and 'brew' is on your PATH",
                    _ => null
                };
                OutputFormatter.WriteError(format, "Failed to start update process", hint, ExitCodes.ServerErrorCode);
                return ExitCodes.ServerError;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var errorMessage = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
                OutputFormatter.WriteError(format, $"Update failed: {errorMessage}", errorCode: ExitCodes.ServerErrorCode);
                return ExitCodes.ServerError;
            }

            return null;
        }
    }

    static string ResolveFormat(string output)
    {
        if (string.Equals(output, OutputFormats.JsonCompact, StringComparison.OrdinalIgnoreCase))
        {
            return OutputFormats.JsonCompact;
        }

        if (!string.Equals(output, OutputFormats.Auto, StringComparison.OrdinalIgnoreCase))
        {
            return output.ToLowerInvariant();
        }

        if (GlobalSettings.IsAiAgentEnvironment())
        {
            return OutputFormats.JsonCompact;
        }

        return Console.IsOutputRedirected ? OutputFormats.Json : OutputFormats.Table;
    }
}
