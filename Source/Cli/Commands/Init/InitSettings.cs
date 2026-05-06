// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Init;

/// <summary>
/// Settings for the init command.
/// </summary>
public class InitSettings : GlobalSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether existing files should be overwritten.
    /// </summary>
    [CommandOption("--force")]
    [Description("Overwrite existing files")]
    [DefaultValue(false)]
    public bool Force { get; set; }

    /// <summary>
    /// Gets or sets the specific AI tool to configure. Omit to auto-detect.
    /// </summary>
    [CommandOption("--tool <NAME>")]
    [Description("Target a specific AI tool: claude, copilot, cursor, windsurf. Omit to auto-detect.")]
    public string? Tool { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether slash commands should be skipped.
    /// </summary>
    [CommandOption("--no-commands")]
    [Description("Skip generating slash commands / prompt files")]
    [DefaultValue(false)]
    public bool NoCommands { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to re-capture the llm-context snapshot
    /// in CHRONICLE.md without reconfiguring AI tool integrations.
    /// </summary>
    [CommandOption("--refresh")]
    [Description("Re-capture the llm-context snapshot in CHRONICLE.md. Skips AI tool configuration.")]
    [DefaultValue(false)]
    public bool Refresh { get; set; }
}
