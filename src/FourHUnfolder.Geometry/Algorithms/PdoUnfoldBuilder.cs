using System.Numerics;
using FourHUnfolder.Domain.Models;
using FourHUnfolder.Domain.Results;

namespace FourHUnfolder.Geometry.Algorithms;

/// <summary>
/// Converts a PDO pre-computed paper layout into the same <see cref="UnfoldedFace"/>
/// list that <see cref="UnfoldEngine"/> would produce, without running the MST/BFS
/// unfold pipeline.
///
/// Algorithm:
///   1. Classify each mesh edge from the PDO part-index data:
///        • same part  → Fold
///        • diff parts → Cut
///        • boundary   → Boundary
///   2. Build one <see cref="UnfoldedFace"/> per triangle, using the 2-D paper
///      coordinates (mm) stored in <see cref="PdoLayout"/>.
/// </summary>
public sealed class PdoUnfoldBuilder
{
    /// <summary>
    /// Mark mesh edge types and return one <see cref="UnfoldedFace"/> per face.
    /// Requires <paramref name="mesh"/>.<see cref="Mesh.PdoLayout"/> to be non-null.
    /// </summary>
    /// <exception cref="InvalidOperationException">When mesh has no PDO layout.</exception>
    public IReadOnlyList<UnfoldedFace> Build(Mesh mesh)
    {
        var layout = mesh.PdoLayout
            ?? throw new InvalidOperationException(
                "PdoUnfoldBuilder.Build called on a mesh with no PdoLayout.");

        // ── Step 1: faceId → partIndex lookup ────────────────────────────
        var faceToPart = new Dictionary<int, int>(layout.Faces.Count);
        foreach (var pf in layout.Faces)
            faceToPart[pf.FaceIndex] = pf.PartIndex;

        // ── Step 2: stamp edge types ──────────────────────────────────────
        foreach (var edge in mesh.Edges)
        {
            if (edge.IsBoundary)
            {
                edge.Type = EdgeType.Boundary;
            }
            else
            {
                bool gotA = faceToPart.TryGetValue(edge.FaceA, out int partA);
                bool gotB = faceToPart.TryGetValue(edge.FaceB, out int partB);
                edge.Type = (gotA && gotB && partA == partB)
                    ? EdgeType.Fold
                    : EdgeType.Cut;
            }
        }

        // ── Step 3: build UnfoldedFace list ───────────────────────────────
        var result = new List<UnfoldedFace>(layout.Faces.Count);

        foreach (var pf in layout.Faces)
        {
            var meshFace = mesh.Faces[pf.FaceIndex];

            // Edge flags: EdgeIds[0]=AB, EdgeIds[1]=BC, EdgeIds[2]=CA
            var isFold     = new bool[3];
            var isBoundary = new bool[3];
            for (int e = 0; e < Math.Min(3, meshFace.EdgeIds.Count); e++)
            {
                var edge   = mesh.Edges[meshFace.EdgeIds[e]];
                isFold[e]     = edge.Type == EdgeType.Fold;
                isBoundary[e] = edge.Type == EdgeType.Boundary;
            }

            // UV texture coordinates
            Vector2[]? uvCoords = null;
            if (mesh.HasUVs && pf.FaceIndex < mesh.FaceUVs.Count)
            {
                var (ua, ub, uc) = mesh.FaceUVs[pf.FaceIndex];
                if (ua >= 0 && ub >= 0 && uc >= 0)
                    uvCoords = [mesh.UVs[ua], mesh.UVs[ub], mesh.UVs[uc]];
            }

            result.Add(new UnfoldedFace(
                faceId:         pf.FaceIndex,
                v0:             pf.A,
                v1:             pf.B,
                v2:             pf.C,
                edgeIsFold:     isFold,
                edgeIsBoundary: isBoundary,
                uvCoords:       uvCoords,
                materialId:     meshFace.MaterialId));
        }

        return result;
    }
}
