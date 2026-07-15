// src/Wc3.Modeling/MdxParser.cs
using System.Buffers.Binary;
using System.Text;

namespace Wc3.Modeling;

/// <summary>
/// Minimal geometry-only reader for the binary MDX format ("MDLX" chunked).
/// Extracts per-geoset positions/normals/uvs/triangles plus the material→texture
/// mapping a static render needs. Bones, animation, particles, ribbons and
/// cameras are skipped wholesale via chunk sizes. Handles classic v800 and the
/// Reforged v900/v1000 layout differences (shader name, LOD name, TANG/SKIN).
/// </summary>
internal static class MdxParser
{
    public static Model3D Parse(byte[] bytes)
    {
        if (bytes.Length < 8 || ReadTag(bytes, 0) != "MDLX")
            throw new InvalidDataException("Not an MDX file (missing MDLX magic).");

        uint version = 800;
        var textures = new List<string>();
        var materialTexture = new List<int>(); // material index -> texture index
        var raw = new List<RawGeoset>();

        int pos = 4;
        while (pos + 8 <= bytes.Length)
        {
            string tag = ReadTag(bytes, pos);
            int size = checked((int)ReadU32(bytes, pos + 4));
            int payload = pos + 8;
            int end = payload + size;
            if (size < 0 || end > bytes.Length)
                break; // truncated trailing chunk — keep what we already have

            switch (tag)
            {
                case "VERS": if (size >= 4) version = ReadU32(bytes, payload); break;
                case "TEXS": ParseTexs(bytes, payload, end, textures); break;
                case "MTLS": ParseMtls(bytes, payload, end, materialTexture); break;
                case "GEOS": ParseGeos(bytes, payload, end, version, raw); break;
            }
            pos = end;
        }

        var geosets = new List<Geoset>(raw.Count);
        foreach (var g in raw)
        {
            int textureId = g.MaterialId >= 0 && g.MaterialId < materialTexture.Count
                ? materialTexture[g.MaterialId]
                : -1;
            if (textureId < 0 || textureId >= textures.Count) textureId = -1;
            geosets.Add(new Geoset(g.Vertices, g.Normals, g.Uvs, g.Indices, textureId));
        }
        return new Model3D(geosets, textures);
    }

    private sealed record RawGeoset(float[] Vertices, float[] Normals, float[] Uvs, int[] Indices, int MaterialId);

    /// <summary>TEXS: fixed 268-byte entries (u32 replaceableId, char[260] path, u32 flags).</summary>
    private static void ParseTexs(byte[] b, int pos, int end, List<string> textures)
    {
        while (pos + 268 <= end)
        {
            uint replaceableId = ReadU32(b, pos);
            string path = ReadFixedString(b, pos + 4, 260);
            textures.Add(path.Length > 0 ? path : $"ReplaceableId:{replaceableId}");
            pos += 268;
        }
    }

    /// <summary>MTLS: per-material inclusiveSize records; we only want each material's first-layer texture id.</summary>
    private static void ParseMtls(byte[] b, int pos, int end, List<int> materialTexture)
    {
        while (pos + 8 <= end)
        {
            int inclusiveSize = checked((int)ReadU32(b, pos));
            int matEnd = pos + inclusiveSize;
            if (inclusiveSize < 12 || matEnd > end) break;

            int p = pos + 12; // inclusiveSize, priorityPlane, flags
            // v900/v1000 insert a char[80] shader name before LAYS.
            if (p + 4 <= matEnd && ReadTag(b, p) != "LAYS" && p + 84 <= matEnd && ReadTag(b, p + 80) == "LAYS")
                p += 80;

            int texId = -1;
            if (p + 8 <= matEnd && ReadTag(b, p) == "LAYS")
            {
                uint layerCount = ReadU32(b, p + 4);
                int lp = p + 8;
                // Layer: inclusiveSize, filterMode, shadingFlags, textureId, ...
                if (layerCount > 0 && lp + 16 <= matEnd)
                    texId = checked((int)ReadU32(b, lp + 12));
            }
            materialTexture.Add(texId);
            pos = matEnd;
        }
    }

    private static void ParseGeos(byte[] b, int pos, int end, uint version, List<RawGeoset> geosets)
    {
        while (pos + 4 <= end)
        {
            int inclusiveSize = checked((int)ReadU32(b, pos));
            int gEnd = pos + inclusiveSize;
            if (inclusiveSize < 4 || gEnd > end) break;
            geosets.Add(ParseGeoset(b, pos + 4, gEnd, version));
            pos = gEnd;
        }
    }

