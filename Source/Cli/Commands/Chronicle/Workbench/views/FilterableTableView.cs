// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Animation;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Themes;
using UITableRow = SharpConsoleUI.Controls.TableRow;

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Abstract base for workbench views that display a filterable, sortable, paginated table with a detail panel.
/// Sorting is applied to the full filtered dataset before pagination — sort order is always consistent
/// across all pages. Per-column <c>CustomRowComparer</c> delegates ensure typed (not string) comparison.
/// Subclasses only implement domain-specific concerns.
/// </summary>
/// <typeparam name="TItem">The domain item type displayed in each row.</typeparam>
public abstract class FilterableTableView<TItem> : IWorkbenchView
{
    /// <summary>Vertical chrome (toolbar, rule, borders) above/below the table that reduces row capacity.</summary>
    const int ContentChromeHeight = 6;

    /// <summary>Minimum number of table rows to show regardless of terminal height.</summary>
    const int MinPageSize = 5;

    /// <summary>Rows reserved below the table for the pager strip.</summary>
    const int PagerRowHeight = 2;

    ConsoleWindowSystem? _windowSystem;
    WorkbenchTheme? _theme;
    HorizontalGridControl? _root;
    TableControl? _table;
    PanelControl? _detailPanel;
    PromptControl? _filterPrompt;
    ToolbarControl? _toolbar;
    IReadOnlyList<ButtonControl> _actionButtons = [];
    MarkupControl? _pageIndicator;
    ButtonControl? _prevPageButton;
    ButtonControl? _nextPageButton;
    bool _detailPaneVisible = true;
    WorkbenchData? _pendingData;
    string _currentFilter = string.Empty;
    List<TItem> _allItems = [];
    string? _emptyState;
    int _pageIndex;

    int _lastAppliedSortColumn = -1;
    SortDirection _lastAppliedSortDirection = SortDirection.None;

    /// <inheritdoc/>
    public Action<bool>? OnFilterFocusChanged { get; set; }

    /// <inheritdoc/>
    public bool IsActive { get; set; }

    /// <summary>Gets the primary focus target for this view (the main table).</summary>
    public IWindowControl? PrimaryFocusTarget => _table;

    /// <inheritdoc/>
    public string? DetailContent => _detailPanel?.Content;

    /// <summary>Gets the per-view help text shown in the help overlay.</summary>
    public virtual string ViewHelp => string.Empty;

    /// <inheritdoc/>
    public IReadOnlyList<ViewAction> ViewActions => GetToolbarActionTemplate();

    /// <summary>Gets column definitions: (name, justification, fixed width or null for flex).</summary>
    protected abstract IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns { get; }

    /// <summary>Gets the header label shown on the right detail panel.</summary>
    protected virtual string DetailPanelHeader => "DETAIL";

    /// <summary>
    /// Gets the semantic color role for this view's detail-pane border.
    /// Override per view to assign a view-specific role that re-resolves from the theme on every repaint.
    /// </summary>
    protected virtual ColorRole DetailColorRole => ColorRole.Primary;

    /// <summary>
    /// Gets the semantic color role applied to the main data table's row-selection accent.
    /// Defaults to <see cref="DetailColorRole"/> so each view's table shares the view's accent.
    /// Override to use a different role for the table independently of the detail pane.
    /// </summary>
    protected virtual ColorRole TableAccentRole => DetailColorRole;

    /// <summary>
    /// Gets the theme-aware color accessor, bound to the window system on first call.
    /// </summary>
    protected WorkbenchTheme Theme => _theme ??= new WorkbenchTheme(_windowSystem!);

    /// <summary>Gets a value indicating whether to enable checkbox multi-select mode on the table.</summary>
    protected virtual bool HasCheckboxMode => false;

    /// <summary>Gets the zero-based column index to use for the initial sort. -1 = no initial sort.</summary>
    protected virtual int DefaultSortColumn => -1;

    /// <summary>Gets the sort direction applied alongside <see cref="DefaultSortColumn"/> on first display.</summary>
    protected virtual SortDirection DefaultSortDirection => SortDirection.None;

    /// <summary>Gets the width of the right-hand detail pane in character columns.</summary>
    protected virtual int DetailPaneWidth => Math.Max(30, Console.WindowWidth / 3);

    /// <summary>Gets the pending data snapshot.</summary>
    protected WorkbenchData? PendingData => _pendingData;

    /// <summary>Gets the currently selected item, or <see langword="default"/> if no row is selected.</summary>
    protected TItem? SelectedItem =>
        _table?.SelectedRow?.Tag is TItem item ? item : default;

