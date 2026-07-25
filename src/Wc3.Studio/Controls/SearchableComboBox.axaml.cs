using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Wc3.Commands;

namespace Wc3.Studio.Controls;

/// <summary>One dropdown choice: display name + id + optional caller payload.</summary>
public sealed record SearchableComboBoxItem(string Name, string Id, object? Payload = null)
{
    /// <summary>Rich list text: "Name (id)", or the bare id when nameless.</summary>
    public string Display =>
        string.IsNullOrWhiteSpace(Name) || Name == Id ? Id : $"{Name} ({Id})";
}

/// <summary>
/// ComboBox replacement whose dropdown is a single search box above a filtered,
/// display-only list of "Name (id)" rows. Matching and ranking come from the pure
/// <see cref="DropdownFilter"/> (case-insensitive substring on name OR id; exact-id
/// and prefix hits sort first).
///
/// Usage: <see cref="SetItems"/> with the choices (payload rides along untouched);
/// the user picks by click or Enter and <see cref="SelectionChanged"/> fires once
/// per commit; read <see cref="SelectedItem"/>/<see cref="SelectedId"/> any time.
/// Programmatic <see cref="Select"/> is silent by default so panels can set state
/// without triggering their own handlers.
/// </summary>
public partial class SearchableComboBox : UserControl
{
    private IReadOnlyList<SearchableComboBoxItem> _items = Array.Empty<SearchableComboBoxItem>();
    private List<SearchableComboBoxItem> _visible = new();
    private SearchableComboBoxItem? _selected;
    private bool _suppress;

    public SearchableComboBox()
    {
        InitializeComponent();
        DropDown.PlacementTarget = Toggle;
        Toggle.IsCheckedChanged += OnToggleChanged;
        // Light dismiss (or Escape) closes the popup without going through the
        // toggle; keep the chevron button's state in sync.
        DropDown.Closed += (_, _) => Toggle.IsChecked = false;
    }

    /// <summary>Raised once per user selection (click or Enter) that changes the item.</summary>
    public event EventHandler<SearchableComboBoxItem>? SelectionChanged;

    public IReadOnlyList<SearchableComboBoxItem> Items => _items;
    public SearchableComboBoxItem? SelectedItem => _selected;
    public string? SelectedId => _selected?.Id;

    /// <summary>Search box hint, e.g. "Search name or rawcode…".</summary>
    public string Watermark
    {
        get => SearchBox.Watermark ?? "";
        set => SearchBox.Watermark = value;
    }

    /// <summary>
    /// Replace the choices. <paramref name="selectId"/> picks the initial selection;
    /// when it matches nothing, <paramref name="selectFirstWhenNoMatch"/> decides whether
    /// to fall back to the first item (a kind switcher wants that; a picker that waits for
    /// a deliberate choice passes false to stay empty). An empty list always clears. Never
    /// raises <see cref="SelectionChanged"/> - the initial selection is the caller's own doing.
    /// </summary>
    public void SetItems(IReadOnlyList<SearchableComboBoxItem> items, string? selectId = null,
                         bool selectFirstWhenNoMatch = true)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        var initial = FindById(selectId);
        if (initial is null && selectFirstWhenNoMatch)
            initial = _items.Count > 0 ? _items[0] : null;
        SetSelected(initial);
        if (DropDown.IsOpen)
            RefreshList();
    }

    /// <summary>
    /// Programmatic selection by id; returns false when no such item exists.
    /// Raises <see cref="SelectionChanged"/> only when <paramref name="raiseEvent"/>
    /// is true AND the selection actually changed.
    /// </summary>
    public bool Select(string id, bool raiseEvent = false)
    {
        var item = FindById(id);
        if (item is null)
            return false;
        var changed = !ReferenceEquals(item, _selected);
        SetSelected(item);
        if (raiseEvent && changed)
            SelectionChanged?.Invoke(this, item);
        return true;
    }

    private SearchableComboBoxItem? FindById(string? id) =>
        id is null
            ? null
            : _items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));

    private void SetSelected(SearchableComboBoxItem? item)
    {
        _selected = item;
        SelectionText.Text = item?.Display ?? "";
    }

    // --- dropdown plumbing ---

    private void OnToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (Toggle.IsChecked == true)
            OpenDropDown();
        else
            DropDown.IsOpen = false;
    }

    private void OpenDropDown()
    {
        _suppress = true;
        SearchBox.Text = ""; // a fresh dropdown always shows the full list
        _suppress = false;
        RefreshList();
        DropDown.IsOpen = true;
        // Focus once the popup has laid out, so typing filters immediately.
        Dispatcher.UIThread.Post(() => SearchBox.Focus());
    }

    /// <summary>Re-filter + re-rank the visible list; the highlight follows the
    /// committed selection while it still matches.</summary>
    private void RefreshList()
    {
        var query = SearchBox.Text ?? "";
        _visible = _items
            .Where(i => DropdownFilter.Matches(query, i.Name, i.Id))
            .OrderBy(i => DropdownFilter.Rank(query, i.Name, i.Id)) // stable: ties keep item order
            .ToList();

        NoMatchText.IsVisible = _visible.Count == 0;
        _suppress = true;
        ItemList.ItemsSource = _visible;
        ItemList.SelectedItem = _selected is not null && _visible.Contains(_selected) ? _selected : null;
        _suppress = false;
        if (ItemList.SelectedItem is not null)
            ItemList.ScrollIntoView(ItemList.SelectedItem);
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress)
            return;
        RefreshList();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // Commit the highlighted row, else the best (first-ranked) match.
                Commit(ItemList.SelectedItem as SearchableComboBoxItem ?? _visible.FirstOrDefault());
                e.Handled = true;
                break;
            case Key.Down:
                if (_visible.Count > 0)
                {
                    if (ItemList.SelectedIndex < 0)
                    {
                        _suppress = true;
                        ItemList.SelectedIndex = 0;
                        _suppress = false;
                    }
                    ItemList.Focus();
                }
                e.Handled = true;
                break;
            case Key.Escape:
                DropDown.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit(ItemList.SelectedItem as SearchableComboBoxItem);
                e.Handled = true;
                break;
            case Key.Escape:
                DropDown.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    /// <summary>Click commits (fires on release, after the press updated the
    /// selection) - so re-clicking the already-selected row still closes.</summary>
    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        Commit(ItemList.SelectedItem as SearchableComboBoxItem);
    }

    private void Commit(SearchableComboBoxItem? item)
    {
        if (item is null)
            return;
        DropDown.IsOpen = false; // Closed handler unchecks the toggle
        var changed = !ReferenceEquals(item, _selected);
        SetSelected(item);
        if (changed)
            SelectionChanged?.Invoke(this, item);
    }
}
