// tests/Wc3.Tests/ModelOptimizeTests.cs
using System.Buffers.Binary;
using Wc3.Commands;
using Wc3.Modeling;

namespace Wc3.Tests;

public sealed class ModelOptimizeTests
{
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
}
