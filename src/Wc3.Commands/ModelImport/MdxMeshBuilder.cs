// src/Wc3.Commands/ModelImport/MdxMeshBuilder.cs
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Wc3.Modeling;

namespace Wc3.Commands;

/// <summary>
/// The one path from a <see cref="MeshData"/> to a Warcraft III MDX. Every format importer, OBJ
/// included, ends here, so the geoset layout (VRTX, NRMS, PTYP, PCNT, PVTX, GNDX, MTGC, MATS, the
/// untagged tail and UVAS/UVBS), the TEXS texture and the MTLS material are written once.
///
/// Corners are deduplicated into per-vertex position, normal and UV arrays plus a triangle index
/// list. A corner with a real normal keys on it. A corner without one keys on its face, and the
/// face's computed normal is written into that vertex, so a face keeps flat shading and a shared
/// quad still collapses to its four corners. An n-gon face is split into a triangle fan.
/// </summary>
internal static class MdxMeshBuilder
{
    private const uint Version = 800u;

    public static ModelImportResult Build(MeshData mesh, string name, string? texturePath, out byte[] output)
    {
        var (vList, nList, uvList, indices) = BuildMesh(mesh);
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

    private static (List<float> Vertices, List<float> Normals, List<float> Uvs, List<int> Indices)
        BuildMesh(MeshData mesh)
    {
        var vertices = new List<float>();
        var vertexNormals = new List<float>();
        var uvs = new List<float>();
        var indices = new List<int>();
        var map = new Dictionary<(int V, int Uv, int Key), int>();

        int IndexOf(MeshCorner c, int face, Vector3 faceNormal)
        {
            int key = c.Normal >= 0 ? c.Normal : -(face + 1); // a face-local key when the source gives no normal
            if (map.TryGetValue((c.Position, c.Uv, key), out int existing)) return existing;

            int index = vertices.Count / 3;
            var p = mesh.Positions[c.Position];
            vertices.Add(p.X); vertices.Add(p.Y); vertices.Add(p.Z);

            var n = c.Normal >= 0 ? mesh.Normals[c.Normal] : faceNormal;
            n = n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : new Vector3(0, 0, 1);
            vertexNormals.Add(n.X); vertexNormals.Add(n.Y); vertexNormals.Add(n.Z);

            var uv = c.Uv >= 0 ? mesh.Uvs[c.Uv] : Vector2.Zero;
            uvs.Add(uv.X); uvs.Add(mesh.FlipV ? 1f - uv.Y : uv.Y);
            map[(c.Position, c.Uv, key)] = index;
            return index;
        }

        for (int f = 0; f < mesh.Faces.Count; f++)
        {
            var face = mesh.Faces[f];
            var faceNormal = FaceNormal(face, mesh.Positions);
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
    private static Vector3 FaceNormal(MeshCorner[] face, IReadOnlyList<Vector3> positions)
    {
        var n = Vector3.Zero;
        for (int i = 0; i < face.Length; i++)
        {
            var p = positions[face[i].Position];
            var q = positions[face[(i + 1) % face.Length].Position];
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
}
