using System.Numerics;
using FourHUnfolder.Domain.Results;

namespace FourHUnfolder.Geometry.Algorithms;

using static FourHUnfolder.Geometry.GeometryConstants;

/// <summary>
/// Overlap detection using a three-phase approach:
///   1. Spatial grid (uniform bucket partition) — each face is placed in the grid cells
///      its AABB covers; only pairs that share a cell are candidate pairs.
///      Reduces comparisons from O(n²) to O(n·k) where k is the average bucket occupancy.
///   2. Fast AABB pre-check on each candidate pair.
///   3. Accurate SAT (Separating Axis Theorem) — only run when AABBs actually overlap.
///
/// Cell size = max(2 × avg AABB side, max canvas extent / 256).
/// This keeps the grid at most 256×256 cells while ensuring typical faces span 1–4 cells.
/// </summary>
public class OverlapDetector
{
    private readonly record struct AABB(float MinX, float MaxX, float MinY, float MaxY);

    public bool HasOverlaps(IReadOnlyList<UnfoldedFace> faces)
    {
        if (faces.Count < 2) return false;

        // ── Phase 0: pre-compute all AABBs + global bounds + average side ────────
        var boxes = new AABB[faces.Count];
        float totalMinX = float.MaxValue, totalMaxX = float.MinValue;
        float totalMinY = float.MaxValue, totalMaxY = float.MinValue;
        float sumSide   = 0f;

        for (int k = 0; k < faces.Count; k++)
        {
            boxes[k]  = ComputeAABB(faces[k]);
            totalMinX = MathF.Min(totalMinX, boxes[k].MinX);
            totalMaxX = MathF.Max(totalMaxX, boxes[k].MaxX);
            totalMinY = MathF.Min(totalMinY, boxes[k].MinY);
            totalMaxY = MathF.Max(totalMaxY, boxes[k].MaxY);
            sumSide  += (boxes[k].MaxX - boxes[k].MinX + boxes[k].MaxY - boxes[k].MinY) * 0.5f;
        }

        float avgSide  = sumSide / faces.Count;
        float totalW   = totalMaxX - totalMinX;
        float totalH   = totalMaxY - totalMinY;
        // Cell size: at least 2× avg face size, and never so small that the grid exceeds 256×256.
        float cellSize = MathF.Max(avgSide * 2f, MathF.Max(totalW, totalH) / 256f);
        if (cellSize < 1e-6f) cellSize = 1f; // degenerate-geometry guard

        // ── Phase 1: build spatial grid ──────────────────────────────────────────
        // Each face is inserted into all cells its AABB overlaps (typically 1–4 cells).
        var grid = new Dictionary<(int, int), List<int>>();

        for (int k = 0; k < boxes.Length; k++)
        {
            int cx0 = (int)MathF.Floor((boxes[k].MinX - totalMinX) / cellSize);
            int cx1 = (int)MathF.Floor((boxes[k].MaxX - totalMinX) / cellSize);
            int cy0 = (int)MathF.Floor((boxes[k].MinY - totalMinY) / cellSize);
            int cy1 = (int)MathF.Floor((boxes[k].MaxY - totalMinY) / cellSize);

            for (int cx = cx0; cx <= cx1; cx++)
            for (int cy = cy0; cy <= cy1; cy++)
            {
                var cellKey = (cx, cy);
                if (!grid.TryGetValue(cellKey, out var list))
                    grid[cellKey] = list = [];
                list.Add(k);
            }
        }

        // ── Phase 2 + 3: test candidate pairs (AABB then SAT) ───────────────────
        // Pairs are deduplicated with a HashSet<long> encoding (i, j) with i < j.
        // Correctness guarantee: if two AABBs overlap they must share ≥ 1 grid cell,
        // so the spatial grid introduces no false negatives.
        var tested = new HashSet<long>(capacity: faces.Count * 2);

        foreach (var cell in grid.Values)
        {
            if (cell.Count < 2) continue;

            for (int a = 0; a < cell.Count; a++)
            for (int b = a + 1; b < cell.Count; b++)
            {
                int i = cell[a], j = cell[b];
                if (i > j) (i, j) = (j, i); // canonical order: i < j

                long pairKey = ((long)i << 32) | (uint)j;
                if (!tested.Add(pairKey)) continue;

                if (AABBsOverlap(boxes[i], boxes[j]) && TrianglesOverlap(faces[i], faces[j]))
                    return true;
            }
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
            var edge     = a[(i + 1) % 3] - a[i];
            var axis     = new Vector2(-edge.Y, edge.X);
            float axLen  = axis.Length();

            var (minA, maxA) = Project(a, axis);
            var (minB, maxB) = Project(b, axis);

            // Epsilon scaled by axis length for consistent geometric tolerance
            // regardless of edge length (avoids false overlaps on shared fold edges).
            float eps = SatTouchEpsilon * (axLen > 0f ? axLen : 1f);
            if (maxA <= minB + eps || maxB <= minA + eps) return true;
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
