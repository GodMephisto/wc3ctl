// src/Wc3.Render/TerrainRenderer.cs
namespace Wc3.Render;

public static class TerrainRenderer
{
    /// <summary>
    /// Renders a top-down terrain image of the map and returns it as PNG bytes.
    /// The caller (CLI) is responsible for writing the bytes to disk.
    /// </summary>
    public static byte[] RenderTerrainPng(Wc3.Model.MapDocument doc) =>
        throw new NotImplementedException("renderer impl pending");
}
