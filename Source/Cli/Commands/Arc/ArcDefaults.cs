// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Arc;

/// <summary>
/// Default values for Arc CLI commands.
/// </summary>
public static class ArcDefaults
{
    /// <summary>
    /// The default URL for an Arc application.
    /// </summary>
    public const string DefaultUrl = "http://localhost:5000";

    /// <summary>
    /// Environment variable name for the Arc application URL.
    /// </summary>
    public const string UrlEnvVar = "ARC_URL";

    /// <summary>
    /// Standard error message when the CLI cannot reach the Arc application.
    /// </summary>
    public const string CannotConnectMessage = "Cannot connect to Arc application";
}
