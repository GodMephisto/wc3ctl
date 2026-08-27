using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Wc3.Commands;
using Wc3.Studio.Controls;
using Wc3.Model;

namespace Wc3.Studio.Panels;

/// <summary>
/// Read-only trigger browser over <see cref="TriggerReadCommand"/>: a World-Editor
/// style tree of categories and their triggers (plus the global variables) on the
/// left, and the selected trigger's detail on the right. GUI triggers show their
/// event/condition/action tree with nested blocks indented, custom-text triggers
/// show their war3map.wct script body in a read-only monospace box. No write-back,
/// trigger editing is a later wave.
/// </summary>
public partial class TriggerView : UserControl, IMapPanel
{
    private const string SelectHint = "Select a trigger to see its events, conditions and actions.";

    /// <summary>Dim ink for hints and secondary text, the shade the other panels use.</summary>
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#8FA3B8"));

    public TriggerView()
    {
        InitializeComponent();
        TriggerTree.SelectionChanged += (_, _) => OnTreeSelectionChanged();
    }

    public void ShowMap(MapSession session)
    {
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
                // A category or the Variables folder, nothing to detail.
                ClearDetails(SelectHint);
                break;
        }
    }

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