    /// <summary>Gets all items that are currently checked (checkbox mode only).</summary>
    protected IReadOnlyList<TItem> CheckedItems
    {
        get
        {
            if (_table is null)
            {
                return [];
            }

            return [.. _table.GetCheckedRows().Select(r => r.Tag).OfType<TItem>()];
        }
    }

    /// <summary>
    /// Gets the page title shown in the toolbar header strip.
    /// When non-null, a <see cref="ToolbarControl"/> is rendered above the header rule and table,
    /// hosting the title, action buttons for the current selection, and the filter prompt.
    /// Override in concrete views to supply the view-specific title (e.g. <c>"OBSERVERS"</c>).
    /// When <see langword="null"/>, no toolbar is rendered.
    /// </summary>
    protected virtual string? PageTitle => null;

    /// <summary>
    /// Gets the message shown in the table body when the view has no items (and no filter is active).
    /// Views override this with a friendlier line (e.g. "No background jobs running").
    /// </summary>
    protected virtual string EmptyStateMessage => "Nothing to show.";

    /// <summary>
    /// Computes how many rows to load per page from the available content height. Pagination drives
    /// the visible row count (the loaded rows fill the table), so this is derived from the terminal
    /// height minus the surrounding chrome rather than the table's own arranged height.
    /// </summary>
    int PageSize =>
        Math.Max(MinPageSize, Console.WindowHeight - ContentChromeHeight - PagerRowHeight);

