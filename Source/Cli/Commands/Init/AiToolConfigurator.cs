// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Init;

/// <summary>
/// Configures AI tool integrations to reference CHRONICLE.md.
/// </summary>
public static class AiToolConfigurator
{
    const string ChronicleReference = "@CHRONICLE.md";
    const string DiagnoseCommandName = "chronicle-diagnose";

    /// <summary>
    /// Configures the specified AI tool to reference CHRONICLE.md.
    /// </summary>
    /// <param name="tool">The AI tool to configure.</param>
    /// <param name="basePath">The project base directory.</param>
    /// <param name="force">Whether to overwrite existing files.</param>
    /// <param name="includeCommands">Whether to generate slash command files.</param>
    /// <param name="llmContextJson">The serialized llm-context JSON to embed in skill files.</param>
    /// <returns>A list of actions taken.</returns>
    public static IReadOnlyList<string> Configure(AiTool tool, string basePath, bool force, bool includeCommands, string llmContextJson)
    {
        return tool switch
        {
            AiTool.Claude => ConfigureClaude(basePath, force, includeCommands, llmContextJson),
            AiTool.Copilot => ConfigureCopilot(basePath, force, includeCommands, llmContextJson),
            AiTool.Cursor => ConfigureCursor(basePath, force),
            AiTool.Windsurf => ConfigureWindsurf(basePath, force),
            _ => [],
        };
    }

    /// <summary>
    /// Regenerates any skill/command files that were previously created by <c>cratis init</c>.
    /// Only updates files that already exist — does not create new ones.
    /// </summary>
    /// <param name="basePath">The project base directory.</param>
    /// <param name="llmContextJson">The serialized llm-context JSON to embed in skill files.</param>
    /// <returns>A list of actions taken.</returns>
    public static IReadOnlyList<string> RefreshSkillFiles(string basePath, string llmContextJson)
    {
        var actions = new List<string>();
        var skillContent = ChronicleSkillGenerator.Generate(llmContextJson);

        var copilotSkillPath = Path.Combine(basePath, ".github", "skills", ChronicleSkillGenerator.SkillName, "SKILL.md");
        if (File.Exists(copilotSkillPath))
        {
            File.WriteAllText(copilotSkillPath, skillContent);
            actions.Add($"Refreshed .github/skills/{ChronicleSkillGenerator.SkillName}/SKILL.md");
        }

        var claudeCommandPath = Path.Combine(basePath, ".claude", "commands", $"{ChronicleSkillGenerator.SkillName}.md");
        if (File.Exists(claudeCommandPath))
        {
            File.WriteAllText(claudeCommandPath, skillContent);
            actions.Add($"Refreshed .claude/commands/{ChronicleSkillGenerator.SkillName}.md");
        }

        return actions;
    }

    static List<string> ConfigureClaude(string basePath, bool force, bool includeCommands, string llmContextJson)
    {
        var actions = new List<string>();
        var claudeMd = Path.Combine(basePath, "CLAUDE.md");

        if (File.Exists(claudeMd))
        {
            var content = File.ReadAllText(claudeMd);
            if (!content.Contains(ChronicleReference, StringComparison.Ordinal))
            {
                File.AppendAllText(claudeMd, $"\n{ChronicleReference}\n");
                actions.Add("Appended @CHRONICLE.md reference to CLAUDE.md");
            }
            else
            {
                actions.Add("CLAUDE.md already references @CHRONICLE.md (skipped)");
            }
        }
        else
        {
            File.WriteAllText(claudeMd, $"{ChronicleReference}\n");
            actions.Add("Created CLAUDE.md with @CHRONICLE.md reference");
        }

        if (includeCommands)
        {
            var commandsDir = Path.Combine(basePath, ".claude", "commands");
            var commandPath = Path.Combine(commandsDir, $"{DiagnoseCommandName}.md");

            if (!File.Exists(commandPath) || force)
            {
                Directory.CreateDirectory(commandsDir);
                File.WriteAllText(commandPath, SlashCommands.ChronicleDiagnose);
                actions.Add($"Created .claude/commands/{DiagnoseCommandName}.md");
            }
            else
            {
                actions.Add($".claude/commands/{DiagnoseCommandName}.md already exists (skipped, use --force to overwrite)");
            }

            var skillPath = Path.Combine(commandsDir, $"{ChronicleSkillGenerator.SkillName}.md");

            if (!File.Exists(skillPath) || force)
            {
                Directory.CreateDirectory(commandsDir);
                File.WriteAllText(skillPath, ChronicleSkillGenerator.Generate(llmContextJson));
                actions.Add($"Created .claude/commands/{ChronicleSkillGenerator.SkillName}.md");
            }
            else
            {
                actions.Add($".claude/commands/{ChronicleSkillGenerator.SkillName}.md already exists (skipped, use --force to overwrite)");
            }
        }

        return actions;
    }

