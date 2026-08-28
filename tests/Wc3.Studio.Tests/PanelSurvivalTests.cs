// tests/Wc3.Studio.Tests/PanelSurvivalTests.cs
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Panels;
using Xunit.Abstractions;

namespace Wc3.Studio.Tests;

/// <summary>
/// Every panel against the awkward maps, because the Studio is the surface the user actually
/// touches and it had only ever been opened on one healthy map.
///
/// The library holds several shapes that broke something else today. A map whose script is CR
/// separated and stored under scripts\. A map with 65,534 of 65,536 hash slots stuffed by
/// protection, which cannot even be saved. A map with 1,836 of 1,859 entries carrying no
/// recoverable name. A map with duplicated internal names. If a panel throws on any of them the
/// whole workspace goes down, and none of that was covered.
/// </summary>
public class PanelSurvivalTests
{
    private readonly ITestOutputHelper _out;
    public PanelSurvivalTests(ITestOutputHelper output) => _out = output;

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Warcraft III", "Maps", "Download");

    private static IEnumerable<(string Name, Func<UserControl> Make)> Panels()
    {
        yield return ("MapInfo", () => new MapInfoView());
        yield return ("Files", () => new FilesView());
        yield return ("Script", () => new ScriptView());
        yield return ("Triggers", () => new TriggerView());
        yield return ("Strings", () => new StringImportView());
        yield return ("Regions", () => new RegionsView());
        yield return ("Cameras", () => new CamerasView());
        yield return ("Sounds", () => new SoundsView());
        yield return ("Players", () => new PlayerForceView());
        yield return ("Objects", () => new ObjectEditorView());
        yield return ("Terrain", () => new TerrainView());
        yield return ("Palette", () => new PaletteView());
        yield return ("Dependencies", () => new DependencyGraphView());
        yield return ("HeroWiring", () => new HeroWiringView());
    }

    // AvaloniaTheory with InlineData, and both halves were learned the hard way.
    //
    // A plain Theory runs off the Avalonia UI thread, so the headless platform is absent and EVERY
    // panel fails to construct with "Unable to locate 'Avalonia.Platform.IWindowingPlatform'".
    // That reads exactly like 14 product defects and is none.
    //
    // MemberData then made ONE case fail intermittently, in 1ms, with "Unable to locate
    // 'Avalonia.Platform.IFontManagerImpl'" thrown from inside a text measure. It passed on the
    // next run untouched, which is the worse outcome, a flaky test that occasionally accuses the
    // product. InlineData is evaluated statically and does not race the session setup.
    //
    // Each map here is a SHAPE that broke something else today, not just a file.
    [AvaloniaTheory]
    [InlineData("Tom_and_Jerry_2014_v1.05.w3x")]   // CR separated script stored under scripts\
    [InlineData("ORDR_S2_2.305[R]_english.w3x")]   // 65,534 of 65,536 hash slots, cannot be saved
    [InlineData("Angel-samurai-Z-v332A.w3x")]      // 1,306 of 1,326 entries unnamed
    [InlineData("NCD S1 ENGv1b.w3x")]              // 14 duplicated internal names
    [InlineData("HostTest.w3x")]                   // near-empty, the floor case
    [InlineData("GGGA_V0.04g.w3x")]                // the largest readable arena
    [Trait("Category", "Corpus")]
    public void No_panel_throws_on_an_awkward_map(string mapName)
    {
        var path = Path.Combine(Dir, mapName);
        if (!File.Exists(path)) { _out.WriteLine($"{mapName} absent, skipped"); return; }

        GameDataWarmup.Begin(null);
        GameDataWarmup.Running?.Wait(TimeSpan.FromMinutes(2));

        MapDocument doc;
        try
        {
            doc = MapDocument.Load(path);
        }
        catch (Exception ex)
        {
            // A map that cannot even load is a separate finding, and worth failing on, because the
            // Studio's Open would surface it as a crash rather than a message.
            Assert.Fail($"{mapName} failed to LOAD: {ex.GetType().Name} {ex.Message}");
            return;
        }

        _out.WriteLine($"{mapName}: {doc.Files.Count:N0} entries, "
                     + $"{doc.Files.Count(f => f.FileName is null)} unnamed, "
                     + $"{doc.Diagnostics.Count} load diagnostic(s)");

        var session = new MapSession { Current = doc, MapPath = path };
        var broke = new List<string>();

        foreach (var (name, make) in Panels())
        {
            try
            {
                var panel = make();
                var window = new Window { Width = 1280, Height = 800, Content = panel };
                try
                {
                    window.Show();
                    window.UpdateLayout();

                    if (panel is IMapPanel p) p.ShowMap(session);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    _out.WriteLine($"   {name,-13} ok");
                }
                finally
                {
                    // Closed, not leaked. This opened fifteen windows per map case and closed
                    // none, so a full run left around ninety of them alive in the application.
                    // Correct hygiene either way, and NOT a cure for the intermittent "Unable to
                    // locate 'Avalonia.Platform.IFontManagerImpl'" thrown from inside a text
                    // measure: measured over four runs after this change, the failure still
                    // appeared once.
                    //
                    // That flake now has three falsified explanations, the theory data source,
                    // cross-class parallelism, and these leaked windows. It is confined to the
                    // Avalonia headless harness rather than the product (every assertion in the
                    // body passes whenever the session comes up healthy), it fails in about one
                    // run in four, and the remaining suspects are the session lifetime itself
                    // under UseHeadlessDrawing=false across sixteen Avalonia test classes.
                    // Recorded rather than guessed at a fourth time.
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                _out.WriteLine($"   {name,-13} THREW {ex.GetType().Name}: {ex.Message}");
                broke.Add($"{name}: {ex.GetType().Name} {ex.Message}");
            }
        }

        Assert.True(broke.Count == 0,
            $"{mapName} takes panels down:\n" + string.Join("\n", broke));
    }

    /// <summary>
    /// Opening a second map after a first must not carry the first one's state into a panel. Each
    /// panel is shown two different maps in a row, which is what a real session does and what the
    /// regions panel got wrong earlier today in a subtler form.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void A_panel_survives_being_shown_a_second_map()
    {
        var first = Path.Combine(Dir, "HostTest.w3x");
        var second = Path.Combine(Dir, "Tom_and_Jerry_2014_v1.05.w3x");
        if (!File.Exists(first) || !File.Exists(second))
        { _out.WriteLine("maps absent, skipped"); return; }

        GameDataWarmup.Begin(null);
        GameDataWarmup.Running?.Wait(TimeSpan.FromMinutes(2));

        var a = new MapSession { Current = MapDocument.Load(first), MapPath = first };
        var b = new MapSession { Current = MapDocument.Load(second), MapPath = second };
        var broke = new List<string>();

        foreach (var (name, make) in Panels())
        {
            try
            {
                var panel = make();
                var window = new Window { Width = 1280, Height = 800, Content = panel };
                window.Show();
                window.UpdateLayout();

                if (panel is IMapPanel p)
                {
                    p.ShowMap(a);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    p.ShowMap(b);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    p.ShowMap(new MapSession { Current = null });   // and closing the map
                    window.UpdateLayout();
                }
            }
            catch (Exception ex)
            {
                broke.Add($"{name}: {ex.GetType().Name} {ex.Message}");
            }
        }

        _out.WriteLine(broke.Count == 0
            ? "every panel survived two maps and a close"
            : string.Join("\n", broke));
        Assert.Empty(broke);
    }
}
