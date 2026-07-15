// src/Wc3.Render/ModelRenderer.cs
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Wc3.Modeling;

namespace Wc3.Render;

/// <summary>
/// Headless CPU rasterizer for static models: z-buffered triangle fill with
/// directional-light shading and nearest-neighbour texturing. No GPU/OpenGL —
/// runs anywhere the CLI does. Renders the model's parsed pose (no animation)
/// from an orbit camera with WC3's Z-up convention (models stand upright);
/// the default angles give the classic 3/4 view.
/// </summary>
public static class ModelRenderer
{
    // Camera orbit for the default 3/4 view: yaw around the Z (up) axis, then pitch down.
    public const float DefaultYawDegrees = 45f;
    public const float DefaultPitchDegrees = 30f;
    private const float FillFraction = 0.9f;    // model's share of the frame
    private const float AmbientLight = 0.3f;    // shading floor so backfaces stay visible
    private const byte AlphaTestThreshold = 128; // cutout transparency (hair, foliage)

    /// <summary>
    /// Renders <paramref name="model"/> to PNG bytes over a transparent background.
    /// <paramref name="textures"/> maps indices of <see cref="Model3D.Textures"/> to
    /// decoded images; geosets whose texture is absent shade flat gray (covers
    /// ReplaceableId entries and textures the map does not contain).
    /// <paramref name="yawDegrees"/>/<paramref name="pitchDegrees"/> orbit the camera
    /// around Z-up; pitch is clamped just short of the poles to keep the basis stable.
    /// </summary>
    public static byte[] RenderPng(
        Model3D model,
        IReadOnlyDictionary<int, TextureImage> textures,
        int width = 512,
        int height = 512,
        float yawDegrees = DefaultYawDegrees,
        float pitchDegrees = DefaultPitchDegrees)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(textures);
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "image dimensions must be positive");

        var geosets = model.Geosets.Where(g => g.Indices.Length >= 3 && g.Vertices.Length >= 9).ToList();
        if (geosets.Count == 0)
            throw new InvalidDataException("model has no renderable geometry (no geosets with triangles)");

        // Orthographic camera basis from yaw/pitch on the unit sphere (Z-up).
        // A pitch at ±90° would make forward parallel to Z and the right-vector
        // cross product degenerate (NaNs), so stop just short of the poles.
        float yaw = yawDegrees * MathF.PI / 180f;
        float pitch = Math.Clamp(pitchDegrees, -89.9f, 89.9f) * MathF.PI / 180f;
        var eyeDir = new Vector3(
            MathF.Cos(pitch) * MathF.Cos(yaw),
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch));
        var forward = -eyeDir;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        var up = Vector3.Cross(right, forward);

        // Project every vertex to view space once; frame the projected bounds.
        var viewPositions = new Vector3[geosets.Count][];
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int gi = 0; gi < geosets.Count; gi++)
        {
            var v = geosets[gi].Vertices;
            var view = new Vector3[v.Length / 3];
            for (int i = 0; i < view.Length; i++)
            {
                var p = new Vector3(v[i * 3], v[i * 3 + 1], v[i * 3 + 2]);
                var q = new Vector3(Vector3.Dot(p, right), Vector3.Dot(p, up), Vector3.Dot(p, forward));
                view[i] = q;
                if (q.X < minX) minX = q.X;
                if (q.X > maxX) maxX = q.X;
                if (q.Y < minY) minY = q.Y;
                if (q.Y > maxY) maxY = q.Y;
            }
            viewPositions[gi] = view;
        }

        float extentX = MathF.Max(maxX - minX, 1e-6f);
        float extentY = MathF.Max(maxY - minY, 1e-6f);
        float scale = FillFraction * MathF.Min(width / extentX, height / extentY);
        float centerX = (minX + maxX) / 2f, centerY = (minY + maxY) / 2f;

        // Light biased toward the camera and above-left so the visible side reads.
        var light = Vector3.Normalize(eyeDir + 0.6f * up - 0.4f * right);

        var zbuffer = new float[width * height];
        Array.Fill(zbuffer, float.MaxValue);
        using var image = new Image<Rgba32>(width, height); // zeroed → transparent

        for (int gi = 0; gi < geosets.Count; gi++)
        {
            var geoset = geosets[gi];
            var view = viewPositions[gi];
            textures.TryGetValue(geoset.TextureId, out var texture);

            for (int t = 0; t + 2 < geoset.Indices.Length; t += 3)
            {
                int i0 = geoset.Indices[t], i1 = geoset.Indices[t + 1], i2 = geoset.Indices[t + 2];
                // View → screen: +X right, +Y up (flipped into image rows), Z kept for depth.
                float ax = (view[i0].X - centerX) * scale + width / 2f;
                float ay = height / 2f - (view[i0].Y - centerY) * scale;
                float bx = (view[i1].X - centerX) * scale + width / 2f;
                float by = height / 2f - (view[i1].Y - centerY) * scale;
                float cx = (view[i2].X - centerX) * scale + width / 2f;
                float cy = height / 2f - (view[i2].Y - centerY) * scale;

                // Signed doubled area; near-zero → degenerate sliver, skip.
                float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                if (MathF.Abs(area) < 1e-9f) continue;

                int pxMin = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))));
                int pxMax = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
                int pyMin = Math.Max(0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))));
                int pyMax = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))));
                if (pxMin > pxMax || pyMin > pyMax) continue;

                for (int py = pyMin; py <= pyMax; py++)
                {
                    float sy = py + 0.5f;
                    for (int px = pxMin; px <= pxMax; px++)
                    {
                        float sx = px + 0.5f;
                        // Barycentric weights; dividing by the signed area makes the
                        // inside test winding-independent (no backface culling —
                        // WC3 materials are frequently two-sided).
                        float w0 = ((bx - sx) * (cy - sy) - (by - sy) * (cx - sx)) / area;
                        float w1 = ((cx - sx) * (ay - sy) - (cy - sy) * (ax - sx)) / area;
                        float w2 = 1f - w0 - w1;
                        if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

                        float depth = w0 * view[i0].Z + w1 * view[i1].Z + w2 * view[i2].Z;
                        int zi = py * width + px;
                        if (depth >= zbuffer[zi]) continue;

                        var rgba = SampleColor(geoset, texture, i0, i1, i2, w0, w1, w2);
                        if (rgba.A < AlphaTestThreshold) continue; // cutout: keep depth open

                        float shade = ShadeAt(geoset, i0, i1, i2, w0, w1, w2, light);
                        zbuffer[zi] = depth;
                        image[px, py] = new Rgba32(
                            (byte)(rgba.R * shade), (byte)(rgba.G * shade), (byte)(rgba.B * shade), 255);
                    }
                }
            }
        }

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    /// <summary>Nearest-neighbour texture sample at the interpolated UV, or flat gray.</summary>
    private static Rgba32 SampleColor(
        Geoset geoset, TextureImage? texture, int i0, int i1, int i2, float w0, float w1, float w2)
    {
        if (texture is null) return new Rgba32(190, 190, 190, 255);

        float u = w0 * geoset.Uvs[i0 * 2] + w1 * geoset.Uvs[i1 * 2] + w2 * geoset.Uvs[i2 * 2];
        float v = w0 * geoset.Uvs[i0 * 2 + 1] + w1 * geoset.Uvs[i1 * 2 + 1] + w2 * geoset.Uvs[i2 * 2 + 1];
        u -= MathF.Floor(u); // WC3 UVs wrap
        v -= MathF.Floor(v);

        int tx = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
        int ty = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
        int o = (ty * texture.Width + tx) * 4;
        return new Rgba32(texture.Rgba[o], texture.Rgba[o + 1], texture.Rgba[o + 2], texture.Rgba[o + 3]);
    }

    /// <summary>Directional-light intensity from the interpolated normal, with an ambient floor.</summary>
    private static float ShadeAt(
        Geoset geoset, int i0, int i1, int i2, float w0, float w1, float w2, Vector3 light)
    {
        var n = new Vector3(
            w0 * geoset.Normals[i0 * 3] + w1 * geoset.Normals[i1 * 3] + w2 * geoset.Normals[i2 * 3],
            w0 * geoset.Normals[i0 * 3 + 1] + w1 * geoset.Normals[i1 * 3 + 1] + w2 * geoset.Normals[i2 * 3 + 1],
            w0 * geoset.Normals[i0 * 3 + 2] + w1 * geoset.Normals[i1 * 3 + 2] + w2 * geoset.Normals[i2 * 3 + 2]);
        float lengthSq = n.LengthSquared();
        if (lengthSq < 1e-12f) return 1f; // missing/zero normals: render unshaded

        // |dot|: two-sided lighting so form reads regardless of winding.
        float intensity = MathF.Abs(Vector3.Dot(n / MathF.Sqrt(lengthSq), light));
        return AmbientLight + (1f - AmbientLight) * MathF.Min(intensity, 1f);
    }
}