    /// <inheritdoc/>
    public virtual void PopulateContent(SharpConsoleUI.Controls.ScrollablePanelControl panel, ConsoleWindowSystem windowSystem)
    {
        // PopulateContent runs on every navigation to this view; release the previous build first so a
        // re-populate does not leak controls or duplicate the resize / table event subscriptions, and
        // clear the panel up front so a failure mid-build cannot leave stale controls displayed.
        DisposeControls();
        panel.ClearContents();

        _windowSystem = windowSystem;
        _theme = new WorkbenchTheme(windowSystem);
        var tableBuilder = Controls.Table();

        foreach (var (name, justify, width) in Columns)
        {
            tableBuilder.AddColumn(name, justify, width);
        }

        tableBuilder = WorkbenchUi.StyleDataTable(tableBuilder, TableAccentRole, Theme.Muted)
            .Interactive()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .OnSelectedRowChanged((_, _) =>
            {
                RebuildToolbarButtons();
                RefreshDetail();
            })
            .OnRowActivated((_, _) => ActivateSelected())
            .WithName($"{GetType().Name}Table");

        if (HasCheckboxMode)
        {
            tableBuilder = tableBuilder.WithCheckboxMode();
        }

        _table = tableBuilder.Build();
        _table.TruncationFade = true;

        // SortByColumn (called on header click) does NOT fire PropertyChanged for SortColumnIndex
        // or CurrentSortDirection — those properties have no setters. MouseClick fires after
        // SortByColumn completes, giving us the correct new sort state to detect header clicks.
        _table.MouseClick += OnTableMouseClick;
        _table.MouseRightClick += OnTableRightClick;

        // Wire checkbox multi-selection changes so toolbar enabled-state reflects checked count promptly.
        _table.MultiSelectionChanged += OnTableMultiSelectionChanged;

        // Wire per-column typed comparers so SortByColumn uses our comparers, not string comparison.
        // This ensures the sort map produced by SortByColumn matches our pre-sorted page data.
        var columns = _table.Columns;
        for (var i = 0; i < columns.Count; i++)
        {
            var colIndex = i;
            var col = columns[i];
            col.IsSortable = IsSortableColumn(colIndex);
            var comparer = GetColumnComparer(colIndex);
            col.CustomRowComparer = (a, b) =>
                a.Tag is TItem ta && b.Tag is TItem tb ? comparer.Compare(ta, tb) : 0;
        }

        _pageIndicator = new MarkupControl([string.Empty]) { Name = $"{GetType().Name}Page" };

        _prevPageButton = Controls.Button(" ‹ ")
            .OnClick((_, _) => PreviousPage())
            .WithColorRole(ColorRole.Primary)
            .WithName($"{GetType().Name}PrevPage")
            .Build();

        _nextPageButton = Controls.Button(" › ")
            .OnClick((_, _) => NextPage())
            .WithColorRole(ColorRole.Primary)
            .WithName($"{GetType().Name}NextPage")
            .Build();

        // The explicit WithInputWidth gives the hosted prompt a visible field in the toolbar's
        // fixed-width layout; without it an empty prompt measures ~0 and renders invisibly.
        _filterPrompt = Controls.Prompt("/ filter: ")
            .WithHistory(true)
            .WithInputWidth(28)
            .WithTabCompleter((input, _) => GetCompletions(input))
            .OnInputChanged((_, text) =>
            {
                _currentFilter = text ?? string.Empty;
                _pageIndex = 0;
                RebuildRows();
            })
            .OnGotFocus((_, _) => OnFilterFocusChanged?.Invoke(true))
            .OnLostFocus((_, _) => OnFilterFocusChanged?.Invoke(false))
            .WithName($"{GetType().Name}Filter")
            .Build();

        _detailPanel = Controls.Panel()
            .WithContent($"[{Theme.Muted.ToMarkup()}]Select an item.[/]")
            .WithHeader($" {DetailPanelHeader} ")
            .Rounded()
            .WithColorRole(DetailColorRole)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName($"{GetType().Name}Detail")
            .Build();

        // Pager row: fixed-width prev/next buttons flanking a centered page indicator.
        var pageNavRow = Controls.Grid()
            .Columns(SharpConsoleUI.Layout.GridLength.Cells(5), SharpConsoleUI.Layout.GridLength.Star(1), SharpConsoleUI.Layout.GridLength.Cells(5))
            .Rows(SharpConsoleUI.Layout.GridLength.Auto())
            .Place(_prevPageButton, 0, 0)
            .Place(_pageIndicator, 0, 1)
            .Place(_nextPageButton, 0, 2)
            .Build();

        // Left pane: table + pager stacked in a ScrollablePanel.
        // The framework panel (propagated from NavigationView) provides the bounded height chain:
        // framework panel → _root HGC → column → leftPane → table (Fill). No outer wrapper needed.
        var leftPane = Controls.ScrollablePanel()
            .AddControl(_table)
            .AddControl(pageNavRow)
            .WithVerticalScroll(ScrollMode.None)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .Build();

        // Main two-column shell: left pane (flex) | detail panel (fixed width) with a draggable splitter.
        _root = HorizontalGridControl.Create()
            .Column(c => c.Add(leftPane))
            .WithSplitterAfter(0)
            .Column(c => c.Width(DetailPaneWidth).Add(_detailPanel))
            .Build();

        // Fill the framework panel so the HGC receives bounded height, which propagates through
        // leftPane to the table and allows VerticalAlignment.Fill to trigger properly.
        _root.VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Fill;

        // Re-paginate on terminal resize so the page size tracks the new terminal height.
        windowSystem.WindowResized += OnTerminalResized;

        if (_pendingData is not null)
        {
            var wasActive = IsActive;
            IsActive = false;
            UpdateData(_pendingData);
            IsActive = wasActive;
            _filterPrompt.Input = _currentFilter;
        }

        // Add controls directly to the framework panel so it propagates bounded height (avoids the
        // double-wrap — workbench ScrollablePanel → inner ScrollablePanel — that offered the table
        // int.MaxValue, collapsing it to ~6 rows). The panel was already cleared at the top.

        // Build the toolbar when PageTitle is set. The toolbar hosts title + action buttons +
        // fill spacer + the filter prompt (moved out of leftPane so it appears in the header band).
        // Action buttons are captured so RebuildToolbarButtons can update them in-place on selection
        // change without clearing the toolbar (avoids Container=null churn on the filter prompt).
        var pageTitle = PageTitle;
        if (pageTitle is not null)
        {
            (_toolbar, _actionButtons) = WorkbenchUi.BuildToolbarHeader(
                Theme.Accent,
                pageTitle,
                BuildToolbarActionDescriptors(),
                _filterPrompt);

            panel.AddControl(_toolbar);
            panel.AddControl(WorkbenchUi.BuildHeaderRule(TableAccentRole));
            panel.AddControl(_root);
            return;
        }

        var legacyHeader = BuildHeader();
        if (legacyHeader is not null)
        {
            panel.AddControl(legacyHeader);
            panel.AddControl(WorkbenchUi.BuildHeaderRule(TableAccentRole));
            panel.AddControl(_root);
            return;
        }

        panel.AddControl(_root);
    }

    /// <inheritdoc/>
    public void UpdateData(WorkbenchData data)
    {
        _pendingData = data;

        if (_table is null)
        {
            return;
        }

        _allItems = [.. GetItems(data)];

        if (!IsActive)
        {
            RebuildRows();
        }
    }

    /// <inheritdoc/>
    public void ActivateFilter(Window window)
    {
        if (_filterPrompt is not null)
        {
            window.FocusControl(_filterPrompt);
        }
    }

