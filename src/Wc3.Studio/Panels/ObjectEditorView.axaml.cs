using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Studio.Panels;

/// <summary>
/// World-Editor-style object editor: a type switcher over all 7 Object Editor
/// kinds, the map's objects of that kind on the left (multi-select), and the
/// first selected object's merged fields (base game data ⊕ map deltas) on the
/// right. The field grid is read-only; editing happens in a dedicated box below
/// it — select a row, change the value, Apply writes it to every selected object
/// via ObjectSetCommand (units only this slice). This select-then-edit design
/// avoids putting TextBoxes inside the recycled ListBox rows, whose focus/recycle
/// behavior erased in-progress edits on click.
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

    public ObjectEditorView()
    {
        InitializeComponent();
        KindCombo.ItemsSource = Kinds;
        KindCombo.SelectedIndex = 0;
    }

    private KindOption SelectedKind => KindCombo.SelectedItem as KindOption ?? Kinds[0];

    public void ShowMap(MapSession session)
    {
        _session = session;
        _unsavedEdits = 0;
        StatusText.Text = "";

        if (session.Current is null)
        {
            ShowPlaceholder("Object editor panel — no map open");
            return;
        }

        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;
        _suppress = true;
        KindCombo.SelectedIndex = 0; // a freshly shown map starts on Units
        _suppress = false;
        RefreshObjectList();
    }

    private void ShowPlaceholder(string message)
    {
        PlaceholderText.Text = message;
        PlaceholderText.IsVisible = true;
        ContentRoot.IsVisible = false;
    }

    private void OnKindChanged(object? sender, SelectionChangedEventArgs e)
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
            ShowPlaceholder("Object editor panel — no map open");
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
    /// Show the cached object list filtered by the search text — case-insensitive
    /// substring match on rawcode and display name; empty search shows all. The
    /// selection survives filtering while the selected objects still match, so
    /// typing doesn't reload the field pane on every keystroke.
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
            : _allRows.Where(r =>
                r.Rawcode.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.Display.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
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
        MultiSelectNote.IsVisible = selected.Count > 1;
        MultiSelectNote.Text = selected.Count > 1
            ? $"{selected.Count} objects selected — edits apply to all (fields shown are {first.Rawcode}'s)"
            : "";

        try
        {
            var result = ObjectGetCommand.Execute(doc, SelectedKind.Kind, first.Rawcode, _session.GameDir);
            var baseInfo = result.BaseRawcode is null ? "no base" : $"base {result.BaseRawcode}";
            SelectedHeader.Text =
                $"{result.Name ?? first.Rawcode} ({first.Rawcode}) — {baseInfo} — {result.Fields.Count} field(s)";

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
        _suppress = true;
        FieldList.ItemsSource = null;
        _suppress = false;
        SelectedHeader.Text = "";
        MultiSelectNote.IsVisible = false;
        MultiSelectNote.Text = "";
        ResetEditor();
        HideModelArea();
    }

    // --- model file (double-click an object) ---

    private void HideModelArea()
    {
        ModelArea.IsVisible = false;
        ModelText.Text = "";
        ExtractModelButton.IsEnabled = false;
        _modelEntryName = null;
    }

    /// <summary>
    /// Double-click: resolve the object's model file from its merged fields and
    /// show whether it lives in the map (extractable) or in the base game.
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
            modelPath = FindModelPath(SelectedKind.Kind, fields);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to resolve model for {rawcode}: {ex.Message}";
            return;
        }

        ModelArea.IsVisible = true;
        if (modelPath is null)
        {
            ModelText.Text = $"{rawcode}: no model field found.";
            return;
        }

        var entry = FindMapEntry(doc, modelPath);
        if (entry?.FileName is not null)
        {
            _modelEntryName = entry.FileName;
            ModelText.Text = $"{rawcode} model: {entry.FileName} — in map ({entry.RawBytes.Length:N0} bytes)";
            ExtractModelButton.IsEnabled = true;
        }
        else
        {
            ModelText.Text = $"{rawcode} — base game model: {modelPath}";
        }
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
    /// Looks the model path up in the map; base-data model fields often omit the
    /// extension, so an extensionless miss retries with .mdx/.mdl appended.
    /// </summary>
    private static MapFileEntry? FindMapEntry(MapDocument doc, string modelPath)
    {
        if (doc.GetFile(modelPath) is { } entry)
            return entry;
        if (modelPath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)
            || modelPath.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase))
            return null;
        return doc.GetFile(modelPath + ".mdx") ?? doc.GetFile(modelPath + ".mdl");
    }

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
            EditorBox.Text = row.Value;
            UpdateApplyState();
        }
        else
        {
            ResetEditor();
        }
    }

    private void ResetEditor()
    {
        FieldEditLabel.Text = "Select a field to edit";
        EditorBox.Text = "";
        UpdateApplyState();
    }

    /// <summary>Write-back is units-only this slice; other kinds are view-only.</summary>
    private void UpdateApplyState()
    {
        var kind = SelectedKind;
        bool isUnit = kind.Kind == ObjectKind.Unit;
        ApplyButton.IsEnabled = isUnit
            && _session?.Current is not null
            && FieldList.SelectedItem is FieldRow
            && SelectedObjects().Count > 0;
        EditorBox.IsReadOnly = !isUnit;
        EditNote.Text = isUnit
            ? "List fields (abilities, targets, flags) edit as raw comma-separated text for now."
            : $"Editing {kind.Label} is not supported yet — view only.";
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

        var value = EditorBox.Text ?? "";
        int applied = 0;
        var warnings = new List<string>();
        var problems = new List<string>();
        foreach (var target in targets)
        {
            try
            {
                var result = ObjectSetCommand.Execute(doc, target.Rawcode, row.Code, value);
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
            + (applied > 0 ? $" — {_unsavedEdits} unsaved edit(s)" : "");
        if (warnings.Count > 0)
            summary += $" — {string.Join("; ", warnings)}";
        if (problems.Count > 0)
            summary += $" — {string.Join("; ", problems)}";
        StatusText.Text = summary;
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
            StatusText.Text = "No edits applied — nothing to save.";
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

        StatusText.Text = $"Saved {editedPath} — {_unsavedEdits} edit(s) written.";
        _unsavedEdits = 0;
    }

    private static string EditedPath(string mapPath)
    {
        var dir = Path.GetDirectoryName(mapPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(mapPath);
        var ext = Path.GetExtension(mapPath);
        return Path.Combine(dir, $"{stem}.edited{ext}");
    }

    /// <summary>Type-switcher entry; ComboBox renders ToString.</summary>
    private sealed record KindOption(ObjectKind Kind, string Label)
    {
        public override string ToString() => Label;
    }

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