    private static RawGeoset ParseGeoset(byte[] b, int pos, int gEnd, uint version)
    {
        float[] vertices = [], normals = [], uvs = [];
        int[] indices = [];
        int materialId = -1;

        int p = pos;
        while (p + 8 <= gEnd)
        {
            string tag = ReadTag(b, p);
            int count = checked((int)ReadU32(b, p + 4));
            int data = p + 8;
            if (count < 0) throw new InvalidDataException($"Corrupt geoset: negative {tag} count.");

            switch (tag)
            {
                case "VRTX": vertices = ReadFloats(b, data, count * 3, gEnd); p = data + count * 12; break;
                case "NRMS": normals = ReadFloats(b, data, count * 3, gEnd); p = data + count * 12; break;
                case "PTYP": p = data + count * 4; break; // primitive types (4 = triangles; WC3 only uses triangles)
                case "PCNT": p = data + count * 4; break;
                case "PVTX":
                    if (data + count * 2 > gEnd) throw new InvalidDataException("Corrupt geoset: PVTX overruns.");
                    indices = new int[count];
                    for (int i = 0; i < count; i++) indices[i] = ReadU16(b, data + i * 2);
                    p = data + count * 2;
                    break;
                case "GNDX": p = data + count; break;
                case "MTGC": p = data + count * 4; break;
                case "MATS":
                    p = data + count * 4;
                    if (p + 4 <= gEnd) materialId = checked((int)ReadU32(b, p));
                    // Skip the untagged section (materialId, selectionGroup, selectionFlags,
                    // v900+ lod + char[80] name, extents) to reach the next tagged chunk.
                    p = SkipUntaggedGeosetSection(b, p, gEnd, version);
                    break;
                case "TANG": p = data + count * 16; break; // v900+: float[4] per vertex
                case "SKIN": p = data + count; break;      // v900+: raw byte blob
                case "UVAS":
                    // count = number of UV sets, each stored as a following UVBS chunk.
                    p = data;
                    for (int s = 0; s < count && p + 8 <= gEnd; s++)
                    {
                        if (ReadTag(b, p) != "UVBS") break;
                        int n = checked((int)ReadU32(b, p + 4));
                        if (n < 0) throw new InvalidDataException("Corrupt geoset: negative UVBS count.");
                        if (s == 0) uvs = ReadFloats(b, p + 8, n * 2, gEnd);
                        p = p + 8 + n * 8;
                    }
                    p = gEnd; // nothing after the UV sets that we need
                    break;
                default:
                    p = gEnd; // unknown tag — stop rather than misparse
                    break;
            }
        }
        return new RawGeoset(vertices, normals, uvs, indices, materialId);
    }

    /// <summary>
    /// After MATS + materialId the geoset carries selection info, (v900+) LOD name,
    /// and per-sequence extents with no tags. Compute the exact skip for the known
    /// layouts, falling back to a byte scan for 'UVAS'/'TANG'/'SKIN' if that lands
    /// somewhere unrecognizable.
    /// </summary>
    private static int SkipUntaggedGeosetSection(byte[] b, int p, int gEnd, uint version)
    {
        int q = p + 12; // materialId, selectionGroup, selectionFlags
        if (version >= 900) q += 84; // u32 lod + char[80] lod name
        q += 28; // extent: radius + min[3] + max[3]
        if (q + 4 <= gEnd)
        {
            int seqExtents = checked((int)ReadU32(b, q));
            if (seqExtents >= 0) q += 4 + seqExtents * 28;
        }
        if (q + 4 <= gEnd && ReadTag(b, q) is "UVAS" or "TANG" or "SKIN")
            return q;

        // Fallback: scan for the next known tag within the geoset.
        for (int i = p; i + 4 <= gEnd; i++)
        {
            if (ReadTag(b, i) is "UVAS" or "TANG" or "SKIN")
                return i;
        }
        return gEnd;
    }

    private static float[] ReadFloats(byte[] b, int pos, int count, int limit)
    {
        if (pos + count * 4 > limit) throw new InvalidDataException("Corrupt geoset: float array overruns.");
        var result = new float[count];
        for (int i = 0; i < count; i++)
            result[i] = BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(pos + i * 4, 4));
        return result;
    }

    private static string ReadTag(byte[] b, int pos)
        => Encoding.ASCII.GetString(b, pos, 4);

    private static uint ReadU32(byte[] b, int pos)
        => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos, 4));

    private static ushort ReadU16(byte[] b, int pos)
        => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pos, 2));

    private static string ReadFixedString(byte[] b, int pos, int length)
    {
        int nul = Array.IndexOf(b, (byte)0, pos, length);
        int len = nul < 0 ? length : nul - pos;
        return Encoding.UTF8.GetString(b, pos, len);
    }
}
