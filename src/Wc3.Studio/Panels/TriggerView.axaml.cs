using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Wc3.Commands;
using Wc3.Studio.Controls;
using Wc3.Model;

namespace Wc3.Studio.Panels;

/// <summary>
/// Trigger browser and editor over <see cref="TriggerReadCommand"/> and
/// <see cref="TriggerCommand"/>: a World-Editor style tree of categories and their triggers
/// (plus the global variables) on the left, and the selected trigger's detail on the right.
/// GUI triggers show their event/condition/action tree with nested blocks indented,
/// custom-text triggers show their war3map.wct script body in the shared JASS editor.
///
/// Editable here: a category or trigger's name, a trigger's enabled / initially-on /
/// run-on-map-init flags, and adding or removing categories and triggers. Every one of those
/// goes through <see cref="TriggerCommand"/>, so the refusals it computes (orphaning a
/// category's children, or shifting a war3map.wct code body onto the wrong trigger) arrive
/// here as a message on the status line rather than being re-derived in the UI.
///
/// Events, conditions and actions are editable too, added by name against the World-Editor
/// function table, removed by position, and enabled or disabled one at a time.
///
/// NOT editable here: a custom-text trigger's body, which is JASS in war3map.wct, a file whose
/// decoder loses bytes it cannot interpret and which is therefore never written.
///
/// And nothing here compiles. Warcraft III runs war3map.j, the World Editor generates it from
/// this tree, and wc3ctl does not, so a trigger authored here is inert in game until the map is
/// saved in the World Editor. The panel says so beside the controls that create one.
/// </summary>
public partial class TriggerView : UserControl, IMapPanel
{
    private const string SelectHint = "Select a trigger to see its events, conditions and actions.";

    /// <summary>Dim ink for hints and secondary text, the shade the other panels use.</summary>
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#8FA3B8"));

    private MapSession? _session;

    /// <summary>Guards the flag checkboxes while they are being set to match the selected
    /// trigger, so reflecting a selection does not fire an edit for the value it already has.</summary>
    private bool _syncingFlags;

    /// <summary>Raised after an edit lands, which is what enables Save in the host.</summary>
    public event EventHandler? MapEdited;

    public TriggerView()
    {
        InitializeComponent();
        TriggerTree.SelectionChanged += (_, _) => OnTreeSelectionChanged();

        EnabledCheck.IsCheckedChanged += (_, _) =>
            OnFlagToggled((doc, id, on) => TriggerCommand.SetEnabled(doc, id, on),
                EnabledCheck.IsChecked);
        InitiallyOnCheck.IsCheckedChanged += (_, _) =>
            OnFlagToggled((doc, id, on) => TriggerCommand.SetInitiallyOn(doc, id, on),
                InitiallyOnCheck.IsChecked);
        MapInitCheck.IsCheckedChanged += (_, _) =>
            OnFlagToggled((doc, id, on) => TriggerCommand.SetRunOnMapInit(doc, id, on),
                MapInitCheck.IsChecked);

        EcaTree.SelectionChanged += (_, _) =>
        {
            EcaRemoveButton.IsEnabled = EcaBar.IsVisible && SelectedFunctionIndex is not null;
            EcaToggleButton.IsEnabled = EcaRemoveButton.IsEnabled;
        };
    }

    /// <summary>
    /// Position of the selected function within its trigger, or null when the selection is a
    /// section header or nothing.
    ///
    /// The ECA tree groups functions into Events / Conditions / Actions sections for reading,
    /// while the command layer addresses them by their position in the trigger's single function
    /// list. The tag carries that position so the two never have to be reconciled by counting.
    /// </summary>
    private int? SelectedFunctionIndex =>
        EcaTree.SelectedItem is TreeViewItem { Tag: int i } ? i : null;

