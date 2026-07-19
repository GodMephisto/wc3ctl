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

    private MapSession? _session;
    private bool _suppress;
    /// <summary>Fields applied via ObjectSetCommand but not yet written to disk.</summary>
    private int _unsavedEdits;
    /// <summary>The current kind's full object list; SearchBox filters this in memory.</summary>
    private List<ObjectRow> _allRows = new();
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

    // --- typed field editor: one control shown per field, chosen from its metadata ---
    private enum EditorMode { Text, Combo, Multi }
    private EditorMode _editorMode = EditorMode.Text;
    /// <summary>Multiselect tokens in display order, so Apply joins deterministically.</summary>
    private IReadOnlyList<string> _editorMultiTokens = Array.Empty<string>();
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
        _suppress = true;
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
            _allRows = ObjectListCommand.Execute(doc, kind.Kind, _session.GameDir).Items
                .Select(i => new ObjectRow(
                    i.Rawcode, i.Name is null ? i.Rawcode : $"{i.Name} ({i.Rawcode})"))
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
        HideModelArea();   // model info is per double-clicked object; drop it on reselect
        RefreshFieldPane((FieldList.SelectedItem as FieldRow)?.Code);
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
            var result = ObjectGetCommand.Execute(doc, SelectedKind.Kind, first.Rawcode, _session.GameDir);
            var baseInfo = result.BaseRawcode is null ? "no base" : $"base {result.BaseRawcode}";
            SelectedHeader.Text =
                $"{result.Name ?? first.Rawcode} ({first.Rawcode}) - {baseInfo} - {result.Fields.Count} field(s)";

            var rows = result.Fields.Select(f => new FieldRow(f)).ToList();
            _suppress = true;
            FieldList.ItemsSource = rows;
            _suppress = false;

            // Keep the edited field selected across object switches and post-Apply
            // refreshes; the selection handler reloads EditorBox from the new row.
            var keep = preserveFieldCode is null
                ? null
                : rows.FirstOrDefault(r => r.Code == preserveFieldCode);
            FieldList.SelectedItem = keep;
            if (keep is null)
                ResetEditor();

            if (result.Diagnostics.Count > 0)
                StatusText.Text = string.Join("; ", result.Diagnostics);
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

    // --- right-click → "Show dependencies" ---

    /// <summary>
    /// Right-click targets the row under the pointer (like Explorer): when it is
    /// outside the current selection, the selection moves to it, so the context
    /// menu always acts on the row the user clicked. Clicks inside the current
    /// selection keep it - "Show dependencies" then uses the primary object.
    /// </summary>
    private void OnObjectListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(ObjectList).Properties.IsRightButtonPressed)
            return;
        var row = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)
            ?.DataContext as ObjectRow;
        if (row is null || SelectedObjects().Contains(row))
            return;
        ObjectList.SelectedItems?.Clear();
        ObjectList.SelectedItem = row;
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
    /// Double-click: resolve the object's model file from its merged fields.
    /// Map-imported models render as a rotatable preview; base-game models
    /// (not in the map) render from CASC when a WC3 install is available,
    /// otherwise only their path shows.
    /// </summary>
    private void OnObjectDoubleTapped(object? sender, TappedEventArgs e)
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
        if (FieldList.SelectedItem is FieldRow row)
        {
            FieldEditLabel.Text = $"{row.Name} ({row.Code})";
            ConfigureEditor(row);
            UpdateApplyState();
        }
        else
        {
            ResetEditor();
        }
    }

    /// <summary>Pick the editor control from the field's metadata: enumerated fields get a
    /// searchable dropdown (single value) or a checklist (list types); everything else -
    /// ints, reals, strings, paths, or fields with no derivable option set - stays free
    /// text. The current value is always kept selectable so out-of-range data is never
    /// silently lost.</summary>
    private void ConfigureEditor(FieldRow row)
    {
        ObjectFieldOptionsResult opt;
        try
        {
            opt = ObjectFieldOptionsCommand.Execute(SelectedKind.Kind, row.Code, _session?.GameDir);
        }
        catch
        {
            opt = new ObjectFieldOptionsResult("", false, Array.Empty<string>());
        }

        bool freeText = opt.Options.Count == 0
            || opt.Options.Count > MaxEditorOptions
            || IsFreeTextType(opt.Type);
        if (!freeText && opt.IsList)
            ShowMultiEditor(row, opt.Options);
        else if (!freeText)
            ShowComboEditor(row, opt.Options);
        else
            ShowTextEditor(row);

        UpdateEditNote(opt);
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
        EditorBox.IsVisible = true;
        EditorCombo.IsVisible = false;
        EditorMultiHost.IsVisible = false;
    }

    private void ShowComboEditor(FieldRow row, IReadOnlyList<string> options)
    {
        _editorMode = EditorMode.Combo;
        var tokens = options.ToList();
        var current = (row.Value ?? "").Trim();
        if (current.Length > 0 && !tokens.Contains(current, StringComparer.OrdinalIgnoreCase))
            tokens.Insert(0, current);
        EditorCombo.SetItems(
            tokens.Select(t => new SearchableComboBoxItem(t, t)).ToList(),
            selectId: current.Length > 0 ? current : null);
        EditorBox.IsVisible = false;
        EditorCombo.IsVisible = true;
        EditorMultiHost.IsVisible = false;
    }

    private void ShowMultiEditor(FieldRow row, IReadOnlyList<string> options)
    {
        _editorMode = EditorMode.Multi;
        var selected = new HashSet<string>(
            (row.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
        // Options ∪ any current tokens outside the option set, so nothing is dropped.
        var tokens = options.ToList();
        foreach (var s in selected)
            if (!tokens.Contains(s, StringComparer.OrdinalIgnoreCase))
                tokens.Add(s);
        _editorMultiTokens = tokens;
        EditorMulti.ItemsSource = tokens;
        EditorMulti.SelectedItems?.Clear();
        foreach (var t in tokens)
            if (selected.Contains(t))
                EditorMulti.SelectedItems?.Add(t);
        EditorBox.IsVisible = false;
        EditorCombo.IsVisible = false;
        EditorMultiHost.IsVisible = true;
    }

    /// <summary>The value to write, read from whichever editor is currently shown.</summary>
    private string CurrentEditorValue() => _editorMode switch
    {
        EditorMode.Combo => EditorCombo.SelectedId ?? "",
        EditorMode.Multi => string.Join(",",
            _editorMultiTokens.Where(t => EditorMulti.SelectedItems?.Contains(t) == true)),
        _ => EditorBox.Text ?? "",
    };

    private void UpdateEditNote(ObjectFieldOptionsResult opt)
    {
        EditNote.Text = _editorMode switch
        {
            EditorMode.Combo => $"Enumerated field (type '{opt.Type}') — pick a value the base game already uses.",
            EditorMode.Multi => $"List field (type '{opt.Type}') — check tokens to include; saved comma-separated.",
            _ when opt.Diagnostic is { } d => $"Free-text field. ({d})",
            _ => "Free-text field; leveled fields (code:N) edit that level/variation only.",
        };
    }

    private void ResetEditor()
    {
        FieldEditLabel.Text = "Select a field to edit";
        _editorMode = EditorMode.Text;
        _editorMultiTokens = Array.Empty<string>();
        EditorBox.Text = "";
        EditorBox.IsVisible = true;
        EditorCombo.IsVisible = false;
        EditorMultiHost.IsVisible = false;
        EditNote.Text = "";
        UpdateApplyState();
    }

    /// <summary>Apply is available for every kind - the command layer routes the
    /// three modification shapes (Simple/Level/Variation) behind one call.</summary>
    private void UpdateApplyState()
    {
        ApplyButton.IsEnabled = _session?.Current is not null
            && FieldList.SelectedItem is FieldRow
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
        if (FieldList.SelectedItem is not FieldRow row)
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
        int applied = 0;
        var warnings = new List<string>();
        var problems = new List<string>();
        foreach (var target in targets)
        {
            try
            {
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

    /// <summary>Object list row: "Name (rawcode)", or the bare rawcode when nameless.</summary>
    public sealed record ObjectRow(string Rawcode, string Display);

    /// <summary>
    /// Read-only field grid row. Map-sourced fields render gold + semibold, like
    /// the World Editor's modified-field highlight.
    /// </summary>
    public sealed class FieldRow
    {
        private static readonly IBrush BaseBrush = new SolidColorBrush(Color.Parse("#C8CDD3"));
        private static readonly IBrush MapBrush = new SolidColorBrush(Color.Parse("#E8C56A"));

        public FieldRow(MergedField field)
        {
            Code = field.Code;
            Name = field.Name;
            Value = field.Value;
            Source = field.Source;
        }

        public string Code { get; }
        public string Name { get; }
        public string Value { get; }
        public string Source { get; }

        public IBrush RowBrush => Source == "map" ? MapBrush : BaseBrush;
        public FontWeight RowWeight => Source == "map" ? FontWeight.SemiBold : FontWeight.Normal;
    }
}
