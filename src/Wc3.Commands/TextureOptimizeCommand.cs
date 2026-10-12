// src/Wc3.Commands/TextureOptimizeCommand.cs
using Wc3.Render;

namespace Wc3.Commands;

/// <summary>What a texture optimise changed, before and after, for the report.</summary>
public sealed record TextureOptimizeResult(
    string Path,
    string Format,
    int WidthBefore,
    int HeightBefore,
    int WidthAfter,
    int HeightAfter,
    long BytesBefore,
    long BytesAfter);

/// <summary>
/// Shrinks a texture to fit a maximum side, preserving aspect ratio, and writes it back in the same
/// format. A WC3 texture is usually far larger than the engine needs, and it is the other half of
/// making a model compact, since the model only references it. Read the texture through the same
/// decoder the renderer uses, so .blp and .dds are handled alongside the standard formats.
/// </summary>
public static class TextureOptimizeCommand
{
    public static TextureOptimizeResult ExecutePath(string path, int maxDimension, string outPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException($"texture not found: {path}", path);
        var bytes = File.ReadAllBytes(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();

        var (w0, h0) = TextureConvert.Dimensions(bytes, ext);
        var output = TextureConvert.ConvertScaled(bytes, ext, ext, maxDimension);
        File.WriteAllBytes(outPath, output);
        var (w1, h1) = TextureConvert.Dimensions(output, ext);

        return new TextureOptimizeResult(outPath, ext.TrimStart('.'),
            w0, h0, w1, h1, bytes.LongLength, output.LongLength);
    }
}
