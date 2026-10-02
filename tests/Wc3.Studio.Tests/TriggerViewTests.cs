using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using War3Net.Build.Script;
using War3Net.IO.Mpq;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// Headless drive of the Trigger panel's basic edit surface: select a trigger or
/// category in the tree, change the editor controls exactly like a user would,
/// click Apply, and verify that the underlying war3map.wtg mutation reached the
/// command layer and that the panel raised its dirty event.
/// </summary>
public class TriggerViewTests
{
    private static readonly MapTriggersFormatVersion Fmt =
        Enum.GetValues<MapTriggersFormatVersion>()[^1];
    private static readonly MapTriggersSubVersion Sub =
        Enum.GetValues<MapTriggersSubVersion>()[^1];

    private static MapTriggers NewTriggers()
    {
        var mt = (MapTriggers)Activator.CreateInstance(typeof(MapTriggers), Fmt, Sub)!;
        mt.TriggerItems.Add(new TriggerCategoryDefinition(TriggerItemType.RootCategory)
            { Id = 0, ParentId = -1, Name = string.Empty });
        mt.TriggerItems.Add(new TriggerCategoryDefinition(TriggerItemType.Category)
            { Id = 1, ParentId = 0, Name = "Cat" });
        mt.TriggerItems.Add(new TriggerDefinition(TriggerItemType.Gui)
        {
            Id = 2,
            ParentId = 1,
            Name = "Trg",
            IsEnabled = true,
            IsInitiallyOn = true,
        });
        foreach (var g in mt.TriggerItems.GroupBy(i => i.Type))
            mt.TriggerItemCounts[g.Key] = g.Count();
        return mt;
    }

    private static MapDocument Doc() =>
        MapDocument.Load(BuildMap(new Dictionary<string, byte[]>
        {
            [TriggerCommand.FileName] = TriggerCommand.Serialize(NewTriggers()),
        }));

    private static byte[] BuildMap(IReadOnlyDictionary<string, byte[]> files)
    {
        var builder = new MpqArchiveBuilder();
        foreach (var (name, data) in files)
            builder.AddFile(MpqFile.New(new MemoryStream(data), name));

        using var mpq = new MemoryStream();
        builder.SaveTo(mpq, leaveOpen: true);

        using var outStream = new MemoryStream();
        var header = new byte[0x200];
        header[0] = (byte)'H';
        header[1] = (byte)'M';
        header[2] = (byte)'3';
        header[3] = (byte)'W';
        outStream.Write(header, 0, header.Length);
        mpq.Position = 0;
        mpq.CopyTo(outStream);
        return outStream.ToArray();
    }

    [AvaloniaFact]
    public void Apply_updates_trigger_name_and_flags()
    {
        var doc = Doc();
        var view = new TriggerView();
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();

        int edited = 0;
        view.MapEdited += (_, _) => edited++;
        view.ShowMap(new MapSession { Current = doc });

        SelectTreeItem(view, item => item.Tag is TriggerInfo { Id: 2 });

        Field<TextBox>(view, "NameBox").Text = "Renamed Trigger";
        Field<CheckBox>(view, "EnabledCheckBox").IsChecked = false;
        Field<CheckBox>(view, "InitiallyOnCheckBox").IsChecked = false;
        Field<CheckBox>(view, "RunOnMapInitCheckBox").IsChecked = true;
        Field<Button>(view, "ApplyEditButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var changed = TriggerCommand.List(doc).Single(i => i.Id == 2);
        Assert.Equal("Renamed Trigger", changed.Name);
        Assert.False(changed.IsEnabled);
        Assert.False(changed.IsInitiallyOn);
        Assert.True(changed.RunOnMapInit);
        Assert.Equal(1, edited);
    }

    [AvaloniaFact]
    public void Apply_renames_category_without_trigger_flags()
    {
        var doc = Doc();
        var view = new TriggerView();
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();

        int edited = 0;
        view.MapEdited += (_, _) => edited++;
        view.ShowMap(new MapSession { Current = doc });

        SelectTreeItem(view, item => item.Tag is TriggerCategoryInfo { Id: 1 });

        Field<TextBox>(view, "NameBox").Text = "Renamed Category";
        Field<Button>(view, "ApplyEditButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var changed = TriggerCommand.List(doc).Single(i => i.Id == 1);
        Assert.Equal("Renamed Category", changed.Name);
        Assert.False(Field<StackPanel>(view, "TriggerFlagsPanel").IsVisible);
        Assert.Equal(1, edited);
    }

    [AvaloniaFact]
    public void Workspace_marks_trigger_edits_dirty_and_updates_status()
    {
        var doc = Doc();
        var workspace = new MapWorkspaceView();
        var window = new Window { Width = 1000, Height = 700, Content = workspace };
        window.Show();

        workspace.Session.Current = doc;
        workspace.Session.MapPath = "synthetic.w3x";
        var triggers = Field<TriggerView>(workspace, "TriggersPanel");
        triggers.ShowMap(workspace.Session);

        SelectTreeItem(triggers, item => item.Tag is TriggerInfo { Id: 2 });
        Field<TextBox>(triggers, "NameBox").Text = "Dirty Trigger";
        Field<Button>(triggers, "ApplyEditButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.True(Field<Button>(workspace, "SaveButton").IsEnabled);
        Assert.Equal("Edited triggers (unsaved) - click Save to write it to the map.",
            Field<TextBlock>(workspace, "StatusText").Text);
        Assert.Equal("Dirty Trigger", TriggerCommand.List(doc).Single(i => i.Id == 2).Name);
    }

    private static void SelectTreeItem(TriggerView view, Func<TreeViewItem, bool> match)
    {
        var tree = Field<TreeView>(view, "TriggerTree");
        var item = FindTreeItem(tree.Items.OfType<TreeViewItem>(), match)
            ?? throw new InvalidOperationException("matching tree item was not found");
        tree.SelectedItem = item;
        Dispatcher.UIThread.RunJobs();
    }

    private static TreeViewItem? FindTreeItem(IEnumerable<TreeViewItem> items, Func<TreeViewItem, bool> match)
    {
        foreach (var item in items)
        {
            if (match(item))
                return item;
            var child = FindTreeItem(item.Items.OfType<TreeViewItem>(), match);
            if (child is not null)
                return child;
        }
        return null;
    }

    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;
}