    public void ShowMap(MapSession session)
    {
        _session = session;
        StatusText.Text = string.Empty;
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
        // catch is purely defensive (a panel must never take the workspace down).
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
                // Tagged so the toolbar can parent a new trigger to it, rename it or remove
                // it. Untagged category nodes were why editing had nothing to act on.
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
            case TreeViewItem { Tag: TriggerInfo trig }:
                ShowTrigger(trig);
                break;
            case TreeViewItem { Tag: TriggerVariableInfo variable }:
                ShowVariable(variable);
                break;
            default:
                // The Variables folder or the orphan bucket, nothing to detail.
                ClearDetails(SelectHint);
                break;
        }
        UpdateToolbar();
    }

    // ---------------------------------------------------------------- editing

    /// <summary>The selected category, or null when the selection is not one.</summary>
    private TriggerCategoryInfo? SelectedCategory =>
        TriggerTree.SelectedItem is TreeViewItem { Tag: TriggerCategoryInfo c } ? c : null;

    /// <summary>The selected trigger, or null when the selection is not one.</summary>
    private TriggerInfo? SelectedTrigger =>
        TriggerTree.SelectedItem is TreeViewItem { Tag: TriggerInfo t } ? t : null;

    /// <summary>The id and name of whichever item is selected, for rename and remove. A
    /// variable row is deliberately excluded: wtg variables are a separate list from the item
    /// tree and TriggerCommand does not edit them.</summary>
    private (int Id, string Name)? SelectedItem =>
        SelectedCategory is { } c ? (c.Id, c.Name)
        : SelectedTrigger is { } t ? (t.Id, t.Name)
        : null;

    private void UpdateToolbar()
    {
        bool hasMap = _session?.Current is not null;
        var item = SelectedItem;
        AddCategoryButton.IsEnabled = hasMap;
        AddTriggerButton.IsEnabled = hasMap && SelectedCategory is not null;
        RenameButton.IsEnabled = hasMap && item is not null;
        RemoveButton.IsEnabled = hasMap && item is not null;

        var trig = SelectedTrigger;
        FlagBar.IsVisible = trig is not null;
        // Only a GUI trigger has events, conditions and actions. A custom-text trigger's body is
        // JASS in war3map.wct, which is never written, so offering the bar there would offer an
        // edit that can only be refused.
        EcaBar.IsVisible = trig is not null && !trig.IsCustomText;
        EcaCompileNote.IsVisible = EcaBar.IsVisible;
        EcaRemoveButton.IsEnabled = EcaBar.IsVisible && SelectedFunctionIndex is not null;
        EcaToggleButton.IsEnabled = EcaRemoveButton.IsEnabled;
        if (trig is null) return;

        // Set the boxes without letting them fire an edit for the value they already carry.
        _syncingFlags = true;
        EnabledCheck.IsChecked = trig.Enabled;
        InitiallyOnCheck.IsChecked = trig.InitiallyOn;
        // TriggerInfo does not carry run-on-map-init, so read it from the flat item list,
        // which does, rather than showing a value that is always false.
        MapInitCheck.IsChecked = RunOnMapInitOf(trig.Id);
        _syncingFlags = false;
    }

    private bool RunOnMapInitOf(int id)
    {
        if (_session?.Current is not { } doc) return false;
        try
        {
            return TriggerCommand.List(doc)
                .FirstOrDefault(i => i.Id == id)?.RunOnMapInit ?? false;
        }
        catch
        {
            return false;   // a panel must never take the workspace down
        }
    }

    private void OnAddCategoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (RequireName() is not { } name) return;
        // A selected category becomes the parent, so nesting is a selection rather than a
        // separate control.
        int parent = SelectedCategory?.Id ?? -1;
        Mutate(doc => TriggerCommand.AddCategory(doc, name, parent));
    }

    private void OnAddTriggerClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedCategory is not { } cat)
        {
            StatusText.Text = "Select the category to add the trigger to.";
            return;
        }
        if (RequireName() is not { } name) return;
        Mutate(doc => TriggerCommand.AddTrigger(doc, name, cat.Id));
    }

    private void OnRenameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedItem is not { } item)
        {
            StatusText.Text = "Select a category or trigger to rename.";
            return;
        }
        if (RequireName() is not { } name) return;
        Mutate(doc => TriggerCommand.Rename(doc, item.Id, name));
    }

    private void OnRemoveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedItem is not { } item)
        {
            StatusText.Text = "Select a category or trigger to remove.";
            return;
        }
        Mutate(doc => TriggerCommand.Remove(doc, item.Id, RecursiveCheck.IsChecked == true));
    }

    private void OnAddEcaClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedTrigger is not { } trig)
        {
            StatusText.Text = "Select a GUI trigger first.";
            return;
        }
        var name = EcaNameBox.Text?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            StatusText.Text = "Type the function name, for example DisplayTextToForce.";
            return;
        }

        var kind = (EcaKindBox.SelectedIndex) switch
        {
            0 => War3Net.Build.Script.TriggerFunctionType.Event,
            1 => War3Net.Build.Script.TriggerFunctionType.Condition,
            _ => War3Net.Build.Script.TriggerFunctionType.Action,
        };

        // Empty entries are dropped rather than sent as blank values, so "a,,b" cannot silently
        // become a parameter the World Editor shows as empty.
        var values = (EcaParamsBox.Text ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (Mutate(doc => TriggerCommand.AddFunction(doc, trig.Id, kind, name, values)))
            EcaNameBox.Text = EcaParamsBox.Text = string.Empty;
    }

    private void OnToggleEcaClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedTrigger is not { } trig) { StatusText.Text = "Select a GUI trigger first."; return; }
        if (SelectedFunctionIndex is not { } index)
        {
            StatusText.Text = "Select the event, condition or action to toggle.";
            return;
        }

        // Read the current state from the model rather than tracking it here, so the button
        // cannot drift out of step with what the tree is showing.
        bool currentlyEnabled = trig.Functions.Count > index && trig.Functions[index].Enabled;
        Mutate(doc => TriggerCommand.SetFunctionEnabled(doc, trig.Id, index, !currentlyEnabled));
    }

    private void OnRemoveEcaClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedTrigger is not { } trig) { StatusText.Text = "Select a GUI trigger first."; return; }
        if (SelectedFunctionIndex is not { } index)
        {
            StatusText.Text = "Select the event, condition or action to remove.";
            return;
        }
        Mutate(doc => TriggerCommand.RemoveFunction(doc, trig.Id, index));
    }

    private void OnFlagToggled(Func<MapDocument, int, bool, TriggerOpResult> op, bool? value)
    {
        if (_syncingFlags || value is not { } on) return;
        if (SelectedTrigger is not { } trig) return;
        Mutate(doc => op(doc, trig.Id, on));
    }

    /// <summary>The typed name, or null (with a status message) when it is blank.</summary>
    private string? RequireName()
    {
        var name = NameBox.Text?.Trim() ?? string.Empty;
        if (name.Length > 0) return name;
        StatusText.Text = "Type a name in the box first.";
        return null;
    }

    /// <summary>
    /// Runs one command, then rebuilds from the map and reports what happened.
    ///
    /// The rebuild is unconditional, including after a refusal, because a refusal means the
    /// tree on screen is right and the panel should not be left showing a half-applied state
    /// either way. Selection is restored by id afterwards, since rebuilding the tree throws
    /// the TreeViewItem instances away.
    /// </summary>
    private bool Mutate(Func<MapDocument, TriggerOpResult> op)
    {
        if (_session is not { Current: { } doc } session)
        {
            StatusText.Text = "No map open.";
            return false;
        }

        TriggerOpResult result;
        try
        {
            result = op(doc);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Edit failed: {ex.Message}";
            return false;
        }

        int? keep = SelectedItem?.Id;
        ShowMap(session);
        if (keep is { } id) SelectById(id);

        StatusText.Text = result.Message;
        StatusText.Foreground = result.Ok ? DimBrush : WarnBrush;
        if (result.Ok)
        {
            NameBox.Text = string.Empty;
            MapEdited?.Invoke(this, EventArgs.Empty);
        }
        return result.Ok;
    }

    /// <summary>Re-selects the item carrying this id after a rebuild, so an edit does not
    /// bounce the user back to the top of the tree.</summary>
    private void SelectById(int id)
    {
        foreach (var node in AllNodes(TriggerTree.Items))
        {
            int? nodeId = node.Tag switch
            {
                TriggerCategoryInfo c => c.Id,
                TriggerInfo t => t.Id,
                _ => null,
            };
            if (nodeId != id) continue;
            node.IsSelected = true;
            return;
        }
    }

    private static IEnumerable<TreeViewItem> AllNodes(ItemCollection items)
    {
        foreach (var raw in items)
        {
            if (raw is not TreeViewItem node) continue;
            yield return node;
            foreach (var child in AllNodes(node.Items))
                yield return child;
        }
    }

    /// <summary>Ink for a refusal, so "cannot remove this without moving code" does not read
    /// like the same dim aside as a success.</summary>
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#E8B339"));

    private void ShowTrigger(TriggerInfo trig)
    {
        ClearDetails(SelectHint);
        DetailHeader.Text = trig.Name.Length > 0 ? trig.Name : "(unnamed trigger)";
        FlagsText.Text = string.Join(", ",
            trig.IsCustomText ? "Custom-text trigger" : "GUI trigger",
            trig.Enabled ? "enabled" : "disabled",
            trig.InitiallyOn ? "initially on" : "initially off");
        DescriptionText.Text = trig.Description;
        DescriptionText.IsVisible = trig.Description.Length > 0;

        if (trig.IsCustomText)
        {
            if (string.IsNullOrEmpty(trig.CustomText))
            {
                DetailHint.Text = "This custom-text trigger has no script body in war3map.wct.";
                return;
            }
            DetailHint.IsVisible = false;
            // The externals are derived from this body alone, so a one-function trigger
            // highlights its natives without the whole map's script being in hand.
            CustomScript.LoadScript(trig.CustomText,
                JassSyntax.ExternalCalls(trig.CustomText));
            CustomScript.IsVisible = true;
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
    }

    /// <summary>Right tree: the trigger's functions grouped World-Editor style into
    /// Events / Conditions / Actions sections (all three always shown, with counts),
    /// nested blocks as expandable children. Anything with an unexpected kind lands
    /// in an Other section instead of being dropped.</summary>
    private void BuildEcaTree(IReadOnlyList<TriggerFunctionInfo> functions)
    {
        EcaTree.Items.Clear();

        // The tree groups by kind for reading, while TriggerCommand addresses a function by its
        // position in the trigger's single flat list. Capturing that position here means the two
        // never have to be reconciled by counting sections.
        var indexOf = new Dictionary<TriggerFunctionInfo, int>();
        for (int i = 0; i < functions.Count; i++) indexOf[functions[i]] = i;

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
                section.Items.Add(MakeFunctionItem(fn, indexOf));
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
                section.Items.Add(MakeFunctionItem(fn, indexOf));
            EcaTree.Items.Add(section);
        }
    }

    /// <summary>One ECA node rendered as "Kind: Name(params)" using the readable
    /// parameter strings the command layer produced, recursing into nested blocks
    /// (if/then/else, loops, and/or). Long rows trim with the full text on the
    /// tooltip.</summary>
    private static TreeViewItem MakeFunctionItem(
        TriggerFunctionInfo fn, IReadOnlyDictionary<TriggerFunctionInfo, int>? indexOf = null)
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
        // Only top-level functions carry a position, because only those can be removed: a nested
        // block's children belong to their parent rather than to the trigger's list.
        if (indexOf is not null && indexOf.TryGetValue(fn, out int at)) item.Tag = at;
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

    /// <summary>Resets the right pane to a hint (used on rebuild, on non-trigger
    /// selections, and as the base state ShowTrigger layers onto).</summary>
    private void ClearDetails(string hint)
    {
        DetailHeader.Text = "Details";
        FlagsText.Text = string.Empty;
        DescriptionText.Text = string.Empty;
        DescriptionText.IsVisible = false;
        EcaTree.Items.Clear();
        EcaTree.IsVisible = false;
        CustomScript.LoadScript(string.Empty, natives: null);
        CustomScript.IsVisible = false;
        DetailHint.Text = hint;
        DetailHint.IsVisible = true;
    }
}
