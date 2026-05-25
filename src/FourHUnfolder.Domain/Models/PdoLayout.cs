using System.Numerics;

namespace FourHUnfolder.Domain.Models;

/// <summary>
/// Pre-computed 2-D paper layout data embedded inside a PDO file.
/// Each <see cref="PdoFace"/> records the unfolded 2-D coordinates (in mm)
/// for one paper-space triangle, along with which piece it belongs to.
/// </summary>
public sealed class PdoLayout
{
    /// <summary>
    /// One entry per triangle in <see cref="Mesh.Faces"/> (same index order).
    /// </summary>
    public List<PdoFace> Faces { get; } = new();

    /// <summary>
    /// Distinct piece (part) indices present in this layout.
    /// </summary>
    public IReadOnlyList<int> PartIndices =>
        Faces.Select(f => f.PartIndex).Distinct().Order().ToList();
}

/// <summary>
/// 2-D paper-space data for one triangulated face from a PDO file.
/// </summary>
/// <param name="FaceIndex">Index into <see cref="Mesh.Faces"/>.</param>
/// <param name="PartIndex">2-D piece group (Pepakura "part" field).</param>
/// <param name="A">Paper-space position of vertex A (mm).</param>
/// <param name="B">Paper-space position of vertex B (mm).</param>
/// <param name="C">Paper-space position of vertex C (mm).</param>
public sealed record PdoFace(
    int     FaceIndex,
    int     PartIndex,
    Vector2 A,
    Vector2 B,
    Vector2 C);