    /// <inheritdoc/>
    public void ClearFilter()
    {
        _currentFilter = string.Empty;
        SetFilterInput(string.Empty);
        RebuildRows();
    }

    /// <inheritdoc/>
    public void ToggleDetailPane()
    {
        if (_root is null)
        {
            return;
        }

        _detailPaneVisible = !_detailPaneVisible;
        var targetWidth = _detailPaneVisible ? DetailPaneWidth : 0;
        _root.AnimateColumnWidth(1, targetWidth, TimeSpan.FromMilliseconds(180), EasingFunctions.EaseInOut);
    }

    /// <inheritdoc/>
    public void MoveSelectionDown()
    {
        if (_table is null || _table.SelectedRowIndex >= _table.Rows.Count - 1)
        {
            return;
        }

        _table.SelectedRowIndex++;
    }

    /// <inheritdoc/>
    public void MoveSelectionUp()
    {
        if (_table is null || _table.SelectedRowIndex <= 0)
        {
            return;
        }

        _table.SelectedRowIndex--;
    }

    /// <inheritdoc/>
    public void JumpToFirstRow()
    {
        if (_table is null || _table.Rows.Count == 0)
        {
            return;
        }

        _table.SelectedRowIndex = 0;
    }

    /// <inheritdoc/>
    public void JumpToLastRow()
    {
        if (_table is null || _table.Rows.Count == 0)
        {
            return;
        }

        _table.SelectedRowIndex = _table.Rows.Count - 1;
    }

    /// <inheritdoc/>
    public void NextPage()
    {
        if (_table is null)
        {
            return;
        }

        var totalPages = ComputeTotalPages(GetFiltered().Count);
        if (_pageIndex < totalPages - 1)
        {
            _pageIndex++;
            RebuildRows();
        }
    }

    /// <inheritdoc/>
    public void PreviousPage()
    {
        if (_table is null || _pageIndex <= 0)
        {
            return;
        }

        _pageIndex--;
        RebuildRows();
    }

    /// <inheritdoc/>
    public void Dispose() => DisposeControls();

    /// <summary>Sets the filter text and rebuilds the table rows.</summary>
    /// <param name="filter">The filter string to apply.</param>
    public void SetFilter(string filter)
    {
        _currentFilter = filter;
        _pageIndex = 0;
        SetFilterInput(filter);
        RebuildRows();
    }

    /// <summary>Extracts the relevant items from the snapshot.</summary>
    /// <param name="data">The current workbench data snapshot.</param>
    /// <returns>The items to display in the table.</returns>
    protected abstract IEnumerable<TItem> GetItems(WorkbenchData data);

    /// <summary>Returns a stable string key that uniquely identifies this item.</summary>
    /// <param name="item">The item.</param>
    /// <returns>A unique string key.</returns>
    protected abstract string GetKey(TItem item);

    /// <summary>Returns cell values for the table row (must match <see cref="Columns"/> count).</summary>
    /// <param name="item">The item to render.</param>
    /// <returns>Cell values for each column, may contain markup.</returns>
    protected abstract string[] BuildRow(TItem item);

    /// <summary>Returns the markup string shown in the right detail panel for the selected item.</summary>
    /// <param name="item">The selected item, or <see langword="null"/> if nothing is selected.</param>
    /// <param name="data">The current workbench data snapshot.</param>
    /// <returns>Markup content for the detail panel.</returns>
    protected abstract string RenderDetail(TItem? item, WorkbenchData? data);

    /// <summary>Returns true if the item matches the given filter text.</summary>
    /// <param name="item">The item to test.</param>
    /// <param name="filter">The current filter string.</param>
    /// <returns><see langword="true"/> if the item passes the filter.</returns>
    protected abstract bool MatchesFilter(TItem item, string filter);

    /// <summary>
    /// Returns the comparer used to sort the given column.
    /// Default: OrdinalIgnoreCase string comparison on the rendered cell text.
    /// Override for numeric, date, or enum columns to get correct cross-page sort order.
    /// </summary>
    /// <param name="columnIndex">The zero-based column index being sorted.</param>
    /// <returns>A comparer for <typeparamref name="TItem"/>.</returns>
    protected virtual IComparer<TItem> GetColumnComparer(int columnIndex) =>
        Comparer<TItem>.Create((a, b) => string.Compare(
            Markup.Remove(BuildRow(a).ElementAtOrDefault(columnIndex) ?? string.Empty),
            Markup.Remove(BuildRow(b).ElementAtOrDefault(columnIndex) ?? string.Empty),
            StringComparison.OrdinalIgnoreCase));

