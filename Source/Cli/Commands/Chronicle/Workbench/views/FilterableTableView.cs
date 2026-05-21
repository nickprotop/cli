// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;
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
/// Abstract base for workbench views that display a filterable, sortable table with a detail panel.
/// Subclasses only implement domain-specific concerns — all table/filter/selection/pagination boilerplate lives here.
/// </summary>
/// <typeparam name="TItem">The domain item type displayed in each row.</typeparam>
public abstract class FilterableTableView<TItem> : IWorkbenchView
{
    /// <summary>
    /// UI rows consumed by non-table chrome: title bar, filter prompt, page nav, status bar, and borders.
    /// Subtracted from terminal height to compute the usable table row count.
    /// </summary>
    const int NonTableRowOverhead = 16;

    /// <summary>
    /// Minimum number of table rows to show regardless of terminal height.
    /// </summary>
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
    int _sortColumnIndex = -1;
    SortDirection _sortDirection = SortDirection.None;
    int _lastSetSortColumn = -1;
    SortDirection _lastSetSortDirection = SortDirection.None;
    bool _suppressSortSync;

    /// <inheritdoc/>
    public Action<bool>? OnFilterFocusChanged { get; set; }

    /// <inheritdoc/>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets the primary focus target for this view (the table itself; use F to reach the filter bar).
    /// </summary>
    public IWindowControl? PrimaryFocusTarget => _table;

    /// <inheritdoc/>
    public string? DetailContent => _detailPanel?.Content;

    /// <summary>
    /// Gets the per-view help text shown in the help overlay.
    /// Override to provide a brief description and a list of view-specific shortcuts.
    /// </summary>
    public virtual string ViewHelp => string.Empty;

    /// <summary>
    /// Gets column definitions: (name, justification, fixed width or null for flex).
    /// </summary>
    protected abstract IReadOnlyList<(string Name, TextJustification Justify, int? Width)> Columns { get; }

    /// <summary>
    /// Gets the header label shown on the right detail panel.
    /// </summary>
    protected virtual string DetailPanelHeader => "DETAIL";

    /// <summary>
    /// Gets the border color for the right detail panel.
    /// </summary>
    protected virtual SharpConsoleUI.Color DetailBorderColor => WorkbenchColors.Accent;

    /// <summary>
    /// Gets a value indicating whether to enable checkbox multi-select mode on the table.
    /// </summary>
    protected virtual bool HasCheckboxMode => false;

    /// <summary>
    /// Gets the zero-based column index to sort by when the view is first displayed.
    /// Return -1 (default) for no initial sort.
    /// </summary>
    protected virtual int DefaultSortColumn => -1;

    /// <summary>
    /// Gets the sort direction applied alongside <see cref="DefaultSortColumn"/> on first display.
    /// </summary>
    protected virtual SortDirection DefaultSortDirection => SortDirection.None;

    /// <summary>
    /// Gets the width of the right-hand detail pane in character columns.
    /// Defaults to one-third of the terminal width, with a minimum of 30.
    /// Subclasses may override to fix or adjust the width for their content.
    /// </summary>
    protected virtual int DetailPaneWidth => Math.Max(30, Console.WindowWidth / 3);

    /// <summary>
    /// Gets the pending data snapshot (populated during and after <see cref="UpdateData"/>).
    /// </summary>
    protected WorkbenchData? PendingData => _pendingData;

    /// <summary>
    /// Gets the currently selected item, or <see langword="default"/> if no row is selected.
    /// </summary>
    protected TItem? SelectedItem =>
        _table?.SelectedRow?.Tag is TItem item ? item : default;

    /// <summary>
    /// Gets all items that are currently checked (checkbox mode only).
    /// </summary>
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
    /// Number of table rows visible per page, computed from the current terminal height.
    /// </summary>
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

        _sortColumnIndex = DefaultSortColumn;
        _sortDirection = DefaultSortDirection;
        _lastSetSortColumn = _sortColumnIndex;
        _lastSetSortDirection = _sortDirection;

