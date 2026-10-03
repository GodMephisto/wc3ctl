// tests/Wc3.Studio.Tests/PlacementPanelContentTests.cs
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Controls;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// The Regions and Cameras panels were reported as showing nothing. On the map in question they
/// were RIGHT to, its war3map.w3r and war3map.w3c are eight bytes each, a version int and a count
/// of zero, so the map genuinely defines none, and the panels already say so.
///
/// That left the question that actually mattered, whether the panels render content when there IS
/// content, and whether a map that defines none can be given its first one. The second half was
/// broken for cameras, CameraCommand.Add threw on any map with no war3map.w3c, so a panel showing
/// "this map has no cameras" could never stop being right.
///
/// These pin both halves, so "empty" can be trusted to mean empty rather than suspected of being
/// broken.
/// </summary>
public class PlacementPanelContentTests
{
    /// <summary>
    /// Shows a panel in a window and runs a real layout pass.
    /// </summary>
    /// <remarks>
    /// The layout pass is not decoration. The cards live in a StackPanel inside a ScrollViewer,
    /// and a ScrollViewer reaches its content through a templated presenter, so until something
    /// measures the tree that StackPanel is not a visual descendant of the panel at all. A test
    /// that walked the tree without laying it out would report an empty panel on a panel that
    /// works, which is the same false conclusion this whole exercise started from.
    /// </remarks>
    private static T Shown<T>() where T : UserControl, new()
    {
        var view = new T();
        var window = new Window { Width = 900, Height = 650, Content = view };
        window.Show();
        window.UpdateLayout();
        return view;
    }

    private static List<string> TextsOf(Control view)
    {
        (view.GetVisualRoot() as Window)?.UpdateLayout();
        return view.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
    }

