// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Cli.Commands.LlmContext;

namespace Cratis.Cli.Commands.Init;

/// <summary>
/// Generates CHRONICLE.md and configures AI tools for the current project directory.
/// </summary>
[LlmDescription("Generates a CHRONICLE.md documentation file and configures AI tools (Claude Code, GitHub Copilot, Cursor, Windsurf) for the current project. Run once per project.")]
[CliCommand("init", "Generate CHRONICLE.md and configure AI tools for the current project")]
[CliExample("init")]
[CliExample("init", "--tool", "claude")]
[CliExample("init", "--force", "--no-commands")]
[LlmOption("--force", "bool", "Overwrite existing files")]
[LlmOption("--tool", "string", "Target a specific AI tool: claude, copilot, cursor, windsurf. Omit to auto-detect.")]
[LlmOption("--no-commands", "bool", "Skip generating slash commands / prompt files")]
[LlmOption("--refresh", "bool", "Re-capture the llm-context snapshot in CHRONICLE.md without reconfiguring AI tool integrations.")]
public class InitCommand : AsyncCommand<InitSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteAsync(CommandContext context, InitSettings settings, CancellationToken cancellationToken)
    {
        var format = settings.ResolveOutputFormat();
        var basePath = Directory.GetCurrentDirectory();
        var chronicleMdPath = Path.Combine(basePath, "CHRONICLE.md");
        var allActions = new List<string>();

        // --refresh: re-capture llm-context snapshot only, skip tool configuration
        if (settings.Refresh)
        {
            var existed = File.Exists(chronicleMdPath);
            var refreshJson = LlmContextCommand.BuildDescriptorJson();
            var refreshContent = ChronicleDocGenerator.Generate();
            await File.WriteAllTextAsync(chronicleMdPath, refreshContent, cancellationToken);
            var refreshActions = new List<string> { existed ? "Refreshed CHRONICLE.md" : "Created CHRONICLE.md" };
            refreshActions.AddRange(AiToolConfigurator.RefreshSkillFiles(basePath, refreshJson));

            if (!string.Equals(format, OutputFormats.Quiet, StringComparison.Ordinal))
            {
                foreach (var a in refreshActions)
                {
                    OutputFormatter.WriteMessage(format, a);
                }
            }

            return ExitCodes.Success;
        }

        // Step 1: Generate CHRONICLE.md
        string llmJson;

        if (File.Exists(chronicleMdPath) && !settings.Force)
        {
            allActions.Add("CHRONICLE.md already exists (skipped, use --force to overwrite)");
            llmJson = LlmContextCommand.BuildDescriptorJson();
        }
        else
        {
            var existed = File.Exists(chronicleMdPath);
            llmJson = LlmContextCommand.BuildDescriptorJson();
            var content = ChronicleDocGenerator.Generate();
            await File.WriteAllTextAsync(chronicleMdPath, content, cancellationToken);
            allActions.Add(existed ? "Overwrote CHRONICLE.md" : "Created CHRONICLE.md");
        }

        // Step 2: Determine which AI tools to configure
        IReadOnlyList<AiTool> tools;

        if (settings.Tool is not null)
        {
            if (!AiToolDetector.TryParse(settings.Tool, out var tool))
            {
                OutputFormatter.WriteError(format, $"Unknown AI tool: '{settings.Tool}'", "Valid tools: claude, copilot, cursor, windsurf", ExitCodes.ValidationErrorCode);
                return ExitCodes.ValidationError;
            }

            tools = [tool];
        }
        else
        {
            tools = AiToolDetector.Detect(basePath);
        }

        // Step 3: Configure each tool
        if (tools.Count == 0)
        {
            allActions.Add("No AI tools detected. Use --tool to target a specific tool.");
        }
        else
        {
            var includeCommands = !settings.NoCommands;
            foreach (var tool in tools)
            {
                var actions = AiToolConfigurator.Configure(tool, basePath, settings.Force, includeCommands, llmJson);
                allActions.AddRange(actions);
            }
        }

        // Step 4: Output summary
        if (string.Equals(format, OutputFormats.Json, StringComparison.Ordinal) || string.Equals(format, OutputFormats.JsonCompact, StringComparison.Ordinal))
        {
            OutputFormatter.WriteObject(format, new
            {
                chronicleMd = chronicleMdPath,
                toolsConfigured = tools.Select(t => t.ToString().ToLowerInvariant()).ToArray(),
                actions = allActions.ToArray(),
            });
        }
        else if (!string.Equals(format, OutputFormats.Quiet, StringComparison.Ordinal))
        {
            foreach (var action in allActions)
            {
                OutputFormatter.WriteMessage(format, action);
            }

            if (tools.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"  [{OutputFormatter.Muted.ToMarkup()}]Run 'cratis llm-context' for the full machine-readable capability descriptor.[/]");
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [{OutputFormatter.Muted.ToMarkup()}]Run 'cratis completions install' to enable shell tab-completion.[/]");
        }

        return ExitCodes.Success;
    }
}
