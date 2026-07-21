// src/Wc3.Render/TerrainMesh.cs
using System;
using System.IO;
using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;
using War3Net.Build.Environment;

namespace Wc3.Render;

/// <summary>
/// GPU-ready terrain geometry derived from a map's <c>war3map.w3e</c> tilepoint grid.
/// Vertices are interleaved <c>position(3) · normal(3) · colour(3)</c> in WC3 world
/// space (Z-up), using the exact same height math as
/// <see cref="TerrainRenderer.RenderPerspectivePng"/> so the GPU mesh lines up with the
/// CPU render and with <see cref="TerrainRenderer.PickTerrain"/>.
/// </summary>
public readonly record struct TerrainMesh(
    float[] Vertices,
    uint[] Indices,
    Vector3 Center,
    float Radius)
{
    /// <summary>Floats per vertex: px,py,pz, nx,ny,nz, r,g,b.</summary>
    public const int Stride = 9;
}

/// <summary>Builds a <see cref="TerrainMesh"/> from a parsed map document.</summary>
public static class TerrainMeshBuilder
{
    public static TerrainMesh Build(Wc3.Model.MapDocument doc)
    {
        if (doc.GetFile("war3map.w3e")?.Model is not MapEnvironment env)
            throw new InvalidDataException("no terrain (war3map.w3e) in map");

        int w = (int)env.Width + 1;      // tilepoint grid = tiles + 1 per axis
        int h = (int)env.Height + 1;
        var tiles = env.TerrainTiles;
        if (tiles is null || tiles.Count < w * h)
            throw new InvalidDataException("terrain tilepoint data is incomplete");

        var typeColors = new Rgba32[env.TerrainTypes.Count];
        for (int i = 0; i < typeColors.Length; i++)
            typeColors[i] = TerrainRenderer.ColorForTerrainType(env.TerrainTypes[i]);

        const float TileWorld = TerrainRenderer.TerrainTransform.TileWorld; // 128 world units / tile
        const float StepWorld = 128f;                                        // one cliff step in world Z
        float originX = -(int)env.Width * (TileWorld / 2f);
        float originY = -(int)env.Height * (TileWorld / 2f);

        int n = w * h;
        var zg = new float[n];
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                var t = tiles[j * w + i];
                float z = (t.Height + t.CliffLevel) * StepWorld;
                zg[j * w + i] = z;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }

        var verts = new float[n * TerrainMesh.Stride];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                int idx = j * w + i;
                // Central-difference normal (Z-up); borders clamp to the edge sample.
                float zL = zg[j * w + Math.Max(i - 1, 0)];
                float zR = zg[j * w + Math.Min(i + 1, w - 1)];
                float zD = zg[Math.Max(j - 1, 0) * w + i];
                float zU = zg[Math.Min(j + 1, h - 1) * w + i];
                var nrm = Vector3.Normalize(new Vector3(-(zR - zL), -(zU - zD), 2f * TileWorld));

                var t = tiles[idx];
                Vector3 col = (t.Texture >= 0 && t.Texture < typeColors.Length)
                    ? ToVec(typeColors[t.Texture])
                    : new Vector3(0.47f, 0.47f, 0.47f);

                int o = idx * TerrainMesh.Stride;
                verts[o + 0] = originX + i * TileWorld;
                verts[o + 1] = originY + j * TileWorld;
                verts[o + 2] = zg[idx];
                verts[o + 3] = nrm.X; verts[o + 4] = nrm.Y; verts[o + 5] = nrm.Z;
                verts[o + 6] = col.X; verts[o + 7] = col.Y; verts[o + 8] = col.Z;
            }

        // Two triangles per cell, wound CCW as seen from +Z (above).
        var idxs = new uint[(w - 1) * (h - 1) * 6];
        int p = 0;
        for (int j = 0; j < h - 1; j++)
            for (int i = 0; i < w - 1; i++)
            {
                uint a = (uint)(j * w + i);
                uint b = a + 1;
                uint c = (uint)((j + 1) * w + i);
                uint d = c + 1;
                idxs[p++] = a; idxs[p++] = b; idxs[p++] = d;
                idxs[p++] = a; idxs[p++] = d; idxs[p++] = c;
            }

        var center = new Vector3(0f, 0f, (minZ + maxZ) * 0.5f);
        float span = MathF.Max(w * TileWorld, h * TileWorld);
        float radius = MathF.Max(span, maxZ - minZ) * 0.5f + TileWorld;
        return new TerrainMesh(verts, idxs, center, radius);
    }

    private static Vector3 ToVec(Rgba32 c) => new(c.R / 255f, c.G / 255f, c.B / 255f);
}
