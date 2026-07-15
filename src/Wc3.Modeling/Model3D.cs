// src/Wc3.Modeling/Model3D.cs
namespace Wc3.Modeling;

/// <summary>
/// Renderer-neutral static model: geometry plus the texture paths it references.
/// No bones, animation, particles or cameras — just what a static render needs.
/// </summary>
public sealed record Model3D(IReadOnlyList<Geoset> Geosets, IReadOnlyList<string> Textures);

/// <summary>
/// One drawable mesh chunk. <paramref name="Vertices"/> and <paramref name="Normals"/>
/// are packed x,y,z triples; <paramref name="Uvs"/> packed u,v pairs; <paramref name="Indices"/>
/// triangle-list indices into the vertex arrays. <paramref name="TextureId"/> indexes
/// <see cref="Model3D.Textures"/>, or -1 when the geoset has no resolvable texture.
/// </summary>
public sealed record Geoset(
    float[] Vertices,
    float[] Normals,
    float[] Uvs,
    int[] Indices,
    int TextureId);