        _table.PropertyChanged += OnTablePropertyChanged;
        _table.MouseRightClick += OnTableRightClick;

        _pageIndicator = new MarkupControl([string.Empty]) { Name = $"{GetType().Name}Page" };

        _prevPageButton = Controls.Button(" ◄ ")
            .OnClick((_, _) => PreviousPage())
            .WithName($"{GetType().Name}PrevPage")
            .Build();

        _nextPageButton = Controls.Button(" ► ")
            .OnClick((_, _) => NextPage())
            .WithName($"{GetType().Name}NextPage")
            .Build();

        _filterPrompt = Controls.Prompt("🔍 Filter: ")
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
            // Force-rebuild even if IsActive is already true — BuildContent runs during NavigationView
            // lazy init, which may happen after IsActive is set.
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
    public void Dispose()
    {
        UnsubscribeTableEvents();
        _root?.Dispose();
        _table?.Dispose();
        _detailPanel?.Dispose();
        _filterPrompt?.Dispose();
        _pageIndicator?.Dispose();
        _prevPageButton?.Dispose();
        _nextPageButton?.Dispose();
    }

    /// <summary>
    /// Sets the filter text and rebuilds the table rows. Can be called externally to pre-filter the view.
    /// </summary>
    /// <param name="key">The filter string to apply.</param>
    public void SetFilter(string key)
    {
        _currentFilter = key;
        _pageIndex = 0;
        SetFilterInput(key);
        RebuildRows();
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

    /// <summary>
    /// Extracts the relevant items from the snapshot.
    /// </summary>
    /// <param name="data">The current workbench data snapshot.</param>
    /// <returns>The items to display in the table.</returns>
    protected abstract IEnumerable<TItem> GetItems(WorkbenchData data);

    /// <summary>
    /// Returns a stable string key that uniquely identifies this item (used for selection restore).
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>A unique string key.</returns>
    protected abstract string GetKey(TItem item);

    /// <summary>
    /// Returns cell values for the table row (must match <see cref="Columns"/> count).
    /// </summary>
    /// <param name="item">The item to render.</param>
    /// <returns>Cell values for each column, may contain SharpConsoleUI markup.</returns>
    protected abstract string[] BuildRow(TItem item);

    /// <summary>
    /// Returns the markup string shown in the right detail panel for the selected item.
    /// </summary>
    /// <param name="item">The selected item, or <see langword="null"/> if nothing is selected.</param>
    /// <param name="data">The current workbench data snapshot.</param>
    /// <returns>Markup content for the detail panel.</returns>
    protected abstract string RenderDetail(TItem? item, WorkbenchData? data);

    /// <summary>
    /// Returns true if the item matches the given filter text.
    /// </summary>
    /// <param name="item">The item to test.</param>
    /// <param name="filter">The current filter string.</param>
    /// <returns><see langword="true"/> if the item passes the filter.</returns>
    protected abstract bool MatchesFilter(TItem item, string filter);

    /// <summary>
    /// Returns the plain-text value used for sorting column <paramref name="columnIndex"/>.
    /// Default: strips markup from the corresponding <see cref="BuildRow"/> cell.
    /// Subclasses may override to provide numeric-aware or custom sort keys.
    /// </summary>
    /// <param name="item">The item to sort.</param>
    /// <param name="columnIndex">The zero-based column index being sorted.</param>
    /// <returns>A plain-text sort key.</returns>
    protected virtual string GetSortValue(TItem item, int columnIndex)
    {
        var cells = BuildRow(item);
        return columnIndex < cells.Length ? Markup.Remove(cells[columnIndex]) : string.Empty;
    }

    /// <summary>
    /// Tab-completion tokens for the filter prompt. Default: none.
    /// </summary>
    /// <param name="input">The current user input.</param>
    /// <returns>Completion suggestions.</returns>
    protected virtual IEnumerable<string> GetCompletions(string input) => [];

    /// <summary>
    /// Called when Enter is pressed on a row (or row double-clicked). Default: no-op.
    /// </summary>
    /// <param name="item">The activated item.</param>
    protected virtual void OnRowActivated(TItem item)
    {
    }

    /// <summary>
    /// Returns the context-menu actions available when the user right-clicks the given item.
    /// The base implementation returns an empty sequence — override to provide view-specific actions.
    /// </summary>
    /// <param name="item">The row item that was right-clicked.</param>
    /// <returns>
    /// A sequence of <c>(Label, Shortcut, Execute)</c> tuples. <c>Shortcut</c> may be <see langword="null"/>.
    /// </returns>
    protected virtual IEnumerable<(string Label, string? Shortcut, Action Execute)> GetContextMenuActions(TItem item) => [];

    /// <summary>
    /// Returns <see langword="true"/> when the user may sort by the given column index.
    /// Override to restrict sorting to specific columns only.
    /// </summary>
    /// <param name="columnIndex">The zero-based column index to test.</param>
    /// <returns><see langword="true"/> if the column is sortable; <see langword="false"/> otherwise.</returns>
    protected virtual bool IsSortableColumn(int columnIndex) => true;

    static int ContextMenuWidth(List<(string Label, string? Shortcut, Action Execute)> actions)
    {
        var maxLabel = actions.Max(a => a.Label.Length);
        var maxShortcut = actions.Max(a => a.Shortcut?.Length ?? 0);
        return maxLabel + (maxShortcut > 0 ? maxShortcut + 4 : 0) + 4;
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

    void SetFilterInput(string value)
    {
        if (_filterPrompt is null)
        {
            return;
        }

        _filterPrompt.Input = value;
    }

    void OnTableRightClick(object? sender, MouseEventArgs e)
    {
        if (_windowSystem is null || SelectedItem is not TItem item)
        {
            return;
        }

        var actions = GetContextMenuActions(item).ToList();
        if (actions.Count == 0)
        {
            return;
        }

        ShowContextMenu(e.AbsolutePosition.X, e.AbsolutePosition.Y, actions);
    }

    void ShowContextMenu(int x, int y, List<(string Label, string? Shortcut, Action Execute)> actions)
    {
        if (_windowSystem is null)
        {
            return;
        }

        var menuBuilder = Controls.Menu().Vertical()
            .WithMenuBarColors(WorkbenchColors.Background, WorkbenchColors.Foreground, WorkbenchColors.Accent, WorkbenchColors.Background)
            .WithDropdownColors(WorkbenchColors.Background, WorkbenchColors.Foreground, WorkbenchColors.Accent, WorkbenchColors.Background);

        foreach (var (label, shortcut, execute) in actions)
        {
            menuBuilder.AddItem(label, shortcut ?? string.Empty, execute);
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

    void UnsubscribeTableEvents()
    {
        if (_table is null)
        {
            return;
        }

        _table.PropertyChanged -= OnTablePropertyChanged;
        _table.MouseRightClick -= OnTableRightClick;
    }

    /// <summary>
    /// Responds to <c>PropertyChanged</c> events on the table — specifically <c>SortColumnIndex</c> and
    /// <c>CurrentSortDirection</c> which fire when the user clicks a column header. Captures the new
    /// sort intent, rejects sorts on columns where <see cref="IsSortableColumn"/> returns <see langword="false"/>,
    /// and immediately triggers a full-dataset rebuild so the correct order is shown
    /// without waiting for the next data refresh cycle.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The property changed event args containing the changed property name.</param>
    void OnTablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressSortSync)
        {
            return;
        }

        if (e.PropertyName is not (nameof(TableControl.SortColumnIndex) or nameof(TableControl.CurrentSortDirection)))
        {
            return;
        }

        var newCol = _table!.SortColumnIndex;
        var newDir = _table.CurrentSortDirection;

        if (newCol >= 0 && !IsSortableColumn(newCol))
        {
            RestoreSortIndicator();
            return;
        }

        _sortColumnIndex = newCol;
        _sortDirection = newDir;
        _lastSetSortColumn = _sortColumnIndex;
        _lastSetSortDirection = _sortDirection;
        RebuildRows();
    }

    /// <summary>
    /// Sorts <paramref name="items"/> by the active sort column, falling back to <see cref="DefaultSortColumn"/>
    /// and <see cref="DefaultSortDirection"/> when no user-initiated sort is active.
    /// Sorting is applied to the full dataset before pagination so cross-page order is consistent.
    /// </summary>
    /// <param name="items">The filtered item list to sort.</param>
    List<TItem> ApplySort(List<TItem> items)
    {
        var col = _sortColumnIndex >= 0 ? _sortColumnIndex : DefaultSortColumn;
        var dir = _sortDirection != SortDirection.None ? _sortDirection : DefaultSortDirection;

        if (col < 0 || dir == SortDirection.None)
        {
            return items;
        }

        return dir == SortDirection.Ascending
            ? [.. items.OrderBy(i => GetSortValue(i, col), StringComparer.OrdinalIgnoreCase)]
            : [.. items.OrderByDescending(i => GetSortValue(i, col), StringComparer.OrdinalIgnoreCase)];
    }

    void RebuildRows()
    {
        if (_table is null)
        {
            return;
        }

        SyncSortFromTable();

        var selectedKey = SelectedItem is TItem sel ? GetKey(sel) : null;
        var pageSize = PageSize;
        var filtered = GetFiltered();
        var sorted = ApplySort(filtered);
        var totalPages = ComputeTotalPages(sorted.Count);

        if (_pageIndex >= totalPages)
        {
            _pageIndex = totalPages - 1;
        }

        _table.ClearRows();

        foreach (var item in sorted.Skip(_pageIndex * pageSize).Take(pageSize))
        {
            _table.AddRow(new UITableRow(BuildRow(item)) { Tag = item });
        }

        RestoreSortIndicator();

        if (selectedKey is not null)
        {
            RestoreSelection(selectedKey);
        }

        UpdatePageIndicator(sorted.Count, totalPages, pageSize);
        RefreshDetail();
    }

    /// <summary>
    /// Detects sort changes made by clicking column headers (SharpConsoleUI updates <c>SortColumnIndex</c>
    /// immediately on click) and persists the new intent into our own fields so it survives across rebuilds.
    /// </summary>
    void SyncSortFromTable()
    {
        var tableCol = _table!.SortColumnIndex;
        var tableDir = _table.CurrentSortDirection;

        // A difference from what we last set means the user changed the sort via column header.
        if (tableCol != _lastSetSortColumn || tableDir != _lastSetSortDirection)
        {
            _sortColumnIndex = tableCol;
            _sortDirection = tableDir;
        }
    }

    /// <summary>
    /// Restores the sort direction indicator on the column header.
    /// The rows are already in sorted order from <see cref="ApplySort"/>; SharpConsoleUI's
    /// in-table re-sort on the current page is a stable no-op.
    /// Uses <see cref="_suppressSortSync"/> to prevent the <c>PropertyChanged</c> callbacks
    /// fired by <c>ClearSort</c> and <c>SortByColumn</c> from triggering a recursive rebuild.
    /// </summary>
    void RestoreSortIndicator()
    {
        _suppressSortSync = true;
        try
        {
            _table!.ClearSort();
            _lastSetSortColumn = _sortColumnIndex;
            _lastSetSortDirection = _sortDirection;

            if (_sortColumnIndex < 0 || _sortDirection == SortDirection.None)
            {
                return;
            }

            _table.SortByColumn(_sortColumnIndex);
            if (_sortDirection == SortDirection.Descending)
            {
                _table.SortByColumn(_sortColumnIndex);
            }
        }
        finally
        {
            _suppressSortSync = false;
        }
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

        for (var i = 0; i < _table.Rows.Count; i++)
        {
            if (_table.Rows[i].Tag is TItem item && GetKey(item) == key)
            {
                _table.SelectedRowIndex = i;
                return;
            }
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