    static List<string> ConfigureCopilot(string basePath, bool force, bool includeCommands, string llmContextJson)
    {
        var actions = new List<string>();
        var instructionsPath = Path.Combine(basePath, ".github", "copilot-instructions.md");

        if (File.Exists(instructionsPath))
        {
            var content = File.ReadAllText(instructionsPath);
            if (!content.Contains(ChronicleReference, StringComparison.Ordinal))
            {
                File.AppendAllText(instructionsPath, $"\n{ChronicleReference}\n");
                actions.Add("Appended @CHRONICLE.md reference to .github/copilot-instructions.md");
            }
            else
            {
                actions.Add(".github/copilot-instructions.md already references @CHRONICLE.md (skipped)");
            }
        }
        else
        {
            var dir = Path.GetDirectoryName(instructionsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(instructionsPath, $"{ChronicleReference}\n");
            actions.Add("Created .github/copilot-instructions.md with @CHRONICLE.md reference");
        }

        if (includeCommands)
        {
            var promptsDir = Path.Combine(basePath, ".github", "copilot", "prompts");
            var promptPath = Path.Combine(promptsDir, $"{DiagnoseCommandName}.prompt.md");

            if (!File.Exists(promptPath) || force)
            {
                Directory.CreateDirectory(promptsDir);
                File.WriteAllText(promptPath, SlashCommands.ChronicleDiagnose);
                actions.Add($"Created .github/copilot/prompts/{DiagnoseCommandName}.prompt.md");
            }
            else
            {
                actions.Add($".github/copilot/prompts/{DiagnoseCommandName}.prompt.md already exists (skipped, use --force to overwrite)");
            }

            var skillDir = Path.Combine(basePath, ".github", "skills", ChronicleSkillGenerator.SkillName);
            var skillPath = Path.Combine(skillDir, "SKILL.md");

            if (!File.Exists(skillPath) || force)
            {
                Directory.CreateDirectory(skillDir);
                File.WriteAllText(skillPath, ChronicleSkillGenerator.Generate(llmContextJson));
                actions.Add($"Created .github/skills/{ChronicleSkillGenerator.SkillName}/SKILL.md");
            }
            else
            {
                actions.Add($".github/skills/{ChronicleSkillGenerator.SkillName}/SKILL.md already exists (skipped, use --force to overwrite)");
            }
        }

        return actions;
    }

    static List<string> ConfigureCursor(string basePath, bool force)
    {
        var actions = new List<string>();
        var rulesDir = Path.Combine(basePath, ".cursor", "rules");
        var rulePath = Path.Combine(rulesDir, "chronicle.mdc");

        if (!File.Exists(rulePath) || force)
        {
            Directory.CreateDirectory(rulesDir);
            File.WriteAllText(rulePath, $"{ChronicleReference}\n");
            actions.Add("Created .cursor/rules/chronicle.mdc with @CHRONICLE.md reference");
        }
        else
        {
            actions.Add(".cursor/rules/chronicle.mdc already exists (skipped, use --force to overwrite)");
        }

        return actions;
    }

    static List<string> ConfigureWindsurf(string basePath, bool force)
    {
        var actions = new List<string>();
        var rulesPath = Path.Combine(basePath, ".windsurfrules");

        if (File.Exists(rulesPath))
        {
            var content = File.ReadAllText(rulesPath);
            if (!content.Contains(ChronicleReference, StringComparison.Ordinal))
            {
                File.AppendAllText(rulesPath, $"\n{ChronicleReference}\n");
                actions.Add("Appended @CHRONICLE.md reference to .windsurfrules");
            }
            else
            {
                actions.Add(".windsurfrules already references @CHRONICLE.md (skipped)");
            }
        }
        else if (force)
        {
            File.WriteAllText(rulesPath, $"{ChronicleReference}\n");
            actions.Add("Created .windsurfrules with @CHRONICLE.md reference");
        }
        else
        {
            actions.Add("No .windsurfrules found (skipped — Windsurf detected but no rules file exists)");
        }

        return actions;
    }
}
