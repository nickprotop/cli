// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Persisted workbench state saved between sessions under <c>~/.cratis/workbench-state.json</c>.
/// </summary>
public class WorkbenchState
{
    static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cratis",
        "workbench-state.json");

    static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    /// <summary>Gets or sets the refresh interval in seconds.</summary>
    public int Interval { get; set; } = 5;

    /// <summary>Gets or sets the index of the last active navigation item.</summary>
    public int LastNavIndex { get; set; }

    /// <summary>
    /// Loads the workbench state from disk, or returns defaults if the file does not exist.
    /// </summary>
    /// <returns>The persisted <see cref="WorkbenchState"/>, or a new default instance.</returns>
    public static WorkbenchState Load()
    {
        try
        {
            if (!File.Exists(_path)) return new WorkbenchState();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<WorkbenchState>(json) ?? new WorkbenchState();
        }
        catch
        {
            return new WorkbenchState();
        }
    }

    /// <summary>
    /// Saves the current state to disk. Failures are silently ignored.
    /// </summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(this, _options));
        }
        catch
        {
            // Best-effort persistence — never throw from a background save.
        }
    }
}
