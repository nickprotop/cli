// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Settings for the workbench command.
/// </summary>
public class WorkbenchSettings : EventStoreSettings
{
    /// <summary>
    /// Gets or sets the refresh interval in seconds.
    /// </summary>
    [CommandOption("--interval <SECONDS>")]
    [Description("Refresh interval in seconds (default: 5)")]
    [DefaultValue(5)]
    public int Interval { get; set; } = 5;
}
