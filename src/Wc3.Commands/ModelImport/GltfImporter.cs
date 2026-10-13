// src/Wc3.Commands/ModelImport/GltfImporter.cs
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Wc3.Commands;

/// <summary>
/// Parses a glTF 2.0 model into a <see cref="MeshData"/>, both the .gltf JSON form and the .glb
/// binary container. Reads the first mesh primitive's POSITION, NORMAL and TEXCOORD_0 accessors and
/// its indices, honouring the accessor componentType (float, unsigned byte, unsigned short, unsigned
/// int, signed views), the normalized flag, and the bufferView byteOffset and byteStride. A buffer
/// URI is either a data URI holding base64 or a path relative to the .gltf, and a .glb buffer with
/// no URI is the binary chunk. Triangle lists are used directly, and a triangle strip or fan is
/// converted. glTF texture coordinates already have a top-left origin, so V is left unflipped.
/// </summary>
internal static class GltfImporter
{
    public static MeshData ParseGltf(string json, string name, string? baseDir)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var doc = JsonDocument.Parse(json);
        var buffers = ResolveBuffers(doc.RootElement, baseDir, name, glbBin: null);
        return Extract(doc.RootElement, buffers, name);
    }

    public static MeshData ParseGlb(byte[] bytes, string name)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 12 || bytes[0] != 'g' || bytes[1] != 'l' || bytes[2] != 'T' || bytes[3] != 'F')
            throw new InvalidDataException("not a GLB file, no glTF magic");

        int p = 12;
        string? json = null;
        byte[]? bin = null;
        while (p + 8 <= bytes.Length)
        {
            int chunkLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p, 4)));
            uint chunkType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p + 4, 4));
            p += 8;
            if (chunkLength < 0 || p + chunkLength > bytes.Length) break;
            var span = bytes.AsSpan(p, chunkLength);
            if (chunkType == 0x4E4F534A) json = Encoding.UTF8.GetString(span);       // "JSON"
            else if (chunkType == 0x004E4942) bin = span.ToArray();                  // "BIN\0"
            p += chunkLength;
        }
        if (json is null) throw new InvalidDataException("GLB has no JSON chunk");

        using var doc = JsonDocument.Parse(json);
        var buffers = ResolveBuffers(doc.RootElement, baseDir: null, name, bin);
        return Extract(doc.RootElement, buffers, name);
    }

    private static List<byte[]> ResolveBuffers(JsonElement root, string? baseDir, string name, byte[]? glbBin)
    {
        var buffers = new List<byte[]>();
        if (!root.TryGetProperty("buffers", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return buffers;

        foreach (var buffer in arr.EnumerateArray())
        {
            if (buffer.TryGetProperty("uri", out var uriEl) && uriEl.ValueKind == JsonValueKind.String)
                buffers.Add(LoadUri(uriEl.GetString()!, baseDir, name));
            else if (glbBin is not null)
                buffers.Add(glbBin);
            else
                throw new InvalidDataException($"glTF '{name}' has a buffer with no URI and no binary chunk");
        }
        return buffers;
    }

    private static byte[] LoadUri(string uri, string? baseDir, string name)
    {
        int comma = uri.IndexOf(',');
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
        {
            string meta = uri[..comma];
            string payload = uri[(comma + 1)..];
            return meta.Contains(";base64", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(payload)
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
        }
        if (baseDir is null)
            throw new InvalidDataException($"glTF '{name}' references the external buffer '{uri}', which needs the file on disk");
        return File.ReadAllBytes(Path.Combine(baseDir, Uri.UnescapeDataString(uri).Replace('/', Path.DirectorySeparatorChar)));
    }

    private static MeshData Extract(JsonElement root, List<byte[]> buffers, string name)
    {
        if (!root.TryGetProperty("meshes", out var meshes) || meshes.ValueKind != JsonValueKind.Array || meshes.GetArrayLength() == 0)
            throw new ArgumentException($"glTF '{name}' has no meshes");

        var primitives = meshes[0].GetProperty("primitives");
        if (primitives.GetArrayLength() == 0)
            throw new ArgumentException($"glTF '{name}' has no primitives");
        var primitive = primitives[0];
        var attributes = primitive.GetProperty("attributes");

        if (!attributes.TryGetProperty("POSITION", out var positionAccessor))
            throw new ArgumentException($"glTF '{name}' primitive has no POSITION attribute");

        var positions = ToVectors(ReadFloats(root, buffers, positionAccessor.GetInt32(), 3), 3);
        int vertexCount = positions.Count;

        var normals = attributes.TryGetProperty("NORMAL", out var normalAccessor)
            ? ToVectors(ReadFloats(root, buffers, normalAccessor.GetInt32(), 3), 3)
            : new List<Vector3>();
        var uvs = attributes.TryGetProperty("TEXCOORD_0", out var uvAccessor)
            ? ToVectors2(ReadFloats(root, buffers, uvAccessor.GetInt32(), 2))
            : new List<Vector2>();

        bool hasNormals = normals.Count == vertexCount;
        bool hasUvs = uvs.Count == vertexCount;

        int[] indices;
        if (primitive.TryGetProperty("indices", out var indexAccessor))
            indices = ReadIndices(root, buffers, indexAccessor.GetInt32());
        else
        {
            indices = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++) indices[i] = i;
        }

        int mode = primitive.TryGetProperty("mode", out var modeEl) ? modeEl.GetInt32() : 4;
        indices = ToTriangles(indices, mode, name);

        var faces = new List<MeshCorner[]>(indices.Length / 3);
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            faces.Add(new[]
            {
                new MeshCorner(a, hasNormals ? a : -1, hasUvs ? a : -1),
                new MeshCorner(b, hasNormals ? b : -1, hasUvs ? b : -1),
                new MeshCorner(c, hasNormals ? c : -1, hasUvs ? c : -1),
            });
        }

        return new MeshData(positions, normals, uvs, faces, FlipV: false);
    }

    private static int[] ToTriangles(int[] indices, int mode, string name)
    {
        switch (mode)
        {
            case 4: // TRIANGLES
                {
                    int usable = indices.Length - indices.Length % 3;
                    return usable == indices.Length ? indices : indices[..usable];
                }
            case 5: // TRIANGLE_STRIP
                {
                    var tri = new List<int>();
                    for (int i = 0; i + 2 < indices.Length; i++)
                    {
                        if ((i & 1) == 0) { tri.Add(indices[i]); tri.Add(indices[i + 1]); tri.Add(indices[i + 2]); }
                        else { tri.Add(indices[i + 1]); tri.Add(indices[i]); tri.Add(indices[i + 2]); }
                    }
                    return tri.ToArray();
                }
            case 6: // TRIANGLE_FAN
                {
                    var tri = new List<int>();
                    for (int i = 1; i + 1 < indices.Length; i++)
                    {
                        tri.Add(indices[0]); tri.Add(indices[i]); tri.Add(indices[i + 1]);
                    }
                    return tri.ToArray();
                }
            default:
                throw new NotSupportedException($"glTF '{name}' primitive mode {mode} is not drawable geometry (need triangles)");
        }
    }

    private readonly record struct Accessor(
        int ComponentType, int Count, bool Normalized, bool HasView, int BufferIndex, int ByteOffset, int Stride, int Comps);

    private static Accessor ReadAccessor(JsonElement root, int index, int comps)
    {
        var acc = root.GetProperty("accessors")[index];
        int componentType = acc.GetProperty("componentType").GetInt32();
        int count = acc.GetProperty("count").GetInt32();
        bool normalized = acc.TryGetProperty("normalized", out var nz) && nz.GetBoolean();
        if (!acc.TryGetProperty("bufferView", out var viewEl))
            return new Accessor(componentType, count, normalized, false, 0, 0, 0, comps);

        var view = root.GetProperty("bufferViews")[viewEl.GetInt32()];
        int bufferIndex = view.GetProperty("buffer").GetInt32();
        int viewOffset = view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0;
        int stride = view.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : 0;
        int accOffset = acc.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
        int compSize = ComponentSize(componentType);
        if (stride <= 0) stride = compSize * comps;
        return new Accessor(componentType, count, normalized, true, bufferIndex, viewOffset + accOffset, stride, comps);
    }

    private static float[] ReadFloats(JsonElement root, List<byte[]> buffers, int index, int comps)
    {
        var acc = ReadAccessor(root, index, comps);
        var result = new float[acc.Count * comps];
        if (!acc.HasView) return result; // no bufferView means the accessor is zero-filled
        var buffer = buffers[acc.BufferIndex];
        int compSize = ComponentSize(acc.ComponentType);
        for (int i = 0; i < acc.Count; i++)
            for (int c = 0; c < comps; c++)
            {
                int off = acc.ByteOffset + i * acc.Stride + c * compSize;
                result[i * comps + c] = ReadComponent(buffer, off, acc.ComponentType, acc.Normalized);
            }
        return result;
    }

    private static int[] ReadIndices(JsonElement root, List<byte[]> buffers, int index)
    {
        var acc = ReadAccessor(root, index, 1);
        var result = new int[acc.Count];
        if (!acc.HasView) return result;
        var buffer = buffers[acc.BufferIndex];
        int compSize = ComponentSize(acc.ComponentType);
        for (int i = 0; i < acc.Count; i++)
        {
            int off = acc.ByteOffset + i * acc.Stride;
            result[i] = (int)ReadComponent(buffer, off, acc.ComponentType, false);
        }
        return result;
    }

    private static float ReadComponent(byte[] buffer, int offset, int componentType, bool normalized)
    {
        switch (componentType)
        {
            case 5126: return BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(offset, 4));
            case 5125: return BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4));
            case 5123:
                {
                    ushort v = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2));
                    return normalized ? v / 65535f : v;
                }
            case 5122:
                {
                    short v = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset, 2));
                    return normalized ? MathF.Max(v / 32767f, -1f) : v;
                }
            case 5121:
                {
                    byte v = buffer[offset];
                    return normalized ? v / 255f : v;
                }
            case 5120:
                {
                    sbyte v = unchecked((sbyte)buffer[offset]);
                    return normalized ? MathF.Max(v / 127f, -1f) : v;
                }
            default:
                throw new InvalidDataException($"glTF has an unknown accessor componentType {componentType}");
        }
    }

    private static int ComponentSize(int componentType) => componentType switch
    {
        5120 or 5121 => 1,
        5122 or 5123 => 2,
        5125 or 5126 => 4,
        _ => throw new InvalidDataException($"glTF has an unknown accessor componentType {componentType}"),
    };

    private static List<Vector3> ToVectors(float[] values, int comps)
    {
        var list = new List<Vector3>(values.Length / comps);
        for (int i = 0; i + comps <= values.Length; i += comps)
            list.Add(new Vector3(values[i], values[i + 1], values[i + 2]));
        return list;
    }

    private static List<Vector2> ToVectors2(float[] values)
    {
        var list = new List<Vector2>(values.Length / 2);
        for (int i = 0; i + 2 <= values.Length; i += 2)
            list.Add(new Vector2(values[i], values[i + 1]));
        return list;
    }
}
