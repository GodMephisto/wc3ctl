// src/Wc3.Commands/RenderModelCommand.cs
using Wc3.Model;
using Wc3.Modeling;
using Wc3.Render;

namespace Wc3.Commands;

/// <summary>
/// Renders a map-imported model to PNG bytes: model file from the map archive,
/// textures resolved from the map's imports (missing/ReplaceableId textures fall
/// back to flat shading). Base-game models (CASC) are not resolved yet.
/// </summary>
public static class RenderModelCommand
{
    /// <summary>Object-data field that names each kind's model file.</summary>
    private static readonly IReadOnlyDictionary<ObjectKind, string> ModelFieldByKind =
        new Dictionary<ObjectKind, string>
        {
            [ObjectKind.Unit] = "umdl",
            [ObjectKind.Doodad] = "dfil",
            [ObjectKind.Destructable] = "bfil",
            [ObjectKind.Item] = "ifil",
        };

    /// <summary>Renders the model at an internal map path (slash and .mdx/.mdl variants tried).</summary>
    public static byte[] Execute(MapDocument doc, string modelInternalPath)
    {
        var entry = FindModelEntry(doc, modelInternalPath)
            ?? throw new InvalidDataException(
                $"model '{modelInternalPath}' is not in the map — only map-imported models render (base-game CASC models are a follow-up)");
        var model = ModelParser.Parse(entry.RawBytes, entry.FileName!);
        return ModelRenderer.RenderPng(model, ResolveTextures(doc, model));
    }

    /// <summary>Renders one object's model, resolving the kind's model-file field.</summary>
    public static byte[] Execute(MapDocument doc, ObjectKind kind, string rawcode, string? gameDir)
    {
        if (!ModelFieldByKind.TryGetValue(kind, out var fieldCode))
            throw new InvalidDataException($"{kind} objects have no model-file field to render");
        var merged = ObjectGetCommand.Execute(doc, kind, rawcode, gameDir);
        return Execute(doc, ModelPathFrom(merged, rawcode, new[] { fieldCode }));
    }

    /// <summary>Kind-agnostic: probes every kind for the rawcode, then any model-file field.</summary>
    public static byte[] Execute(MapDocument doc, string rawcode, string? gameDir)
    {
        var merged = ObjectGetCommand.Execute(doc, rawcode, gameDir);
        return Execute(doc, ModelPathFrom(merged, rawcode, ModelFieldByKind.Values));
    }

    private static string ModelPathFrom(
        MergedObjectResult merged, string rawcode, IEnumerable<string> fieldCodes)
    {
        if (!merged.Found)
            throw new InvalidDataException($"object '{rawcode}' not found in the map or game data");
        foreach (var code in fieldCodes)
        {
            var field = merged.Fields.FirstOrDefault(
                f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase));
            // Variation-heavy fields can list several paths comma-separated; render the first.
            if (!string.IsNullOrWhiteSpace(field?.Value))
                return field.Value.Split(',')[0].Trim();
        }
        throw new InvalidDataException(
            $"object '{rawcode}' has no model-file field ({string.Join("/", fieldCodes)})");
    }

    private static MapFileEntry? FindModelEntry(MapDocument doc, string path)
    {
        foreach (var candidate in PathCandidates(path))
            if (doc.GetFile(candidate) is { } entry && entry.RawBytes.Length > 0)
                return entry;
        return null;
    }

    /// <summary>Both slash conventions x {as-given, sibling .mdx/.mdl, extensionless + either}.</summary>
    private static IEnumerable<string> PathCandidates(string path)
    {
        foreach (var p in new[] { path, path.Replace('/', '\\'), path.Replace('\\', '/') }.Distinct())
        {
            yield return p;
            var ext = Path.GetExtension(p);
            if (ext.Equals(".mdx", StringComparison.OrdinalIgnoreCase))
                yield return Path.ChangeExtension(p, ".mdl");
            else if (ext.Equals(".mdl", StringComparison.OrdinalIgnoreCase))
                yield return Path.ChangeExtension(p, ".mdx");
            else
            {
                yield return p + ".mdx";
                yield return p + ".mdl";
            }
        }
    }

    /// <summary>
    /// Decodes every model texture the map actually contains, keyed by texture index.
    /// ReplaceableId entries (team colour etc.), base-game paths and undecodable
    /// formats are simply absent — the renderer shades those geosets flat.
    /// </summary>
    private static IReadOnlyDictionary<int, TextureImage> ResolveTextures(MapDocument doc, Model3D model)
    {
        var textures = new Dictionary<int, TextureImage>();
        for (int i = 0; i < model.Textures.Count; i++)
        {
            var path = model.Textures[i];
            if (string.IsNullOrWhiteSpace(path)
                || path.StartsWith("ReplaceableId:", StringComparison.OrdinalIgnoreCase))
                continue;
            var entry = doc.GetFile(path)
                ?? doc.GetFile(path.Replace('/', '\\'))
                ?? doc.GetFile(path.Replace('\\', '/'));
            if (entry is null || entry.RawBytes.Length == 0) continue;
            try
            {
                textures[i] = BlpDecoder.Decode(entry.RawBytes);
            }
            catch
            {
                // Not a decodable BLP (tga/dds import or corrupt) — flat fallback.
            }
        }
        return textures;
    }
}
