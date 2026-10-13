// src/Wc3.Commands/ModelImport/MeshData.cs
using System.Numerics;

namespace Wc3.Commands;

/// <summary>
/// One face corner of a <see cref="MeshData"/>, indexing the mesh's position, normal and UV
/// arrays. A negative normal or UV index means that attribute is absent for the corner, so the
/// shared builder computes a per-face normal or zeroes the UV. This is the one vertex shape every
/// format importer produces, so OBJ, glTF, STL and PLY all feed the same MDX path.
/// </summary>
internal readonly record struct MeshCorner(int Position, int Normal, int Uv);

/// <summary>
/// A format-neutral mesh, the shape every importer returns and
/// <see cref="MdxMeshBuilder.Build"/> consumes. <see cref="Positions"/> is required.
/// <see cref="Normals"/> and <see cref="Uvs"/> may be empty when the source format carries none,
/// and a corner referencing a missing attribute (index -1) gets a computed normal or a zero UV.
/// <see cref="Faces"/> are vertex-index polygons in winding order, split into a triangle fan when
/// longer than a triangle. <see cref="FlipV"/> is true only for formats whose V axis runs bottom-up
/// like OBJ, so glTF (top-left origin) and STL (no UVs) leave V untouched.
/// </summary>
internal sealed record MeshData(
    IReadOnlyList<Vector3> Positions,
    IReadOnlyList<Vector3> Normals,
    IReadOnlyList<Vector2> Uvs,
    IReadOnlyList<MeshCorner[]> Faces,
    bool FlipV);
