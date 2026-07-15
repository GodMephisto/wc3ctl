// src/Wc3.Modeling/MdlParser.cs
using System.Globalization;

namespace Wc3.Modeling;

/// <summary>
/// Minimal geometry-only reader for the text MDL format. Extracts Geoset
/// vertices/normals/TVertices/faces, texture paths from Textures/Bitmap, and
/// each geoset's material→texture mapping. Everything else (bones, sequences,
/// animation blocks) is skipped as balanced brace blocks.
/// </summary>
internal static class MdlParser
{
    public static Model3D Parse(string text)
    {
        var tok = new Tokenizer(text);
        var textures = new List<string>();
        var materialTexture = new List<int>(); // material index -> texture index
        var raw = new List<(float[] Vertices, float[] Normals, float[] Uvs, int[] Indices, int MaterialId)>();

        for (string? t = tok.Next(); t is not null; t = tok.Next())
        {
            switch (t)
            {
                case "Textures": ParseTextures(tok, textures); break;
                case "Materials": ParseMaterials(tok, materialTexture); break;
                case "Geoset": raw.Add(ParseGeoset(tok)); break;
                case "{": tok.SkipBalanced(); break; // block of an unhandled keyword
                default: break; // stray keywords/arguments stream by harmlessly
            }
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

    /// <summary>Textures <n> { Bitmap { Image "path", ReplaceableId <n>, } ... }</summary>
    private static void ParseTextures(Tokenizer tok, List<string> textures)
    {
        if (!tok.SkipToOpenBrace()) return;
        int depth = 1;
        string? path = null;
        long replaceableId = 0;
        bool inBitmap = false;

        for (string? t = tok.Next(); t is not null; t = tok.Next())
        {
            if (t == "{") { depth++; continue; }
            if (t == "}")
            {
                depth--;
                if (inBitmap && depth == 1)
                {
                    textures.Add(!string.IsNullOrEmpty(path) ? path : $"ReplaceableId:{replaceableId}");
                    (path, replaceableId, inBitmap) = (null, 0, false);
                }
                if (depth == 0) return;
                continue;
            }
            if (depth == 1 && t == "Bitmap") { inBitmap = true; continue; }
            if (inBitmap && depth == 2 && t == "Image" && tok.Next() is { } img && img.StartsWith('"'))
                path = img[1..];
            else if (inBitmap && depth == 2 && t == "ReplaceableId" && long.TryParse(tok.Next(), out var rid))
                replaceableId = rid;
        }
    }

    /// <summary>Materials <n> { Material { Layer { static TextureID <n>, } } ... }</summary>
    private static void ParseMaterials(Tokenizer tok, List<int> materialTexture)
    {
        if (!tok.SkipToOpenBrace()) return;
        int depth = 1;
        int texId = -1;
        bool sawLayer = false;

        for (string? t = tok.Next(); t is not null; t = tok.Next())
        {
            if (t == "{") { depth++; continue; }
            if (t == "}")
            {
                depth--;
                if (depth == 1) // closed a Material block
                {
                    materialTexture.Add(texId);
                    (texId, sawLayer) = (-1, false);
                }
                if (depth == 0) return;
                continue;
            }
            if (depth == 2 && t == "Layer")
            {
                if (sawLayer) { tok.SkipToOpenBrace(); tok.SkipBalanced(); continue; } // only the first layer
                sawLayer = true;
            }
            else if (depth == 3 && t == "TextureID" && texId < 0 && int.TryParse(tok.Next(), out var id))
                texId = id;
        }
    }

    private static (float[], float[], float[], int[], int) ParseGeoset(Tokenizer tok)
    {
        float[] vertices = [], normals = [], uvs = [];
        int[] indices = [];
        int materialId = -1;
        bool sawUvs = false;

        if (!tok.SkipToOpenBrace()) return (vertices, normals, uvs, indices, materialId);

        for (string? t = tok.Next(); t is not null; t = tok.Next())
        {
            switch (t)
            {
                case "}": return (vertices, normals, uvs, indices, materialId);
                case "Vertices": vertices = ReadNumericBlock(tok); break;
                case "Normals": normals = ReadNumericBlock(tok); break;
                case "TVertices":
                    var set = ReadNumericBlock(tok);
                    if (!sawUvs) { uvs = set; sawUvs = true; } // first UV set only
                    break;
                case "Faces":
                    var nums = ReadNumericBlock(tok);
                    indices = Array.ConvertAll(nums, f => (int)f);
                    break;
                case "MaterialID":
                    if (int.TryParse(tok.Next(), out var id)) materialId = id;
                    break;
                case "{": tok.SkipBalanced(); break; // block of an unhandled keyword
                default: break; // scalar statement arguments stream by
            }
        }
        return (vertices, normals, uvs, indices, materialId);
    }

    /// <summary>
    /// Consumes optional count tokens, then a balanced brace block, returning every
    /// token inside that parses as a number. Handles nested tuples ({x, y, z}) and
    /// wrapper words (Triangles) transparently.
    /// </summary>
    private static float[] ReadNumericBlock(Tokenizer tok)
    {
        if (!tok.SkipToOpenBrace()) return [];
        var values = new List<float>();
        int depth = 1;
        for (string? t = tok.Next(); t is not null; t = tok.Next())
        {
            if (t == "{") { depth++; continue; }
            if (t == "}") { if (--depth == 0) break; continue; }
            if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                values.Add(f);
        }
        return values.ToArray();
    }

    /// <summary>Streams MDL tokens: braces, quote-prefixed strings, bare words/numbers. Commas are whitespace; // comments skipped.</summary>
    private sealed class Tokenizer(string s)
    {
        private int _i;

        public string? Next()
        {
            while (_i < s.Length)
            {
                char c = s[_i];
                if (char.IsWhiteSpace(c) || c == ',' || c == ':') { _i++; continue; }
                if (c == '/' && _i + 1 < s.Length && s[_i + 1] == '/')
                {
                    while (_i < s.Length && s[_i] != '\n') _i++;
                    continue;
                }
                if (c == '{' || c == '}') { _i++; return c.ToString(); }
                if (c == '"')
                {
                    int start = _i++;
                    while (_i < s.Length && s[_i] != '"') _i++;
                    string str = s[(start + 1).._i];
                    if (_i < s.Length) _i++; // closing quote
                    return "\"" + str; // quote prefix marks a string literal
                }
                int w = _i;
                while (_i < s.Length && !char.IsWhiteSpace(s[_i])
                       && s[_i] is not (',' or ':' or '{' or '}' or '"'))
                    _i++;
                return s[w.._i];
            }
            return null;
        }

        /// <summary>Advances past tokens until an opening brace is consumed. False at EOF.</summary>
        public bool SkipToOpenBrace()
        {
            for (string? t = Next(); t is not null; t = Next())
            {
                if (t == "{") return true;
                if (t == "}") return false; // never escape the enclosing block
            }
            return false;
        }

        /// <summary>Skips a balanced block whose opening brace was already consumed.</summary>
        public void SkipBalanced()
        {
            int depth = 1;
            while (depth > 0)
            {
                string? t = Next();
                if (t is null) return;
                if (t == "{") depth++;
                else if (t == "}") depth--;
            }
        }
    }
}
