using System.Numerics;
using FluentAssertions;
using FourHUnfolder.Domain.Models;
using FourHUnfolder.Geometry.Algorithms;
using Xunit;

namespace FourHUnfolder.Tests;

public class UnfoldEngineTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static Mesh TwoFlatTriangles()
    {
        var mesh = new Mesh();
        mesh.AddVertex(new Vertex(0, new Vector3(0, 0, 0)));
        mesh.AddVertex(new Vertex(1, new Vector3(1, 0, 0)));
        mesh.AddVertex(new Vertex(2, new Vector3(0, 1, 0)));
        mesh.AddVertex(new Vertex(3, new Vector3(1, 1, 0)));
        mesh.AddFace(0, 1, 2);
        mesh.AddFace(1, 3, 2);
        return mesh;
    }

    private static Mesh SimpleCube()
    {
        var mesh  = new Mesh();
        var verts = new Vector3[]
        {
            new(0,0,0), new(1,0,0), new(1,1,0), new(0,1,0),
            new(0,0,1), new(1,0,1), new(1,1,1), new(0,1,1)
        };
        for (int i = 0; i < verts.Length; i++)
            mesh.AddVertex(new Vertex(i, verts[i]));

        int[][] quads =
        [
            [0,1,2,3], [4,5,6,7], [0,1,5,4],
            [2,3,7,6], [1,2,6,5], [0,3,7,4]
        ];
        foreach (var q in quads)
        { mesh.AddFace(q[0], q[1], q[2]); mesh.AddFace(q[0], q[2], q[3]); }
        return mesh;
    }

    /// Runs the full pipeline and returns (unfold result, fold edge IDs).
    private static FourHUnfolder.Domain.Results.UnfoldResult RunPipeline(Mesh mesh)
    {
        var dg          = new DualGraphBuilder().Build(mesh);
        var mst         = new KruskalMstBuilder().Build(dg);
        var foldEdgeIds = mst.Select(e => e.SharedMeshEdgeId).ToHashSet();
        new EdgeMarker().Mark(mesh, mst);
        return new UnfoldEngine().Unfold(mesh, foldEdgeIds);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoTriangles_BothFacesUnfolded()
        => RunPipeline(TwoFlatTriangles()).Faces.Should().HaveCount(2);

    [Fact]
    public void RootFace_FirstVertexAtOrigin()
    {
        var root = RunPipeline(TwoFlatTriangles()).Faces.Single(f => f.FaceId == 0);
        root.V0.X.Should().BeApproximately(0f, 0.001f);
        root.V0.Y.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void RootFace_SecondVertexOnXAxis()
    {
        var root = RunPipeline(TwoFlatTriangles()).Faces.Single(f => f.FaceId == 0);
        root.V1.Y.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void EdgeLengthsPreservedAfterUnfold()
    {
        var mesh   = TwoFlatTriangles();
        var result = RunPipeline(mesh);

        foreach (var face in result.Faces)
        {
            var mf     = mesh.Faces[face.FaceId];
            var v2d    = face.Vertices;
            int[] gIds = [mf.A, mf.B, mf.C];

            for (int i = 0; i < 3; i++)
            {
                float len3d = Vector3.Distance(
                    mesh.Vertices[gIds[i]].Position,
                    mesh.Vertices[gIds[(i + 1) % 3]].Position);
                float len2d = Vector2.Distance(v2d[i], v2d[(i + 1) % 3]);
                len2d.Should().BeApproximately(len3d, 0.001f);
            }
        }
    }

    [Fact]
    public void FlatMesh_SharedEdge_SamePositionsInBothFaces()
    {
        var mesh   = TwoFlatTriangles();
        var result = RunPipeline(mesh);

        var f0 = result.Faces.Single(f => f.FaceId == 0);
        var f1 = result.Faces.Single(f => f.FaceId == 1);

        var face0Map = new Dictionary<int, Vector2>
        {
            [mesh.Faces[0].A] = f0.V0,
            [mesh.Faces[0].B] = f0.V1,
            [mesh.Faces[0].C] = f0.V2
        };
        var face1Map = new Dictionary<int, Vector2>
        {
            [mesh.Faces[1].A] = f1.V0,
            [mesh.Faces[1].B] = f1.V1,
            [mesh.Faces[1].C] = f1.V2
        };

        foreach (int sharedId in new[] { 1, 2 })
            Vector2.Distance(face0Map[sharedId], face1Map[sharedId])
                   .Should().BeLessThan(0.001f);
    }

    [Fact]
    public void Cube_AllTwelveFacesUnfolded()
        => RunPipeline(SimpleCube()).Faces.Should().HaveCount(12);

    [Fact]
    public void Cube_EdgeMarking_MSTEdgesAreFold()
    {
        var mesh = SimpleCube();
        var dg   = new DualGraphBuilder().Build(mesh);
        var mst  = new KruskalMstBuilder().Build(dg);
        new EdgeMarker().Mark(mesh, mst);

        mesh.Edges.Count(e => e.Type == EdgeType.Fold).Should().Be(11);
    }

    [Fact]
    public void EdgeMarker_AllInteriorEdgesLabelled()
    {
        var mesh = SimpleCube();
        var dg   = new DualGraphBuilder().Build(mesh);
        var mst  = new KruskalMstBuilder().Build(dg);
        new EdgeMarker().Mark(mesh, mst);

        mesh.Edges.Where(e => e.ConnectsFaces).Should()
            .OnlyContain(e => e.Type == EdgeType.Fold || e.Type == EdgeType.Cut);
    }

    [Fact]
    public void PieceComputer_TwoFlatTriangles_OnePiece()
    {
        var mesh = TwoFlatTriangles();
        RunPipeline(mesh);  // marks edges
        var pieces = new PieceComputer().ComputePieces(mesh);
        pieces.Should().HaveCount(1, "both triangles are connected by a fold edge");
    }

    [Fact]
    public void PieceComputer_WithCutOnly_TwoPieces()
    {
        var mesh = TwoFlatTriangles();
        // Force all interior edges to Cut
        var dg  = new DualGraphBuilder().Build(mesh);
        var mst = new KruskalMstBuilder().Build(dg);
        new EdgeMarker().Mark(mesh, mst);

        // Override the fold edge to cut
        foreach (var e in mesh.Edges.Where(e => e.Type == EdgeType.Fold))
            e.Type = EdgeType.Cut;

        var pieces = new PieceComputer().ComputePieces(mesh);
        pieces.Should().HaveCount(2);
    }
}
