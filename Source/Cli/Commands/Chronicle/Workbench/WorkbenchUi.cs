// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.ObjectModel;
using System.Text;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Rendering;
using SharpConsoleUI.Themes;
using SColor = SharpConsoleUI.Color;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Static factory helpers that provide a shared UI construction vocabulary for all Workbench views.
/// Prefer <see cref="ColorRole"/> over concrete colors so kit-built controls auto-recolor on theme change.
/// </summary>
public static class WorkbenchUi
{
    /// <summary>
    /// Quiet desaturated hairline color for borderless-table column separators. Deliberately neutral
    /// (not theme-reactive) so it reads as faint structure on any theme background without competing
    /// with the role-tinted accents.
    /// </summary>
    static readonly SColor _gridLineColor = new(64, 76, 92);

    /// <summary>
    /// Builds a single-line identity strip for a page header.
    /// </summary>
    /// <remarks>
    /// The strip is formatted as:
    /// <c>[bold &lt;accent&gt;]TITLE[/]  ❨hint❩  · Label Value  · Label Value</c>.
    /// The accent color is supplied as a resolved <see cref="SharpConsoleUI.Color"/> snapshot; callers
    /// should pass the theme's accent (e.g. <c>Theme.Accent</c>) and rebuild on each data tick so the
    /// markup stays in sync with the active theme. The header rule below the strip is the reactive part
    /// (see <see cref="BuildHeaderRule"/>).
    /// </remarks>
    /// <param name="title">The page title rendered in bold accent color.</param>
    /// <param name="accent">The resolved accent color for the title markup. Use <c>Theme.Accent</c> or equivalent.</param>
    /// <param name="muted">The resolved muted color for secondary text (hint, separators, labels). Use <c>Theme.Muted</c>.</param>
    /// <param name="hint">An optional contextual hint rendered as a dim chip <c>❨hint❩</c>. Omitted when null.</param>
    /// <param name="facts">Optional label/value pairs appended as <c>Label Value</c> chips separated by <c>·</c>.</param>
    /// <returns>A <see cref="MarkupControl"/> containing the formatted identity strip.</returns>
    public static IWindowControl BuildPageHeader(
        string title,
        SharpConsoleUI.Color accent,
        SharpConsoleUI.Color muted,
        string? hint,
        params (string Label, string Value)[] facts)
    {
        var mut = muted.ToMarkup();
        var sb = new StringBuilder();
        sb.Append($"[bold {accent.ToMarkup()}]{title}[/]");

        if (!string.IsNullOrEmpty(hint))
        {
            sb.Append($"  [{mut}]❨{hint}❩[/]");
        }

        foreach (var (label, value) in facts)
        {
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(value))
                continue;

            sb.Append($"  [{mut}]·[/]  ")
              .Append($"[{mut}]{label}[/] {value}");
        }

