// tests/Wc3.Studio.Tests/TriggerPanelEditTests.cs
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using War3Net.Build.Extensions;
using War3Net.Build.Script;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Panels;
using Xunit;

namespace Wc3.Studio.Tests;

/// <summary>
/// The Triggers panel as an editor rather than a viewer.
///
/// The panel spent its whole life telling the user "Read-only view. Trigger editing is coming in
/// a later update." while four working trigger edits sat in the command layer reachable from no
/// front-end at all. So what is worth pinning is not that the buttons exist, it is that clicking
/// one changes the map, that the tree redraws to match, that the map is reported as edited so Save
/// lights up, and that a refusal from the command layer reaches the user as words instead of
/// being swallowed.
/// </summary>
public class TriggerPanelEditTests
{
    // ---------------------------------------------------------------- fixtures

    private static byte[] Bytes(MapTriggers t)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            w.Write(t);
        return ms.ToArray();
    }

    private static byte[] Bytes(MapCustomTextTriggers t)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            w.Write(t);
        return ms.ToArray();
    }

    /// <summary>A map with one category holding one GUI trigger, in the sub-version format the
    /// Reforged editor writes.</summary>
    private static MapDocument SimpleMap()
    {
        var t = new MapTriggers(MapTriggersFormatVersion.v7, MapTriggersSubVersion.v4);
        t.TriggerItems.Add(new TriggerCategoryDefinition
        { Id = 0x02000001, Name = "Melee Initialization", ParentId = -1, IsExpanded = true });
        t.TriggerItems.Add(new TriggerDefinition(TriggerItemType.Gui)
        {
            Id = 0x03000001, Name = "Melee Init", Description = string.Empty,
            ParentId = 0x02000001, IsEnabled = true, IsInitiallyOn = true,
        });
        foreach (var type in Enum.GetValues<TriggerItemType>())
            t.TriggerItemCounts[type] = t.TriggerItems.Count(i => i.Type == type);

        var doc = BlankMap.Create();
        doc.AddOrReplaceRawFile("war3map.wtg", Bytes(t));
        return MapDocument.Load(doc.SaveToBytes());
    }

    /// <summary>Two custom-text triggers with real bodies, so removing the first one must be
    /// refused: war3map.wct pairs bodies by position and cannot be rewritten.</summary>
    private static MapDocument MapWithCode()
    {
        var t = new MapTriggers(MapTriggersFormatVersion.v7, MapTriggersSubVersion.v4);
        t.TriggerItems.Add(new TriggerCategoryDefinition
        { Id = 0x02000001, Name = "Systems", ParentId = -1, IsExpanded = true });
        foreach (var (id, name) in new[] { (0x03000001, "First"), (0x03000002, "Second") })
            t.TriggerItems.Add(new TriggerDefinition(TriggerItemType.Script)
            {
                Id = id, Name = name, Description = string.Empty, ParentId = 0x02000001,
                IsEnabled = true, IsInitiallyOn = true, IsCustomTextTrigger = true,
            });
        foreach (var type in Enum.GetValues<TriggerItemType>())
            t.TriggerItemCounts[type] = t.TriggerItems.Count(i => i.Type == type);

        var wct = new MapCustomTextTriggers(MapCustomTextTriggersFormatVersion.v1, null)
        {
            GlobalCustomScriptComment = string.Empty,
            GlobalCustomScriptCode = new CustomTextTrigger { Code = string.Empty },
        };
        wct.CustomTextTriggers.Add(new CustomTextTrigger { Code = "// the body of First" });
        wct.CustomTextTriggers.Add(new CustomTextTrigger { Code = "// the body of Second" });

        var doc = BlankMap.Create();
        doc.AddOrReplaceRawFile("war3map.wtg", Bytes(t));
        doc.AddOrReplaceRawFile("war3map.wct", Bytes(wct));
        return MapDocument.Load(doc.SaveToBytes());
    }

    private static (TriggerView View, MapSession Session, Window Window) Shown(MapDocument doc)
    {
        var view = new TriggerView();
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.UpdateLayout();
        var session = new MapSession { Current = doc };
        view.ShowMap(session);
        window.UpdateLayout();
        return (view, session, window);
    }

    private static T Named<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static string Status(TriggerView view) =>
        Named<TextBlock>(view, "StatusText").Text ?? string.Empty;

    /// <summary>
    /// Selects a node by what it CARRIES rather than by its label text.
    ///
    /// An earlier version matched a label prefix, and "Melee Init" is a prefix of "Melee
    /// Initialization", so it selected the category when the test meant the trigger inside it.
    /// Three tests then measured an edit to the wrong item and read as product failures.
    /// </summary>
    private static void SelectTrigger(TriggerView view, string name) =>
        Node(view, n => n.Tag is TriggerInfo t && t.Name == name, $"trigger '{name}'");

    private static void SelectCategory(TriggerView view, string name) =>
        Node(view, n => n.Tag is TriggerCategoryInfo c && c.Name == name, $"category '{name}'");

    private static void Node(TriggerView view, Func<TreeViewItem, bool> match, string what)
    {
        var node = view.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault(match);
        Assert.True(node is not null, $"no tree node carries {what}");
        node!.IsSelected = true;
    }

    /// <summary>Presses a button, then lays the window out, because an edit rebuilds the tree and
    /// the new TreeViewItems are not visuals until layout has run.</summary>
    private static void Press(TriggerView view, Window window, string button)
    {
        var b = Named<Button>(view, button);
        Assert.True(b.IsEnabled, $"{button} was disabled");
        b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
    }

    private static void Type(TriggerView view, string text) =>
        Named<TextBox>(view, "NameBox").Text = text;

    // ---------------------------------------------------------------- tests

    [AvaloniaFact]
    public void The_panel_no_longer_claims_to_be_read_only()
    {
        var (view, _, window) = Shown(SimpleMap());
        var texts = view.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.DoesNotContain(texts, t => t.Contains("Read-only", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(texts, t => t.Contains("coming in a later update",
            StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public void Adding_a_category_changes_the_map_and_the_tree()
    {
        var (view, session, window) = Shown(SimpleMap());
        Type(view, "New Systems");
        Press(view, window, "AddCategoryButton");

        Assert.Contains("Added category", Status(view));
        var model = TriggerReadCommand.GetTriggers(session.Current!);
        Assert.Contains(model.Categories, c => c.Name == "New Systems");

        // And the tree redrew, rather than the map being ahead of what is on screen.
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
            t => (t.Text ?? string.Empty).StartsWith("New Systems", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void Adding_a_category_reports_the_map_as_edited_so_save_lights_up()
    {
        var (view, _, window) = Shown(SimpleMap());
        int edits = 0;
        view.MapEdited += (_, _) => edits++;

        Type(view, "Another");
        Press(view, window, "AddCategoryButton");

        Assert.Equal(1, edits);
    }

    [AvaloniaFact]
    public void Add_trigger_needs_a_category_selected_and_then_works()
    {
        var (view, session, window) = Shown(SimpleMap());

        // Nothing selected, so the button is off rather than silently doing nothing.
        Assert.False(Named<Button>(view, "AddTriggerButton").IsEnabled);

        SelectCategory(view, "Melee Initialization");
        Type(view, "My New Trigger");
        Press(view, window, "AddTriggerButton");

        Assert.Contains("Added gui trigger", Status(view));
        var model = TriggerReadCommand.GetTriggers(session.Current!);
        var added = model.Triggers.Single(t => t.Name == "My New Trigger");
        Assert.True(added.Enabled);
        Assert.True(added.InitiallyOn);
        Assert.Equal(0x02000001, added.ParentCategoryId);
    }

    [AvaloniaFact]
    public void An_empty_name_is_reported_rather_than_creating_a_blank_item()
    {
        var (view, session, window) = Shown(SimpleMap());
        int before = TriggerReadCommand.GetTriggers(session.Current!).Categories.Count;

        Type(view, "   ");
        Press(view, window, "AddCategoryButton");

        Assert.Contains("Type a name", Status(view));
        Assert.Equal(before, TriggerReadCommand.GetTriggers(session.Current!).Categories.Count);
    }

    [AvaloniaFact]
    public void Renaming_the_selected_trigger_persists()
    {
        var (view, session, window) = Shown(SimpleMap());
        SelectTrigger(view, "Melee Init");
        Type(view, "Renamed Init");
        Press(view, window, "RenameButton");

        Assert.Contains("Renamed trigger item", Status(view));
        Assert.Contains(TriggerReadCommand.GetTriggers(session.Current!).Triggers,
            t => t.Name == "Renamed Init");
    }

    [AvaloniaFact]
    public void Unchecking_enabled_disables_the_selected_trigger()
    {
        var (view, session, window) = Shown(SimpleMap());
        SelectTrigger(view, "Melee Init");

        var check = Named<CheckBox>(view, "EnabledCheck");
        Assert.True(check.IsChecked);       // reflects the trigger it is showing
        check.IsChecked = false;            // the user's click
        window.UpdateLayout();

        Assert.Contains("Disabled trigger", Status(view));
        Assert.False(TriggerReadCommand.GetTriggers(session.Current!).Triggers.Single().Enabled);
    }

    [AvaloniaFact]
    public void Selecting_a_trigger_does_not_itself_edit_anything()
    {
        // The flag boxes are set to match the selection, and setting a checkbox raises the same
        // event a click does. Without a guard, merely clicking through the tree would write to
        // the map and mark it dirty.
        var (view, _, window) = Shown(SimpleMap());
        int edits = 0;
        view.MapEdited += (_, _) => edits++;

        SelectTrigger(view, "Melee Init");
        SelectCategory(view, "Melee Initialization");
        SelectTrigger(view, "Melee Init");

        Assert.Equal(0, edits);
        Assert.Equal(string.Empty, Status(view));
    }

    [AvaloniaFact]
    public void Removing_the_selected_trigger_persists()
    {
        var (view, session, window) = Shown(SimpleMap());
        SelectTrigger(view, "Melee Init");
        Press(view, window, "RemoveButton");

        Assert.Contains("Removed", Status(view));
        Assert.Empty(TriggerReadCommand.GetTriggers(session.Current!).Triggers);
    }

    [AvaloniaFact]
    public void Removing_a_category_with_children_shows_the_refusal_instead_of_orphaning()
    {
        var (view, session, window) = Shown(SimpleMap());
        SelectCategory(view, "Melee Initialization");
        Press(view, window, "RemoveButton");

        Assert.Contains("orphan", Status(view));
        // The map is untouched, and the panel still shows both items.
        var model = TriggerReadCommand.GetTriggers(session.Current!);
        Assert.Single(model.Categories);
        Assert.Single(model.Triggers);
    }

    [AvaloniaFact]
    public void The_with_children_box_turns_that_refusal_into_a_subtree_removal()
    {
        var (view, session, window) = Shown(SimpleMap());
        Named<CheckBox>(view, "RecursiveCheck").IsChecked = true;
        window.UpdateLayout();
        SelectCategory(view, "Melee Initialization");
        Press(view, window, "RemoveButton");

        Assert.Contains("Removed", Status(view));
        var model = TriggerReadCommand.GetTriggers(session.Current!);
        Assert.Empty(model.Categories);
        Assert.Empty(model.Triggers);
    }

    [AvaloniaFact]
    public void A_removal_that_would_move_a_code_body_is_refused_in_words()
    {
        var (view, session, window) = Shown(MapWithCode());
        SelectTrigger(view, "First");
        Press(view, window, "RemoveButton");

        Assert.Contains("without moving code", Status(view));
        Assert.Contains("Second", Status(view));

        // Nothing moved: both bodies still belong to their own trigger.
        var model = TriggerReadCommand.GetTriggers(session.Current!);
        Assert.Equal("// the body of First",
            model.Triggers.Single(t => t.Name == "First").CustomText);
        Assert.Equal("// the body of Second",
            model.Triggers.Single(t => t.Name == "Second").CustomText);
    }

    [AvaloniaFact]
    public void A_refused_edit_does_not_report_the_map_as_edited()
    {
        var (view, _, window) = Shown(MapWithCode());
        int edits = 0;
        view.MapEdited += (_, _) => edits++;

        SelectTrigger(view, "First");
        Press(view, window, "RemoveButton");

        Assert.Contains("without moving code", Status(view));
        Assert.Equal(0, edits);
    }

    // ---------------------------------------------------------------- events, conditions, actions

    [AvaloniaFact]
    public void Adding_an_action_from_the_panel_persists_and_stays_readable()
    {
        var (view, session, window) = Shown(SimpleMap());
        SelectTrigger(view, "Melee Init");

        Named<TextBox>(view, "EcaNameBox").Text = "DisplayTextToForce";
        Named<TextBox>(view, "EcaParamsBox").Text = "GetPlayersAll, hello from the panel";
        Press(view, window, "EcaAddButton");

        Assert.Contains("Added action", Status(view));

        // Read back through the real reader, which is the check that matters: a function written
        // with the wrong arity makes the whole wtg unparseable and the panel would show nothing.
        var model = TriggerReadCommand.GetTriggers(session.Current!);
        var fn = model.Triggers.Single(t => t.Name == "Melee Init").Functions.Single();
        Assert.Equal("Action", fn.Kind);
        Assert.Equal("DisplayTextToForce", fn.Name);
        Assert.Equal(2, fn.Parameters.Count);
    }

    [AvaloniaFact]
    public void An_unknown_function_name_is_reported_and_nothing_is_written()
    {
        var (view, session, window) = Shown(SimpleMap());
        SelectTrigger(view, "Melee Init");
        Named<TextBox>(view, "EcaNameBox").Text = "NotARealAction";
        Press(view, window, "EcaAddButton");

        Assert.Contains("declares no action", Status(view));
        Assert.Empty(TriggerReadCommand.GetTriggers(session.Current!)
            .Triggers.Single(t => t.Name == "Melee Init").Functions);
    }

    [AvaloniaFact]
    public void The_eca_bar_is_hidden_for_a_custom_text_trigger()
    {
        // Its body is JASS in war3map.wct, which is never written, so the bar would only be able
        // to refuse.
        var (view, _, window) = Shown(MapWithCode());
        SelectTrigger(view, "First");
        window.UpdateLayout();
        Assert.False(Named<WrapPanel>(view, "EcaBar").IsVisible);
    }

    [AvaloniaFact]
    public void The_eca_bar_is_shown_for_a_gui_trigger()
    {
        var (view, _, window) = Shown(SimpleMap());
        SelectTrigger(view, "Melee Init");
        window.UpdateLayout();
        Assert.True(Named<WrapPanel>(view, "EcaBar").IsVisible);
    }
}
