// src/Wc3.Commands/ModelImport/ModelFormatDetector.cs
using System.Buffers.Binary;
using System.Text;

namespace Wc3.Commands;

/// <summary>The foreign model formats the importer understands.</summary>
internal enum ModelFormat
{
    Obj,
    Gltf,
    Glb,
    Stl,
    Ply,
}

/// <summary>
/// Chooses a <see cref="ModelFormat"/> from the file name and the first bytes, so the extension is
/// never trusted on its own. A GLB's "glTF" magic, a PLY's "ply" magic and a binary STL whose
/// declared triangle count matches its exact length are each strong enough to override a wrong or
/// missing extension. Otherwise the extension decides, and when it is unknown the content is sniffed
/// as ASCII STL, glTF JSON or OBJ.
/// </summary>
internal static class ModelFormatDetector
{
    public static ModelFormat Detect(string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(bytes);

        if (IsGlb(bytes)) return ModelFormat.Glb;
        if (IsPly(bytes)) return ModelFormat.Ply;
        if (StlImporter.StlIsBinary(bytes)) return ModelFormat.Stl;

        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".obj": return ModelFormat.Obj;
            case ".gltf": return ModelFormat.Gltf;
            case ".glb": return ModelFormat.Glb;
            case ".stl": return ModelFormat.Stl;
            case ".ply": return ModelFormat.Ply;
        }

        if (IsAsciiStl(bytes)) return ModelFormat.Stl;
        if (LooksLikeJson(bytes)) return ModelFormat.Gltf;
        if (LooksLikeObj(bytes)) return ModelFormat.Obj;

        throw new NotSupportedException(
            $"'{path}' is not a recognized model format (expected OBJ, glTF, GLB, STL or PLY)");
    }

    private static bool IsGlb(byte[] b) =>
        b.Length >= 4 && b[0] == 'g' && b[1] == 'l' && b[2] == 'T' && b[3] == 'F';

    private static bool IsPly(byte[] b) =>
        b.Length >= 3 && b[0] == 'p' && b[1] == 'l' && b[2] == 'y'
        && (b.Length == 3 || b[3] is (byte)'\n' or (byte)'\r' or (byte)' ');

    private static bool IsAsciiStl(byte[] b)
    {
        var head = Head(b, 512).TrimStart();
        if (!head.StartsWith("solid", StringComparison.OrdinalIgnoreCase)) return false;
        return Head(b, 2048).Contains("facet", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeJson(byte[] b)
    {
        var head = Head(b, 64).TrimStart();
        return head.Length > 0 && (head[0] == '{' || head[0] == '[');
    }

    private static bool LooksLikeObj(byte[] b)
    {
        bool vertex = false, face = false;
        foreach (var line in Head(b, 4096).Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("v ", StringComparison.Ordinal)) vertex = true;
            else if (trimmed.StartsWith("f ", StringComparison.Ordinal)) face = true;
        }
        return vertex && face;
    }

    private static string Head(byte[] b, int max) =>
        Encoding.ASCII.GetString(b, 0, Math.Min(max, b.Length));
}