    /// <summary>
    /// A real map from the user's own Maps folder, newest Anime_WOS2 first. Hardcoding one
    /// filename rots every time they update the map, and the earlier attempt to hardcode a
    /// worktree copy silently skipped instead of failing, which is the worst of both.
    /// </summary>
    private static string RealMapOrEmpty()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                               "Warcraft III", "Maps", "Download");
        if (!Directory.Exists(dir)) return "";
        return Directory.EnumerateFiles(dir, "Anime_WOS2_*.w3x")
            .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? "";
    }

    /// <summary>
    /// The panel's empty state as the user would see it, or null when no empty state is showing.
    /// Asserting on the words alone is not enough, they were always present, on the bottom status
    /// line where nobody read them. This asserts they render in the card region.
    /// </summary>
    private static string? EmptyStateOf(Control view)
    {
        (view.GetVisualRoot() as Window)?.UpdateLayout();
        var block = view.GetVisualDescendants().OfType<StackPanel>()
            .FirstOrDefault(p => p.Name == "EmptyState");
        if (block is null || !block.IsVisible) return null;
        return string.Join(" ", block.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsVisible)
            .Select(t => t.Text ?? ""));
    }

    // ---- regions ----------------------------------------------------------------------

    [AvaloniaFact]
    public void A_map_with_no_regions_says_so_rather_than_going_blank()
    {
        var view = Shown<RegionsView>();
        view.ShowMap(new MapSession { Current = BlankMap.Create() });

        // A bare empty list is indistinguishable from a panel that failed to load, so the panel
        // has to say which it is, WHERE the entries would have been.
        var empty = EmptyStateOf(view);
        Assert.NotNull(empty);
        Assert.Contains("no regions", empty, StringComparison.OrdinalIgnoreCase);
        // And it names the file, so the answer is checkable rather than just reassuring.
        Assert.Contains("war3map.w3r", empty);
    }

    [AvaloniaFact]
    public void A_region_added_to_the_map_appears_in_the_panel()
    {
        var doc = BlankMap.Create();
        var added = PlacementCommand.PlaceRegion(doc, "TestArena", -512f, -512f, 512f, 512f);
        Assert.True(added.Ok, added.Message);

        var view = Shown<RegionsView>();
        view.ShowMap(new MapSession { Current = doc });

        var texts = TextsOf(view);
        Assert.Contains(texts, t => t.Contains("TestArena", StringComparison.OrdinalIgnoreCase));
        // The bounds are on the card, so this also proves the card body rendered and not just
        // its title.
        Assert.Contains(texts, t => t.Contains("-512") && t.Contains("512"));
        Assert.Contains(texts, t => t.Contains("1 region"));
    }

    [AvaloniaFact]
    public void A_region_the_map_defines_survives_a_reopen_of_the_panel()
    {
        // Showing a second map must not leave the first map's cards behind, and must not blank
        // the panel either.
        var empty = BlankMap.Create();
        var full = BlankMap.Create();
        Assert.True(PlacementCommand.PlaceRegion(full, "SecondMap", 0f, 0f, 256f, 256f).Ok);

        var view = Shown<RegionsView>();
        view.ShowMap(new MapSession { Current = full });
        Assert.Contains(TextsOf(view), t => t.Contains("SecondMap"));

        Assert.Null(EmptyStateOf(view));   // content showing, so no empty state

        view.ShowMap(new MapSession { Current = empty });
        var afterEmpty = TextsOf(view);
        Assert.DoesNotContain(afterEmpty, t => t.Contains("SecondMap"));
        Assert.Contains("no regions", EmptyStateOf(view), StringComparison.OrdinalIgnoreCase);

        view.ShowMap(new MapSession { Current = full });
        Assert.Contains(TextsOf(view), t => t.Contains("SecondMap"));
        // The state that was set last must not win. Going empty and back again has to clear it.
        Assert.Null(EmptyStateOf(view));
    }

    // ---- cameras ----------------------------------------------------------------------

    [AvaloniaFact]
    public void A_map_with_no_cameras_says_so_rather_than_going_blank()
    {
        var view = Shown<CamerasView>();
        view.ShowMap(new MapSession { Current = BlankMap.Create() });

        var empty = EmptyStateOf(view);
        Assert.NotNull(empty);
        Assert.Contains("no cameras", empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("war3map.w3c", empty);
    }

    [AvaloniaFact]
    public void A_map_with_no_camera_section_can_still_be_given_its_first_camera()
    {
        // The defect. A blank map has no war3map.w3c, and Add used to throw rather than create
        // the section, so the Cameras panel could report "no cameras" forever.
        var doc = BlankMap.Create();
        Assert.Null(doc.GetFile(CameraCommand.FileName));

        var added = CameraCommand.Add(doc, "IntroCam", 128f, 256f);
        Assert.True(added.Ok, added.Message);
        Assert.Equal(1, added.Count);
        Assert.Single(CameraCommand.List(doc));
    }

    [AvaloniaFact]
    public void Set_and_remove_on_a_map_with_no_camera_section_report_rather_than_throw()
    {
        var doc = BlankMap.Create();
        var set = CameraCommand.Set(doc, "Nope", "ZOffset", "100");
        Assert.False(set.Ok);
        Assert.Contains("Nope", set.Message);

        var removed = CameraCommand.Remove(doc, "Nope");
        Assert.False(removed.Ok);
    }

    [AvaloniaFact]
    public void An_empty_camera_section_serializes_to_the_eight_bytes_real_maps_carry()
    {
        // Measured against ggga-nanaya.w3x, aca-nanaya.w3x and base-wos2.w3x, whose war3map.w3c
        // is 8 bytes each, version 0 followed by a count of 0. Creating the section on demand
        // must produce exactly that, so a map that gains a camera and then loses it again is not
        // left carrying a section the World Editor would not have written.
        var doc = BlankMap.Create();
        Assert.True(CameraCommand.Add(doc, "Temp", 0f, 0f).Ok);
        Assert.True(CameraCommand.Remove(doc, "Temp").Ok);

        // CurrentBytes, not RawBytes. RawBytes is the entry's ORIGINAL content and stays at
        // the first write forever, which is exactly the trap this session found in 67
        // places. CurrentBytes is what Save will actually put in the archive.
        var bytes = doc.GetFile(CameraCommand.FileName)!.CurrentBytes;
        Assert.Equal(8, bytes.Length);
        Assert.Equal(0, BitConverter.ToInt32(bytes, 0));   // format version
        Assert.Equal(0, BitConverter.ToInt32(bytes, 4));   // camera count
    }

    [AvaloniaFact]
    public void A_camera_added_to_the_map_appears_in_the_panel()
    {
        var doc = BlankMap.Create();
        Assert.True(CameraCommand.Add(doc, "IntroCam", 128f, 256f).Ok);

        var view = Shown<CamerasView>();
        view.ShowMap(new MapSession { Current = doc });

        Assert.Contains(TextsOf(view),
            t => t.Contains("IntroCam", StringComparison.OrdinalIgnoreCase));
    }

    // ---- against a real map ------------------------------------------------------------

    /// <summary>
    /// The definitive measurement, on a map the user actually opens. The Anime_WOS2
    /// build measured here carries a 1174 byte war3map.w3r, and `wc3ctl region list` names
    /// Caster, Base and Test in it, so if
    /// the panel shows nothing here the panel is wrong, and if it names them the panel is right
    /// and the maps that look empty genuinely are.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void The_regions_panel_lists_a_real_maps_regions()
    {
        var map = RealMapOrEmpty();
        if (map.Length == 0) return;

        var doc = MapDocument.Load(map);
        var fromCommand = PlacementCommand.ListRegions(doc);
        Assert.NotEmpty(fromCommand);

        var view = Shown<RegionsView>();
        view.ShowMap(new MapSession { Current = doc });

        var texts = TextsOf(view);
        // Every region the command layer found must be on screen. A panel that shows some but
        // not all is as broken as one that shows none.
        foreach (var r in fromCommand)
            Assert.Contains(texts, t => t.Contains(r.Name, StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains($"{fromCommand.Count} region"));
        Assert.Null(EmptyStateOf(view));
    }

    /// <summary>
    /// The same map's cameras. Its war3map.w3c is 8 bytes, version 0 and a count of 0, so the
    /// honest answer is "none" and the panel must say that rather than look broken.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void The_cameras_panel_is_honest_about_a_real_map_with_no_cameras()
    {
        var map = RealMapOrEmpty();
        if (map.Length == 0) return;

        var doc = MapDocument.Load(map);
        Assert.Empty(CameraCommand.List(doc));

        var view = Shown<CamerasView>();
        view.ShowMap(new MapSession { Current = doc });
        Assert.Contains("no cameras", EmptyStateOf(view), StringComparison.OrdinalIgnoreCase);
    }
}
