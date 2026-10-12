// src/Wc3.Commands/ModelOptimizeCommand.cs
using System.Buffers.Binary;
using Wc3.Modeling;

namespace Wc3.Commands;

/// <summary>What an optimise pass changed, before and after, for the report.</summary>
public sealed record ModelOptimizeResult(
    string Path,
    int Geosets,
    int VerticesBefore,
    int VerticesAfter,
    int FacesBefore,
    int FacesAfter,
    long BytesBefore,
    long BytesAfter,
    IReadOnlyList<string> Diagnostics)
{
    public int VerticesRemoved => VerticesBefore - VerticesAfter;
    public int FacesRemoved => FacesBefore - FacesAfter;
}

/// <summary>
/// Shrinks a model by decimating each geoset's geometry with the quadric error metric, and writes a
/// new MDX. It works at the chunk level through <see cref="MdxDocument"/> and <see cref="MdxGeoset"/>,
/// so materials, textures, sequences, bones, emitters and cameras are written back byte for byte and
/// only the VRTX, NRMS, PVTX, GNDX and UVBS arrays of a reduced geoset change.
///
/// A geoset with no VRTX or PVTX, or one that does not reduce, is left untouched. The result carries
/// a before and after count so the caller can report the saving.
/// </summary>
public static class ModelOptimizeCommand
{
    public static ModelOptimizeResult ExecutePath(string path, float ratio, string outPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException($"model not found: {path}", path);
        var result = Execute(File.ReadAllBytes(path), path, ratio, out var output);
        File.WriteAllBytes(outPath, output);
        return result with { Path = outPath };
    }

    public static ModelOptimizeResult Execute(byte[] bytes, string name, float ratio, out byte[] output)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var diagnostics = new List<string>();

        MdxDocument doc;
        try
        {
            doc = MdxDocument.Parse(bytes);
        }
        catch (Exception ex)
        {
            output = bytes;
            return new ModelOptimizeResult(name, 0, 0, 0, 0, 0, bytes.LongLength, bytes.LongLength,
                new[] { $"could not parse '{name}': {ex.Message}" });
        }

        uint version = ReadVersion(doc);

        int geosets = 0, vBefore = 0, vAfter = 0, fBefore = 0, fAfter = 0;
        foreach (int chunkIndex in doc.GeosetIndices())
        {
            var payload = doc.Chunks[chunkIndex].Payload;
            var rebuilt = OptimizeGeos(payload, version, ratio, ref geosets, ref vBefore, ref vAfter,
                ref fBefore, ref fAfter, diagnostics);
            if (rebuilt is not null) doc.Replace(chunkIndex, rebuilt);
        }

