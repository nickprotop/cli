// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI.Layout;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Recommendations tab — filterable table of pending recommendations with apply/ignore actions.
/// </summary>
public class RecommendationsView : FilterableTableView<Recommendation>
{
    /// <summary>Gets the currently selected recommendation, or <see langword="null"/> if none is selected.</summary>
    public Recommendation? SelectedRecommendation => SelectedItem;

    /// <summary>
    /// Gets or sets the callback invoked when the user applies a recommendation.
    /// </summary>
    public Action<Recommendation>? OnApply { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user ignores a recommendation.
    /// </summary>
    public Action<Recommendation>? OnIgnore { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user requests a bulk apply of all checked recommendations.
    /// </summary>
    public Action<IReadOnlyList<Recommendation>>? OnApplyAll { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user requests a bulk ignore of all checked recommendations.
    /// </summary>
    public Action<IReadOnlyList<Recommendation>>? OnIgnoreAll { get; set; }

    /// <summary>
    /// Gets all recommendations that are currently checked (checkbox mode).
    /// </summary>
    public IReadOnlyList<Recommendation> Checked => CheckedItems;

    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns =>
    [
        ("Name", TextJustification.Left, null),
        ("Type", TextJustification.Left, 20)
    ];

    /// <inheritdoc/>
    protected override string DetailPanelHeader => "RECOMMENDATION";

    /// <inheritdoc/>
    protected override SharpConsoleUI.Color DetailBorderColor => WorkbenchColors.Warning;

    /// <inheritdoc/>
    protected override bool HasCheckboxMode => true;

    /// <inheritdoc/>
    protected override IEnumerable<(string Label, string? Shortcut, Action Execute)> GetContextMenuActions(Recommendation item)
    {
        if (OnApply is not null)
        {
            yield return ("Apply recommendation", "A", () => OnApply(item));
        }

        if (OnIgnore is not null)
        {
            yield return ("Ignore recommendation", "I", () => OnIgnore(item));
        }

        var checkedCount = Checked.Count;
        if (OnApplyAll is not null && checkedCount > 1)
        {
            yield return ($"Apply {checkedCount} checked", null, () => OnApplyAll(Checked));
        }

        if (OnIgnoreAll is not null && checkedCount > 1)
        {
            yield return ($"Ignore {checkedCount} checked", null, () => OnIgnoreAll(Checked));
        }
    }

    /// <inheritdoc/>
    protected override IEnumerable<Recommendation> GetItems(WorkbenchData data) => data.Recommendations;

    /// <inheritdoc/>
    protected override string GetKey(Recommendation item) => item.Id.ToString();

    /// <inheritdoc/>
    protected override string[] BuildRow(Recommendation item) =>
        [item.Name ?? item.Id.ToString(), item.Type ?? "—"];

    /// <inheritdoc/>
    protected override string RenderDetail(Recommendation? item, WorkbenchData? data)
    {
        if (item is null)
        {
            return $"[{WorkbenchColors.Muted.ToMarkup()}]Select a recommendation.[/]";
        }

        var mut = WorkbenchColors.Muted.ToMarkup();

        var lines = new List<string>
        {
            $"[{mut}]Name[/]  {item.Name ?? item.Id.ToString()}",
            $"[{mut}]Type[/]  {item.Type ?? "—"}"
        };

        if (!string.IsNullOrEmpty(item.Description))
        {
            lines.Add(string.Empty);
            lines.Add($"[{mut}]Description:[/]");
            lines.Add($"  {item.Description}");
        }

        lines.Add(string.Empty);
        if (OnApply is not null)
        {
            lines.Add($"[{mut}]Press[/] [bold]A[/] [{mut}]to apply[/]");
        }

        if (OnIgnore is not null)
        {
            lines.Add($"[{mut}]Press[/] [bold]I[/] [{mut}]to ignore[/]");
        }

        return string.Join('\n', lines);
    }

    /// <inheritdoc/>
    protected override bool MatchesFilter(Recommendation item, string filter) =>
        (item.Name ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        (item.Type ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        item.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase);
}
