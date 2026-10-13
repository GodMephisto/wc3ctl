// src/Wc3.Commands/ModelImportCommand.cs
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using Wc3.Modeling;

namespace Wc3.Commands;

/// <summary>
/// What an OBJ import produced, with the counts the caller reports and the MDX bytes to write.
/// <see cref="Mdx"/> is the assembled model, so a caller can write it wherever it likes, and
/// <see cref="Bytes"/> is its length.
/// </summary>
public sealed record ModelImportResult(
    string Path,
    int Geosets,
    int Vertices,
    int Faces,
    int Textures,
    long Bytes,
    byte[] Mdx,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Imports a Wavefront OBJ text into a Warcraft III MDX. It reads position, texture and normal
/// declarations and the face lines in their three forms (v, v/vt, v/vt/vn, and v//vn), builds one
/// v800 GEOS geoset (VRTX, NRMS, PTYP, PCNT, PVTX, GNDX, MTGC, MATS, the untagged tail and
/// UVAS/UVBS) plus one TEXS texture and one MTLS material, and writes the chunks through
/// <see cref="MdxDocument"/> and <see cref="MdxGeoset"/>. An n-gon face is split into a triangle
/// fan. When the OBJ carries no normals a per-face normal is computed and used for that face's
/// corners, so flat shading survives the import.
///
/// Coordinates pass through unchanged, since <see cref="ObjExporter"/> writes them in WC3's Z-up
/// convention, and the OBJ V axis is flipped back to WC3's top-down sense, so an export followed by
/// an import round-trips the geometry.
/// </summary>
public static class ModelImportCommand
{
    /// <summary>The geoset segment order the game writes, reused for the untagged tail.</summary>
    private const uint Version = 800u;

    public static ModelImportResult ExecutePath(string objPath, string outPath, string? texturePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objPath);
        if (!File.Exists(objPath)) throw new FileNotFoundException($"OBJ not found: {objPath}", objPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outPath);

        var result = Execute(File.ReadAllText(objPath), objPath, out var output, texturePath);
        var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (dir is { Length: > 0 }) Directory.CreateDirectory(dir);
        File.WriteAllBytes(outPath, output);
        return result with { Path = outPath };
    }

    /// <summary>
    /// Parses <paramref name="objText"/> and returns the assembled MDX in <paramref name="output"/>.
    /// <paramref name="name"/> is used for reporting and for the default texture path. Throws
    /// <see cref="ArgumentException"/> on an OBJ with no faces or an out-of-range index.
    /// </summary>
    public static ModelImportResult Execute(string objText, string name, out byte[] output, string? texturePath = null)
    {
        ArgumentNullException.ThrowIfNull(objText);

        var positions = new List<Vector3>();
        var texcoords = new List<Vector2>();
        var normals = new List<Vector3>();
        var faces = new List<Corner[]>();

        foreach (var raw in objText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int sp = line.IndexOf(' ');
            if (sp < 0) continue;
            string keyword = line[..sp];
            var parts = line[(sp + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (keyword)
            {
                case "v" when parts.Length >= 3:
                    positions.Add(new Vector3(F(parts[0]), F(parts[1]), F(parts[2])));
                    break;
                case "vt" when parts.Length >= 2:
                    texcoords.Add(new Vector2(F(parts[0]), F(parts[1])));
                    break;
                case "vn" when parts.Length >= 3:
                    normals.Add(new Vector3(F(parts[0]), F(parts[1]), F(parts[2])));
                    break;
                case "f" when parts.Length >= 3:
                    faces.Add(ParseFace(parts, positions.Count, texcoords.Count, normals.Count));
                    break;
            }
        }

        if (positions.Count == 0 || faces.Count == 0)
            throw new ArgumentException($"OBJ '{name}' holds no drawable geometry (no v/f lines)");

        var (vList, nList, uvList, indices) = BuildMesh(positions, texcoords, normals, faces);
        int vertexCount = vList.Count / 3;
        int triangleCount = indices.Count / 3;

        var texture = string.IsNullOrWhiteSpace(texturePath)
            ? DefaultTexturePath(name)
            : texturePath!;

        var geosetBytes = BuildGeoset(vList, nList, uvList, indices).Write();
        var geosPayload = new byte[4 + geosetBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(geosPayload.AsSpan(0, 4), (uint)(geosetBytes.Length + 4));
        geosetBytes.CopyTo(geosPayload, 4);

        var doc = MdxDocument.Create(new[]
        {
            new MdxDocument.Chunk("VERS", U32(Version)),
            new MdxDocument.Chunk("TEXS", BuildTexs(texture)),
            new MdxDocument.Chunk("MTLS", BuildMtls()),
            new MdxDocument.Chunk("GEOS", geosPayload),
        });

        output = doc.Write();
        return new ModelImportResult(name, 1, vertexCount, triangleCount, 1, output.LongLength,
            output, Array.Empty<string>());
    }

    /// <summary>One face corner, with -1 for a missing texture or normal index.</summary>
    private readonly record struct Corner(int V, int Vt, int Vn);

    private static Corner[] ParseFace(string[] tokens, int vertexCount, int texCount, int normalCount)
    {
        var corners = new Corner[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            var fields = tokens[i].Split('/');
            int v = Resolve(fields[0], vertexCount, "vertex");
            int vt = fields.Length >= 2 && fields[1].Length > 0 ? Resolve(fields[1], texCount, "texture") : -1;
            int vn = fields.Length >= 3 && fields[2].Length > 0 ? Resolve(fields[2], normalCount, "normal") : -1;
            corners[i] = new Corner(v, vt, vn);
        }
        return corners;
    }

    /// <summary>OBJ indices are 1-based, and negative indices count back from the current end.</summary>
    private static int Resolve(string field, int count, string kind)
    {
        if (!int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            throw new ArgumentException($"OBJ face has a non-numeric {kind} index '{field}'");
        int resolved = index > 0 ? index - 1 : count + index;
        if (resolved < 0 || resolved >= count)
            throw new ArgumentException($"OBJ face {kind} index {index} is out of range (have {count})");
        return resolved;
    }

    /// <summary>
    /// Deduplicates corners into per-vertex position, normal and UV arrays plus a triangle index
    /// list. A corner with a real normal keys on it. A corner without one keys on its face, and the
    /// face's computed normal is written into that vertex, so a face keeps flat shading and a shared
    /// quad still collapses to its four corners.
    /// </summary>
    private static (List<float> Vertices, List<float> Normals, List<float> Uvs, List<int> Indices)
        BuildMesh(List<Vector3> positions, List<Vector2> texcoords, List<Vector3> normals, List<Corner[]> faces)
    {
        var vertices = new List<float>();
        var vertexNormals = new List<float>();
        var uvs = new List<float>();
        var indices = new List<int>();
        var map = new Dictionary<(int V, int Vt, int Vn), int>();

        int IndexOf(Corner c, int face, Vector3 faceNormal)
        {
            int vn = c.Vn >= 0 ? c.Vn : -(face + 1); // a face-local key when the OBJ gives no normal
            if (map.TryGetValue((c.V, c.Vt, vn), out int existing)) return existing;

            int index = vertices.Count / 3;
            var p = positions[c.V];
            vertices.Add(p.X); vertices.Add(p.Y); vertices.Add(p.Z);

            var n = c.Vn >= 0 ? normals[c.Vn] : faceNormal;
            n = n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : new Vector3(0, 0, 1);
            vertexNormals.Add(n.X); vertexNormals.Add(n.Y); vertexNormals.Add(n.Z);

            var uv = c.Vt >= 0 ? texcoords[c.Vt] : Vector2.Zero;
            uvs.Add(uv.X); uvs.Add(1f - uv.Y); // OBJ V is bottom-up, WC3 V is top-down
            map[(c.V, c.Vt, vn)] = index;
            return index;
        }

        for (int f = 0; f < faces.Count; f++)
        {
            var face = faces[f];
            var faceNormal = FaceNormal(face, positions);
            for (int k = 1; k + 1 < face.Length; k++) // triangle fan
            {
                indices.Add(IndexOf(face[0], f, faceNormal));
                indices.Add(IndexOf(face[k], f, faceNormal));
                indices.Add(IndexOf(face[k + 1], f, faceNormal));
            }
        }
        return (vertices, vertexNormals, uvs, indices);
    }

    /// <summary>The polygon normal by Newell's method, so a non-planar or n-gon face still gets one.</summary>
    private static Vector3 FaceNormal(Corner[] face, List<Vector3> positions)
    {
        var n = Vector3.Zero;
        for (int i = 0; i < face.Length; i++)
        {
            var p = positions[face[i].V];
            var q = positions[face[(i + 1) % face.Length].V];
            n.X += (p.Y - q.Y) * (p.Z + q.Z);
            n.Y += (p.Z - q.Z) * (p.X + q.X);
            n.Z += (p.X - q.X) * (p.Y + q.Y);
        }
        return n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : new Vector3(0, 0, 1);
    }

    private static MdxGeoset BuildGeoset(
        List<float> vertices, List<float> normals, List<float> uvs, List<int> indices)
    {
        int vertexCount = vertices.Count / 3;
        int triangleCount = indices.Count / 3;
        var (radius, min, max) = Bounds(vertices);

        return MdxGeoset.Create(new[]
        {
            FloatSegment("VRTX", vertices, vertexCount),
            FloatSegment("NRMS", normals, vertexCount),
            U32Segment("PTYP", new[] { 4u }),        // 4 = triangle list
            U32Segment("PCNT", new[] { (uint)triangleCount }),
            U16Segment("PVTX", indices),
            BytesSegment("GNDX", new byte[vertexCount]), // every vertex sits in matrix group 0
            U32Segment("MTGC", new[] { 1u }),
            U32Segment("MATS", new[] { 0u }),
            Tail(radius, min, max),
            UvasSegment(uvs, vertexCount),
        }, Version);
    }

    /// <summary>count + x,y,z per vertex.</summary>
    private static MdxGeoset.Segment FloatSegment(string tag, List<float> values, int count)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((uint)count);
        foreach (var f in values) w.Write(f);
        w.Flush();
        return new MdxGeoset.Segment(tag, ms.ToArray());
    }

    private static MdxGeoset.Segment U16Segment(string tag, List<int> values)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((uint)values.Count);
        foreach (var v in values) w.Write((ushort)v);
        w.Flush();
        return new MdxGeoset.Segment(tag, ms.ToArray());
    }

    private static MdxGeoset.Segment U32Segment(string tag, uint[] values)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((uint)values.Length);
        foreach (var v in values) w.Write(v);
        w.Flush();
        return new MdxGeoset.Segment(tag, ms.ToArray());
    }

    private static MdxGeoset.Segment BytesSegment(string tag, byte[] values)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((uint)values.Length);
        w.Write(values);
        w.Flush();
        return new MdxGeoset.Segment(tag, ms.ToArray());
    }

    /// <summary>The untagged tail, material id, selection fields, extent and per-sequence extents.</summary>
    private static MdxGeoset.Segment Tail(float radius, Vector3 min, Vector3 max)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(0u);            // materialId
        w.Write(0u);            // selectionGroup
        w.Write(0u);            // selectionFlags
        w.Write(radius);        // extent radius
        w.Write(min.X); w.Write(min.Y); w.Write(min.Z);
        w.Write(max.X); w.Write(max.Y); w.Write(max.Z);
        w.Write(0u);            // per-sequence extent count
        w.Flush();
        return new MdxGeoset.Segment("", ms.ToArray());
    }

    /// <summary>UVAS, one UV set. Payload is the set count then a UVBS with count + u,v per vertex.</summary>
    private static MdxGeoset.Segment UvasSegment(List<float> uvs, int vertexCount)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(1u);            // number of UV sets
        w.Write("UVBS"u8);
        w.Write((uint)vertexCount);
        foreach (var f in uvs) w.Write(f);
        w.Flush();
        return new MdxGeoset.Segment("UVAS", ms.ToArray());
    }

    private static (float Radius, Vector3 Min, Vector3 Max) Bounds(List<float> vertices)
    {
        if (vertices.Count == 0) return (0f, Vector3.Zero, Vector3.Zero);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i + 2 < vertices.Count; i += 3)
        {
            min = Vector3.Min(min, new Vector3(vertices[i], vertices[i + 1], vertices[i + 2]));
            max = Vector3.Max(max, new Vector3(vertices[i], vertices[i + 1], vertices[i + 2]));
        }
        var center = (min + max) * 0.5f;
        float radius = 0f;
        for (int i = 0; i + 2 < vertices.Count; i += 3)
            radius = MathF.Max(radius, Vector3.Distance(center, new Vector3(vertices[i], vertices[i + 1], vertices[i + 2])));
        return (radius, min, max);
    }

    /// <summary>One 268-byte TEXS row, replaceableId 0 then a fixed 260-byte path then flags 0.</summary>
    private static byte[] BuildTexs(string path)
    {
        var entry = new byte[268];
        var bytes = Encoding.ASCII.GetBytes(path);
        Array.Copy(bytes, 0, entry, 4, Math.Min(bytes.Length, 260));
        return entry;
    }

    /// <summary>One material with one LAYS layer referencing texture 0.</summary>
    private static byte[] BuildMtls()
    {
        const uint layerSize = 28u;
        uint matSize = 4 + 4 + 4 + 8 + layerSize; // inclusiveSize, priorityPlane, flags, LAYS+count, layer
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(matSize);
        w.Write(0u);            // priorityPlane
        w.Write(0u);            // flags
        w.Write("LAYS"u8);
        w.Write(1u);            // layer count
        w.Write(layerSize);
        w.Write(0u);            // filterMode, opaque
        w.Write(0u);            // shadingFlags
        w.Write(0u);            // textureId 0
        w.Write(0xFFFFFFFFu);   // textureAnimationId
        w.Write(0u);            // coordId
        w.Write(1.0f);          // alpha
        w.Flush();
        return ms.ToArray();
    }

    private static string DefaultTexturePath(string name)
    {
        string baseName = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Imported";
        return "war3mapImported\\" + baseName + ".blp";
    }

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static float F(string s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
}
