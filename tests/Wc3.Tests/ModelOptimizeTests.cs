// tests/Wc3.Tests/ModelOptimizeTests.cs
using System.Buffers.Binary;
using System.Text;
using Wc3.Commands;
using Wc3.Modeling;

namespace Wc3.Tests;

public sealed class ModelOptimizeTests
{
    /// <summary>A v800 MDX with one geoset holding an n by n grid, 2*(n-1)^2 triangles.</summary>
    private static byte[] GridMdx(int n, bool degenerate = false)
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
        if (degenerate) { idx.Add(V(0, 0)); idx.Add(V(1, 0)); idx.Add(V(2, 0)); } // collinear, zero area
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
        gw.Write(0u); gw.Write(0u); gw.Write(0u); // materialId, selectionGroup, selectionFlags
        foreach (var f in new float[] { 2, -1, -1, 0, n, n, 0 }) gw.Write(f); // extent
        gw.Write(0u); // per-sequence extent count
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

    [Fact]
    public void Duplicate_textures_are_pruned()
    {
        var bytes = DupTextureMdx();
        ModelOptimizeCommand.Execute(bytes, "dup.mdx", 1.0f, out var output);
        Assert.True(output.Length < bytes.Length);
        var doc = MdxDocument.Parse(output);
        Assert.Equal(268, doc.Chunks.First(c => c.Tag == "TEXS").Payload.Length); // one entry left
    }

    [Fact]
    public void Ratio_one_leaves_the_model_unchanged()
    {
        var bytes = GridMdx(9);
        ModelOptimizeCommand.Execute(bytes, "grid.mdx", 1.0f, out var output);
        Assert.Equal(bytes, output);
    }

    [Fact]
    public void Decimation_shrinks_the_geoset_and_stays_parseable()
    {
        var bytes = GridMdx(9);
        var r = ModelOptimizeCommand.Execute(bytes, "grid.mdx", 0.5f, out var output);
        Assert.Equal(1, r.Geosets);
        Assert.True(r.FacesAfter < r.FacesBefore);
        Assert.True(r.VerticesAfter < r.VerticesBefore);
        Assert.True(output.Length <= bytes.Length);

        var model = ModelParser.Parse(output, "grid.mdx");
        Assert.NotEmpty(model.Geosets);
        Assert.True(model.Geosets[0].Indices.Length > 0);
    }

    [Fact]
    public void A_zero_area_face_is_compacted_away()
    {
        var bytes = GridMdx(5, degenerate: true);
        var r = ModelOptimizeCommand.Execute(bytes, "grid.mdx", 1.0f, out var output);
        Assert.Equal(r.FacesBefore - 1, r.FacesAfter); // the collinear triangle is dropped
        var model = ModelParser.Parse(output, "grid.mdx");
        Assert.NotEmpty(model.Geosets);
    }
}