    /// <summary>Tab-completion tokens for the filter prompt. Default: none.</summary>
    /// <param name="input">The current user input.</param>
    /// <returns>Completion suggestions.</returns>
    protected virtual IEnumerable<string> GetCompletions(string input) => [];

    /// <summary>
    /// Called when a row is activated — Enter key or double-click. Activation is a non-destructive
    /// "inspect" gesture: it delegates to <see cref="OnInspect"/>, never a mutating action. Destructive
    /// operations (replay, retry, stop, apply) are reachable only via their explicit shortcut key,
    /// toolbar button, or context menu — so a reflexive double-click can never trigger one.
    /// </summary>
    /// <param name="item">The activated item.</param>
    protected void OnRowActivated(TItem item) => OnInspect(item);

    /// <summary>
    /// Inspects the activated row. The default opens a read-only detail overlay (a reading modal) built
    /// from <see cref="RenderDetail"/>, so activating a row always surfaces its full detail. Views with a
    /// richer read view (e.g. a tabbed instances overlay, or navigating to a definition) override this.
    /// </summary>
    /// <param name="item">The row to inspect.</param>
    protected virtual void OnInspect(TItem item) => ShowReadingModal(item);

    /// <summary>
    /// Opens a read-only detail overlay (reading modal) for the given row. The body markup is supplied by
    /// the caller — defaulting to <see cref="RenderDetail"/>, but overridable so a view can embed richer
    /// or different content in the modal than in the side pane. The view's single-item actions (replay,
    /// retry, stop, …) are surfaced as buttons so they can be triggered directly from the modal.
    /// </summary>
    /// <param name="item">The row to display.</param>
    /// <param name="content">
    /// The body markup; when <see langword="null"/>, falls back to <see cref="RenderDetail"/> for the item.
    /// </param>
    protected void ShowReadingModal(TItem item, string? content = null)
    {
        if (_windowSystem is null)
        {
            return;
        }

        var body = content ?? RenderDetail(item, _pendingData);
        var title = $" {GetDetailTitle(item)} ";

        // Surface the single-item actions (those bound to a row, identified by a trigger key) as buttons,
        // with the shortcut embedded in the caption (e.g. "Replay (R)") and the key itself live inside the
        // modal. Bulk actions operate on checked rows, not the single inspected item, so they are excluded.
        var actions = GetToolbarActionTemplate()
            .Where(a => a.TriggerKey is not null)
            .Select(a => (
                Label: a.KeyHint is { } key ? $"{a.Label} ({key})" : a.Label,
                Key: a.TriggerKey,
                a.Execute))
            .ToList();

        var overlay = new DetailOverlayWindow();
        var window = overlay.Build(_windowSystem, title, body, actions);
        _windowSystem.AddWindow(window, activateWindow: true);
    }

    /// <summary>
    /// Gets the title for the reading modal opened on row activation. Defaults to the row key; views
    /// override for a friendlier label (e.g. an observer or container name).
    /// </summary>
    /// <param name="item">The row being inspected.</param>
    /// <returns>The modal title text.</returns>
    protected virtual string GetDetailTitle(TItem item) => GetKey(item);

    /// <summary>
    /// Builds the muted "Select a/an &lt;noun&gt;." prompt shown in the detail pane when no row is selected.
    /// </summary>
    /// <param name="noun">The entity noun including its article (e.g. "an observer", "a job").</param>
    /// <returns>The muted prompt markup.</returns>
    protected string SelectPrompt(string noun) => $"[{Theme.Muted.ToMarkup()}]Select {noun}.[/]";

    /// <summary>
    /// Builds a single-item toolbar action that operates on the current <see cref="SelectedItem"/>: it is
    /// enabled while a row is selected and runs <paramref name="onSelected"/> with that row when triggered.
    /// </summary>
    /// <param name="label">The action label.</param>
    /// <param name="key">The shortcut key (also shown as the toolbar key hint).</param>
    /// <param name="onSelected">The callback run with the selected item.</param>
    /// <returns>The configured <see cref="ViewAction"/>.</returns>
    protected ViewAction SingleAction(string label, ConsoleKey key, Action<TItem> onSelected) =>
        new(
            label,
            key.ToString(),
            key,
            default,
            () =>
            {
                if (SelectedItem is TItem item)
                {
                    onSelected(item);
                }
            },
            Enabled: SelectedItem is not null);

