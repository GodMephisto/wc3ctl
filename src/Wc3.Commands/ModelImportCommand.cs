// src/Wc3.Commands/ModelImportCommand.cs
using System.Text;
using Wc3.Modeling;

namespace Wc3.Commands;

/// <summary>
/// What a model import produced, with the counts the caller reports and the MDX bytes to write.
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
/// Imports a foreign model into a Warcraft III MDX. The format is chosen by extension and then by
/// content, so a renamed or extensionless file is still read correctly, and every format runs
/// through the same MDX path.
///
/// Wavefront OBJ reads position, texture and normal declarations and the v/vt/vn face forms, and its
/// V axis is flipped back to WC3's top-down sense, so an <see cref="ObjExporter"/> export followed
/// by an import round-trips the geometry. glTF 2.0 (.gltf JSON with external or embedded base64
/// buffers, and the .glb binary container) reads the first mesh primitive's POSITION, NORMAL and
/// TEXCOORD_0 accessors and its indices. STL reads the binary form (a triangle count at offset 80)
/// and the ASCII form, and carries no UVs. PLY reads ASCII and binary little-endian vertex x/y/z,
/// nx/ny/nz and s/t plus the face index list.
///
/// The result is one v800 GEOS geoset with normals and UVs, plus one TEXS texture and one MTLS
/// material. An n-gon face is split into a triangle fan. When a format carries no normals a
/// per-face normal is computed and used for that face's corners, so flat shading survives the
/// import, and when it carries no UVs the corners get a zero UV.
/// </summary>
public static class ModelImportCommand
{
    /// <summary>
    /// Imports the model at <paramref name="modelPath"/> and writes the MDX to
    /// <paramref name="outPath"/>. The format is detected from the file, not the extension alone.
    /// </summary>
    public static ModelImportResult ExecutePath(string modelPath, string outPath, string? texturePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        if (!File.Exists(modelPath)) throw new FileNotFoundException($"Model not found: {modelPath}", modelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outPath);

        var bytes = File.ReadAllBytes(modelPath);
        string? baseDir = Path.GetDirectoryName(Path.GetFullPath(modelPath));
        var result = Import(bytes, modelPath, baseDir, texturePath, out var output);

        var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (dir is { Length: > 0 }) Directory.CreateDirectory(dir);
        File.WriteAllBytes(outPath, output);
        return result with { Path = outPath };
    }

    /// <summary>
    /// Imports model text already in memory, kept for the OBJ text path. <paramref name="name"/> is
    /// used for reporting, format detection and the default texture path. Throws
    /// <see cref="ArgumentException"/> on a model with no drawable geometry or an out-of-range index.
    /// </summary>
    public static ModelImportResult Execute(string modelText, string name, out byte[] output, string? texturePath = null)
    {
        ArgumentNullException.ThrowIfNull(modelText);
        return Import(Encoding.UTF8.GetBytes(modelText), name, baseDir: null, texturePath, out output);
    }

    /// <summary>Imports raw model bytes, for a caller that already read the file.</summary>
    public static ModelImportResult Execute(byte[] modelBytes, string name, out byte[] output,
        string? texturePath = null, string? baseDir = null)
    {
        ArgumentNullException.ThrowIfNull(modelBytes);
        return Import(modelBytes, name, baseDir, texturePath, out output);
    }

    private static ModelImportResult Import(byte[] bytes, string name, string? baseDir, string? texturePath, out byte[] output)
    {
        var format = ModelFormatDetector.Detect(name, bytes);

        MeshData mesh = format switch
        {
            ModelFormat.Obj => ObjImporter.Parse(DecodeText(bytes), name),
            ModelFormat.Gltf => GltfImporter.ParseGltf(DecodeText(bytes), name, baseDir),
            ModelFormat.Glb => GltfImporter.ParseGlb(bytes, name),
            ModelFormat.Stl => StlImporter.Parse(bytes),
            ModelFormat.Ply => PlyImporter.Parse(bytes),
            _ => throw new NotSupportedException($"Unsupported model format for '{name}'"),
        };

        if (mesh.Positions.Count == 0 || mesh.Faces.Count == 0)
            throw new ArgumentException($"'{name}' holds no drawable geometry");

        return MdxMeshBuilder.Build(mesh, name, texturePath, out output);
    }

    private static string DecodeText(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text; // strip BOM
    }
}
