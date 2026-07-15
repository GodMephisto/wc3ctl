// src/Wc3.Modeling/BlpDecoder.cs
using War3Net.Drawing.Blp;

namespace Wc3.Modeling;

/// <summary>A decoded texture: RGBA8, row-major, top-left origin.</summary>
public sealed record TextureImage(int Width, int Height, byte[] Rgba);

/// <summary>
/// Decodes Warcraft III .blp textures (BLP0/BLP1 JPEG-content and palettized,
/// plus BLP2) by wrapping the War3Net.Drawing.Blp package.
/// </summary>
public static class BlpDecoder
{
    public static TextureImage Decode(byte[] blp)
    {
        ArgumentNullException.ThrowIfNull(blp);
        using var stream = new MemoryStream(blp, writable: false);
        using var file = new BlpFile(stream);
        var rgba = file.GetPixels(0, out int width, out int height, bgra: false);
        return new TextureImage(width, height, rgba);
    }
}