        return new MarkupControl([sb.ToString()]);
    }

    /// <summary>
    /// Builds a <see cref="ToolbarControl"/> hosting a bold-accent title, an optional separator,
    /// role-styled action buttons, and a filter prompt pushed to the right by a spacer.
    /// Returns the built toolbar and the action button controls so callers can update them in-place
    /// on selection change without clearing the toolbar.
    /// </summary>
    /// <remarks>
    /// Layout (left→right): title MarkupControl · separator · action buttons · separator · filter control.
    /// Action buttons use <see cref="ColorRole.Danger"/> when the label contains "stop", "remove",
    /// "delete", or "ignore" (case-insensitive) and the action is enabled; disabled buttons always
    /// use <see cref="ColorRole.Default"/> so they render neutral-grey.
    /// </remarks>
    /// <param name="accent">The resolved accent color snapshot used to tint the title.</param>
    /// <param name="title">The page title rendered in bold accent markup.</param>
    /// <param name="actions">
    /// Ordered list of action descriptors: button text, semantic color role, enabled flag, and click callback.
    /// Pass an empty list when no actions are available for the current selection.
    /// </param>
    /// <param name="filter">The filter prompt control to host at the trailing end of the toolbar.</param>
    /// <returns>
    /// A tuple of the fully configured <see cref="ToolbarControl"/> and an ordered list of the
    /// action <see cref="ButtonControl"/> instances — held by the caller for in-place updates.
    /// </returns>
    public static (ToolbarControl Toolbar, IReadOnlyList<ButtonControl> ActionButtons) BuildToolbarHeader(
        SColor accent,
        string title,
        IReadOnlyList<(string Text, ColorRole Role, bool Enabled, Action OnClick)> actions,
        IWindowControl filter)
    {
        var titleControl = new MarkupControl([$"[bold {accent.ToMarkup()}]{title}[/]"]);

        var toolbar = ToolbarControl.Create()
            .WithSpacing(1)
            .WithWrap()
            .Add(titleControl)
            .AddSeparator(1)
            .Build();

        var buttons = AddActionButtons(toolbar, actions, filter);
        return (toolbar, buttons);
    }

    /// <summary>
    /// Updates the action buttons in-place on the toolbar built by <see cref="BuildToolbarHeader"/>.
    /// Each button's <see cref="ButtonControl.Text"/>, <see cref="ButtonControl.IsEnabled"/>, and
    /// <see cref="ButtonControl.ColorRole"/> are updated without clearing or rebuilding the toolbar,
    /// preserving the filter prompt's container reference and avoiding Container=null churn.
    /// </summary>
    /// <remarks>
    /// This method requires that the action set returned by the view is STABLE in count — the same
    /// number of buttons on every selection change. This is guaranteed by the always-show-full-set
    /// design introduced in S3.3. If the action count somehow differs from the button list length,
    /// the method falls back to a full rebuild via <see cref="SetToolbarActions"/> and returns the
    /// new button list so the caller can update its reference.
    /// </remarks>
    /// <param name="toolbar">The toolbar that owns the buttons.</param>
    /// <param name="actionButtons">The button controls to update, obtained from <see cref="BuildToolbarHeader"/>.</param>
    /// <param name="actions">The current action descriptors.</param>
    /// <param name="filter">The filter control (used only for fallback rebuild).</param>
    /// <returns>
    /// The updated button list. Normally the same instance as <paramref name="actionButtons"/>;
    /// a new list when the fallback rebuild path runs.
    /// </returns>
    public static IReadOnlyList<ButtonControl> UpdateToolbarActions(
        ToolbarControl toolbar,
        IReadOnlyList<ButtonControl> actionButtons,
        IReadOnlyList<(string Text, ColorRole Role, bool Enabled, Action OnClick)> actions,
        IWindowControl filter)
    {
        if (actionButtons.Count != actions.Count)
        {
            // The toolbar contract is that a view's action set has a stable count (the always-show-full-set
            // design). A mismatch means a view's GetToolbarActionTemplate violated that — surface it loudly
            // in Debug while still recovering gracefully in Release via a full rebuild.
            System.Diagnostics.Debug.Assert(
                actionButtons.Count == actions.Count,
                "Toolbar action count changed between updates — GetToolbarActionTemplate must return a stable count.");

            // Action set count changed (first selection after load, or view wiring changed) — fall back
            // to full rebuild and return the new button list so the caller can update its reference.
            return SetToolbarActions(toolbar, 2, actions, filter);
        }

        for (var i = 0; i < actionButtons.Count; i++)
        {
            var btn = actionButtons[i];
            var (text, role, enabled, _) = actions[i];
            btn.Text = text;
            btn.IsEnabled = enabled;
            btn.ColorRole = enabled ? role : ColorRole.Default;
        }

        return actionButtons;
    }

    /// <summary>
    /// Replaces the action buttons in an existing toolbar built by <see cref="BuildToolbarHeader"/>,
    /// keeping the title and separator at the front and the filter at the end.
    /// Used as a fallback when the action set count changes between selections.
    /// </summary>
    /// <remarks>
    /// Mutation strategy: clear all items, re-add the first <paramref name="fixedPrefixCount"/> items
    /// (title + separator), insert the new action buttons, then re-add the filter. This avoids
    /// rebuilding or swapping the entire toolbar, preserving the control's identity in the layout tree.
    /// </remarks>
    /// <param name="toolbar">The toolbar to mutate.</param>
    /// <param name="fixedPrefixCount">
    /// Number of fixed items at the start of the toolbar (title + separator = 2) that must be preserved.
    /// </param>
    /// <param name="actions">The new action set to insert after the fixed prefix.</param>
    /// <param name="filter">The filter control to append at the end after a separator.</param>
    /// <returns>The newly created action <see cref="ButtonControl"/> instances.</returns>
    public static IReadOnlyList<ButtonControl> SetToolbarActions(
        ToolbarControl toolbar,
        int fixedPrefixCount,
        IReadOnlyList<(string Text, ColorRole Role, bool Enabled, Action OnClick)> actions,
        IWindowControl filter)
    {
        // Capture the prefix items before clearing.
        var prefix = toolbar.Items.Take(fixedPrefixCount).ToList();

        toolbar.Clear();

        foreach (var item in prefix)
        {
            toolbar.AddItem(item);
        }

        return AddActionButtons(toolbar, actions, filter);
    }

    /// <summary>
    /// Builds a reactive horizontal rule tinted with the given semantic color role.
    /// The rule re-colors automatically when the active theme changes because it holds a
    /// <see cref="ColorRole"/> reference rather than a concrete color snapshot.
    /// </summary>
    /// <param name="accentRole">The semantic color role that drives the rule line color.</param>
    /// <returns>An <see cref="IWindowControl"/> (a <see cref="RuleControl"/>) configured as a single-line left-aligned accent separator.</returns>
    public static IWindowControl BuildHeaderRule(ColorRole accentRole) =>
        Controls.RuleBuilder()
            .WithColorRole(accentRole)
            .TitleLeft()
            .WithBorderStyle(BorderStyle.Single)
            .Build();

    /// <summary>
    /// Applies the workbench's common "filling, borderless, role-tinted" styling to an existing
    /// table builder, leaving caller-supplied behavior (interactivity, event handlers, checkbox mode)
    /// intact. Use when the caller needs to chain its own wiring; use <see cref="BuildDataTable"/>
    /// for a fully-built simple table.
    /// </summary>
    /// <param name="builder">The table builder to style.</param>
    /// <param name="accentRole">The semantic role driving the table's accent color.</param>
    /// <param name="separator">Optional column-separator hairline color; defaults to a fixed neutral when omitted. Pass a theme-derived faint color so it follows theme switches.</param>
    /// <returns>The same builder, styled, for further chaining.</returns>
    public static TableControlBuilder StyleDataTable(TableControlBuilder builder, ColorRole accentRole, SColor? separator = null) =>
        builder.WithSorting().NoBorder().WithColorRole(accentRole)
               .WithColumnSeparator('│', separator ?? _gridLineColor, padded: true)
               .ScrollbarGutter().StretchHorizontal()
               .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill);

    /// <summary>
    /// Builds a borderless, full-width, sortable data table tinted with the given semantic color role.
    /// The table stretches horizontally to fill available width, fills vertically, reserves a scrollbar
    /// gutter, and fades truncated cell text.
    /// </summary>
    /// <param name="accentRole">The semantic color role that drives the row-selection accent color.</param>
    /// <param name="columns">
    /// Column descriptors: each tuple specifies the header name, text justification, and an optional
    /// fixed width (null = auto-size).
    /// </param>
    /// <returns>A fully configured <see cref="TableControl"/> ready for row population.</returns>
    public static TableControl BuildDataTable(
        ColorRole accentRole,
        IEnumerable<(string Name, TextJustification Justify, int? Width)> columns)
    {
        var builder = columns.Aggregate(Controls.Table(), (b, col) => b.AddColumn(col.Name, col.Justify, col.Width));
        var t = StyleDataTable(builder, accentRole).Build();
        t.TruncationFade = true;
        return t;
    }

    /// <summary>
    /// Builds an in-line gradient progress/weight bar as a markup string: a run of filled block cells
    /// sweeping a cool blue→cyan gradient proportional to <paramref name="value"/>/<paramref name="max"/>,
    /// followed by dim empty cells. Use inside a markup-rendered detail/row string.
    /// </summary>
    /// <param name="value">The current value (clamped to 0..max).</param>
    /// <param name="max">The maximum value. When ≤ 0 the bar renders fully empty.</param>
    /// <param name="width">Total bar width in cells.</param>
    /// <param name="fillStart">Optional gradient start color for the filled run. When both endpoints are supplied the theme gradient is used instead of the built-in cool sweep.</param>
    /// <param name="fillEnd">Optional gradient end color for the filled run.</param>
    /// <param name="empty">Optional color for the empty track; defaults to a fixed neutral. Pass <c>Theme.Muted</c> to follow theme switches.</param>
    /// <returns>A markup string of <paramref name="width"/> cells representing the bar.</returns>
    public static string GradientBar(double value, double max, int width, SColor? fillStart = null, SColor? fillEnd = null, SColor? empty = null)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        var ratio = max > 0 ? Math.Clamp(value / max, 0.0, 1.0) : 0.0;
        var filled = (int)Math.Round(ratio * width, MidpointRounding.AwayFromZero);
        if (ratio > 0 && filled == 0)
        {
            filled = 1;
        }

        var sb = new StringBuilder();

        // Theme-derived gradient when endpoints are supplied; falls back to the built-in cool sweep.
        var useThemeColors = fillStart.HasValue && fillEnd.HasValue;
        var gradient = useThemeColors ? null : ColorGradient.Predefined["cool"];

        for (var i = 0; i < filled; i++)
        {
            var t = filled > 1 ? (double)i / (filled - 1) : 0.0;
            var c = useThemeColors ? fillStart!.Value.Mix(fillEnd!.Value, t) : gradient!.Interpolate(t);
            sb.Append($"[{c.ToMarkup()}]█[/]");
        }

        if (filled < width)
        {
            var emptyMarkup = (empty ?? new SColor(128, 128, 128)).ToMarkup();
            sb.Append($"[{emptyMarkup}]{new string('░', width - filled)}[/]");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a standard workbench dialog: a content body, an edge-to-edge rule, and a centered action
    /// toolbar — the rule and toolbar pinned to the bottom so the buttons stay visible while the body
    /// scrolls. The Escape key dismisses the dialog (unless a custom key handler consumes it).
    /// </summary>
    /// <remarks>
    /// Layout (top → bottom): body · bottom-sticky rule · bottom-sticky centered toolbar. The modal is
    /// centered, role-bordered (color driven by <see cref="DialogOptions.Severity"/>), and given the
    /// elevated background gradient. Action buttons are role-styled; a trailing "Close" is appended
    /// unless <see cref="DialogOptions.ShowCloseButton"/> is false.
    /// </remarks>
    /// <param name="windowSystem">The window system that hosts the modal.</param>
    /// <param name="theme">The workbench theme, used for the border color and gradient.</param>
    /// <param name="title">The dialog title.</param>
    /// <param name="content">The body controls, added in order.</param>
    /// <param name="buttons">Ordered action descriptors.</param>
    /// <param name="options">Optional configuration (severity, body mode, key contract, sizing). Defaults apply when null.</param>
    /// <returns>The built modal <see cref="Window"/>, ready to add to the window system.</returns>
    public static Window BuildDialog(
        ConsoleWindowSystem windowSystem,
        WorkbenchTheme theme,
        string title,
        IReadOnlyList<IWindowControl> content,
        IReadOnlyList<DialogButton> buttons,
        DialogOptions? options = null)
    {
        options ??= new DialogOptions();
        var (borderColor, accentRole) = SeverityColors(theme, options.Severity);
        var width = options.Width ?? Math.Clamp(Console.WindowWidth - 12, 56, 116);
        var height = options.Height ?? Math.Clamp(Console.WindowHeight - 6, 12, 36);

        Window? dialog = null;
        void Close()
        {
            if (dialog is not null)
            {
                windowSystem.CloseWindow(dialog, activateParent: true, force: false);
            }
        }

        // The body either fills (controls that scroll themselves, e.g. a tab control) or is hosted in
        // a scrollable panel that scrolls when the content overflows.
        IWindowControl bodyControl;
        if (options.FillBody && content.Count == 1)
        {
            bodyControl = content[0];
        }
        else
        {
            var panel = Controls.ScrollablePanel()
                .WithVerticalScroll(ScrollMode.Scroll)
                .WithPadding(1, 0, 1, 0)
                .Build();
            foreach (var control in content)
            {
                panel.AddControl(control);
            }

            bodyControl = panel;
        }

        // Edge-to-edge divider above the toolbar, pinned to the bottom so it tracks the toolbar.
        var rule = Controls.RuleBuilder()
            .WithColorRole(accentRole)
            .WithBorderStyle(BorderStyle.Single)
            .StickyBottom()
            .Build();

        var toolbar = Controls.Toolbar()
            .WithSpacing(2)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center);
        foreach (var button in buttons)
        {
            var captured = button.OnClick;
            var closeOnAction = options.CloseOnAction;
            toolbar.AddButton(new ButtonBuilder()
                .WithText(button.Label)
                .WithColorRole(button.Role)
                .OnClick((_, _) =>
                {
                    if (closeOnAction)
                    {
                        Close();
                    }

                    captured();
                }));
        }

        if (options.ShowCloseButton)
        {
            toolbar.AddButton(new ButtonBuilder()
                .WithText("Close")
                .WithColorRole(ColorRole.Default)
                .OnClick((_, _) => Close()));
        }

        var bg = theme.Surface;
        var optionsKey = options.OnKey;
        var built = new WindowBuilder(windowSystem)
            .WithTitle($" {title} ")
            .WithSize(width, height)
            .Centered()
            .AsModal()
            .WithBackgroundGradient(
                ColorGradient.FromColors([bg.Tint(0.10), bg, bg.Shade(0.30)]),
                GradientDirection.Vertical)
            .WithBorderStyle(BorderStyle.Rounded)
            .WithBorderColor(borderColor)
            .Minimizable(false)
            .Maximizable(false)
            .OnKeyPressed((_, e) =>
            {
                if (optionsKey is not null && optionsKey(e.KeyInfo, Close))
                {
                    e.Handled = true;
                    return;
                }

                if (e.KeyInfo.Key == ConsoleKey.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            })
            .AddControl(bodyControl)
            .AddControl(rule)
            .AddControl(toolbar.StickyBottom().Build())
            .Build();

        dialog = built;
        return built;
    }

    /// <summary>
    /// Resolves the border color and rule role for a dialog severity.
    /// </summary>
    /// <param name="theme">The workbench theme.</param>
    /// <param name="severity">The dialog severity.</param>
    /// <returns>The border color and the rule's color role.</returns>
    static (SColor Border, ColorRole Rule) SeverityColors(WorkbenchTheme theme, DialogSeverity severity) =>
        severity switch
        {
            DialogSeverity.Danger => (theme.Danger, ColorRole.Danger),
            DialogSeverity.Warning => (theme.Warning, ColorRole.Warning),
            _ => (theme.DimAccent, ColorRole.Primary)
        };

    static ReadOnlyCollection<ButtonControl> AddActionButtons(
        ToolbarControl toolbar,
        IReadOnlyList<(string Text, ColorRole Role, bool Enabled, Action OnClick)> actions,
        IWindowControl filter)
    {
        List<ButtonControl> buttons = [];

        foreach (var (text, role, enabled, onClick) in actions)
        {
            var btn = new ButtonBuilder()
                .WithText(text)
                .WithColorRole(enabled ? role : ColorRole.Default)
                .Enabled(enabled)
                .OnClick((_, _) => onClick())
                .Build();

            toolbar.AddItem(btn);
            buttons.Add(btn);
        }

        // A separator then the filter, placed directly after the action buttons. The toolbar lays
        // items left-to-right with no native right-align, so the filter sits next to the buttons
        // (a Stretch spacer measures 0 in the fixed-width layout and would swallow the filter's slot).
        toolbar.AddItem(new SeparatorControl());
        toolbar.AddItem(filter);

        return buttons.AsReadOnly();
    }
}
