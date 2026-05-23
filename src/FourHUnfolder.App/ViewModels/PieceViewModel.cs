using System.Numerics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using FourHUnfolder.Domain.Results;

namespace FourHUnfolder.App.ViewModels;

/// <summary>
/// Represents one paper piece (connected component of fold edges) in the 2-D layout.
/// All coordinates are in millimetres; position is the piece centroid on the paper.
/// </summary>
public partial class PieceViewModel : ObservableObject
{
    public int        GroupId  { get; }
    public FaceData[] Faces    { get; }
    public TabData[]  GlueTabs { get; }

    [ObservableProperty] private double _positionX;
    [ObservableProperty] private double _positionY;
    [ObservableProperty] private double _rotation;    // degrees
    [ObservableProperty] private bool   _isSelected;

    public PieceViewModel(int groupId, FaceData[] faces, TabData[] glueTabs,
                          double posX, double posY)
    {
        GroupId   = groupId;
        Faces     = faces;
        GlueTabs  = glueTabs;
        PositionX = posX;
        PositionY = posY;
    }

    // ── nested data ──────────────────────────────────────────────────────────

    /// One triangle within the piece, expressed in piece-local mm coordinates.
    public sealed class FaceData
    {
        public int     FaceId      { get; }
        public int[]   MeshEdgeIds { get; }   // Mesh.Edges indices for edges 0,1,2
        public Point   V0          { get; }
        public Point   V1          { get; }
        public Point   V2          { get; }
        public bool[]  EdgeIsFold     { get; }   // [e01, e12, e20]
        public bool[]  EdgeIsBoundary { get; }   // true = outer mesh boundary, no adjacent face

        /// UV texture coordinates for V0, V1, V2.  Null when the mesh has no UV data.
        public Vector2[]? UVCoords { get; }

        /// 1-based pair ID for each cut edge; 0 for fold/boundary edges.
        public int[] EdgePairIds { get; }

        public FaceData(int faceId, int[] meshEdgeIds,
                        Point v0, Point v1, Point v2,
                        bool[] edgeIsFold, bool[]? edgeIsBoundary = null,
                        Vector2[]? uvCoords = null,
                        int[]? edgePairIds = null)
        {
            FaceId         = faceId;
            MeshEdgeIds    = meshEdgeIds;
            V0             = v0; V1 = v1; V2 = v2;
            EdgeIsFold     = edgeIsFold;
            EdgeIsBoundary = edgeIsBoundary ?? [false, false, false];
            UVCoords       = uvCoords;
            EdgePairIds    = edgePairIds ?? [0, 0, 0];
        }
    }

    /// One glue tab trapezoid in piece-local mm coordinates.
    public sealed class TabData
    {
        public int     FaceId       { get; }
        public int     LocalEdgeIdx { get; }
        public Point[] Points       { get; }
        public TabData(int faceId, int localEdgeIdx, Point p0, Point p1, Point p2, Point p3)
        {
            FaceId       = faceId;
            LocalEdgeIdx = localEdgeIdx;
            Points       = [p0, p1, p2, p3];
        }
    }

    // ── factory ──────────────────────────────────────────────────────────────

    public static PieceViewModel Create(
        int                       groupId,
        IReadOnlyList<int>        faceIds,
        UnfoldResult              unfoldResult,
        Domain.Models.Mesh        mesh,
        double                    scaleMmPerUnit)
    {
        var faceSet = new HashSet<int>(faceIds);
        var uFaces  = unfoldResult.Faces.Where(f => faceSet.Contains(f.FaceId)).ToList();

        // Centroid of all vertices (in model units)
        var allPts = uFaces.SelectMany(f => f.Vertices).ToList();
        double cx  = allPts.Average(p => (double)p.X);
        double cy  = allPts.Average(p => (double)p.Y);

        Point ToLocal(Vector2 v) =>
            new((v.X - cx) * scaleMmPerUnit,
                (v.Y - cy) * scaleMmPerUnit);

        var faceDatas = uFaces.Select(uf =>
        {
            var mf     = mesh.Faces[uf.FaceId];
            var eIds   = mf.EdgeIds.ToArray();
            var pairIds = new int[3];
            for (int i = 0; i < 3; i++)
                pairIds[i] = unfoldResult.CutEdgePairIds.GetValueOrDefault(eIds[i]);
            return new FaceData(
                uf.FaceId,
                eIds,
                ToLocal(uf.V0), ToLocal(uf.V1), ToLocal(uf.V2),
                uf.EdgeIsFold, uf.EdgeIsBoundary,
                uf.UVCoords, pairIds);
        }).ToArray();

        // Glue tabs belonging to faces in this piece
        var tabDatas = unfoldResult.GlueTabs
            .Where(t => faceSet.Contains(t.FaceId))
            .Select(t => new TabData(
                t.FaceId, t.LocalEdgeIdx,
                ToLocal(t.P0), ToLocal(t.P1),
                ToLocal(t.P2), ToLocal(t.P3)))
            .ToArray();

        return new PieceViewModel(
            groupId, faceDatas, tabDatas,
            posX: cx * scaleMmPerUnit,
            posY: cy * scaleMmPerUnit);
    }
}
