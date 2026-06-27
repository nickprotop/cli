// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Themes;
using SRectangle = System.Drawing.Rectangle;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// A floating portal overlay that provides VS-Code-style command palette functionality for the
/// Chronicle Workbench. Anchored near the top of the window (row 1), horizontally centered — NOT
/// a centered modal. Hosts a search prompt, a result list, and a footer hint line.
/// </summary>
/// <remarks>
/// <para>
/// Adapted from the dotnet-skills <c>CommandPalettePortal</c> pattern.
/// The palette intercepts all keystrokes while open; the hosting window must forward
/// <c>PreviewKeyPressed</c> events to <see cref="ProcessKey"/> and set
/// <see cref="KeyPressedEventArgs.Handled"/> to <see langword="true"/> to prevent keys from reaching any
/// focused control on the page beneath.
/// </para>
/// <para>
/// Each list item carries its navigate <see cref="Action"/> in <see cref="ListItem.Tag"/>.
/// Pressing Enter raises <see cref="CommandChosen"/> with that action; Esc raises
/// <see cref="EscapeRequested"/>. Framework-initiated dismissals (outside-click, debounce) fire
/// the base <see cref="PortalContentBase.DismissRequested"/> event.
/// </para>
/// </remarks>
public sealed class WorkbenchCommandPalette : PortalContentContainer
{
    const int PaletteMaxWidth = 85;
    const int PaletteMaxHeight = 22;

    readonly IReadOnlyList<(string Kind, string Label, string SearchKey, Action Navigate)> _allItems;
    readonly ListControl _list;
    readonly int _maxVisible;
    readonly string _mutedMarkup;