    /// <summary>
    /// Builds a bulk toolbar action over the currently checked rows: when nothing is checked the label is
    /// just "{verb} checked" (and the action is disabled); once rows are checked it reads "{verb} N checked"
    /// and runs <paramref name="onChecked"/> with the checked set. Bulk actions have no shortcut key (they
    /// are toolbar / context-menu only).
    /// </summary>
    /// <param name="verb">The action verb (e.g. "Replay", "Stop").</param>
    /// <param name="onChecked">The callback run with the checked items.</param>
    /// <returns>The configured <see cref="ViewAction"/>.</returns>
    protected ViewAction BulkAction(string verb, Action<IReadOnlyList<TItem>> onChecked)
    {
        var count = CheckedItems.Count;
        var label = count == 0 ? $"{verb} checked" : $"{verb} {count} checked";
        return new ViewAction(
            label,
            null,
            null,
            default,
            () =>
            {
                var items = CheckedItems;
                if (items.Count > 0)
                {
                    onChecked(items);
                }
            },
            Enabled: count > 0);
    }

    /// <summary>
    /// Returns the complete, selection-independent action template for this view's toolbar.
    /// The returned list has a STABLE count that never changes, so buttons are built once and updated
    /// in-place on every selection or check-state change. Each action's <see cref="ViewAction.Execute"/>
    /// and <see cref="ViewAction.Enabled"/> must resolve the current <see cref="SelectedItem"/> and
    /// <see cref="CheckedItems"/> at invocation time — never capture a specific item.
    /// </summary>
    /// <returns>
    /// The full ordered set of toolbar actions for this view, or an empty list for views with no actions.
    /// </returns>
    protected virtual IReadOnlyList<ViewAction> GetToolbarActionTemplate() => [];

    /// <summary>
    /// Returns <see langword="true"/> when the user may sort by the given column.
    /// Override to restrict sorting to specific columns only.
    /// </summary>
    /// <param name="columnIndex">The zero-based column index to test.</param>
    /// <returns><see langword="true"/> if the column is sortable.</returns>
    protected virtual bool IsSortableColumn(int columnIndex) => true;

    /// <summary>
    /// Optionally returns a page header control rendered above the table and splitter.
    /// When non-null, the returned control is stacked above a rule and the main content pane.
    /// Override in views that should show a <see cref="WorkbenchUi.BuildPageHeader"/> identity strip;
    /// the default returns <see langword="null"/> so no header is shown.
    /// </summary>
    /// <returns>
    /// A header <see cref="IWindowControl"/>, typically built with <see cref="WorkbenchUi.BuildPageHeader"/>,
    /// or <see langword="null"/> to suppress the header entirely.
    /// </returns>
    protected virtual IWindowControl? BuildHeader() => null;

    static void SetButtonEnabled(ButtonControl? button, bool enabled)
    {
        if (button is null)
        {
            return;
        }

        button.IsEnabled = enabled;
    }

    IReadOnlyList<(string Text, ColorRole Role, bool Enabled, Action OnClick)> BuildToolbarActionDescriptors()
    {
        var template = GetToolbarActionTemplate();
        return [.. template.Select((a, i) =>
        {
            var text = a.KeyHint is not null ? $"{a.Label} ({a.KeyHint})" : a.Label;
            var lc = a.Label.ToUpperInvariant();
            var role = lc.Contains("STOP") || lc.Contains("REMOVE") || lc.Contains("DELETE") || lc.Contains("IGNORE")
                ? ColorRole.Danger
                : ColorRole.Warning;
            var index = i;
            return (text, role, a.Enabled, (Action)(() => InvokeToolbarAction(index)));
        })];
    }

    void InvokeToolbarAction(int index)
    {
        var template = GetToolbarActionTemplate();
        if (index < template.Count && template[index].Enabled)
        {
            template[index].Execute();
        }
    }

    void RebuildToolbarButtons()
    {
        if (_toolbar is null || _filterPrompt is null)
        {
            return;
        }

        // Update each action button in-place (IsEnabled, ColorRole, Text) without clearing the
        // toolbar — preserves the filter prompt's container reference across selection changes.
        // Returns the (possibly new) button list when a fallback rebuild was needed.
        _actionButtons = WorkbenchUi.UpdateToolbarActions(_toolbar, _actionButtons, BuildToolbarActionDescriptors(), _filterPrompt);
    }

    void OnTableMultiSelectionChanged(object? sender, int count) => RebuildToolbarButtons();

    int ComputeTotalPages(int itemCount)
    {
        var pageSize = PageSize;
        return Math.Max(1, (itemCount + pageSize - 1) / pageSize);
    }

    List<TItem> GetFiltered() =>
        string.IsNullOrEmpty(_currentFilter)
            ? _allItems
            : [.. _allItems.Where(item => MatchesFilter(item, _currentFilter))];

