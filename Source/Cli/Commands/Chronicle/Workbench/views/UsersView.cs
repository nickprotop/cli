// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI.Layout;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Users navigation item — filterable table of registered users with a detail pane.
/// </summary>
public class UsersView : FilterableTableView<User>
{
    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns =>
    [
        ("Username", TextJustification.Left, null),
        ("Email", TextJustification.Left, 30),
        ("Active", TextJustification.Left, 8)
    ];

    /// <inheritdoc/>
    protected override string DetailPanelHeader => "USER";

    /// <inheritdoc/>
    protected override string? PageTitle => "USERS";

    /// <inheritdoc/>
    protected override IEnumerable<User> GetItems(WorkbenchData data) =>
        data.Users.OrderBy(u => u.Username);

    /// <inheritdoc/>
    protected override string GetKey(User item) => item.Id.ToString();

    /// <inheritdoc/>
    protected override string[] BuildRow(User item)
    {
        var activeColor = item.IsActive ? Theme.Success.ToMarkup() : Theme.Muted.ToMarkup();
        return
        [
            item.Username,
            item.Email ?? string.Empty,
            $"[{activeColor}]{(item.IsActive ? "Yes" : "No")}[/]"
        ];
    }

    /// <inheritdoc/>
    protected override string RenderDetail(User? item, WorkbenchData? data)
    {
        if (item is null)
        {
            return $"[{Theme.Muted.ToMarkup()}]Select a user.[/]";
        }

        var mut = Theme.Muted.ToMarkup();
        var suc = Theme.Success.ToMarkup();
        var activeColor = item.IsActive ? suc : mut;

        return string.Join(
            "\n",
            $"[{mut}]Id[/]           {item.Id}",
            $"[{mut}]Username[/]     {item.Username}",
            $"[{mut}]Email[/]        {item.Email ?? "—"}",
            $"[{mut}]Active[/]       [{activeColor}]{(item.IsActive ? "Yes" : "No")}[/]",
            $"[{mut}]Has Logged In[/] {item.HasLoggedIn}",
            $"[{mut}]Created[/]      {item.CreatedAt}");
    }

    /// <inheritdoc/>
    protected override bool MatchesFilter(User item, string filter) =>
        item.Username.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        (item.Email ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        item.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase);
}