    /// <summary>
    /// Initializes a new instance of <see cref="WorkbenchCommandPalette"/> with a pre-computed set
    /// of match results and the current terminal dimensions used to compute portal bounds.
    /// </summary>
    /// <param name="items">
    /// Pre-computed match results. Each tuple carries a short kind label (e.g. "Observer"),
    /// a display label, a search key used for filtering, and the navigation action to invoke on selection.
    /// </param>
    /// <param name="theme">
    /// The active workbench theme — used for chrome colors so the palette reacts to F9/F10/F11 switches.
    /// </param>
    /// <param name="windowWidth">
    /// The current terminal/window width in columns, used to position and size the portal.
    /// </param>
    /// <param name="windowHeight">
    /// The current terminal/window height in rows, used to size the portal vertically.
    /// </param>
    public WorkbenchCommandPalette(
        IReadOnlyList<(string Kind, string Label, string SearchKey, Action Navigate)> items,
        WorkbenchTheme theme,
        int windowWidth,
        int windowHeight)
    {
        _allItems = items;
        _mutedMarkup = theme.Muted.ToMarkup();

        DismissOnOutsideClick = true;
        BorderStyle = BoxChars.Rounded;

        // A heavily-dimmed accent — the accent blended most of the way to the background — so the chrome
        // (border and the two internal divider rules) reads as quiet and the content holds the eye.
        var dimChrome = theme.Accent.Mix(theme.Background, 0.8);
        BorderColor = dimChrome;
        BorderBackgroundColor = theme.Surface;
        BackgroundColor = theme.Surface;
        ForegroundColor = theme.Foreground;

        var searchInput = Controls.Prompt("> ")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .WithMargin(1, 0, 1, 0)
            .Build();
        AddChild(searchInput);

        AddChild(Controls.RuleBuilder()
            .WithColor(dimChrome)
            .Build());

        _list = Controls.List()
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithColorRole(ColorRole.Default)
            .WithDoubleClickActivation(true)
            .WithTitle(string.Empty)
            .Build();
        AddChild(_list);

        AddChild(Controls.RuleBuilder()
            .WithColor(dimChrome)
            .StickyBottom()
            .Build());

        AddChild(Controls.Markup()
            .AddLine($"[{_mutedMarkup}]↑↓ select · Enter open · Esc close[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .StickyBottom()
            .Build());

        var w = Math.Min(PaletteMaxWidth, Math.Max(20, windowWidth - 4));
        var h = Math.Min(PaletteMaxHeight, Math.Max(6, windowHeight - 4));
        var x = (windowWidth - w) / 2;

        // Row 1 — anchored near the top (row 0 = menu bar), VS Code style, not a centered modal.
        PortalBounds = new SRectangle(x, 1, w, h);

        // total height − border(2) − fixed children: prompt(1) + 2 rules(2) + hint(1) = 4
        _maxVisible = Math.Max(1, h - 2 - 4);
        _list.MaxVisibleItems = _maxVisible;

        PopulateList(string.Empty);

        searchInput.InputChanged += (_, text) => PopulateList(text ?? string.Empty);
        _list.ItemActivated += (_, item) => ActivateItem(item);

        SetFocusOnFirstChild();
    }

    /// <summary>
    /// Raised when the user presses Enter on a selected item. The argument carries the navigation
    /// <see cref="Action"/> stored on the selected item, or <see langword="null"/> when no item is selected.
    /// </summary>
    public event EventHandler<Action?>? CommandChosen;

    /// <summary>
    /// Raised when the user presses Escape to close the palette without navigating to an item.
    /// Distinct from the base <see cref="PortalContentBase.DismissRequested"/>, which the framework
    /// fires on outside-click and other automatic dismissals. Subscribers should handle both events
    /// to cover all dismissal paths.
    /// </summary>
    public event EventHandler? EscapeRequested;

    /// <summary>
    /// Processes a keystroke while the palette is open.
    /// </summary>
    /// <remarks>
    /// <para>Esc raises <see cref="EscapeRequested"/>.</para>
    /// <para>Enter activates the selected item by raising <see cref="CommandChosen"/>.</para>
    /// <para>Up/Down/PageUp/PageDown/Home/End move the list selection without changing the prompt.</para>
    /// <para>All other keys are forwarded to the focused child (the search prompt) for typing.</para>
    /// <para>Always returns <see langword="true"/> to swallow every key while the palette is visible.</para>
    /// </remarks>
    /// <param name="key">The key to process.</param>
    /// <returns><see langword="true"/> — the palette consumes all keys while open.</returns>
    public new bool ProcessKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                EscapeRequested?.Invoke(this, EventArgs.Empty);
                return true;

            case ConsoleKey.Enter:
                ActivateItem(_list.SelectedItem);
                return true;

            case ConsoleKey.DownArrow:
                MoveSelection(+1);
                return true;

            case ConsoleKey.UpArrow:
                MoveSelection(-1);
                return true;

            case ConsoleKey.PageDown:
                MoveSelection(+(_list.MaxVisibleItems ?? 10));
                return true;

            case ConsoleKey.PageUp:
                MoveSelection(-(_list.MaxVisibleItems ?? 10));
                return true;

            case ConsoleKey.Home:
                SetSelection(0);
                return true;

            case ConsoleKey.End:
                SetSelection(_list.Items.Count - 1);
                return true;
        }

        // All other keystrokes (typing, backspace) go to the focused child — the search prompt —
        // which fires InputChanged on every character, triggering live re-filtering.
        base.ProcessKey(key);
        return true;
    }

    void ActivateItem(ListItem? item)
    {
        if (item?.Tag is Action navigate)
        {
            CommandChosen?.Invoke(this, navigate);
        }
        else
        {
            CommandChosen?.Invoke(this, null);
        }
    }

    void PopulateList(string query)
    {
        _list.ClearItems();

        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var filtered = _allItems
            .Where(e => e.SearchKey.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(_maxVisible);

        foreach (var (kind, label, _, navigate) in filtered)
        {
            var captured = navigate;
            _list.AddItem(new ListItem($"[{_mutedMarkup}]{kind,-14}[/]  {label}") { Tag = captured });
        }

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    void MoveSelection(int delta)
    {
        var count = _list.Items.Count;
        if (count == 0)
        {
            return;
        }

        var cur = _list.SelectedIndex < 0 ? 0 : _list.SelectedIndex;
        _list.SelectedIndex = Math.Clamp(cur + delta, 0, count - 1);
    }

    void SetSelection(int index)
    {
        var count = _list.Items.Count;
        if (count == 0)
        {
            return;
        }

        _list.SelectedIndex = Math.Clamp(index, 0, count - 1);
    }
}
