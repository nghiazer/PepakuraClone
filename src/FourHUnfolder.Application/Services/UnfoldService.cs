using FourHUnfolder.Domain.Models;
using FourHUnfolder.Domain.Results;
using FourHUnfolder.Domain.Settings;
using FourHUnfolder.Geometry.Algorithms;

namespace FourHUnfolder.Application.Services;

/// <summary>
/// Orchestrates the full unfold pipeline with optional:
///   - Real-world scale (mm per model unit)
///   - User edge-type overrides (join / split)
/// </summary>
public class UnfoldService
{
    private readonly DualGraphBuilder  _graphBuilder    = new();
    private readonly KruskalMstBuilder _mstBuilder      = new();
    private readonly EdgeMarker        _edgeMarker      = new();
    private readonly UnfoldEngine      _unfoldEngine    = new();
    private readonly OverlapDetector   _overlapDetector = new();
    private readonly GlueTabGenerator  _tabGenerator    = new();
    private readonly PieceComputer     _pieceComputer   = new();

    /// <param name="mesh">The mesh to unfold (edge Types will be written).</param>
    /// <param name="edgeOverrides">
    ///   User-modified edge types keyed by mesh edge ID.
    ///   These override the MST result for the named edges.
    /// </param>
    public UnfoldResult Unfold(
        Mesh mesh,
        IReadOnlyDictionary<int, EdgeType>? edgeOverrides = null,
        AppSettings.PrintSettings? printSettings = null)
    {
        // 1. Build dual graph + MST
        var dualGraph    = _graphBuilder.Build(mesh);
        var mstEdges     = _mstBuilder.Build(dualGraph);
        var foldEdgeIds  = new HashSet<int>(mstEdges.Select(e => e.SharedMeshEdgeId));

        // 2. Apply user overrides
        if (edgeOverrides != null)
        {
            foreach (var (id, type) in edgeOverrides)
            {
                if (id < 0 || id >= mesh.Edges.Count) continue;
                if (type == EdgeType.Fold) foldEdgeIds.Add(id);
                else                       foldEdgeIds.Remove(id);
            }
        }

        // 3. Stamp edge types on the mesh
        _edgeMarker.Mark(mesh, foldEdgeIds);

        // 4. BFS unfold
        var rawResult   = _unfoldEngine.Unfold(mesh, foldEdgeIds);
        var hasOverlaps = _overlapDetector.HasOverlaps(rawResult.Faces);
        var tabs = _tabGenerator.Generate(
            rawResult.Faces,
            (float)(printSettings?.GlueTabDepthMm    ?? 4.0),
            (float)(printSettings?.GlueTabInsetRatio ?? 0.15),
            printSettings?.GlueTabShape   ?? "Trapezoid",
            printSettings?.AlternateFlaps ?? false,
            mesh);

        // Assign sequential 1-based IDs to every cut edge pair (both faces share the same ID)
        var cutEdgePairIds = new Dictionary<int, int>();
        int pairCounter = 0;
        foreach (var edge in mesh.Edges)
        {
            if (edge.FaceB >= 0 && !foldEdgeIds.Contains(edge.Id))
                cutEdgePairIds[edge.Id] = ++pairCounter;
        }

        return new UnfoldResult(rawResult.Faces, tabs, hasOverlaps, cutEdgePairIds);
    }

    /// Returns the connected components (pieces) for an already-marked mesh.
    public List<List<int>> ComputePieces(Mesh mesh) =>
        _pieceComputer.ComputePieces(mesh);

    /// Computes a uniform scale factor (mm per model unit) from the mesh bounding box.
    public static double ComputeScale(Mesh mesh, ModelScale scale)
    {
        if (mesh.Vertices.Count == 0) return 1.0;

        float minX = mesh.Vertices.Min(v => v.Position.X);
        float maxX = mesh.Vertices.Max(v => v.Position.X);
        float minY = mesh.Vertices.Min(v => v.Position.Y);
        float maxY = mesh.Vertices.Max(v => v.Position.Y);
        float minZ = mesh.Vertices.Min(v => v.Position.Z);
        float maxZ = mesh.Vertices.Max(v => v.Position.Z);

        float modelDim = scale.Axis switch
        {
            ScaleAxis.Width   => maxX - minX,
            ScaleAxis.Height  => maxY - minY,
            ScaleAxis.Depth   => maxZ - minZ,
            ScaleAxis.Longest => Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ)),
            _                 => Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ))
        };

        return modelDim < 1e-6f ? 1.0 : scale.TargetMm / modelDim;
    }

    /// Returns a human-readable bounding box summary for display in the dialog.
    public static string BoundingBoxInfo(Mesh mesh)
    {
        if (mesh.Vertices.Count == 0) return "—";
        float dx = mesh.Vertices.Max(v => v.Position.X) - mesh.Vertices.Min(v => v.Position.X);
        float dy = mesh.Vertices.Max(v => v.Position.Y) - mesh.Vertices.Min(v => v.Position.Y);
        float dz = mesh.Vertices.Max(v => v.Position.Z) - mesh.Vertices.Min(v => v.Position.Z);
        return $"W={dx:F3}  H={dy:F3}  D={dz:F3}  (model units)";
    }
}
