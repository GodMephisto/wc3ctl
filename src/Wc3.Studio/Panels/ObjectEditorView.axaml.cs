using System.Collections.Concurrent;
using Wc3.GameData;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Render;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Panels;

/// <summary>
/// World-Editor-style object editor: a type switcher over all 7 Object Editor
/// kinds, the map's objects of that kind on the left (multi-select), and the
/// first selected object's merged fields (base game data ⊕ map deltas) on the
/// right. The field grid is read-only; editing happens in a dedicated box below
/// it - select a row, change the value, Apply writes it to every selected object
/// via ObjectSetCommand for the selected kind (all 7 kinds are editable), and
/// New… derives a fresh custom object from the selected one via ObjectNewCommand.
/// This select-then-edit design avoids putting TextBoxes inside the recycled
/// ListBox rows, whose focus/recycle behavior erased in-progress edits on click.
/// </summary>
public partial class ObjectEditorView : UserControl, IMapPanel
{
    private static readonly KindOption[] Kinds =
    {
        new(ObjectKind.Unit, "Units"),
        new(ObjectKind.Item, "Items"),
        new(ObjectKind.Ability, "Abilities"),
        new(ObjectKind.Destructable, "Destructibles"),
        new(ObjectKind.Doodad, "Doodads"),
        new(ObjectKind.Buff, "Buffs"),
        new(ObjectKind.Upgrade, "Upgrades"),
    };

    /// <summary>Model-file field code per kind; other kinds fall back to a value scan.</summary>
    private static readonly Dictionary<ObjectKind, string> ModelFieldCodes = new()
    {
        [ObjectKind.Unit] = "umdl",
        [ObjectKind.Doodad] = "dfil",
        [ObjectKind.Destructable] = "bfil",
        [ObjectKind.Item] = "ifil",
    };

    /// <summary>Interface-icon art field per kind, for the object list's row icons
    /// (same shape as <see cref="ModelFieldCodes"/>). Destructables and doodads have
    /// no icon art field, so their rows keep the empty icon box.</summary>
    private static readonly Dictionary<ObjectKind, string> IconFieldCodes = new()
    {
        [ObjectKind.Unit] = "uico",
        [ObjectKind.Item] = "iico",
        [ObjectKind.Ability] = "aart",
        [ObjectKind.Buff] = "fart",
        [ObjectKind.Upgrade] = "gar1",
    };

    private MapSession? _session;
    private bool _suppress;
    /// <summary>Fields applied via ObjectSetCommand but not yet written to disk.</summary>
    private int _unsavedEdits;

    /// <summary>
    /// The value a field held BEFORE this session first changed it, keyed by kind, object and
    /// field. Captured on the first edit only, so reverting always returns to what the map was
    /// opened with rather than to the previous keystroke.
    /// </summary>
    /// <remarks>
    /// Nothing recorded this before, so once a value was typed over there was no copy of it
    /// anywhere in the app and no way back short of closing the map without saving. Editing
    /// without a visible original and an undo is guesswork.
    /// </remarks>
    private readonly Dictionary<(ObjectKind Kind, string Rawcode, string Field), string> _originalValues = new();
    /// <summary>The current kind's full object list; SearchBox filters this in memory.</summary>
    private List<ObjectRow> _allRows = new();
    /// <summary>rawcode → display name across every kind's map objects, for reference
    /// fields (lazy; dropped on map change and after edits - see <see cref="RefNames"/>).</summary>
    private Dictionary<string, string>? _refNames;
    /// <summary>Metadata type per (kind, bare field code), cached so classifying a
    /// field grid never re-derives the same field's option set twice.</summary>
    private readonly Dictionary<(ObjectKind Kind, string Code), (string Type, bool IsList)> _fieldTypes = new();
    /// <summary>Internal name of the double-clicked object's map-imported model, when it has one.</summary>
    private string? _modelEntryName;

    // --- in-window model preview (all state UI-thread-only) ---
    private const int PreviewSizePx = 256;
    private const float DegreesPerPixel = 0.5f;
    private const float ZoomPerWheelNotch = 1.15f; // multiplicative zoom step
    private const float MinZoom = 0.25f, MaxZoom = 8f;
    /// <summary>Parsed model + textures cached so drag re-renders only rasterize.</summary>
    private RenderModelCommand.PreparedModel? _previewModel;
    private float _previewYaw = ModelRenderer.DefaultYawDegrees;
    private float _previewPitch = ModelRenderer.DefaultPitchDegrees;
    private float _previewZoom = ModelRenderer.DefaultZoom;
    /// <summary>Stamp that invalidates in-flight renders when the preview target changes.</summary>
    private int _previewGeneration;
    private bool _renderInFlight;
    /// <summary>A request arrived mid-render; run one trailing render when it lands.</summary>
    private bool _renderQueued;
    private bool _dragging;
    private Point _dragLast;

    /// <summary>The last (kind, rawcode) surfaced through <see cref="ObjectSelected"/>.</summary>
    private (ObjectKind Kind, string Rawcode)? _lastNotified;

    /// <summary>Anchor row for Shift+click range selection (the last plain-clicked row).</summary>
    private ObjectRow? _anchor;

    // --- typed field editor: one control shown per field, chosen from its metadata ---
    private enum EditorMode { Text, Combo, Multi, RefList }
    private EditorMode _editorMode = EditorMode.Text;
    /// <summary>Multiselect tokens in display order, so Apply joins deterministically.</summary>
    private IReadOnlyList<string> _editorMultiTokens = Array.Empty<string>();
    /// <summary>Reference-list builder entries (rawcodes in list order); Apply joins them.</summary>
    private List<string> _refListTokens = new();
    /// <summary>Above this many derivable options a field is treated as free text (paths,
    /// ids, and other high-cardinality fields aren't real enumerations).</summary>
    private const int MaxEditorOptions = 200;

