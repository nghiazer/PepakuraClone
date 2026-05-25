using System.Numerics;
using FluentAssertions;
using FourHUnfolder.Domain.Models;
using FourHUnfolder.Geometry.Algorithms;
using FourHUnfolder.Infrastructure.Loaders;
using Xunit;

namespace FourHUnfolder.Tests;

/// <summary>
/// Unit tests for <see cref="PdoUnfoldBuilder"/>:
///   - Correct face count from PdoLayout
///   - Edge type classification (Fold = same part, Cut = different parts, Boundary)
///   - 2-D coordinates are preserved exactly from PdoLayout
///   - No-layout guard throws correctly
/// Integration smoke-tests against real PDO files (skipped in CI when files absent).
/// </summary>
public class PdoUnfoldBuilderTests
{
    private static readonly string ProjectRoot =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));

    private static string Pdo(string name) => Path.Combine(ProjectRoot, name);
    private static bool   Exists(string p)  => File.Exists(p);

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// Builds a tiny two-triangle mesh with an explicit PdoLayout.
    /// The two triangles share edge v0-v1 (edge index 0 for face 0 and some
    /// edge for face 1).  Face 0 → part 0, Face 1 → part 1 (so the shared
    /// edge should be Cut).
    private static Mesh MakeTwoTriangleMesh(int partA = 0, int partB = 1)
    {
        var mesh = new Mesh();
        // Vertices
        mesh.AddVertex(new Vertex(0, new Vector3(0, 0, 0)));
        mesh.AddVertex(new Vertex(1, new Vector3(1, 0, 0)));
        mesh.AddVertex(new Vertex(2, new Vector3(0, 1, 0)));
        mesh.AddVertex(new Vertex(3, new Vector3(1, 1, 0)));

        // Two faces sharing edge v1-v2
        mesh.AddFace(0, 1, 2);  // face 0
        mesh.AddFace(1, 3, 2);  // face 1  (shares edge v1-v2 with face 0)

        // PdoLayout: simple paper-space coords, two different parts by default
        var layout = new PdoLayout();
        layout.Faces.Add(new PdoFace(0, partA,
            new Vector2(0, 0), new Vector2(10, 0), new Vector2(0, 10)));
        layout.Faces.Add(new PdoFace(1, partB,
            new Vector2(10, 0), new Vector2(20, 0), new Vector2(10, 10)));
        mesh.PdoLayout = layout;
        return mesh;
    }

    // ── Unit tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Build_FaceCountMatchesPdoLayout()
    {
        var mesh    = MakeTwoTriangleMesh();
        var builder = new PdoUnfoldBuilder();

        var result = builder.Build(mesh);

        result.Should().HaveCount(2, "one UnfoldedFace per PdoFace");
    }

    [Fact]
    public void Build_FaceIdsMatchPdoLayout()
    {
        var mesh   = MakeTwoTriangleMesh();
        var result = new PdoUnfoldBuilder().Build(mesh);

        result[0].FaceId.Should().Be(0);
        result[1].FaceId.Should().Be(1);
    }

    [Fact]
    public void Build_2DCoordinatesPreservedExactly()
    {
        var mesh   = MakeTwoTriangleMesh();
        var layout = mesh.PdoLayout!;
        var result = new PdoUnfoldBuilder().Build(mesh);

        // Face 0: A=(0,0) B=(10,0) C=(0,10) — stored in v0/v1/v2
        result[0].V0.Should().Be(layout.Faces[0].A);
        result[0].V1.Should().Be(layout.Faces[0].B);
        result[0].V2.Should().Be(layout.Faces[0].C);

        // Face 1
        result[1].V0.Should().Be(layout.Faces[1].A);
        result[1].V1.Should().Be(layout.Faces[1].B);
        result[1].V2.Should().Be(layout.Faces[1].C);
    }

    [Fact]
    public void Build_SharedEdgeDifferentParts_IsClassifiedAsCut()
    {
        // partA=0, partB=1  → shared edge must be Cut
        var mesh   = MakeTwoTriangleMesh(partA: 0, partB: 1);
        new PdoUnfoldBuilder().Build(mesh);

        // The shared edge (v1-v2) is between face 0 and face 1 which have different parts
        var sharedEdge = mesh.Edges.Single(e => e.FaceA >= 0 && e.FaceB >= 0);
        sharedEdge.Type.Should().Be(EdgeType.Cut,
            "faces in different parts → Cut edge");
    }

    [Fact]
    public void Build_SharedEdgeSamePart_IsClassifiedAsFold()
    {
        // partA=0, partB=0  → shared edge must be Fold
        var mesh   = MakeTwoTriangleMesh(partA: 0, partB: 0);
        new PdoUnfoldBuilder().Build(mesh);

        var sharedEdge = mesh.Edges.Single(e => e.FaceA >= 0 && e.FaceB >= 0);
        sharedEdge.Type.Should().Be(EdgeType.Fold,
            "faces in the same part → Fold edge");
    }

    [Fact]
    public void Build_BoundaryEdges_AreClassifiedAsBoundary()
    {
        var mesh   = MakeTwoTriangleMesh();
        new PdoUnfoldBuilder().Build(mesh);

        var boundaryEdges = mesh.Edges.Where(e => e.IsBoundary).ToList();
        boundaryEdges.Should().NotBeEmpty("two triangles have boundary edges");
        boundaryEdges.Should().OnlyContain(e => e.Type == EdgeType.Boundary);
    }

    [Fact]
    public void Build_NoLayout_ThrowsInvalidOperationException()
    {
        var mesh   = MakeTwoTriangleMesh();
        mesh.PdoLayout = null;   // strip the layout
        var builder = new PdoUnfoldBuilder();

        var act = () => builder.Build(mesh);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*PdoUnfoldBuilder*");
    }

    // ── Integration smoke-tests against real PDO files ────────────────────────

    [Fact]
    public void SoundEmitter_Build_FaceCountMatchesMesh()
    {
        var path = Pdo("SoundEmitterObject.pdo");
        if (!Exists(path)) return;

        var mesh   = new PdoMeshLoader().Load(path);
        mesh.PdoLayout.Should().NotBeNull("SoundEmitter is a fully-laid-out PDO");

        var result = new PdoUnfoldBuilder().Build(mesh);
        result.Count.Should().Be(mesh.Faces.Count,
            "one UnfoldedFace per mesh face");
    }

    [Fact]
    public void SoundEmitter_Build_AllEdgesClassified()
    {
        var path = Pdo("SoundEmitterObject.pdo");
        if (!Exists(path)) return;

        var mesh = new PdoMeshLoader().Load(path);
        new PdoUnfoldBuilder().Build(mesh);

        // Every edge should have been classified (not left as default/unknown)
        var unclassified = mesh.Edges
            .Where(e => e.Type != EdgeType.Fold
                     && e.Type != EdgeType.Cut
                     && e.Type != EdgeType.Boundary)
            .ToList();

        unclassified.Should().BeEmpty("all edges must be classified after Build()");
    }

    [Fact]
    public void SoundEmitter_Build_HasFoldAndCutEdges()
    {
        var path = Pdo("SoundEmitterObject.pdo");
        if (!Exists(path)) return;

        var mesh = new PdoMeshLoader().Load(path);
        new PdoUnfoldBuilder().Build(mesh);

        mesh.Edges.Should().Contain(e => e.Type == EdgeType.Fold,  "a real PDO has fold edges");
        mesh.Edges.Should().Contain(e => e.Type == EdgeType.Cut,   "a real PDO has cut edges");
    }

    [Fact]
    public void Pillar_Build_FaceCountMatchesMesh()
    {
        var path = Pdo("Pillar.pdo");
        if (!Exists(path)) return;

        var mesh   = new PdoMeshLoader().Load(path);
        var result = new PdoUnfoldBuilder().Build(mesh);

        result.Count.Should().Be(mesh.Faces.Count);
    }
}
