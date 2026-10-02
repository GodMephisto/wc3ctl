using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Wc3.Commands;

namespace Wc3.Studio.Panels;

/// <summary>
/// Trigger browser and basic metadata editor over <see cref="TriggerReadCommand"/>
/// and <see cref="TriggerCommand"/>: a World-Editor style tree of categories and
/// triggers (plus the global variables) on the left, and the selected item's
/// detail on the right. GUI triggers show their event/condition/action tree with
/// nested blocks indented, custom-text triggers show their war3map.wct script
/// body in a read-only monospace box. Categories and triggers can be renamed here,
/// and triggers can toggle Enabled / Initially On / Run on Map Init. Full ECA
/// editing stays in a later wave.
/// </summary>
public partial class TriggerView : UserControl, IMapPanel
{
    private enum EditableSelectionKind { None, Category, Trigger }

    private const string SelectHint = "Select a trigger to see its events, conditions and actions.";

    /// <summary>Dim ink for hints and secondary text, the shade the other panels use.</summary>
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#8FA3B8"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#D96C6C"));
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.Parse("#8FA3B8"));

    private MapSession? _session;
    private Dictionary<int, TriggerItemFields> _itemsById = new();
    private EditableSelectionKind _editableSelection = EditableSelectionKind.None;
    private int _editableItemId = -1;
    private bool _canRename;

    public TriggerView()
    {
        InitializeComponent();
        TriggerTree.SelectionChanged += (_, _) => OnTreeSelectionChanged();
    }

    /// <summary>Raised after an Apply writes into the in-memory map, so the workspace
    /// host can enable Save and report the dirty state.</summary>
    public event EventHandler? MapEdited;

    public void ShowMap(MapSession session)
    {
        _session = session;
        _itemsById = new Dictionary<int, TriggerItemFields>();
        _editableSelection = EditableSelectionKind.None;
        _editableItemId = -1;
        _canRename = false;

        // Rebuild from scratch on every call, like the other panels.
        TriggerTree.Items.Clear();
        ClearDetails(SelectHint);

        if (session.Current is not { } doc)
        {
            ContentRoot.IsVisible = false;
            PlaceholderText.IsVisible = true;
            return;
        }

        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;

        // GetTriggers returns an empty-but-valid model for trigger-less maps, so the
        // catch is purely defensive, a panel must never take the workspace down.
        TriggerModel model;
        try
        {
            model = TriggerReadCommand.GetTriggers(doc);
        }
        catch (Exception ex)
        {
            HeaderText.Text = "Triggers could not be read";
            LanguageText.Text = string.Empty;
            TriggerTree.IsVisible = false;
            TreeHint.IsVisible = true;
            TreeHint.Text = $"Failed to read the map's trigger data: {ex.Message}";
            return;
        }
        try
        {
            _itemsById = TriggerCommand.List(doc).ToDictionary(i => i.Id);
        }
        catch
        {
            _itemsById = new Dictionary<int, TriggerItemFields>();
        }

        LanguageText.Text = $"Script language: {model.ScriptLanguage}";

        bool empty = model.Categories.Count == 0 && model.Triggers.Count == 0
            && model.Variables.Count == 0;
        if (empty)
        {
            HeaderText.Text = "No triggers in this map";
            TriggerTree.IsVisible = false;
            TreeHint.IsVisible = true;
            TreeHint.Text = "This map has no GUI triggers (no readable war3map.wtg). "
                + "Maps that keep their logic in plain script show it in the Script tab.";
            return;
        }

        HeaderText.Text =
            $"{model.Triggers.Count} trigger(s), {model.Categories.Count} categor" +
            (model.Categories.Count == 1 ? "y" : "ies") + $", {model.Variables.Count} variable(s)";
        TriggerTree.IsVisible = true;
        TreeHint.IsVisible = false;
        BuildTree(model);
    }

    /// <summary>Left tree: one node per category (wtg order) holding its triggers,
    /// an extra bucket for triggers whose category id resolves to nothing, and a
    /// Variables node at the bottom.</summary>
    private void BuildTree(TriggerModel model)
    {
        var byCategory = model.Triggers.ToLookup(t => t.ParentCategoryId);
        var knownIds = new HashSet<int>(model.Categories.Select(c => c.Id));

        foreach (var cat in model.Categories)
        {
            var triggers = byCategory[cat.Id].ToList();
            var name = cat.Name.Length > 0 ? cat.Name : "(unnamed category)";
            var catItem = new TreeViewItem
            {
                Header = MakeLabel($"{name} ({triggers.Count})", bold: true),
                IsExpanded = true,
                Tag = cat,
            };
            foreach (var trig in triggers)
                catItem.Items.Add(MakeTriggerItem(trig));
            TriggerTree.Items.Add(catItem);
        }

        var orphans = model.Triggers
            .Where(t => !knownIds.Contains(t.ParentCategoryId))
            .ToList();
        if (orphans.Count > 0)
        {
            var orphanItem = new TreeViewItem
            {
                Header = MakeLabel($"(no category) ({orphans.Count})", bold: true),
                IsExpanded = true,
            };
            foreach (var trig in orphans)
                orphanItem.Items.Add(MakeTriggerItem(trig));
            TriggerTree.Items.Add(orphanItem);
        }

        if (model.Variables.Count > 0)
        {
            var varsItem = new TreeViewItem
            {
                Header = MakeLabel($"Variables ({model.Variables.Count})", bold: true),
                IsExpanded = false,
            };
            foreach (var v in model.Variables)
                varsItem.Items.Add(new TreeViewItem
                {
                    Header = MakeLabel(VariableLabel(v)),
                    Tag = v,
                });
            TriggerTree.Items.Add(varsItem);
        }
    }

    /// <summary>One trigger row: its name plus a dim state hint. A GUI trigger with
    /// no functions shows "empty" (World-Editor comments also land here, the wtg
    /// read does not distinguish them).</summary>
    private static TreeViewItem MakeTriggerItem(TriggerInfo trig)
    {
        var hints = new List<string>();
        if (!trig.Enabled) hints.Add("disabled");
        if (trig.IsCustomText) hints.Add("custom text");
        if (!trig.InitiallyOn) hints.Add("initially off");
        if (!trig.IsCustomText && trig.Functions.Count == 0) hints.Add("empty");

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var name = new TextBlock
        {
            Text = trig.Name.Length > 0 ? trig.Name : "(unnamed trigger)",
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (!trig.Enabled)
            name.Foreground = DimBrush;
        row.Children.Add(name);
        if (hints.Count > 0)
            row.Children.Add(new TextBlock
            {
                Text = $"({string.Join(", ", hints)})",
                FontSize = 11,
                Foreground = DimBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });

        return new TreeViewItem { Header = row, Tag = trig };
    }

    private static string VariableLabel(TriggerVariableInfo v)
    {
        var label = $"{v.Name} : {v.Type}{(v.IsArray ? "[]" : string.Empty)}";
        return v.InitialValue is null ? label : $"{label} = {v.InitialValue}";
    }

    private void OnTreeSelectionChanged()
    {
        switch (TriggerTree.SelectedItem)
        {
            case TreeViewItem { Tag: TriggerCategoryInfo cat }:
                ShowCategory(cat);
                break;
            case TreeViewItem { Tag: TriggerInfo trig }:
                ShowTrigger(trig);
                break;
            case TreeViewItem { Tag: TriggerVariableInfo variable }:
                ShowVariable(variable);
                break;
            default:
                // A folder node like Variables or the orphan bucket, nothing to detail.
                ClearDetails(SelectHint);
                break;
        }
    }

    private void ShowCategory(TriggerCategoryInfo cat)
    {
        ClearDetails(SelectHint);
        DetailHeader.Text = cat.Name.Length > 0 ? cat.Name : "(unnamed category)";
        FlagsText.Text = cat.Kind;
        DetailHint.Text = cat.Kind.Equals("RootCategory", StringComparison.OrdinalIgnoreCase)
            ? "The root trigger folder is format-defined and is not renamed here."
            : "Categories organize triggers. Rename the selected folder above if needed.";
        DetailHint.IsVisible = true;
        ConfigureEditor(
            EditableSelectionKind.Category,
            cat.Id,
            cat.Name,
            canRename: !cat.Kind.Equals("RootCategory", StringComparison.OrdinalIgnoreCase),
            editorTitle: "Edit category",
            hint: "Edits update the in-memory map immediately. Use Save to persist them to disk.");
    }

    private void ShowTrigger(TriggerInfo trig)
    {
        ClearDetails(SelectHint);
        DetailHeader.Text = trig.Name.Length > 0 ? trig.Name : "(unnamed trigger)";
        if (!_itemsById.TryGetValue(trig.Id, out var fields))
        {
            DetailHint.Text = "This trigger could not be matched to its editable fields.";
            return;
        }
        bool runOnMapInit = fields.RunOnMapInit ?? false;
        FlagsText.Text = string.Join(", ",
            trig.IsCustomText ? "Custom-text trigger" : "GUI trigger",
            trig.Enabled ? "enabled" : "disabled",
            trig.InitiallyOn ? "initially on" : "initially off",
            runOnMapInit ? "runs on map init" : "no map-init run");
        DescriptionText.Text = trig.Description;
        DescriptionText.IsVisible = trig.Description.Length > 0;
        ConfigureEditor(
            EditableSelectionKind.Trigger,
            trig.Id,
            trig.Name,
            canRename: true,
            editorTitle: "Edit trigger",
            hint: "Edits update the in-memory map immediately. Use Save to persist them to disk.",
            enabled: fields.IsEnabled ?? trig.Enabled,
            initiallyOn: fields.IsInitiallyOn ?? trig.InitiallyOn,
            runOnMapInit: runOnMapInit);

        if (trig.IsCustomText)
        {
            if (string.IsNullOrEmpty(trig.CustomText))
            {
                DetailHint.Text = "This custom-text trigger has no script body in war3map.wct.";
                return;
            }
            DetailHint.IsVisible = false;
            CustomTextBox.Text = trig.CustomText;
            CustomTextBox.CaretIndex = 0;
            CustomTextBox.IsVisible = true;
            return;
        }

        if (trig.Functions.Count == 0)
        {
            DetailHint.Text = "This trigger has no events, conditions or actions "
                + "(it may be a World-Editor comment).";
            return;
        }

        DetailHint.IsVisible = false;
        EcaTree.IsVisible = true;
        BuildEcaTree(trig.Functions);
    }

    private void ShowVariable(TriggerVariableInfo variable)
    {
        ClearDetails(variable.InitialValue is null
            ? "Global variable, no explicit initial value."
            : $"Global variable, initial value: {variable.InitialValue}");
        DetailHeader.Text = variable.Name;
        FlagsText.Text = variable.IsArray ? $"{variable.Type} array" : variable.Type;
        DetailHint.Text += " Variable editing is not wired in this panel yet.";
        HideEditor();
    }

    private void ConfigureEditor(
        EditableSelectionKind kind,
        int itemId,
        string name,
        bool canRename,
        string editorTitle,
        string hint,
        bool? enabled = null,
        bool? initiallyOn = null,
        bool? runOnMapInit = null)
    {
        _editableSelection = kind;
        _editableItemId = itemId;
        _canRename = canRename;

        EditorCard.IsVisible = true;
        EditorTitleText.Text = editorTitle;
        NameField.IsVisible = true;
        NameBox.Text = name;
        NameBox.IsEnabled = canRename;
        TriggerFlagsPanel.IsVisible = kind == EditableSelectionKind.Trigger;
        EnabledCheckBox.IsChecked = enabled ?? false;
        InitiallyOnCheckBox.IsChecked = initiallyOn ?? false;
        RunOnMapInitCheckBox.IsChecked = runOnMapInit ?? false;
        EditorHintText.Text = hint;
        EditorHintText.IsVisible = hint.Length > 0;
        ApplyEditButton.IsEnabled = canRename || kind == EditableSelectionKind.Trigger;
        EditStatusText.Text = string.Empty;
        EditStatusText.Foreground = OkBrush;
    }

    private void HideEditor()
    {
        _editableSelection = EditableSelectionKind.None;
        _editableItemId = -1;
        _canRename = false;
        EditorCard.IsVisible = false;
        NameField.IsVisible = true;
        NameBox.Text = string.Empty;
        TriggerFlagsPanel.IsVisible = false;
        ApplyEditButton.IsEnabled = false;
        EditorHintText.Text = string.Empty;
        EditorHintText.IsVisible = false;
        EditStatusText.Text = string.Empty;
        EditStatusText.Foreground = OkBrush;
    }

    private void OnApplyEditClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc || _editableItemId < 0
            || _editableSelection == EditableSelectionKind.None)
        {
            SetEditStatus("No editable trigger item is selected.", isError: true);
            return;
        }
        if (!_itemsById.TryGetValue(_editableItemId, out var fields))
        {
            SetEditStatus("The selected trigger item is no longer in the map.", isError: true);
            return;
        }

        var messages = new List<string>();
        bool anyOk = false;
        bool allOk = true;

        void Run(TriggerOpResult result)
        {
            anyOk |= result.Ok;
            allOk &= result.Ok;
            messages.Add(result.Message);
        }

        string editedName = NameBox.Text ?? string.Empty;
        if (_canRename && !string.Equals(editedName, fields.Name, StringComparison.Ordinal))
            Run(TriggerCommand.Rename(doc, _editableItemId, editedName));

        if (_editableSelection == EditableSelectionKind.Trigger)
        {
            bool enabled = EnabledCheckBox.IsChecked ?? false;
            bool initiallyOn = InitiallyOnCheckBox.IsChecked ?? false;
            bool runOnMapInit = RunOnMapInitCheckBox.IsChecked ?? false;

            if ((fields.IsEnabled ?? false) != enabled)
                Run(TriggerCommand.SetEnabled(doc, _editableItemId, enabled));
            if ((fields.IsInitiallyOn ?? false) != initiallyOn)
                Run(TriggerCommand.SetInitiallyOn(doc, _editableItemId, initiallyOn));
            if ((fields.RunOnMapInit ?? false) != runOnMapInit)
                Run(TriggerCommand.SetRunOnMapInit(doc, _editableItemId, runOnMapInit));
        }

        if (messages.Count == 0)
        {
            SetEditStatus("No changes to apply.", isError: false);
            return;
        }

        ReloadAndReselect(_editableItemId);
        SetEditStatus(
            (allOk ? string.Empty : "Some edits failed. ") + string.Join(" ", messages),
            isError: !allOk);
        if (anyOk)
            MapEdited?.Invoke(this, EventArgs.Empty);
    }

    private void ReloadAndReselect(int itemId)
    {
        if (_session is null)
            return;
        ShowMap(_session);
        if (!TrySelectTreeItem(itemId))
            ClearDetails("The edited trigger item is no longer in the map.");
    }

    private bool TrySelectTreeItem(int itemId)
    {
        foreach (var root in TriggerTree.Items.OfType<TreeViewItem>())
            if (TryFindTreeItem(root, itemId, out var found))
            {
                TriggerTree.SelectedItem = found;
                OnTreeSelectionChanged();
                return true;
            }
        return false;
    }

    private static bool TryFindTreeItem(TreeViewItem item, int itemId, out TreeViewItem? found)
    {
        switch (item.Tag)
        {
            case TriggerCategoryInfo cat when cat.Id == itemId:
                found = item;
                return true;
            case TriggerInfo trig when trig.Id == itemId:
                found = item;
                return true;
        }

        foreach (var child in item.Items.OfType<TreeViewItem>())
            if (TryFindTreeItem(child, itemId, out found))
                return true;

        found = null;
        return false;
    }

    private void SetEditStatus(string text, bool isError)
    {
        EditStatusText.Text = text;
        EditStatusText.Foreground = isError ? ErrorBrush : OkBrush;
    }

    /// <summary>Right tree: the trigger's functions grouped World-Editor style into
    /// Events / Conditions / Actions sections (all three always shown, with counts),
    /// nested blocks as expandable children. Anything with an unexpected kind lands
    /// in an Other section instead of being dropped.</summary>
    private void BuildEcaTree(IReadOnlyList<TriggerFunctionInfo> functions)
    {
        EcaTree.Items.Clear();

        var sections = new (string Kind, string Title)[]
        {
            ("Event", "Events"),
            ("Condition", "Conditions"),
            ("Action", "Actions"),
        };
        var sectionKinds = new HashSet<string>(sections.Select(s => s.Kind));

        foreach (var (kind, title) in sections)
        {
            var group = functions.Where(f => f.Kind == kind).ToList();
            var section = new TreeViewItem
            {
                Header = MakeLabel($"{title} ({group.Count})", bold: true),
                IsExpanded = true,
            };
            foreach (var fn in group)
                section.Items.Add(MakeFunctionItem(fn));
            EcaTree.Items.Add(section);
        }

        var other = functions.Where(f => !sectionKinds.Contains(f.Kind)).ToList();
        if (other.Count > 0)
        {
            var section = new TreeViewItem
            {
                Header = MakeLabel($"Other ({other.Count})", bold: true),
                IsExpanded = true,
            };
            foreach (var fn in other)
                section.Items.Add(MakeFunctionItem(fn));
            EcaTree.Items.Add(section);
        }
    }

    /// <summary>One ECA node rendered as "Kind: Name(params)" using the readable
    /// parameter strings the command layer produced, recursing into nested blocks
    /// (if/then/else, loops, and/or). Long rows trim with the full text on the
    /// tooltip.</summary>
    private static TreeViewItem MakeFunctionItem(TriggerFunctionInfo fn)
    {
        var text = $"{fn.Kind}: {fn.Name}({string.Join(", ", fn.Parameters)})";
        if (!fn.Enabled)
            text += "  [disabled]";

        var label = new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (!fn.Enabled)
            label.Foreground = DimBrush;
        ToolTip.SetTip(label, text);

        var item = new TreeViewItem { Header = label, IsExpanded = true };
        foreach (var child in fn.Children)
            item.Items.Add(MakeFunctionItem(child));
        return item;
    }

    private static TextBlock MakeLabel(string text, bool bold = false)
    {
        var label = new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (bold)
            label.FontWeight = FontWeight.SemiBold;
        ToolTip.SetTip(label, text);
        return label;
    }

    /// <summary>Resets the right pane to a hint (used on rebuild, on non-item
    /// selections, and as the base state ShowTrigger layers onto).</summary>
    private void ClearDetails(string hint)
    {
        DetailHeader.Text = "Details";
        FlagsText.Text = string.Empty;
        DescriptionText.Text = string.Empty;
        DescriptionText.IsVisible = false;
        HideEditor();
        EcaTree.Items.Clear();
        EcaTree.IsVisible = false;
        CustomTextBox.Text = string.Empty;
        CustomTextBox.IsVisible = false;
        DetailHint.Text = hint;
        DetailHint.IsVisible = true;
    }
}