    public ObjectEditorView()
    {
        InitializeComponent();
        KindCombo.SetItems(
            Kinds.Select(k => new SearchableComboBoxItem(k.Label, KindId(k.Kind), k)).ToList(),
            selectId: KindId(Kinds[0].Kind));
        KindCombo.Watermark = "Search kind name or id…";
        KindCombo.SelectionChanged += OnKindChanged;
        // Tunnel so right-click retargets the selection BEFORE the context menu opens.
        ObjectList.AddHandler(PointerPressedEvent, OnObjectListPointerPressed,
            RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Raised when the primary selected object (the first of the selection)
    /// changes - including the automatic first-row selection after a list/kind
    /// refresh. The workspace feeds this to the Dependencies tab.
    /// </summary>
    public event EventHandler<(ObjectKind Kind, string Rawcode)>? ObjectSelected;

    /// <summary>Raised by the object list's right-click "Show dependencies": an
    /// explicit ask to open the Dependencies tab on this object.</summary>
    public event EventHandler<(ObjectKind Kind, string Rawcode)>? DependenciesRequested;

    private KindOption SelectedKind => KindCombo.SelectedItem?.Payload as KindOption ?? Kinds[0];

    /// <summary>Canonical dropdown id for a kind - the enum name the CLI parses (case-insensitive).</summary>
    private static string KindId(ObjectKind kind) => kind.ToString().ToLowerInvariant();

    public void ShowMap(MapSession session)
    {
        _session = session;
        _unsavedEdits = 0;
        _refNames = null;   // another map's objects; rebuild lazily
        _fieldTypes.Clear(); // GameDir may differ per session
        StatusText.Text = "";

        if (session.Current is null)
        {
            HideModelArea(); // drop any preview still rendering against the old map
            ShowPlaceholder("Object editor panel - no map open");
            return;
        }

        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;
        // A freshly shown map starts on Units; Select never raises SelectionChanged,
        // so the single RefreshObjectList below is the only requery.
        KindCombo.Select(KindId(Kinds[0].Kind));
        RefreshObjectList();
    }

    private void ShowPlaceholder(string message)
    {
        PlaceholderText.Text = message;
        PlaceholderText.IsVisible = true;
        ContentRoot.IsVisible = false;
    }

    private void OnKindChanged(object? sender, SearchableComboBoxItem item)
    {
        if (_suppress)
            return;
        RefreshObjectList();
    }

    /// <summary>Re-query the selected kind's objects; keeps the switcher usable when empty.</summary>
    private void RefreshObjectList()
    {
        ClearFieldPane();
        _anchor = null; // rows are rebuilt with new instances; the old anchor is stale
        _suppress = true;
        // Start each kind/map with an unfiltered list: a search typed for the previous
        // kind would otherwise carry over and filter the new kind to nothing, which reads
        // as "this kind is empty" (the "can't see abilities" bug).
        SearchBox.Text = "";
        ObjectList.ItemsSource = null;
        _suppress = false;
        _allRows = new List<ObjectRow>();

        if (_session?.Current is not { } doc)
        {
            ShowPlaceholder("Object editor panel - no map open");
            return;
        }

        var kind = SelectedKind;
        try
        {
            // One icon loader per list load, like the palette: a kind or map switch
            // rebuilds rows with a fresh loader, so a decode still in flight can only
            // ever land on a row the list no longer shows.
            var icons = IconFieldCodes.TryGetValue(kind.Kind, out var iconField)
                ? new ObjectIconLoader(doc, kind.Kind, iconField, _session.GameDir)
                : null;
            _allRows = ObjectListCommand.Execute(doc, kind.Kind, _session.GameDir).Items
                .Select(i => new ObjectRow(
                    i.Rawcode, i.Name is null ? i.Rawcode : $"{i.Name} ({i.Rawcode})")
                { IconLoader = icons })
                .ToList();
        }
        catch (Exception ex)
        {
            EmptyListText.Text = $"Failed to list {kind.Label}: {ex.Message}";
            EmptyListText.IsVisible = true;
            ListCountText.Text = "";
            return;
        }

        ApplyObjectFilter();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress || _session?.Current is null)
            return;
        ApplyObjectFilter();
    }

    /// <summary>
    /// Show the cached object list filtered by the search text - DropdownFilter
    /// matching (case-insensitive substring on display name OR rawcode) with the
    /// best hits ranked first; empty search shows all in file order. The selection
    /// survives filtering while the selected objects still match, so typing
    /// doesn't reload the field pane on every keystroke.
    /// </summary>
    private void ApplyObjectFilter()
    {
        var kind = SelectedKind;
        if (_allRows.Count == 0)
        {
            EmptyListText.Text = $"This map has no {kind.Label} object data.";
            EmptyListText.IsVisible = true;
            ListCountText.Text = $"0 {kind.Label}";
            _suppress = true;
            ObjectList.ItemsSource = null;
            _suppress = false;
            ClearFieldPane();
            return;
        }

        var query = SearchBox.Text?.Trim() ?? "";
        var filtered = query.Length == 0
            ? _allRows
            : _allRows
                .Where(r => DropdownFilter.Matches(query, r.Display, r.Rawcode))
                .OrderBy(r => DropdownFilter.Rank(query, r.Display, r.Rawcode)) // best hits first; stable
                .ToList();
        ListCountText.Text = query.Length == 0
            ? $"{_allRows.Count} {kind.Label}"
            : $"{filtered.Count}/{_allRows.Count} {kind.Label}";

        if (filtered.Count == 0)
        {
            EmptyListText.Text = $"No {kind.Label} match \"{query}\".";
            EmptyListText.IsVisible = true;
            _suppress = true;
            ObjectList.ItemsSource = null;
            _suppress = false;
            ClearFieldPane();
            return;
        }

        EmptyListText.IsVisible = false;
        var previous = SelectedObjects();
        var prevFirst = previous.Count > 0 ? previous[0].Rawcode : null;
        var keep = previous.Select(r => r.Rawcode).ToHashSet(StringComparer.Ordinal);

        _suppress = true;
        ObjectList.ItemsSource = filtered;
        var reselect = filtered.Where(r => keep.Contains(r.Rawcode)).ToList();
        foreach (var row in reselect)
            ObjectList.SelectedItems?.Add(row);
        _suppress = false;

        if (reselect.Count == 0)
            ObjectList.SelectedIndex = 0;   // fires the selection handler → field pane refresh
        else if (reselect[0].Rawcode != prevFirst)
            RefreshFieldPane((FieldList.SelectedItem as FieldRow)?.Code);
    }

    private List<ObjectRow> SelectedObjects() =>
        ObjectList.SelectedItems?.OfType<ObjectRow>().ToList() ?? new List<ObjectRow>();

    private void OnObjectSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress)
            return;
        ApplySelectionToPanes(); // keyboard nav (arrow keys) still routes through here
    }

    /// <summary>
    /// Show the first selected object's merged fields; with a multi-selection that
    /// object is the editing template and Apply targets the whole selection.
    /// </summary>
    private void RefreshFieldPane(string? preserveFieldCode)
    {
        var selected = SelectedObjects();
        if (_session?.Current is not { } doc || selected.Count == 0)
        {
            ClearFieldPane();
            return;
        }

        var first = selected[0];
        NotifyObjectSelected(SelectedKind.Kind, first.Rawcode);
        MultiSelectNote.IsVisible = selected.Count > 1;
        MultiSelectNote.Text = selected.Count > 1
            ? $"{selected.Count} objects selected - edits apply to all (fields shown are {first.Rawcode}'s)"
            : "";

        try
        {
            // The form, not the raw field list: grouped, ordered, applicability-filtered and
            // bounds-annotated from the game's own metadata. Building that here would put map
            // logic in a panel, so it lives in Wc3.Commands and is shared with the CLI and MCP.
            var form = ObjectFormCommand.Execute(doc, SelectedKind.Kind, first.Rawcode, _session.GameDir);
            var baseInfo = form.BaseRawcode is null ? "no base" : $"base {form.BaseRawcode}";
            var hiddenNote = form.HiddenFieldCount > 0
                ? $", {form.HiddenFieldCount} hidden"
                : "";
            SelectedHeader.Text =
                $"{form.Name ?? first.Rawcode} ({first.Rawcode}) - {baseInfo} - "
                + $"{form.FieldCount} field(s) in {form.Groups.Count} group(s){hiddenNote}";

            var rows = BuildFieldRows(form);
            _suppress = true;
            FieldList.ItemsSource = rows;
            _suppress = false;

            // Keep the edited field selected across object switches and post-Apply
            // refreshes; the selection handler reloads EditorBox from the new row.
            // Sub-rows are display-only children and never the preserved selection.
            var keep = string.IsNullOrEmpty(preserveFieldCode)
                ? null
                : rows.FirstOrDefault(r => r.IsSelectable && r.Code == preserveFieldCode);
            FieldList.SelectedItem = keep;
            if (keep is null)
                ResetEditor();

            if (form.Diagnostics.Count > 0)
                StatusText.Text = string.Join("; ", form.Diagnostics);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load {first.Rawcode}: {ex.Message}";
        }
    }

    private void ClearFieldPane()
    {
        _lastNotified = null; // reselecting the same object later re-notifies
        _suppress = true;
        FieldList.ItemsSource = null;
        _suppress = false;
        SelectedHeader.Text = "";
        MultiSelectNote.IsVisible = false;
        MultiSelectNote.Text = "";
        ResetEditor();
        HideModelArea();
    }

    /// <summary>Surface the primary selection once per (kind, rawcode) change - the
    /// field pane refreshes more often than the selection actually moves.</summary>
    private void NotifyObjectSelected(ObjectKind kind, string rawcode)
    {
        if (_lastNotified == (kind, rawcode))
            return;
        _lastNotified = (kind, rawcode);
        ObjectSelected?.Invoke(this, (kind, rawcode));
    }

    // --- reference fields: show referenced objects' names next to their rawcodes ---

    /// <summary>
    /// Grid rows for the merged fields. Object-reference fields get their referenced
    /// objects' names: a single reference inline ("A000 (Naginata Combo)"), a reference
    /// LIST as indented read-only sub-rows ("A000 - Naginata Combo") under the parent
    /// row, one per entry. Every other field is one plain row, exactly as before.
    /// </summary>
    /// <summary>
    /// Flattens the form into list rows: one heading per category, then that category's fields in
    /// the metadata's own order, with reference lists still expanded beneath their field.
    /// </summary>
    private List<FieldRow> BuildFieldRows(ObjectForm form)
    {
        var rows = new List<FieldRow>(form.FieldCount + form.Groups.Count);
        foreach (var group in form.Groups)
        {
            rows.Add(FieldRow.GroupHeader(group.Title, group.Fields.Count));
            rows.AddRange(BuildGroupRows(group.Fields));
        }
        return rows;
    }

    /// <summary>Legal range and target layer, shown beside the value so an edit that the game
    /// would reject, or that would land in the layer the game ignores, is visible before it is
    /// made rather than after the map fails to load.</summary>
    private static string HintFor(FormField f)
    {
        var bounds = f.MinValue is null && f.MaxValue is null
            ? ""
            : $"{f.MinValue ?? "*"}..{f.MaxValue ?? "*"}";
        var layer = f.Layer == ObjectLayer.Skin ? "skin" : "";
        return string.Join("  ", new[] { bounds, layer }.Where(x => x.Length > 0));
    }

    private List<FieldRow> BuildGroupRows(IReadOnlyList<FormField> fields)
    {
        var kind = SelectedKind.Kind;
        var rows = new List<FieldRow>(fields.Count);
        foreach (var ff in fields)
        {
            var f = new MergedField(ff.Code, ff.Name, ff.Value, ff.Source) { Display = ff.Display };
            var hint = HintFor(ff);
            if (!LooksLikeRawcodes(f.Value) || !IsReferenceField(kind, f.Code, out var isList))
            {
                rows.Add(new FieldRow(f, null, hint, ff));
                continue;
            }
            if (isList)
            {
                rows.Add(new FieldRow(f, null, hint, ff));
                bool mapSource = f.Source == "map";
                foreach (var token in f.Value.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    rows.Add(FieldRow.SubRow(ReferenceLabel(token), mapSource));
            }
            else
            {
                var token = f.Value.Trim();
                var display = RefNames().TryGetValue(token, out var name)
                    ? $"{token} ({name})"
                    : f.Display;
                rows.Add(new FieldRow(f, display, hint, ff));
            }
        }
        return rows;
    }

    /// <summary>Cheap value gate before the metadata lookup: only values carrying a
    /// 4-char alphanumeric token (alone or in a comma list) can hold rawcodes, so
    /// ints/reals/paths never cost an option-set scan.</summary>
    private static bool LooksLikeRawcodes(string value) =>
        value.Contains(',')
            ? value.Split(',').Any(t => IsRawcodeShaped(t.Trim()))
            : IsRawcodeShaped(value.Trim());

    private static bool IsRawcodeShaped(string token) =>
        token.Length == 4 && token.All(char.IsLetterOrDigit);

    /// <summary>True when the field's metadata type says its value is (a list of)
    /// object rawcodes. Cached per (kind, bare code); leveled keys ("code:N") resolve
    /// by the bare code. No game data ⇒ never a reference (renders as today).</summary>
    private bool IsReferenceField(ObjectKind kind, string fieldCode, out bool isList)
    {
        int colon = fieldCode.IndexOf(':');
        var code = colon < 0 ? fieldCode : fieldCode[..colon];
        if (!_fieldTypes.TryGetValue((kind, code), out var t))
        {
            try
            {
                var opt = ObjectFieldOptionsCommand.Execute(kind, code, _session?.GameDir);
                t = (opt.Type, opt.IsList);
            }
            catch
            {
                t = ("", false);
            }
            _fieldTypes[(kind, code)] = t;
        }
        isList = t.IsList;
        return IsObjectReferenceType(t.Type);
    }

    /// <summary>Metadata type tokens whose values are object rawcodes: a known object
    /// stem + "Code" (single) or "List" (comma-separated). Non-object lists like
    /// "targetList"/"stringList" stay out - the stem whitelist keeps this conservative.</summary>
    private static bool IsObjectReferenceType(string type)
    {
        var t = type.ToLowerInvariant();
        if (!t.EndsWith("code", StringComparison.Ordinal) && !t.EndsWith("list", StringComparison.Ordinal))
            return false;
        return t[..^4] is "unit" or "abil" or "ability" or "heroability" or "abilityskin"
            or "item" or "tech" or "upgrade" or "buff" or "effect";
    }

    /// <summary>"rawcode - name" when the rawcode resolves to a map object, else the bare rawcode.</summary>
    private string ReferenceLabel(string rawcode) =>
        RefNames().TryGetValue(rawcode, out var name) ? $"{rawcode} - {name}" : rawcode;

    /// <summary>
    /// rawcode → name over every kind's map objects, built once per map from
    /// <see cref="ObjectListCommand"/> (first hit across kinds wins) and dropped after
    /// edits, which can rename. One list pass per kind keeps resolving 500+ references
    /// out of the per-rawcode command path; base-game-only rawcodes stay unresolved
    /// and render bare.
    /// </summary>
    private Dictionary<string, string> RefNames()
    {
        if (_refNames is not null)
            return _refNames;
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_session?.Current is { } doc)
        {
            foreach (var kind in Kinds)
            {
                try
                {
                    foreach (var item in ObjectListCommand.Execute(doc, kind.Kind, _session.GameDir).Items)
                        if (item.Name is { Length: > 0 } name)
                            names.TryAdd(item.Rawcode, name);
                }
                catch
                {
                    // A kind that fails to enumerate just resolves no names.
                }
            }
        }
        return _refNames = names;
    }

    // --- right-click → "Show dependencies" ---

    /// <summary>
    /// Owns the object list's mouse selection so plain clicks SWITCH the shown
    /// object (World-Editor style) instead of piling up a multi-selection.
    /// Avalonia's <c>SelectionMode="Multiple"</c> otherwise accumulates on every
    /// plain click, and the detail pane + the Dependencies/Port seam only ever
    /// follow the FIRST selected object - so after one click the panel appeared
    /// frozen and porting targeted the wrong unit. This tunnel handler runs before
    /// the ListBox's own selection logic and marks the event handled, giving:
    ///   • plain left-click  → select only that row (switch)
    ///   • Ctrl+left-click   → toggle that row in/out of the selection
    ///   • Shift+left-click  → range from the anchor to that row
    ///   • double-click      → open the model preview (was DoubleTapped)
    ///   • right-click       → retarget to the row under the pointer for the menu
    /// Keyboard navigation stays with the ListBox's default (arrow keys) handling.
    /// </summary>
    private void OnObjectListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(ObjectList).Properties;
        var row = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)
            ?.DataContext as ObjectRow;

        if (props.IsRightButtonPressed)
        {
            // Explorer-style: move the selection to the clicked row unless it is
            // already part of the current selection (so "Show dependencies" acts
            // on what the user clicked). Left to the default menu open otherwise.
            if (row is not null && !SelectedObjects().Contains(row))
            {
                _suppress = true;
                ObjectList.SelectedItems?.Clear();
                ObjectList.SelectedItem = row;
                _suppress = false;
                _anchor = row;
                ApplySelectionToPanes();
            }
            return;
        }

        if (!props.IsLeftButtonPressed || row is null)
            return; // clicks on empty space fall through to the default (no-op)

        // We own the click (marked handled below), so the list won't get focus from the
        // ListBoxItem's own press handling - give it focus explicitly so arrow keys work.
        ObjectList.Focus();

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        // Only a PLAIN double-click opens the preview; a fast Ctrl/Shift double is just
        // two toggles/ranges and must not collapse the multi-selection.
        bool preview = e.ClickCount >= 2 && !ctrl && !shift;

        // Mutate selection silently, then refresh the field pane exactly once - each
        // SelectedItems change would otherwise re-run ObjectGetCommand (N times for a range).
        _suppress = true;
        if (ctrl)
        {
            if (SelectedObjects().Contains(row))
                ObjectList.SelectedItems?.Remove(row);
            else
                ObjectList.SelectedItems?.Add(row);
            _anchor = row;
        }
        else if (shift && _anchor is not null)
        {
            SelectRange(_anchor, row);
        }
        else
        {
            SelectSingle(row);
            _anchor = row;
        }
        _suppress = false;
        ApplySelectionToPanes();
        e.Handled = true; // suppress Avalonia's Multiple-mode accumulate

        if (preview)
            ShowSelectedObjectModel();
    }

    /// <summary>Reflect the current selection in the detail panes: drop the per-object
    /// model preview and re-merge the (first) selected object's fields, keeping the
    /// edited field selected. Called once per selection gesture (mouse or keyboard).</summary>
    private void ApplySelectionToPanes()
    {
        HideModelArea();
        RefreshFieldPane((FieldList.SelectedItem as FieldRow)?.Code);
    }

    /// <summary>Make <paramref name="row"/> the only selected object.</summary>
    private void SelectSingle(ObjectRow row)
    {
        var current = SelectedObjects();
        if (current.Count == 1 && ReferenceEquals(current[0], row))
            return; // already the sole selection - don't churn the field pane
        ObjectList.SelectedItems?.Clear();
        ObjectList.SelectedItem = row;
    }

    /// <summary>Select the inclusive range between two rows in the visible (filtered) list.</summary>
    private void SelectRange(ObjectRow anchor, ObjectRow target)
    {
        if (ObjectList.ItemsSource is not IEnumerable<ObjectRow> src)
        {
            SelectSingle(target);
            return;
        }
        var rows = src.ToList();
        int a = rows.IndexOf(anchor), b = rows.IndexOf(target);
        if (a < 0 || b < 0)
        {
            SelectSingle(target);
            return;
        }
        if (a > b)
            (a, b) = (b, a);
        ObjectList.SelectedItems?.Clear();
        for (int i = a; i <= b; i++)
            ObjectList.SelectedItems?.Add(rows[i]);
    }

    private void OnObjectContextMenuOpening(object? sender, CancelEventArgs e)
    {
        var target = SelectedObjects().FirstOrDefault();
        ShowDependenciesItem.IsEnabled = target is not null;
        ShowDependenciesItem.Header = target is null
            ? "Show dependencies"
            : $"Show dependencies of {target.Rawcode}";
    }

    private void OnShowDependenciesClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedObjects().FirstOrDefault() is { } target)
            DependenciesRequested?.Invoke(this, (SelectedKind.Kind, target.Rawcode));
    }

    // --- model preview (double-click an object) ---

    private void HideModelArea()
    {
        ModelArea.IsVisible = false;
        ModelText.Text = "";
        ExtractModelButton.IsEnabled = false;
        _modelEntryName = null;
        HidePreview();
    }

    /// <summary>Drop the rendered preview and invalidate any render still in flight.</summary>
    private void HidePreview()
    {
        _previewGeneration++;
        _previewModel = null;
        _renderQueued = false;
        _dragging = false;
        PreviewBorder.IsVisible = false;
        PreviewHint.IsVisible = false;
        var old = PreviewImage.Source as Bitmap;
        PreviewImage.Source = null;
        old?.Dispose();
    }

    /// <summary>
    /// Double-click: resolve the (first) selected object's model file from its
    /// merged fields. Map-imported models render as a rotatable preview; base-game
    /// models (not in the map) render from CASC when a WC3 install is available,
    /// otherwise only their path shows.
    /// </summary>
    private void ShowSelectedObjectModel()
    {
        if (_session?.Current is not { } doc)
            return;
        var selected = SelectedObjects();
        if (selected.Count == 0)
            return;
        var rawcode = selected[0].Rawcode;

        string? modelPath;
        try
        {
            var fields = ObjectGetCommand.Execute(doc, SelectedKind.Kind, rawcode, _session.GameDir).Fields;
            // Merged fields first (a map delta always wins); untouched base objects keep
            // their art in Reforged skin profiles, resolved through the command layer.
            modelPath = FindModelPath(SelectedKind.Kind, fields)
                ?? RenderModelCommand.BaseModelPath(SelectedKind.Kind, rawcode, _session.GameDir);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to resolve model for {rawcode}: {ex.Message}";
            return;
        }

        HideModelArea(); // reset stale extract/preview state from the last object
        ModelArea.IsVisible = true;
        if (modelPath is null)
        {
            ModelText.Text = $"{rawcode}: no model field found - nothing to preview.";
            return;
        }

        var entry = FindMapEntry(doc, modelPath);
        if (entry?.FileName is not null)
        {
            _modelEntryName = entry.FileName;
            ModelText.Text = $"{rawcode} model: {entry.FileName} - in map ({entry.RawBytes.Length:N0} bytes)";
            ExtractModelButton.IsEnabled = true;
            StartPreview(doc, entry.FileName);
        }
        else
        {
            // Not imported - try the base game (CASC). Preparation failures append a
            // "Preview unavailable" note, leaving the path message as the fallback.
            ModelText.Text = $"{rawcode} - base game model: {modelPath}";
            StartPreview(doc, modelPath);
        }
    }

    /// <summary>
    /// Parse the model, resolve its textures and render the first frame, all off
    /// the UI thread; the prepared model is cached so drag re-renders only
    /// rasterize. Resolution goes through the command layer's map-then-CASC
    /// fallback, so base-game models render when an install is available (the
    /// first CASC open can take seconds - also off the UI thread). The generation
    /// stamp drops results that land after the preview target changed (another
    /// double-click, selection change, map close).
    /// </summary>
    private void StartPreview(MapDocument doc, string internalName)
    {
        _previewYaw = ModelRenderer.DefaultYawDegrees;
        _previewPitch = ModelRenderer.DefaultPitchDegrees;
        _previewZoom = ModelRenderer.DefaultZoom;
        int gen = ++_previewGeneration;
        float yaw = _previewYaw, pitch = _previewPitch, zoom = _previewZoom;
        string? gameDir = _session?.GameDir;
        Task.Run(() =>
        {
            try
            {
                var prepared = RenderModelCommand.PrepareWithFallback(doc, internalName, gameDir);
                var png = prepared.RenderPng(PreviewSizePx, PreviewSizePx, yaw, pitch, zoom);
                Dispatcher.UIThread.Post(() =>
                {
                    if (gen != _previewGeneration)
                        return;
                    _previewModel = prepared;
                    PreviewBorder.IsVisible = true;
                    PreviewHint.IsVisible = true;
                    SetPreviewBitmap(png);
                });
            }
            catch (Exception ex) // unparsable model, no geometry, …
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (gen != _previewGeneration)
                        return;
                    ModelText.Text += $"\nPreview unavailable: {ex.Message}";
                });
            }
        });
    }

    /// <summary>
    /// Re-render at the current angles with at most one render in flight:
    /// requests arriving mid-render collapse into a single trailing render at
    /// whatever the angles are by then, so releasing a drag always settles on
    /// the final orientation without a backlog of stale frames.
    /// </summary>
    private void RequestPreviewRender()
    {
        if (_previewModel is not { } prepared)
            return;
        if (_renderInFlight)
        {
            _renderQueued = true;
            return;
        }
        _renderInFlight = true;
        int gen = _previewGeneration;
        float yaw = _previewYaw, pitch = _previewPitch, zoom = _previewZoom;
        Task.Run(() =>
        {
            byte[]? png = null;
            string? error = null;
            try
            {
                png = prepared.RenderPng(PreviewSizePx, PreviewSizePx, yaw, pitch, zoom);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            Dispatcher.UIThread.Post(() =>
            {
                _renderInFlight = false;
                if (gen != _previewGeneration)
                {
                    _renderQueued = false;
                    return;
                }
                if (png is not null)
                    SetPreviewBitmap(png);
                else
                    StatusText.Text = $"Preview render failed: {error}";
                if (_renderQueued)
                {
                    _renderQueued = false;
                    RequestPreviewRender();
                }
            });
        });
    }

    /// <summary>Swap the preview image, disposing the bitmap it replaces.</summary>
    private void SetPreviewBitmap(byte[] png)
    {
        using var ms = new MemoryStream(png);
        var bmp = new Bitmap(ms);
        var old = PreviewImage.Source as Bitmap;
        PreviewImage.Source = bmp;
        old?.Dispose();
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_previewModel is null)
            return;
        _dragging = true;
        _dragLast = e.GetPosition(PreviewBorder);
        e.Pointer.Capture(PreviewBorder);
    }

    /// <summary>Orbit so the model follows the drag: right spins it right, down tilts its top toward you.</summary>
    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
            return;
        var pos = e.GetPosition(PreviewBorder);
        _previewYaw = (_previewYaw - (float)(pos.X - _dragLast.X) * DegreesPerPixel) % 360f;
        _previewPitch = Math.Clamp(
            _previewPitch + (float)(pos.Y - _dragLast.Y) * DegreesPerPixel, -89f, 89f);
        _dragLast = pos;
        RequestPreviewRender();
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
    }

    /// <summary>Scroll to zoom: wheel up magnifies, wheel down pulls back (clamped).</summary>
    private void OnPreviewWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_previewModel is null)
            return;
        float factor = MathF.Pow(ZoomPerWheelNotch, (float)e.Delta.Y);
        _previewZoom = Math.Clamp(_previewZoom * factor, MinZoom, MaxZoom);
        e.Handled = true; // don't let the wheel scroll the list underneath
        RequestPreviewRender();
    }

    /// <summary>
    /// The kind's dedicated model field first (umdl/dfil/bfil/ifil); otherwise the
    /// first field whose value looks like a model path.
    /// </summary>
    private static string? FindModelPath(ObjectKind kind, IReadOnlyList<MergedField> fields)
    {
        if (ModelFieldCodes.TryGetValue(kind, out var code))
        {
            var hit = fields.FirstOrDefault(f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(hit?.Value))
                return hit.Value.Trim();
        }
        return fields.Select(f => f.Value.Trim()).FirstOrDefault(v =>
            v.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)
            || v.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves the model path against the map, reusing the shared command-layer
    /// resolver so the preview finds exactly what the renderer does - including the
    /// .mdx/.mdl swap (maps reference a model as ".mdl" but store the binary as ".mdx").
    /// </summary>
    private static MapFileEntry? FindMapEntry(MapDocument doc, string modelPath) =>
        RenderModelCommand.FindModelEntry(doc, modelPath);

    /// <summary>Write the double-clicked object's map-imported model into a picked folder.</summary>
    private async void OnExtractModelClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc || _modelEntryName is null)
            return;
        if (doc.GetFile(_modelEntryName) is not { } entry)
        {
            StatusText.Text = $"{_modelEntryName} is no longer in the map.";
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
            return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Extract model to folder",
            AllowMultiple = false,
        });
        if (folders.Count != 1 || folders[0].TryGetLocalPath() is not { } dir)
            return;

        // Keep the base filename; internal paths use '\' so take the last segment.
        var baseName = SanitizeFileName(_modelEntryName.Split('\\', '/').Last());
        if (baseName.Length == 0)
            baseName = "model.bin";
        var dest = Path.Combine(dir, baseName);
        try
        {
            await Task.Run(() => File.WriteAllBytes(dest, entry.RawBytes));
            StatusText.Text = $"Extracted {_modelEntryName} → {dest} ({entry.RawBytes.Length:N0} bytes)";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Extract failed: {ex.Message}";
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.TrimEnd(' ', '.');   // Windows rejects trailing dots/spaces
    }

    private void OnFieldSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress)
            return;
        // Sub-rows (a reference list's expanded entries) are display-only; their
        // containers are disabled, but guard anyway so they never reach the editor.
        // A category heading is not a field. Its containers are disabled so a click cannot
        // select one, but keyboard navigation can, and it would arrive here as "Art ()".
        if (FieldList.SelectedItem is FieldRow { IsSelectable: true } row)
        {
            FieldEditLabel.Text = $"{row.Name} ({row.Code})";
            ShowValueContext(row);
            ConfigureEditor(row);
            UpdateApplyState();
        }
        else
        {
            ResetEditor();
        }
    }

    /// <summary>Pick the editor control from the field's metadata: object-reference LISTS
    /// (a unit's abilities, an item drop set, …) get the add/remove/reorder builder;
    /// other enumerated fields get a searchable dropdown (single value) or a checklist
    /// (list types); everything else - ints, reals, strings, paths, or fields with no
    /// derivable option set - stays free text. The current value is always kept
    /// selectable so out-of-range data is never silently lost.</summary>
    private void ConfigureEditor(FieldRow row)
    {
        ObjectFieldOptionsResult opt;
        try
        {
            opt = ObjectFieldOptionsCommand.Execute(SelectedKind.Kind, row.Code, _session?.GameDir);
        }
        catch
        {
            opt = new ObjectFieldOptionsResult("", false, Array.Empty<EnumOption>());
        }

        bool freeText = opt.Options.Count == 0
            || opt.Options.Count > MaxEditorOptions
            || IsFreeTextType(opt.Type);
        // Metadata-gated (not value-gated) so an EMPTY reference list still gets the
        // builder - that's exactly when the user wants to add the first entry.
        if (opt.IsList && IsObjectReferenceType(opt.Type))
            ShowRefListEditor(row, opt.Type);
        else if (!freeText && opt.IsList)
            ShowMultiEditor(row, opt.Options);
        else if (!freeText)
            ShowComboEditor(row, opt.Options);
        else
            ShowTextEditor(row);

        UpdateEditNote(opt);
        // The grid shows resolved text for wts references; the editor holds the raw token,
        // so flag it rather than let the user think the box "lost" the readable value.
        if (row.Value.Contains("TRIGSTR_", StringComparison.Ordinal))
            EditNote.Text = "String-table reference (war3map.wts) — the grid shows the resolved "
                + "text; editing here replaces the reference with a literal value.";
    }

    /// <summary>Metadata types edited as free text; anything else with a small option set
    /// is treated as an enumeration.</summary>
    private static bool IsFreeTextType(string type) => type.ToLowerInvariant() switch
    {
        "int" or "real" or "unreal" or "string" => true,
        _ => false,
    };

    private void ShowTextEditor(FieldRow row)
    {
        _editorMode = EditorMode.Text;
        EditorBox.Text = row.Value;

        // The metadata marks long text with stringext, and a tooltip or a description is exactly
        // that. Editing several lines of it through a one-line box, where the newlines are present
        // but invisible and Enter does nothing, is the worst affordance in this panel.
        bool longText = row.Form?.MultiLine == true
            || (row.Value?.Contains('|') == true)   // WC3 uses |n as its line break in tooltips
            || (row.Value?.Length ?? 0) > 120;
        EditorBox.AcceptsReturn = longText;
        EditorBox.TextWrapping = longText
            ? Avalonia.Media.TextWrapping.Wrap
            : Avalonia.Media.TextWrapping.NoWrap;
        EditorBox.MinHeight = longText ? 96 : 0;
        EditorBox.MaxHeight = longText ? 220 : double.PositiveInfinity;

        EditorBox.IsVisible = true;
        EditorCombo.IsVisible = false;
        EditorMultiHost.IsVisible = false;
        RefListHost.IsVisible = false;
    }

    private void ShowComboEditor(FieldRow row, IReadOnlyList<EnumOption> options)
    {
        _editorMode = EditorMode.Combo;
        // The id is the value the game stores. The label is what the World Editor calls it, from
        // UnitEditorData.txt. Showing the token where a name exists makes a closed set unreadable.
        var items = options
            .Select(o => new SearchableComboBoxItem(o.Value, o.Label))
            .ToList();
        var current = (row.Value ?? "").Trim();
        if (current.Length > 0
            && !items.Any(i => string.Equals(i.Id, current, StringComparison.OrdinalIgnoreCase)))
            // The stored value is not one the game defines. Keep it selectable and say so, rather
            // than silently dropping data the map already relies on.
            items.Insert(0, new SearchableComboBoxItem(current, $"{current}  (not a listed value)"));
        EditorCombo.SetItems(items, selectId: current.Length > 0 ? current : null);
        EditorBox.IsVisible = false;
        EditorCombo.IsVisible = true;
        EditorMultiHost.IsVisible = false;
        RefListHost.IsVisible = false;
    }

    private void ShowMultiEditor(FieldRow row, IReadOnlyList<EnumOption> options)
    {
        _editorMode = EditorMode.Multi;
        var selected = new HashSet<string>(
            (row.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

        // The listed values, plus any the field already holds that the game does not list. Keeping
        // the strays visible and selected is what stops an Apply from dropping data the map relies
        // on.
        var tokens = options.Select(o => o.Value).ToList();
        foreach (var s in selected)
            if (!tokens.Contains(s, StringComparer.OrdinalIgnoreCase))
                tokens.Add(s);
        _editorMultiTokens = tokens;

        // Deliberately the raw values here, NOT the display names. CurrentEditorValue rebuilds the
        // field by matching SelectedItems against these exact strings, so showing labels would make
        // every Apply write an empty list. The single-value combo is where names are safe, because
        // it carries the id separately from the label.
        EditorMulti.ItemsSource = tokens;
        EditorMulti.SelectedItems?.Clear();
        foreach (var t in tokens)
            if (selected.Contains(t))
                EditorMulti.SelectedItems?.Add(t);

        EditorBox.IsVisible = false;
        EditorCombo.IsVisible = false;
        EditorMultiHost.IsVisible = true;
        RefListHost.IsVisible = false;
    }

    // --- object-reference LIST builder (EditorMode.RefList) ---

    /// <summary>
    /// Editable builder for object-reference LIST fields (e.g. a unit's abilities
    /// 'uhab' = "A000,A001"): the current entries in order ("rawcode - name" via
    /// <see cref="ReferenceLabel"/>), an Add picker over the referenced kind's map
    /// objects, Remove and Up/Down reorder. <see cref="CurrentEditorValue"/> joins
    /// the rawcodes comma-separated in list order, so the normal Apply path (bulk
    /// multi-select write via ObjectSetCommand) works unchanged. The read-only
    /// sub-row expansion in the grid above stays as the at-a-glance display.
    /// </summary>
    private void ShowRefListEditor(FieldRow row, string type)
    {
        _editorMode = EditorMode.RefList;
        _refListTokens = (row.Value ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        RefreshRefListRows(selectIndex: -1);
        RefListAddCombo.Watermark = "Search object to add…";
        // No auto-selection: adding is a deliberate pick, never a default first item.
        RefListAddCombo.SetItems(RefListCandidates(type), selectId: null,
            selectFirstWhenNoMatch: false);
        EditorBox.IsVisible = false;
        EditorCombo.IsVisible = false;
        EditorMultiHost.IsVisible = false;
        RefListHost.IsVisible = true;
    }

    /// <summary>
    /// Add-picker choices: the map's objects of the referenced kind, resolved from
    /// the field's metadata type stem (abilList → Abilities, …). Enumeration failure
    /// just yields an empty picker - the existing entries are never affected.
    /// </summary>
    private List<SearchableComboBoxItem> RefListCandidates(string type)
    {
        if (_session?.Current is not { } doc)
            return new List<SearchableComboBoxItem>();
        try
        {
            return ObjectListCommand.Execute(doc, RefTargetKind(type), _session.GameDir).Items
                .Select(i => new SearchableComboBoxItem(i.Name ?? i.Rawcode, i.Rawcode))
                .ToList();
        }
        catch
        {
            return new List<SearchableComboBoxItem>();
        }
    }

    /// <summary>The kind a reference type's values name, from the stems
    /// <see cref="IsObjectReferenceType"/> accepts. Ambiguous stems ("tech" can name
    /// units or upgrades) fall back to the current kind.</summary>
    private ObjectKind RefTargetKind(string type)
    {
        var t = type.ToLowerInvariant();
        var stem = t.Length >= 4 ? t[..^4] : t; // strip the "code"/"list" suffix
        return stem switch
        {
            "unit" => ObjectKind.Unit,
            "abil" or "ability" or "heroability" or "abilityskin" => ObjectKind.Ability,
            "item" => ObjectKind.Item,
            "upgrade" => ObjectKind.Upgrade,
            "buff" or "effect" => ObjectKind.Buff,
            _ => SelectedKind.Kind,
        };
    }

    /// <summary>One builder row: index + "rawcode - name" label. The index keeps
    /// duplicate rawcodes distinct, so selection and reorder stay unambiguous.</summary>
    private sealed record RefListEntry(int Index, string Display)
    {
        public override string ToString() => Display;
    }

    /// <summary>Rebuild the builder's rows from the token list and re-select
    /// <paramref name="selectIndex"/> (clamped; -1 = no selection).</summary>
    private void RefreshRefListRows(int selectIndex)
    {
        RefListBox.ItemsSource = _refListTokens
            .Select((t, i) => new RefListEntry(i, ReferenceLabel(t)))
            .ToList();
        RefListBox.SelectedIndex = Math.Min(selectIndex, _refListTokens.Count - 1);
        UpdateRefListButtons();
    }

    /// <summary>Append the picked candidate (duplicates allowed - list order matters).</summary>
    private void OnRefListAddClick(object? sender, RoutedEventArgs e)
    {
        if (_editorMode != EditorMode.RefList)
            return;
        if (RefListAddCombo.SelectedId is not { Length: > 0 } rawcode)
        {
            StatusText.Text = "Pick an object to add first.";
            return;
        }
        _refListTokens.Add(rawcode);
        RefreshRefListRows(_refListTokens.Count - 1);
    }

    private void OnRefListRemoveClick(object? sender, RoutedEventArgs e)
    {
        int i = RefListBox.SelectedIndex;
        if (_editorMode != EditorMode.RefList || i < 0 || i >= _refListTokens.Count)
            return;
        _refListTokens.RemoveAt(i);
        RefreshRefListRows(i); // clamps to the new last entry when the tail was removed
    }

    private void OnRefListUpClick(object? sender, RoutedEventArgs e) => MoveRefListEntry(-1);

    private void OnRefListDownClick(object? sender, RoutedEventArgs e) => MoveRefListEntry(+1);

    /// <summary>Swap the selected entry with its neighbour, keeping it selected.</summary>
    private void MoveRefListEntry(int delta)
    {
        int i = RefListBox.SelectedIndex, j = i + delta;
        if (_editorMode != EditorMode.RefList
            || i < 0 || i >= _refListTokens.Count || j < 0 || j >= _refListTokens.Count)
            return;
        (_refListTokens[i], _refListTokens[j]) = (_refListTokens[j], _refListTokens[i]);
        RefreshRefListRows(j);
    }

    private void OnRefListSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        UpdateRefListButtons();

    /// <summary>Remove/Up/Down act on the selected entry (Add is always live).</summary>
    private void UpdateRefListButtons()
    {
        int i = RefListBox.SelectedIndex, n = _refListTokens.Count;
        RefListRemoveButton.IsEnabled = i >= 0 && i < n;
        RefListUpButton.IsEnabled = i > 0 && i < n;
        RefListDownButton.IsEnabled = i >= 0 && i < n - 1;
    }

    /// <summary>The value to write, read from whichever editor is currently shown.</summary>
    private string CurrentEditorValue() => _editorMode switch
    {
        EditorMode.Combo => EditorCombo.SelectedId ?? "",
        EditorMode.Multi => string.Join(",",
            _editorMultiTokens.Where(t => EditorMulti.SelectedItems?.Contains(t) == true)),
        EditorMode.RefList => string.Join(",", _refListTokens),
        _ => EditorBox.Text ?? "",
    };

    private void UpdateEditNote(ObjectFieldOptionsResult opt)
    {
        EditNote.Text = _editorMode switch
        {
            EditorMode.Combo => $"Enumerated field (type '{opt.Type}') — pick a value the base game already uses.",
            EditorMode.Multi => $"List field (type '{opt.Type}') — check tokens to include; saved comma-separated.",
            EditorMode.RefList => $"Object-reference list (type '{opt.Type}') — add, remove and reorder entries; "
                + "Apply saves the rawcodes comma-separated in list order.",
            _ when opt.Diagnostic is { } d => $"Free-text field. ({d})",
            _ => "Free-text field; leveled fields (code:N) edit that level/variation only.",
        };
    }

    private void ResetEditor()
    {
        FieldEditLabel.Text = "Select a field to edit";
        _editorMode = EditorMode.Text;
        _editorMultiTokens = Array.Empty<string>();
        _refListTokens = new List<string>();
        EditorBox.Text = "";
        EditorBox.IsVisible = true;
        EditorCombo.IsVisible = false;
        EditorMultiHost.IsVisible = false;
        RefListHost.IsVisible = false;
        EditNote.Text = "";
        UpdateApplyState();
    }

    /// <summary>Apply is available for every kind - the command layer routes the
    /// three modification shapes (Simple/Level/Variation) behind one call.</summary>
    private void UpdateApplyState()
    {
        ApplyButton.IsEnabled = _session?.Current is not null
            && FieldList.SelectedItem is FieldRow { IsSubRow: false }
            && SelectedObjects().Count > 0;
    }

    /// <summary>Bulk edit: write the editor value to the selected field on every selected object.</summary>
    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc)
        {
            StatusText.Text = "No map open.";
            return;
        }
        if (FieldList.SelectedItem is not FieldRow { IsSelectable: true } row)
        {
            StatusText.Text = "Select a field first.";
            return;
        }
        var targets = SelectedObjects();
        if (targets.Count == 0)
        {
            StatusText.Text = "Select at least one object.";
            return;
        }

        var value = CurrentEditorValue();

        // The metadata states the legal range and whether a blank is allowed, and until now the
        // panel showed those and enforced nothing, so a value the game rejects could be written and
        // would only surface as a map that misbehaves. The rule lives in the command layer, so ask
        // it rather than re-deriving it here.
        if (row.Form?.Validate(value) is { } problem)
        {
            StatusText.Text = $"not applied. {problem}";
            return;
        }

        int applied = 0;
        var warnings = new List<string>();
        var problems = new List<string>();
        foreach (var target in targets)
        {
            try
            {
                // Remember what the field held before the FIRST change to it, so Revert can
                // return to the value the map was opened with rather than to the last keystroke.
                var key = (SelectedKind.Kind, target.Rawcode, row.Code);
                if (!_originalValues.ContainsKey(key))
                    _originalValues[key] = StoredValue(doc, target.Rawcode, row.Code) ?? "";

                var result = ObjectSetCommand.Execute(doc, SelectedKind.Kind, target.Rawcode, row.Code, value);
                if (result.Ok)
                {
                    applied++;
                    if (result.Warning is not null && !warnings.Contains(result.Warning))
                        warnings.Add(result.Warning);
                }
                else
                {
                    problems.Add($"{target.Rawcode}: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                problems.Add($"{target.Rawcode}: {ex.Message}");
            }
        }

        _unsavedEdits += applied;
        if (applied > 0)
            _refNames = null; // an edit can rename an object other rows reference

        // Re-merge so the grid shows the new value with its gold map-source
        // highlight; then report, so diagnostics don't clobber the summary.
        RefreshFieldPane(row.Code);

        var summary = $"Applied {row.Code}={value} to {applied}/{targets.Count} object(s)"
            + (applied > 0 ? $" - {_unsavedEdits} unsaved edit(s)" : "");
        if (warnings.Count > 0)
            summary += $" - {string.Join("; ", warnings)}";
        if (problems.Count > 0)
            summary += $" - {string.Join("; ", problems)}";
        StatusText.Text = summary;
    }

    /// <summary>
    /// Create a new custom object of the current kind derived from the first
    /// selected object (full inheritance, zero field mods), then refresh the
    /// list and select the newcomer so editing can start immediately.
    /// </summary>
    private void OnNewObjectClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc)
        {
            StatusText.Text = "No map open.";
            return;
        }
        var selected = SelectedObjects();
        if (selected.Count == 0)
        {
            StatusText.Text = "Select the object to base the new one on first.";
            return;
        }

        ObjectNewCommand.ObjectNewResult result;
        try
        {
            result = ObjectNewCommand.Execute(doc, SelectedKind.Kind, selected[0].Rawcode);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Create failed: {ex.Message}";
            return;
        }
        if (!result.Ok || result.NewRawcode is null)
        {
            StatusText.Text = result.Message;
            return;
        }

        _unsavedEdits++;
        _refNames = null; // the newcomer is now a resolvable reference target
        // Clear the search so the fresh object is visible, then hand selection to it.
        _suppress = true;
        SearchBox.Text = "";
        _suppress = false;
        RefreshObjectList();
        if (_allRows.FirstOrDefault(r => r.Rawcode == result.NewRawcode) is { } row)
        {
            ObjectList.SelectedItems?.Clear();
            ObjectList.SelectedItem = row;
            ObjectList.ScrollIntoView(row);
        }
        StatusText.Text = $"{result.Message} - {_unsavedEdits} unsaved edit(s)";
    }

    private void OnSaveEditsClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc)
        {
            StatusText.Text = "No map open.";
            return;
        }
        if (_session.MapPath is not { } mapPath)
        {
            StatusText.Text = "The map has no file path to save next to.";
            return;
        }
        if (_unsavedEdits == 0)
        {
            StatusText.Text = "No edits applied - nothing to save.";
            return;
        }

        // Never overwrite the original map: save next to it as <name>.edited<ext>.
        var editedPath = EditedPath(mapPath);
        try
        {
            doc.Save(editedPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Saving failed: {ex.Message}";
            return;
        }

        StatusText.Text = $"Saved {editedPath} - {_unsavedEdits} edit(s) written.";
        _unsavedEdits = 0;
    }

    private static string EditedPath(string mapPath)
    {
        var dir = Path.GetDirectoryName(mapPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(mapPath);
        var ext = Path.GetExtension(mapPath);
        return Path.Combine(dir, $"{stem}.edited{ext}");
    }

    /// <summary>Type-switcher entry; carried as the combo item's payload.</summary>
    private sealed record KindOption(ObjectKind Kind, string Label);

    /// <summary>
    /// Object list row: "Name (rawcode)" (or the bare rawcode when nameless) plus a
    /// lazily decoded interface icon. Notifies for <see cref="Icon"/>, which lands
    /// after an off-thread resolve + decode, exactly like the palette's tiles.
    /// </summary>
    public sealed class ObjectRow : INotifyPropertyChanged
    {
        public ObjectRow(string rawcode, string display)
        {
            Rawcode = rawcode;
            Display = display;
        }

        public string Rawcode { get; }
        public string Display { get; }
        /// <summary>Loader shared by every row of one list load. Null when the kind has
        /// no icon art (destructables, doodads) - those rows never request anything.</summary>
        internal ObjectIconLoader? IconLoader { get; init; }

        private Bitmap? _icon;
        private bool _iconRequested;

        /// <summary>
        /// The row's decoded icon, or null (pending or absent - the template's icon
        /// box just stays empty, keeping the text aligned). Lazy like the palette:
        /// the first read, which happens when the list realizes the row and binds it,
        /// kicks off an off-thread resolve + decode and the binding refreshes via
        /// PropertyChanged when it lands. Never blocks the UI thread, never throws.
        /// A kind or map switch can't show stale icons - it rebuilds rows and loader
        /// wholesale, so a late decode only ever reaches an unbound row.
        /// </summary>
        public Bitmap? Icon
        {
            get
            {
                if (!_iconRequested)
                {
                    _iconRequested = true;
                    if (IconLoader is not null)
                    {
                        if (IconLoader.TryGetCached(Rawcode, out var cached))
                            _icon = cached;
                        else
                            IconLoader.Load(Rawcode, bmp =>
                            {
                                if (bmp is null) return; // graceful fallback: empty box
                                _icon = bmp;
                                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
                            });
                    }
                }
                return _icon;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Resolves and decodes object-list row icons off the UI thread, mirroring the
    /// palette's loader (request coalescing, caching, thread marshalling) with the
    /// command layer doing all real work. Units resolve through the unit palette,
    /// whose per-entry art already folds the map's 'uico' delta over the Reforged
    /// skin-profile art ('uico' is Profile-backed, so the SLK-backed merged fields
    /// never carry an icon the map didn't override). Every other kind reads its
    /// merged icon field via <see cref="ObjectGetCommand"/>, where a map delta wins
    /// over base data. Decoding is <see cref="PaletteCommand.IconPng(MapDocument,
    /// string, string?)"/> (map imports first, then base-game CASC, BLP/DDS/TGA
    /// sniffed). The per-rawcode caches are touched on the UI thread only, like the
    /// palette's. The per-path bitmap cache is consulted inside the off-thread
    /// resolve (the path is unknown until then), so that one is concurrent. One
    /// instance per list load.
    /// </summary>
    internal sealed class ObjectIconLoader
    {
        private readonly MapDocument _doc;
        private readonly ObjectKind _kind;
        private readonly string _iconField;
        private readonly string? _gameDir;
        private readonly Dictionary<string, Bitmap?> _done = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Action<Bitmap?>>> _pending = new(StringComparer.Ordinal);
        /// <summary>Decoded art shared across rows - many objects reuse one icon file.</summary>
        private readonly ConcurrentDictionary<string, Bitmap?> _byPath = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>rawcode to icon art from the unit palette, built once on first use
        /// (off-thread, and Lazy coalesces concurrent builders). The first build per
        /// install can take seconds (CASC open), exactly like the palette tab's load.</summary>
        private readonly Lazy<Dictionary<string, string>> _unitIcons;

        public ObjectIconLoader(MapDocument doc, ObjectKind kind, string iconField, string? gameDir)
        {
            _doc = doc;
            _kind = kind;
            _iconField = iconField;
            _gameDir = gameDir;
            _unitIcons = new(BuildUnitIcons);
        }

        /// <summary>A completed lookup for the rawcode (null bitmap = no or undecodable icon).</summary>
        public bool TryGetCached(string rawcode, out Bitmap? bitmap) =>
            _done.TryGetValue(rawcode, out bitmap);

        /// <summary>Requests an off-thread resolve + decode. <paramref name="onLoaded"/>
        /// runs later on the UI thread (null = no icon). Concurrent requests for the
        /// same rawcode share one lookup.</summary>
        public void Load(string rawcode, Action<Bitmap?> onLoaded)
        {
            if (_done.TryGetValue(rawcode, out var hit)) { onLoaded(hit); return; }
            if (_pending.TryGetValue(rawcode, out var waiters)) { waiters.Add(onLoaded); return; }
            _pending[rawcode] = new List<Action<Bitmap?>> { onLoaded };
            Task.Run(() =>
            {
                Bitmap? bmp = null;
                try
                {
                    if (ResolveIconPath(rawcode) is { } path)
                        bmp = DecodeShared(path);
                }
                catch { /* no icon - the row keeps its empty box */ }
                Dispatcher.UIThread.Post(() =>
                {
                    _done[rawcode] = bmp;
                    if (_pending.Remove(rawcode, out var callbacks))
                        foreach (var cb in callbacks) cb(bmp);
                });
            });
        }

        /// <summary>The row's icon art path, or null. Non-unit kinds read the merged
        /// icon field - bare code first, then level 1 for leveled kinds (upgrades
        /// store 'gar1' per level). Art values can list several paths comma-separated,
        /// the first one is the icon (as the palette resolves it).</summary>
        private string? ResolveIconPath(string rawcode)
        {
            if (_kind == ObjectKind.Unit)
                return _unitIcons.Value.TryGetValue(rawcode, out var art) ? art : null;
            var fields = ObjectGetCommand.Execute(_doc, _kind, rawcode, _gameDir).Fields;
            var value = FieldValue(fields, _iconField) ?? FieldValue(fields, _iconField + ":1");
            return string.IsNullOrWhiteSpace(value) ? null : value.Split(',')[0].Trim().Trim('"');
        }

        private static string? FieldValue(IReadOnlyList<MergedField> fields, string code) =>
            fields.FirstOrDefault(f => f.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.Value;

        /// <summary>Every unit palette entry's resolved icon art, keyed by rawcode. The
        /// map's own units are palette entries too, so one build covers the whole list.</summary>
        private Dictionary<string, string> BuildUnitIcons()
        {
            var icons = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (var e in PaletteCommand.UnitPalette(_doc, _gameDir).Entries)
                    if (e.IconPath is { Length: > 0 } art)
                        icons.TryAdd(e.Rawcode, art);
            }
            catch { /* palette unavailable - unit rows stay iconless */ }
            return icons;
        }

        /// <summary>Decode via the command layer, one bitmap per distinct art path. A
        /// rare concurrent double-decode publishes one bitmap and disposes the loser.</summary>
        private Bitmap? DecodeShared(string path)
        {
            if (_byPath.TryGetValue(path, out var cached)) return cached;
            Bitmap? bmp = null;
            if (PaletteCommand.IconPng(_doc, path, _gameDir) is { } png)
            {
                using var ms = new MemoryStream(png);
                bmp = new Bitmap(ms);
            }
            var winner = _byPath.GetOrAdd(path, bmp);
            if (!ReferenceEquals(winner, bmp)) bmp?.Dispose();
            return winner;
        }
    }

    /// <summary>
    /// Read-only field grid row. Map-sourced fields render gold + semibold, like
    /// the World Editor's modified-field highlight. A reference-list field is
    /// followed by display-only SUB-rows ("rawcode - name", one per entry) that
    /// share the parent's colour but are never selectable or editable.
    /// </summary>
    public sealed class FieldRow
    {
        private static readonly IBrush BaseBrush = new SolidColorBrush(Color.Parse("#C8CDD3"));
        private static readonly IBrush MapBrush = new SolidColorBrush(Color.Parse("#E8C56A"));
        private static readonly IBrush HeaderBrush = new SolidColorBrush(Color.Parse("#7FB2E5"));

        private readonly bool _mapSource;

        /// <summary>
        /// The form field this row was built from, when it came from one. Carried so the panel can
        /// ask the command layer whether a value is legal instead of re-deriving the rules, and so
        /// the multi-line hint reaches the editor.
        /// </summary>
        public FormField? Form { get; private init; }

        public FieldRow(MergedField field, string? displayOverride = null, string hint = "",
            FormField? form = null)
        {
            Form = form;
            Code = field.Code;
            Name = field.Name;
            Value = field.Value;
            DisplayValue = displayOverride ?? field.Display;
            Source = field.Source;
            Hint = hint;
            _mapSource = field.Source == "map";
        }

        private FieldRow(string display, bool mapSource)
        {
            Code = "";
            Name = "";
            Value = "";
            DisplayValue = display;
            Source = "";
            IsSubRow = true;
            _mapSource = mapSource;
        }

        /// <summary>An expanded reference-list entry rendered beneath its field row.</summary>
        public static FieldRow SubRow(string display, bool mapSource) => new(display, mapSource);

        private FieldRow(string title, int count, bool header)
        {
            Code = "";
            Name = title;
            Value = "";
            DisplayValue = "";
            Source = "";
            Hint = $"{count} field(s)";
            IsGroupHeader = header;
            _mapSource = false;
        }

        /// <summary>A category heading. The World Editor shows these as collapsible sections and
        /// they are the difference between a form and a list of 169 rows.</summary>
        public static FieldRow GroupHeader(string title, int count) => new(title, count, true);

        public string Code { get; }
        public string Name { get; }
        /// <summary>Raw stored value (TRIGSTR_ refs intact) — what the editor edits and writes back.</summary>
        public string Value { get; }
        /// <summary>TRIGSTR_-resolved value shown in the grid; equals Value when not a reference.</summary>
        public string DisplayValue { get; }
        public string Source { get; }
        /// <summary>Display-only child of a reference-list field (not a field itself).</summary>
        public bool IsSubRow { get; }

        /// <summary>A category heading rather than an editable field.</summary>
        public bool IsGroupHeader { get; private init; }

        /// <summary>Legal range and target layer, from the game's own field metadata. Empty when
        /// there is no install to read it from.</summary>
        public string Hint { get; private init; } = "";

        /// <summary>Only a real field row can be picked up by the editor pane.</summary>
        public bool IsSelectable => !IsSubRow && !IsGroupHeader;

        public IBrush RowBrush => IsGroupHeader ? HeaderBrush : _mapSource ? MapBrush : BaseBrush;
        public FontWeight RowWeight =>
            IsGroupHeader || (_mapSource && !IsSubRow) ? FontWeight.SemiBold : FontWeight.Normal;
        /// <summary>Headings get air above them so the groups read as blocks.</summary>
        public Thickness RowMargin => IsGroupHeader ? new Thickness(0, 8, 0, 2) : new Thickness(0);
        /// <summary>Sub-rows indent their text under the parent's value column.</summary>
        public Thickness ValueMargin => IsSubRow ? new Thickness(24, 0, 4, 0) : new Thickness(4, 0);
    }

    /// <summary>The value a field holds in the document right now, or null when it holds none.</summary>
    private string? StoredValue(MapDocument doc, string rawcode, string fieldCode)
    {
        try
        {
            var merged = ObjectGetCommand.Execute(doc, SelectedKind.Kind, rawcode, _session?.GameDir);
            return merged.Fields
                .FirstOrDefault(f => string.Equals(f.Code, fieldCode, StringComparison.OrdinalIgnoreCase))
                ?.Value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Shows what the selected field holds now and, when this session has changed it, what it held
    /// when the map was opened. Also decides whether Revert and Reset Box can do anything.
    /// </summary>
    private void ShowValueContext(FieldRow row)
    {
        FieldCurrentText.IsVisible = false;
        FieldOriginalText.IsVisible = false;
        RevertButton.IsEnabled = false;
        ResetBoxButton.IsEnabled = false;

        if (row.Code.Length == 0) return;

        var current = row.DisplayValue;
        FieldCurrentText.Text = $"stored: {Trim(current)}   [{row.Source}]";
        FieldCurrentText.IsVisible = true;
        ResetBoxButton.IsEnabled = true;

        // Only the single-selection case has one unambiguous original to offer.
        var targets = SelectedObjects();
        if (targets.Count != 1) return;

        if (_originalValues.TryGetValue((SelectedKind.Kind, targets[0].Rawcode, row.Code), out var original))
        {
            FieldOriginalText.Text = $"changed this session, was: {Trim(original)}";
            FieldOriginalText.IsVisible = true;
            RevertButton.IsEnabled = true;
        }
    }

    private static string Trim(string v)
    {
        v = (v ?? "").Replace('\n', ' ').Replace('\r', ' ');
        if (v.Length == 0) return "(empty)";
        return v.Length <= 120 ? v : v[..120] + "...";
    }

    /// <summary>Discards what was typed and shows the field's stored value again.</summary>
    private void OnResetBoxClick(object? sender, RoutedEventArgs e)
    {
        if (FieldList.SelectedItem is FieldRow { IsSelectable: true } row)
        {
            ConfigureEditor(row);
            UpdateApplyState();
            StatusText.Text = $"editor reset to the stored value of {row.Code}";
        }
    }

    /// <summary>
    /// Puts the field back to the value it held when the map was opened, through the same write
    /// path as any other edit, then forgets the record so the field reads as untouched again.
    /// </summary>
    private void OnRevertClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc) { StatusText.Text = "No map open."; return; }
        if (FieldList.SelectedItem is not FieldRow { IsSelectable: true } row) return;

        var targets = SelectedObjects();
        if (targets.Count != 1)
        {
            StatusText.Text = "Revert works on one object at a time.";
            return;
        }
        var key = (SelectedKind.Kind, targets[0].Rawcode, row.Code);
        if (!_originalValues.TryGetValue(key, out var original))
        {
            StatusText.Text = "This field has not been changed in this session.";
            return;
        }

        var result = ObjectSetCommand.Execute(doc, SelectedKind.Kind, targets[0].Rawcode, row.Code, original);
        if (!result.Ok)
        {
            StatusText.Text = $"could not revert {row.Code}: {result.Message}";
            return;
        }

        // Reverting is itself an unsaved change to the document, so the counter goes UP, not down.
        // The field matches the opened map again; the file on disk does not yet.
        _originalValues.Remove(key);
        _unsavedEdits++;
        RefreshFieldPane(row.Code);
        StatusText.Text = $"{row.Code} reverted to {Trim(original)}. Save Edits to write it.";
    }
}