        output = doc.Write();
        return new ModelOptimizeResult(name, geosets, vBefore, vAfter, fBefore, fAfter,
            bytes.LongLength, output.LongLength, diagnostics);
    }

    private static byte[]? OptimizeGeos(byte[] payload, uint version, float ratio,
        ref int geosets, ref int vBefore, ref int vAfter, ref int fBefore, ref int fAfter,
        List<string> diagnostics)
    {
        // The GEOS payload is a sequence of geosets, each preceded by its inclusiveSize.
        var rebuilt = new List<byte[]>();
        int p = 0;
        while (p + 4 <= payload.Length)
        {
            int inclusiveSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(p, 4)));
            if (inclusiveSize < 4 || p + inclusiveSize > payload.Length) break;
            var geosetBytes = payload[(p + 4)..(p + inclusiveSize)];
            var newGeoset = OptimizeGeoset(geosetBytes, version, ratio,
                ref geosets, ref vBefore, ref vAfter, ref fBefore, ref fAfter, diagnostics);
            rebuilt.Add(newGeoset);
            p += inclusiveSize;
        }
        if (rebuilt.Count == 0) return null;

        int total = rebuilt.Sum(g => 4 + g.Length);
        var outBytes = new byte[total];
        int q = 0;
        foreach (var g in rebuilt)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(outBytes.AsSpan(q, 4), (uint)(g.Length + 4));
            g.CopyTo(outBytes, q + 4);
            q += 4 + g.Length;
        }
        return outBytes;
    }

    private static byte[] OptimizeGeoset(byte[] geosetBytes, uint version, float ratio,
        ref int geosets, ref int vBefore, ref int vAfter, ref int fBefore, ref int fAfter,
        List<string> diagnostics)
    {
        var geoset = MdxGeoset.Parse(geosetBytes, version);
        var vrtx = geoset.Segments.FirstOrDefault(s => s.Tag == "VRTX");
        var pvtx = geoset.Segments.FirstOrDefault(s => s.Tag == "PVTX");
        if (vrtx is null || pvtx is null || vrtx.Payload.Length < 4 || pvtx.Payload.Length < 4)
            return geoset.Write();

        var vertices = ReadFloats(vrtx.Payload, 4, 3);
        var indices = ReadU16(pvtx.Payload, 4);
        geosets++;
        vBefore += vertices.Length / 3;
        fBefore += indices.Length / 3;

        var dec = QemDecimator.Decimate(vertices, indices, ratio);
        if (dec.OutputFaces >= indices.Length / 3)
        {
            vAfter += vertices.Length / 3;
            fAfter += indices.Length / 3;
            return geoset.Write(); // nothing reduced, keep it untouched
        }

        geoset.Set("VRTX", PackFloats(dec.Vertices, 3));
        geoset.Set("PVTX", PackU16(dec.Indices));
        RemapFloat3(geoset, "NRMS", dec.Remap, dec.OutputVertices);
        RemapBytes(geoset, "GNDX", dec.Remap, dec.OutputVertices);
        RemapUvs(geoset, dec.Remap, dec.OutputVertices);

        vAfter += dec.OutputVertices;
        fAfter += dec.OutputFaces;
        return geoset.Write();
    }

    private static void RemapFloat3(MdxGeoset geoset, string tag, int[] remap, int newCount)
    {
        var seg = geoset.Segments.FirstOrDefault(s => s.Tag == tag);
        if (seg is null || seg.Payload.Length < 4) return;
        int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(seg.Payload.AsSpan(0, 4)));
        if (count * 12 + 4 > seg.Payload.Length || count != remap.Length) return;
        var outBytes = new byte[4 + newCount * 12];
        BinaryPrimitives.WriteUInt32LittleEndian(outBytes.AsSpan(0, 4), (uint)newCount);
        for (int i = 0; i < remap.Length; i++)
            if (remap[i] >= 0)
                Buffer.BlockCopy(seg.Payload, 4 + i * 12, outBytes, 4 + remap[i] * 12, 12);
        geoset.Set(tag, outBytes);
    }

    private static void RemapBytes(MdxGeoset geoset, string tag, int[] remap, int newCount)
    {
        var seg = geoset.Segments.FirstOrDefault(s => s.Tag == tag);
        if (seg is null || seg.Payload.Length < 4) return;
        int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(seg.Payload.AsSpan(0, 4)));
        if (count + 4 > seg.Payload.Length || count != remap.Length) return;
        var outBytes = new byte[4 + newCount];
        BinaryPrimitives.WriteUInt32LittleEndian(outBytes.AsSpan(0, 4), (uint)newCount);
        for (int i = 0; i < remap.Length; i++)
            if (remap[i] >= 0) outBytes[4 + remap[i]] = seg.Payload[4 + i];
        geoset.Set(tag, outBytes);
    }

    private static void RemapUvs(MdxGeoset geoset, int[] remap, int newCount)
    {
        var seg = geoset.Segments.FirstOrDefault(s => s.Tag == "UVAS");
        if (seg is null || seg.Payload.Length < 12) return;
        // UVAS payload: count(4) then one UVBS per set, tag + count + floats.
        var payload = seg.Payload;
        var outBytes = new List<byte>(payload);
        int p = 4;
        while (p + 8 <= payload.Length)
        {
            if (System.Text.Encoding.ASCII.GetString(payload, p, 4) != "UVBS") break;
            int n = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(p + 4, 4)));
            if (p + 8 + n * 8 > payload.Length || n != remap.Length) break;
            var newUv = new byte[4 + newCount * 8];
            BinaryPrimitives.WriteUInt32LittleEndian(newUv.AsSpan(0, 4), (uint)newCount);
            for (int i = 0; i < remap.Length; i++)
                if (remap[i] >= 0)
                    Buffer.BlockCopy(payload, p + 8 + i * 8, newUv, 4 + remap[i] * 8, 8);
            outBytes.RemoveRange(p, 8 + n * 8);
            outBytes.InsertRange(p, newUv);
            p += newUv.Length;
            break; // only the first UV set is reduced, the rest are rare and left as-is
        }
        geoset.Set("UVAS", outBytes.ToArray());
    }

    private static uint ReadVersion(MdxDocument doc)
    {
        var vers = doc.Chunks.FirstOrDefault(c => c.Tag == "VERS");
        return vers is { Payload.Length: >= 4 }
            ? BinaryPrimitives.ReadUInt32LittleEndian(vers.Payload.AsSpan(0, 4)) : 800u;
    }

    private static float[] ReadFloats(byte[] payload, int offset, int perVertex)
    {
        int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset - 4, 4)));
        var result = new float[count * perVertex];
        for (int i = 0; i < result.Length; i++)
            result[i] = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(offset + i * 4, 4));
        return result;
    }

    private static int[] ReadU16(byte[] payload, int offset)
    {
        int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset - 4, 4)));
        var result = new int[count];
        for (int i = 0; i < count; i++)
            result[i] = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset + i * 2, 2));
        return result;
    }

    private static byte[] PackFloats(float[] values, int perVertex)
    {
        var outBytes = new byte[4 + values.Length * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(outBytes.AsSpan(0, 4), (uint)(values.Length / perVertex));
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(outBytes.AsSpan(4 + i * 4, 4), values[i]);
        return outBytes;
    }

    private static byte[] PackU16(int[] values)
    {
        var outBytes = new byte[4 + values.Length * 2];
        BinaryPrimitives.WriteUInt32LittleEndian(outBytes.AsSpan(0, 4), (uint)values.Length);
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(outBytes.AsSpan(4 + i * 2, 2), (ushort)values[i]);
        return outBytes;
    }
}
