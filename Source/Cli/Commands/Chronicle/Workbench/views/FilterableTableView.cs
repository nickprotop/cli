// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Animation;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
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
    /// <summary>Rows consumed by non-table chrome. Subtracted from terminal height to compute page size.</summary>
    const int NonTableRowOverhead = 14;

    /// <summary>Minimum number of table rows to show regardless of terminal height.</summary>
    const int MinPageSize = 5;

    ConsoleWindowSystem? _windowSystem;
    HorizontalGridControl? _root;
    TableControl? _table;
    PanelControl? _detailPanel;
    PromptControl? _filterPrompt;
    MarkupControl? _pageIndicator;
    ButtonControl? _prevPageButton;
    ButtonControl? _nextPageButton;
    bool _detailPaneVisible = true;
    WorkbenchData? _pendingData;
    string _currentFilter = string.Empty;
    List<TItem> _allItems = [];
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
    public IReadOnlyList<ViewAction> ViewActions =>
        SelectedItem is TItem item ? GetAvailableActions(item) : [];

    /// <summary>Gets column definitions: (name, justification, fixed width or null for flex).</summary>
    protected abstract IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns { get; }

    /// <summary>Gets the header label shown on the right detail panel.</summary>
    protected virtual string DetailPanelHeader => "DETAIL";

    /// <summary>Gets the border color for the right detail panel.</summary>
    protected virtual SharpConsoleUI.Color DetailBorderColor => WorkbenchColors.Accent;

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

    int PageSize => Math.Max(MinPageSize, Console.WindowHeight - NonTableRowOverhead);

    /// <inheritdoc/>
    public virtual IWindowControl BuildContent(ConsoleWindowSystem windowSystem)
    {
        _windowSystem = windowSystem;
        var tableBuilder = Controls.Table();

        foreach (var (name, justify, width) in Columns)
        {
            tableBuilder.AddColumn(name, justify, width);
        }

        tableBuilder = tableBuilder
            .Interactive()
            .WithSorting()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .OnSelectedRowChanged((_, _) => RefreshDetail())
            .OnRowActivated((_, _) => ActivateSelected())
            .WithName($"{GetType().Name}Table");

        if (HasCheckboxMode)
        {
            tableBuilder = tableBuilder.WithCheckboxMode();
        }

        _table = tableBuilder.Build();

        // SortByColumn (called on header click) does NOT fire PropertyChanged for SortColumnIndex
        // or CurrentSortDirection — those properties have no setters. MouseClick fires after
        // SortByColumn completes, giving us the correct new sort state to detect header clicks.
        _table.MouseClick += OnTableMouseClick;
        _table.MouseRightClick += OnTableRightClick;

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

        _prevPageButton = Controls.Button(" ◄ ")
            .OnClick((_, _) => PreviousPage())
            .WithName($"{GetType().Name}PrevPage")
            .Build();

        _nextPageButton = Controls.Button(" ► ")
            .OnClick((_, _) => NextPage())
            .WithName($"{GetType().Name}NextPage")
            .Build();

        _filterPrompt = Controls.Prompt("/ filter: ")
            .WithHistory(true)
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
            .WithContent($"[{WorkbenchColors.Muted.ToMarkup()}]Select an item.[/]")
            .WithHeader($" {DetailPanelHeader} ")
            .Rounded()
            .WithBorderColor(DetailBorderColor)
            .WithPadding(1, 0, 1, 0)
            .FillVertical()
            .WithName($"{GetType().Name}Detail")
            .Build();

        var pageNavRow = HorizontalGridControl.Create()
            .Column(c => c.Width(5).Add(_prevPageButton))
            .Column(c => c.Add(_pageIndicator))
            .Column(c => c.Width(5).Add(_nextPageButton))
            .Build();

        var leftPane = Controls.ScrollablePanel()
            .AddControl(_filterPrompt)
            .AddControl(_table)
            .AddControl(pageNavRow)
            .WithVerticalScroll(ScrollMode.None)
            .Build();

        _root = HorizontalGridControl.Create()
            .Column(c => c.Add(leftPane))
            .WithSplitterAfter(0)
            .Column(c => c.Width(DetailPaneWidth).Add(_detailPanel))
            .Build();

        if (_pendingData is not null)
        {
            var wasActive = IsActive;
            IsActive = false;
            UpdateData(_pendingData);
            IsActive = wasActive;
            _filterPrompt.Input = _currentFilter;
        }

        return _root;
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
    public void Dispose()
    {
        if (_table is not null)
        {
            _table.MouseClick -= OnTableMouseClick;
            _table.MouseRightClick -= OnTableRightClick;
        }

        _root?.Dispose();
        _table?.Dispose();
        _detailPanel?.Dispose();
        _filterPrompt?.Dispose();
        _pageIndicator?.Dispose();
        _prevPageButton?.Dispose();
        _nextPageButton?.Dispose();
    }

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

    /// <summary>Called when Enter is pressed on a row. Default: no-op.</summary>
    /// <param name="item">The activated item.</param>
    protected virtual void OnRowActivated(TItem item)
    {
    }

    /// <summary>
    /// Returns the actions available when <paramref name="item"/> is selected.
    /// Override in action views to expose view-specific actions to the keyboard dispatcher
    /// and right-click context menu.
    /// </summary>
    /// <param name="item">The currently selected item.</param>
    /// <returns>The list of actions the user can invoke on this item.</returns>
    protected virtual IReadOnlyList<ViewAction> GetAvailableActions(TItem item) => [];

    /// <summary>
    /// Returns <see langword="true"/> when the user may sort by the given column.
    /// Override to restrict sorting to specific columns only.
    /// </summary>
    /// <param name="columnIndex">The zero-based column index to test.</param>
    /// <returns><see langword="true"/> if the column is sortable.</returns>
    protected virtual bool IsSortableColumn(int columnIndex) => true;

    static int ContextMenuWidth(List<ViewAction> actions)
    {
        var maxLabel = actions.Max(a => a.Label.Length);
        var maxHint = actions.Max(a => a.KeyHint?.Length ?? 0);
        return maxLabel + (maxHint > 0 ? maxHint + 4 : 0) + 4;
    }

    static void SetButtonEnabled(ButtonControl? button, bool enabled)
    {
        if (button is null)
        {
            return;
        }

        button.IsEnabled = enabled;
    }

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
        if (_windowSystem is null || SelectedItem is not TItem item)
        {
            return;
        }

        var actions = GetAvailableActions(item).ToList();
        if (actions.Count == 0)
        {
            return;
        }

        ShowContextMenu(e.AbsolutePosition.X, e.AbsolutePosition.Y, actions);
    }

    void ShowContextMenu(int x, int y, List<ViewAction> actions)
    {
        if (_windowSystem is null)
        {
            return;
        }

        var menuBuilder = Controls.Menu().Vertical()
            .WithMenuBarColors(WorkbenchColors.Background, WorkbenchColors.Foreground, WorkbenchColors.Accent, WorkbenchColors.Background)
            .WithDropdownColors(WorkbenchColors.Background, WorkbenchColors.Foreground, WorkbenchColors.Accent, WorkbenchColors.Background);

        foreach (var action in actions)
        {
            menuBuilder.AddItem(action.Label, action.KeyHint ?? string.Empty, action.Execute);
        }

        var menu = menuBuilder.Build();
        Window? contextWindow = null;

        menu.ItemSelected += (_, _) => _windowSystem.CloseWindow(contextWindow, activateParent: true, force: false);

        var width = Math.Max(20, ContextMenuWidth(actions));
        var height = actions.Count + 2;
        var clampedX = Math.Max(0, Math.Min(x, Console.WindowWidth - width));
        var clampedY = Math.Max(0, Math.Min(y, Console.WindowHeight - height));

        contextWindow = new WindowBuilder(_windowSystem)
            .WithTitle(string.Empty)
            .HideTitle()
            .HideCloseButton()
            .WithColors(WorkbenchColors.Foreground, WorkbenchColors.Background)
            .WithSize(width, height)
            .AtPosition(clampedX, clampedY)
            .WithCloseOnDeactivate(true)
            .AddControl(menu)
            .OnKeyPressed((_, ke) =>
            {
                if (ke.KeyInfo.Key == ConsoleKey.Escape)
                {
                    _windowSystem.CloseWindow(contextWindow, activateParent: true, force: false);
                    ke.Handled = true;
                }
            })
            .Build();

        _windowSystem.AddWindow(contextWindow, activateWindow: true);
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
            var mut = WorkbenchColors.Muted.ToMarkup();
            _pageIndicator.Text = totalItems <= pageSize
                ? $"[{mut}]{totalItems} item{(totalItems == 1 ? string.Empty : "s")}[/]"
                : $"[{mut}]{(_pageIndex * pageSize) + 1}–{Math.Min((_pageIndex + 1) * pageSize, totalItems)} of {totalItems}[/]";
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

        _detailPanel.Content = RenderDetail(SelectedItem, _pendingData);
    }

    void ActivateSelected()
    {
        if (SelectedItem is TItem item)
        {
            OnRowActivated(item);
        }
    }
}