    List<TItem> ApplySort(List<TItem> items, int sortCol, SortDirection sortDir)
    {
        var col = sortCol >= 0 ? sortCol : DefaultSortColumn;
        var dir = sortDir != SortDirection.None ? sortDir : DefaultSortDirection;

        if (col < 0 || dir == SortDirection.None)
        {
            return items;
        }

        var comparer = GetColumnComparer(col);
        return dir == SortDirection.Ascending
            ? [.. items.Order(comparer)]
            : [.. items.OrderDescending(comparer)];
    }

    void SetFilterInput(string value)
    {
        if (_filterPrompt is null)
        {
            return;
        }

        _filterPrompt.Input = value;
    }

    /// <summary>
    /// Re-applies the explicit grid size and re-paginates when the terminal is resized.
    /// The GridControl is sized from terminal dimensions, so it must be updated on every resize.
    /// Pagination is also re-computed because the table's visible-row capacity changes with height.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="size">The new terminal size.</param>
    void OnTerminalResized(object? sender, SharpConsoleUI.Helpers.Size size)
    {
        if (_table is null)
        {
            return;
        }

        // The page size is derived from the terminal height, so on resize clamp the page index to the
        // new capacity and re-render so the pager and loaded rows reflect the new height.
        var filtered = GetFiltered();
        var totalPages = ComputeTotalPages(filtered.Count);
        if (_pageIndex >= totalPages)
        {
            _pageIndex = Math.Max(0, totalPages - 1);
        }

        RebuildRows();
    }

    /// <summary>Detaches the terminal-resize handler if it was attached.</summary>
    void UnsubscribeFromResize()
    {
        if (_windowSystem is null)
        {
            return;
        }

        _windowSystem.WindowResized -= OnTerminalResized;
    }

    /// <summary>
    /// Detaches event handlers and disposes the built controls. Called from <see cref="Dispose"/> and
    /// at the start of <see cref="PopulateContent"/> (which re-runs on every navigation to this view)
    /// so a re-populate does not leak the previous build's controls or duplicate its event handlers.
    /// </summary>
    void DisposeControls()
    {
        UnsubscribeFromResize();

        if (_table is not null)
        {
            _table.MouseClick -= OnTableMouseClick;
            _table.MouseRightClick -= OnTableRightClick;
            _table.MultiSelectionChanged -= OnTableMultiSelectionChanged;
        }

        _root?.Dispose();
        _table?.Dispose();
        _detailPanel?.Dispose();
        _toolbar?.Dispose();

        // _filterPrompt is owned by _toolbar (added as a toolbar item) — ToolbarControl.OnDisposing
        // disposes all its items, so we must NOT dispose _filterPrompt here to avoid double-dispose.
        // Only dispose it directly when no toolbar was built (PageTitle == null path).
        if (_toolbar is null)
        {
            _filterPrompt?.Dispose();
        }

        _pageIndicator?.Dispose();
        _prevPageButton?.Dispose();
        _nextPageButton?.Dispose();
    }

    /// <summary>
    /// Fires after every left-click on the table, including header clicks.
    /// <c>SortByColumn</c> runs before <c>MouseClick</c> fires, so we can compare the table's new
    /// sort state against what we applied last time. When it differs, a header click changed the
    /// sort and we rebuild the full dataset with the new sort applied across all pages.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">Mouse event args.</param>
    void OnTableMouseClick(object? sender, MouseEventArgs e)
    {
        if (_table is null)
        {
            return;
        }

        var newCol = _table.SortColumnIndex;
        var newDir = _table.CurrentSortDirection;

        if (newCol != _lastAppliedSortColumn || newDir != _lastAppliedSortDirection)
        {
            RebuildRows();
        }
    }

    void OnTableRightClick(object? sender, MouseEventArgs e)
    {
        if (_windowSystem is null)
        {
            return;
        }

        var actions = GetToolbarActionTemplate().Where(a => a.Enabled).ToList();
        WorkbenchContextMenu.Show(_windowSystem, Theme, e.AbsolutePosition.X, e.AbsolutePosition.Y, actions);
    }

