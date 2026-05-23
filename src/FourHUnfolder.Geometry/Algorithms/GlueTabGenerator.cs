using System.Numerics;
using FourHUnfolder.Domain.Models;
using FourHUnfolder.Domain.Results;

namespace FourHUnfolder.Geometry.Algorithms;

using static FourHUnfolder.Geometry.GeometryConstants;

/// <summary>
/// Generates glue tabs on cut edges.
/// Supports Trapezoid / Rectangle / Triangle shapes and alternate-flap placement.
/// </summary>
public class GlueTabGenerator
{
    public IReadOnlyList<GlueTab> Generate(
        IReadOnlyList<UnfoldedFace> faces,
        float tabDepthMm    = 4f,
        float tabInsetRatio = 0.15f,
        string tabShape     = "Trapezoid",
        bool alternateFlaps = false,
        Mesh? mesh          = null)
    {
        var tabs = new List<GlueTab>();

        // Build alternate-flap exclusion set: for each cut mesh edge, only the face
        // with the lower FaceId gets a tab when alternateFlaps is enabled.
        HashSet<(int faceId, int edgeIdx)>? alternateDeny = null;
        if (alternateFlaps && mesh != null)
        {
            alternateDeny = new HashSet<(int, int)>();
            // For each face, check each edge. If the mesh edge's FaceA != this face → deny.
            foreach (var face in faces)
            {
                var mf = mesh.Faces[face.FaceId];
                for (int i = 0; i < 3; i++)
                {
                    if (face.EdgeIsFold[i] || face.EdgeIsBoundary[i]) continue;
                    int eId = mf.EdgeIds[i];
                    var me  = mesh.Edges[eId];
                    // The face with the higher FaceId is denied the tab
                    if (me.FaceB >= 0 && face.FaceId != Math.Min(me.FaceA, me.FaceB))
                        alternateDeny.Add((face.FaceId, i));
                }
            }
        }

        foreach (var face in faces)
        {
            var verts    = face.Vertices;
            var centroid = (verts[0] + verts[1] + verts[2]) / 3f;

            for (int i = 0; i < 3; i++)
            {
                if (face.EdgeIsFold[i] || face.EdgeIsBoundary[i]) continue;
                if (alternateDeny != null && alternateDeny.Contains((face.FaceId, i))) continue;

                var p0 = verts[i];
                var p1 = verts[(i + 1) % 3];
                tabs.Add(BuildTab(face.FaceId, i, p0, p1, centroid, tabDepthMm, tabInsetRatio, tabShape));
            }
        }

        return tabs;
    }

    private static GlueTab BuildTab(int faceId, int edgeIdx,
                                    Vector2 p0, Vector2 p1, Vector2 centroid,
                                    float depth, float insetRatio, string shape)
    {
        var edge  = p1 - p0;
        float len = edge.Length();
        if (len < GeometryConstants.DegenerateTab) return new GlueTab(faceId, edgeIdx, p0, p1, p1, p0);

        var dir  = edge / len;
        var perp = new Vector2(-dir.Y, dir.X);

        var mid      = (p0 + p1) * 0.5f;
        var toCenter = centroid - mid;
        if (Vector2.Dot(toCenter, perp) > 0f) perp = -perp;

        return shape switch
        {
            "Rectangle" => BuildRect    (faceId, edgeIdx, p0, p1, perp, depth),
            "Triangle"  => BuildTriangle(faceId, edgeIdx, p0, p1, perp, depth),
            _           => BuildTrapezoid(faceId, edgeIdx, p0, p1, dir, perp, depth, insetRatio)
        };
    }

    private static GlueTab BuildTrapezoid(int faceId, int edgeIdx,
        Vector2 p0, Vector2 p1, Vector2 dir, Vector2 perp, float depth, float insetRatio)
    {
        float len   = (p1 - p0).Length();
        float inset = len * insetRatio;
        var   q0    = p0 + inset * dir + depth * perp;
        var   q1    = p1 - inset * dir + depth * perp;
        return new GlueTab(faceId, edgeIdx, p0, p1, q1, q0);
    }

    private static GlueTab BuildRect(int faceId, int edgeIdx,
        Vector2 p0, Vector2 p1, Vector2 perp, float depth)
    {
        var q0 = p0 + depth * perp;
        var q1 = p1 + depth * perp;
        return new GlueTab(faceId, edgeIdx, p0, p1, q1, q0);
    }

    private static GlueTab BuildTriangle(int faceId, int edgeIdx,
        Vector2 p0, Vector2 p1, Vector2 perp, float depth)
    {
        var tip = (p0 + p1) * 0.5f + depth * perp;
        // Degenerate quad with p2 == p3 == tip renders as a triangle
        return new GlueTab(faceId, edgeIdx, p0, p1, tip, tip);
    }
}
