using System.Numerics;
using FluentAssertions;
using FourHUnfolder.Domain.Models;
using FourHUnfolder.Domain.Results;
using FourHUnfolder.Geometry.Algorithms;
using FourHUnfolder.Infrastructure.Loaders;
using Xunit;

namespace FourHUnfolder.Tests;

public class OverlapDetectorTests
{
    private static UnfoldedFace MakeFace(int id, Vector2 v0, Vector2 v1, Vector2 v2) =>
        new(id, v0, v1, v2,
            edgeIsFold: [false, false, false],
            edgeIsBoundary: [false, false, false]);

    [Fact]
    public void SeparatedTriangles_NoOverlap()
    {
        // Two triangles well apart
        var a = MakeFace(0, new(0, 0), new(1, 0), new(0, 1));
        var b = MakeFace(1, new(5, 5), new(6, 5), new(5, 6));

        new OverlapDetector().HasOverlaps([a, b]).Should().BeFalse();
    }

    [Fact]
    public void OverlappingTriangles_DetectedAsOverlap()
    {
        // Two triangles that share the same space
        var a = MakeFace(0, new(0, 0), new(2, 0), new(0, 2));
        var b = MakeFace(1, new(0.5f, 0.5f), new(2.5f, 0.5f), new(0.5f, 2.5f));

        new OverlapDetector().HasOverlaps([a, b]).Should().BeTrue();
    }

    [Fact]
    public void AdjacentTriangles_SharingEdge_NotOverlap()
    {
        // Two triangles sharing edge (0,0)-(1,0) — adjacent, not overlapping
        var a = MakeFace(0, new(0, 0), new(1, 0), new(0.5f,  1));
        var b = MakeFace(1, new(0, 0), new(1, 0), new(0.5f, -1));

        new OverlapDetector().HasOverlaps([a, b]).Should().BeFalse();
    }

    [Fact]
    public void SingleFace_NoOverlap()
    {
        var a = MakeFace(0, new(0, 0), new(1, 0), new(0, 1));

        new OverlapDetector().HasOverlaps([a]).Should().BeFalse();
    }
}

public class GlueTabGeneratorTests
{
    private static UnfoldedFace CutFace(int id, Vector2 v0, Vector2 v1, Vector2 v2,
                                         bool[] isBoundary = null!) =>
        new(id, v0, v1, v2,
            edgeIsFold: [false, false, false],
            edgeIsBoundary: isBoundary ?? [false, false, false]);

    [Fact]
    public void AllCutEdges_GeneratesThreeTabs()
    {
        var face = CutFace(0, new(0, 0), new(10, 0), new(5, 8));

        var tabs = new GlueTabGenerator().Generate([face]);

        tabs.Should().HaveCount(3);
    }

    [Fact]
    public void BoundaryEdges_DoNotGetTabs()
    {
        // Edge 0 and 1 are boundary — only edge 2 (V2→V0) should get a tab
        var face = CutFace(0, new(0, 0), new(10, 0), new(5, 8),
                           isBoundary: [true, true, false]);

        var tabs = new GlueTabGenerator().Generate([face]);

        tabs.Should().HaveCount(1);
    }

    [Fact]
    public void FoldEdges_DoNotGetTabs()
    {
        var face = new UnfoldedFace(0,
            new Vector2(0, 0), new Vector2(10, 0), new Vector2(5, 8),
            edgeIsFold:     [true, false, false],
            edgeIsBoundary: [false, false, false]);

        var tabs = new GlueTabGenerator().Generate([face]);

        tabs.Should().HaveCount(2);   // edge 0 is fold → skipped; edges 1 + 2 get tabs
    }

    [Fact]
    public void DegenerateEdge_DoesNotThrow()
    {
        // All three vertices at same point → degenerate triangle
        var face = CutFace(0, new(1, 1), new(1, 1), new(1, 1));

        var act = () => new GlueTabGenerator().Generate([face]);

        act.Should().NotThrow();
    }

    [Fact]
    public void TabOutwardDirection_PointsAwayFromFaceCentroid()
    {
        // Flat horizontal triangle; tab should extend downward (away from centroid above edge)
        var face = CutFace(0, new(0, 0), new(10, 0), new(5, 5));
        var tabs = new GlueTabGenerator().Generate([face]);

        // Tab for edge V0→V1 (bottom edge) — centroid is at y≈1.67 → outward is y<0
        var tab = tabs[0];
        var midOuter = (tab.P2 + tab.P3) / 2;
        midOuter.Y.Should().BeLessThan(0f);
    }
}

public class ObjMeshLoaderTests
{
    private static string TempObj(string content)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"test_{System.Guid.NewGuid():N}.obj");
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void LoadTetrahedron_FourFacesLoaded()
    {
        var obj = TempObj("""
            v 0.0 0.0 0.0
            v 1.0 0.0 0.0
            v 0.5 1.0 0.0
            v 0.5 0.5 1.0
            f 1 2 3
            f 1 2 4
            f 2 3 4
            f 1 3 4
            """);
        try
        {
            var mesh = new ObjMeshLoader().Load(obj);
            mesh.Faces.Should().HaveCount(4);
            mesh.Vertices.Should().HaveCount(4);
        }
        finally { System.IO.File.Delete(obj); }
    }

    [Fact]
    public void MalformedFloatToken_LoadsWithoutException()
    {
        // "1.0e" is an invalid float — should be treated as 0f, not throw
        var obj = TempObj("""
            v 1.0e 0.0 0.0
            v 1.0 0.0 0.0
            v 0.5 1.0 0.0
            f 1 2 3
            """);
        try
        {
            var act = () => new ObjMeshLoader().Load(obj);
            act.Should().NotThrow();
        }
        finally { System.IO.File.Delete(obj); }
    }

    [Fact]
    public void NegativeVertexIndices_IgnoredGracefully()
    {
        // OBJ negative index (-1) should be treated as absent, not throw
        var obj = TempObj("""
            v 0.0 0.0 0.0
            v 1.0 0.0 0.0
            v 0.5 1.0 0.0
            f 1/1 2/2 3/-1
            """);
        try
        {
            var act = () => new ObjMeshLoader().Load(obj);
            act.Should().NotThrow();
        }
        finally { System.IO.File.Delete(obj); }
    }

    [Fact]
    public void QuadFace_FanTriangulated()
    {
        // A quad face (4 vertices) should produce 2 triangles
        var obj = TempObj("""
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            f 1 2 3 4
            """);
        try
        {
            var mesh = new ObjMeshLoader().Load(obj);
            mesh.Faces.Should().HaveCount(2);
        }
        finally { System.IO.File.Delete(obj); }
    }
}