    void RebuildRows()
    {
        if (_table is null)
        {
            return;
        }

        // Capture sort state before clearing rows.
        var sortCol = _table.SortColumnIndex;
        var sortDir = _table.CurrentSortDirection;

        var selectedKey = SelectedItem is TItem sel ? GetKey(sel) : null;

        // Read capacity from the table — GetVisibleRowCount() returns real fitted count post-layout,
        // or RowCount (safe fallback) pre-layout.
        var pageSize = PageSize;

        // Sort the FULL filtered dataset first, then paginate.
        // This guarantees consistent cross-page order regardless of column type.
        var filtered = GetFiltered();
        var sorted = ApplySort(filtered, sortCol, sortDir);
        var totalPages = ComputeTotalPages(sorted.Count);

        if (_pageIndex >= totalPages)
        {
            _pageIndex = Math.Max(0, totalPages - 1);
        }

        _table.ClearRows();

        foreach (var item in sorted.Skip(_pageIndex * pageSize).Take(pageSize))
        {
            _table.AddRow(new UITableRow(BuildRow(item)) { Tag = item });
        }

        // When there are no rows, surface guidance in the detail pane rather than as a table row — the
        // table has no per-row way to opt out of the checkbox column, so a placeholder row would render
        // a stray "[ ]" in checkbox-mode views.
        if (sorted.Count > 0)
        {
            _emptyState = null;
        }
        else
        {
            _emptyState = string.IsNullOrEmpty(_currentFilter) ? EmptyStateMessage : $"No matches for '{_currentFilter}'";
        }

        // Restore sort indicator on the column header. Rows are already in sorted order from
        // ApplySort above; CustomRowComparer matches that comparer so SortByColumn produces
        // an identity map — the visual indicator is set without re-ordering rows.
        // ClearSort and SortByColumn do NOT fire PropertyChanged, so no recursion risk.
        _table.ClearSort();
        if (sortCol >= 0 && sortDir != SortDirection.None)
        {
            _table.SortByColumn(sortCol);
            if (sortDir == SortDirection.Descending)
            {
                _table.SortByColumn(sortCol);
            }
        }
        else if (DefaultSortColumn >= 0 && DefaultSortDirection != SortDirection.None && sortCol < 0)
        {
            _table.SortByColumn(DefaultSortColumn);
            if (DefaultSortDirection == SortDirection.Descending)
            {
                _table.SortByColumn(DefaultSortColumn);
            }
        }

        // Record the sort state we applied so OnTableMouseClick can detect real header-click changes.
        _lastAppliedSortColumn = _table.SortColumnIndex;
        _lastAppliedSortDirection = _table.CurrentSortDirection;

        if (selectedKey is not null)
        {
            RestoreSelection(selectedKey);
        }
        else if (_table.Rows.Count > 0)
        {
            // ClearRows() resets SelectedRowIndex to -1. Pre-select the first row so
            // SelectedItem is always non-null and ViewActions always returns actions.
            _table.SelectedRowIndex = 0;
        }

        UpdatePageIndicator(sorted.Count, totalPages, pageSize);
        RefreshDetail();
    }

    void UpdatePageIndicator(int totalItems, int totalPages, int pageSize)
    {
        if (_pageIndicator is not null)
        {
            var mut = Theme.Muted.ToMarkup();
            if (totalItems <= pageSize)
            {
                _pageIndicator.Text = $"[{mut}]{totalItems} item{(totalItems == 1 ? string.Empty : "s")}[/]";
            }
            else
            {
                var first = (_pageIndex * pageSize) + 1;
                var last = Math.Min((_pageIndex + 1) * pageSize, totalItems);
                _pageIndicator.Text = $"[{mut}]{first}–{last} of {totalItems}[/]";
            }
        }

        SetButtonEnabled(_prevPageButton, _pageIndex > 0);
        SetButtonEnabled(_nextPageButton, _pageIndex < totalPages - 1);
    }

    void RestoreSelection(string key)
    {
        if (_table is null)
        {
            return;
        }

        // Iterate display positions to account for the active sort map.
        var count = _table.Rows.Count;
        for (var dispIdx = 0; dispIdx < count; dispIdx++)
        {
            _table.SelectedRowIndex = dispIdx;
            if (_table.SelectedRow?.Tag is TItem item && GetKey(item) == key)
            {
                return;
            }
        }

        if (count > 0)
        {
            _table.SelectedRowIndex = 0;
        }
    }

    void RefreshDetail()
    {
        if (_detailPanel is null)
        {
            return;
        }

        // With no rows, show the empty-state guidance in the detail pane (the table itself is blank);
        // otherwise render the selected item's detail.
        _detailPanel.Content = _emptyState is { } empty
            ? $"[{Theme.Muted.ToMarkup()}]{empty}[/]"
            : RenderDetail(SelectedItem, _pendingData);
    }

    void ActivateSelected()
    {
        if (SelectedItem is TItem item)
        {
            OnRowActivated(item);
        }
    }
}
