using System.Numerics;
using FourHUnfolder.Domain.Results;

namespace FourHUnfolder.Geometry.Algorithms;

/// <summary>
/// Overlap detection using a two-phase approach:
///   1. Fast AABB (axis-aligned bounding box) pre-check — O(n²) comparisons of cheap box tests
///   2. Accurate SAT (Separating Axis Theorem) — only run when AABBs actually overlap
///
/// For typical unfolded meshes where most pieces don't overlap, the AABB phase rejects
/// the majority of pairs before the expensive SAT runs.
/// </summary>
public class OverlapDetector
{
    private readonly record struct AABB(float MinX, float MaxX, float MinY, float MaxY);

    public bool HasOverlaps(IReadOnlyList<UnfoldedFace> faces)
    {
        // Pre-compute bounding boxes once
        var boxes = faces.Select(ComputeAABB).ToArray();

        for (int i = 0; i < faces.Count; i++)
        for (int j = i + 1; j < faces.Count; j++)
        {
            // Phase 1: fast AABB reject
            if (!AABBsOverlap(boxes[i], boxes[j])) continue;

            // Phase 2: accurate SAT (only reached when boxes overlap)
            if (TrianglesOverlap(faces[i], faces[j])) return true;
        }
        return false;
    }

    // ── AABB ─────────────────────────────────────────────────────────────────

    private static AABB ComputeAABB(UnfoldedFace f)
    {
        var v = f.Vertices;
        return new AABB(
            MathF.Min(v[0].X, MathF.Min(v[1].X, v[2].X)),
            MathF.Max(v[0].X, MathF.Max(v[1].X, v[2].X)),
            MathF.Min(v[0].Y, MathF.Min(v[1].Y, v[2].Y)),
            MathF.Max(v[0].Y, MathF.Max(v[1].Y, v[2].Y)));
    }

    private static bool AABBsOverlap(AABB a, AABB b) =>
        a.MaxX > b.MinX && b.MaxX > a.MinX &&
        a.MaxY > b.MinY && b.MaxY > a.MinY;

    // ── SAT ──────────────────────────────────────────────────────────────────

    private static bool TrianglesOverlap(UnfoldedFace a, UnfoldedFace b)
    {
        var ta = a.Vertices;
        var tb = b.Vertices;
        return !HasSeparatingAxis(ta, tb) && !HasSeparatingAxis(tb, ta);
    }

    private static bool HasSeparatingAxis(Vector2[] a, Vector2[] b)
    {
        for (int i = 0; i < 3; i++)
        {
            var edge = a[(i + 1) % 3] - a[i];
            var axis = new Vector2(-edge.Y, edge.X);

            var (minA, maxA) = Project(a, axis);
            var (minB, maxB) = Project(b, axis);

            // Add small epsilon to ignore near-touching edges
            if (maxA <= minB + 1e-5f || maxB <= minA + 1e-5f) return true;
        }
        return false;
    }

    private static (float min, float max) Project(Vector2[] verts, Vector2 axis)
    {
        float d0 = Vector2.Dot(verts[0], axis);
        float d1 = Vector2.Dot(verts[1], axis);
        float d2 = Vector2.Dot(verts[2], axis);
        return (MathF.Min(d0, MathF.Min(d1, d2)), MathF.Max(d0, MathF.Max(d1, d2)));
    }
}
