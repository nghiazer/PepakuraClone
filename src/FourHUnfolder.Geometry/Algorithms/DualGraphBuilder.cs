using System.Numerics;
using FourHUnfolder.Domain.DualGraph;
using FourHUnfolder.Domain.Models;

namespace FourHUnfolder.Geometry.Algorithms;

/// <summary>
/// Builds a dual graph from a triangle mesh.
/// One node per face; one graph edge per interior mesh edge (shared by two faces).
/// Edge weight = dihedral angle between adjacent face normals
/// (flat = 0 rad → prefer fold; sharp = π rad → prefer cut).
/// </summary>
public class DualGraphBuilder
{
    public DualGraph Build(Mesh mesh)
    {
        var graph = new DualGraph();

        for (int i = 0; i < mesh.Faces.Count; i++)
            graph.Nodes.Add(new GraphNode(i));

        foreach (var edge in mesh.Edges)
        {
            if (!edge.ConnectsFaces) continue;

            var weight = ComputeDihedralAngle(mesh, edge);
            var ge     = new GraphEdge(graph.Edges.Count, edge.FaceA, edge.FaceB, edge.Id, weight);
            graph.Edges.Add(ge);
            graph.Nodes[edge.FaceA].GraphEdgeIds.Add(ge.Id);
            graph.Nodes[edge.FaceB].GraphEdgeIds.Add(ge.Id);
        }

        return graph;
    }

    private static float ComputeDihedralAngle(Mesh mesh, Edge edge)
    {
        var n1  = ComputeFaceNormal(mesh, edge.FaceA);
        var n2  = ComputeFaceNormal(mesh, edge.FaceB);
        var dot = Vector3.Dot(n1, n2);
        return MathF.Acos(Math.Clamp(dot, -1f, 1f));
    }

    private static Vector3 ComputeFaceNormal(Mesh mesh, int faceId)
    {
        var f      = mesh.Faces[faceId];
        var a      = mesh.Vertices[f.A].Position;
        var b      = mesh.Vertices[f.B].Position;
        var c      = mesh.Vertices[f.C].Position;
        var cross  = Vector3.Cross(b - a, c - a);
        float len  = cross.Length();
        // Guard against degenerate (zero-area) triangles
        return len > 1e-10f ? cross / len : Vector3.UnitY;
    }
}
