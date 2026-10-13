// tests/Wc3.Studio.Tests/OptimizeViewTests.cs
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Controls;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// The optimise panel over synthetic inputs and a synthesized blank map, so nothing here needs a
/// WC3 install or a corpus. Each test drives the panel the way a user does, pick an action, set the
/// input, Run, then asserts the shared command's result reached the visual tree. That pins the panel
/// as a thin view over Wc3.Commands rather than a second implementation of any pass.
/// </summary>
public class OptimizeViewTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // --- model audit, hermetic --------------------------------------------

    /// <summary>A v800 MDX with two identical TEXS entries and one material referencing the second.</summary>
    private static byte[] DupTextureMdx()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("MDLX"u8);
        w.Write("VERS"u8); w.Write(4u); w.Write(800u);
        w.Write("TEXS"u8); w.Write(536u);
        for (int t = 0; t < 2; t++)
        {
            w.Write(0u);
            var path = Encoding.ASCII.GetBytes(@"Textures\Test.blp");
            w.Write(path); w.Write(new byte[260 - path.Length]); w.Write(0u);
        }
        const uint layerSize = 28u;
        uint matSize = 4 + 4 + 4 + 8 + layerSize;
        w.Write("MTLS"u8); w.Write(matSize); w.Write(matSize); w.Write(0u); w.Write(0u);
        w.Write("LAYS"u8); w.Write(1u); w.Write(layerSize);
        w.Write(0u); w.Write(0u); w.Write(1u); w.Write(0xFFFFFFFFu); w.Write(0u); w.Write(1.0f);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>A v800 MDX with one geoset holding an n by n grid, 2*(n-1)^2 triangles.</summary>
    private static byte[] GridMdx(int n)
    {
        var verts = new List<float>();
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++) { verts.Add(x); verts.Add(y); verts.Add(0); }
        int V(int x, int y) => y * n + x;
        var idx = new List<int>();
        for (int y = 0; y < n - 1; y++)
            for (int x = 0; x < n - 1; x++)
            {
                idx.Add(V(x, y)); idx.Add(V(x + 1, y)); idx.Add(V(x, y + 1));
                idx.Add(V(x + 1, y)); idx.Add(V(x + 1, y + 1)); idx.Add(V(x, y + 1));
            }
        int count = verts.Count / 3;

        using var gs = new MemoryStream();
        using var gw = new BinaryWriter(gs);
        gw.Write("VRTX"u8); gw.Write((uint)count); foreach (var f in verts) gw.Write(f);
        gw.Write("NRMS"u8); gw.Write((uint)count);
        for (int i = 0; i < count; i++) { gw.Write(0f); gw.Write(0f); gw.Write(1f); }
        gw.Write("PTYP"u8); gw.Write(1u); gw.Write(4u);
        gw.Write("PCNT"u8); gw.Write(1u); gw.Write((uint)idx.Count);
        gw.Write("PVTX"u8); gw.Write((uint)idx.Count); foreach (var i in idx) gw.Write((ushort)i);
        gw.Write("GNDX"u8); gw.Write((uint)count); gw.Write(new byte[count]);
        gw.Write("MTGC"u8); gw.Write(1u); gw.Write(1u);
        gw.Write("MATS"u8); gw.Write(1u); gw.Write(0u);
        gw.Write(0u); gw.Write(0u); gw.Write(0u);
        foreach (var f in new float[] { 2, -1, -1, 0, n, n, 0 }) gw.Write(f);
        gw.Write(0u);
        gw.Write("UVAS"u8); gw.Write(1u);
        gw.Write("UVBS"u8); gw.Write((uint)count);
        for (int i = 0; i < count; i++) { gw.Write(0f); gw.Write(0f); }
        gw.Flush();
        var geoset = gs.ToArray();

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("MDLX"u8);
        w.Write("VERS"u8); w.Write(4u); w.Write(800u);
        w.Write("GEOS"u8); w.Write((uint)(geoset.Length + 4)); w.Write((uint)(geoset.Length + 4)); w.Write(geoset);
        w.Flush();
        return ms.ToArray();
    }

    [AvaloniaFact]
    public void Model_audit_shows_the_duplicate_texture_the_command_found()
    {
        var path = TempFile(".mdx", DupTextureMdx());
        try
        {
            var view = Show();
            view.SelectKind(OptimizeKind.ModelAudit);
            view.SetInput(path);
            view.Run();
            var report = PumpUntilReport(view);

            Assert.False(report.Clean);
            Assert.Contains(report.Rows, r => r.Tag == nameof(ModelIssueKind.DuplicateTexture)
                && r.Tone == ReportTone.Problem);

            // The result reached the visual tree, not only the field.
            var catalog = Field<CatalogEditorView>(view, "Catalog");
            Assert.True(Field<StackPanel>(catalog, "ItemHost").Children.Count >= 2,
                "the summary card and the finding card should both be in the tree");
            Assert.Contains("issue", Field<TextBlock>(catalog, "StatusText").Text);
        }
        finally { System.IO.File.Delete(path); }
    }

    [AvaloniaFact]
    public void Model_optimize_writes_a_smaller_model_and_reports_the_saving()
    {
        var dir = TempDir();
        var input = Path.Combine(dir, "grid.mdx");
        System.IO.File.WriteAllBytes(input, GridMdx(9));
        try
        {
            var view = Show();
            view.SelectKind(OptimizeKind.ModelOptimize);
            view.SetInput(input);
            view.SetOutput(Path.Combine(dir, "grid.optimized.mdx"));
            view.Run();
            var report = PumpUntilReport(view);

            var faces = Assert.Single(report.Facts.Where(f => f.Label == "faces"));
            Assert.Contains("->", faces.Value);
            Assert.Contains(report.Facts, f => f.Label == "output" && f.Value == "grid.optimized.mdx");
            Assert.Contains(report.Rows, r => r.Tone == ReportTone.Ok);
            Assert.True(System.IO.File.Exists(Path.Combine(dir, "grid.optimized.mdx")));
        }
        finally { Directory.Delete(dir, true); }
    }

    // --- terrain, hermetic via a synthesized blank map --------------------

    private static MapDocument Blank(int tileEdge) =>
        BlankMap.Create(new BlankMapOptions { TileEdge = tileEdge });

    [AvaloniaFact]
    public void Terrain_audit_reports_the_blank_map_and_renders_a_finding()
    {
        var view = Show();
        view.ShowMap(new MapSession { Current = Blank(8), MapPath = "synthetic.w3x" });
        view.SelectKind(OptimizeKind.TerrainAudit);
        view.Run();

        var report = view.LastReport;
        Assert.NotNull(report);
        Assert.Contains(report!.Facts, f => f.Label == "grid" && f.Value == "9 x 9 corners");
        Assert.Contains(report.Rows, r => r.Tag == nameof(TerrainIssueKind.AllZeroTerrain)
            && r.Tone == ReportTone.Problem);
    }

    [AvaloniaFact]
    public void Terrain_optimize_compacts_the_open_map_and_flags_it_edited()
    {
        var doc = Blank(8);
        var view = Show();
        bool edited = false;
        view.MapEdited += (_, _) => edited = true;

        view.ShowMap(new MapSession { Current = doc, MapPath = "synthetic.w3x" });
        view.SelectKind(OptimizeKind.TerrainOptimize);
        view.Run();

        var report = view.LastReport;
        Assert.NotNull(report);
        // The blank palette declares five ground tiles, every corner uses one, so four are dropped.
        Assert.Contains(report!.Facts, f => f.Label == "ground tiles" && f.Value.StartsWith("5 -> 1"));
        Assert.True(edited, "compacting the terrain must enable the workspace Save button");
    }

    [AvaloniaFact]
    public void Terrain_action_without_a_map_shows_a_hint()
    {
        var view = Show();
        view.SelectKind(OptimizeKind.TerrainAudit);
        view.Run();

        Assert.Null(view.LastReport);
        var catalog = Field<CatalogEditorView>(view, "Catalog");
        Assert.Contains("Open a map", Field<TextBlock>(catalog, "PlaceholderText").Text);
    }

    // --- helpers ----------------------------------------------------------

    private static OptimizeView Show()
    {
        var view = new OptimizeView();
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        return view;
    }

    private static OptimizeReport PumpUntilReport(OptimizeView view)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < Timeout)
        {
            Dispatcher.UIThread.RunJobs();
            if (view.LastReport is { } report)
                return report;
            Thread.Sleep(20);
        }
        throw new TimeoutException($"the optimise panel produced no report within {Timeout.TotalSeconds:F0}s");
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wc3-studio-optimize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string TempFile(string extension, byte[] bytes)
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "input" + extension);
        System.IO.File.WriteAllBytes(path, bytes);
        return path;
    }

    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;
}
