using System.Numerics;
using FourHUnfolder.Domain.Results;

namespace FourHUnfolder.Geometry.Algorithms;

/// <summary>
/// Generates trapezoidal glue tabs on every cut edge.
/// Each tab records which face+edge produced it so it can be
/// associated with the correct piece during layout.
/// </summary>
public class GlueTabGenerator
{
    private const float TabDepth = 4f;
    private const float TabInset = 0.15f;

    public IReadOnlyList<GlueTab> Generate(IReadOnlyList<UnfoldedFace> faces)
    {
        var tabs = new List<GlueTab>();

        foreach (var face in faces)
        {
            var verts = face.Vertices;
            for (int i = 0; i < 3; i++)
            {
                if (face.EdgeIsFold[i]) continue;

                var p0       = verts[i];
                var p1       = verts[(i + 1) % 3];
                var centroid = (verts[0] + verts[1] + verts[2]) / 3f;

                tabs.Add(BuildTab(face.FaceId, i, p0, p1, centroid));
            }
        }

        return tabs;
    }

    private static GlueTab BuildTab(int faceId, int edgeIdx,
                                    Vector2 p0, Vector2 p1, Vector2 centroid)
    {
        var edge  = p1 - p0;
        float len = edge.Length();
        if (len < 1e-4f) return new GlueTab(faceId, edgeIdx, p0, p1, p1, p0);

        var dir  = edge / len;
        var perp = new Vector2(-dir.Y, dir.X);

        // Outward direction: away from the face centroid
        var mid      = (p0 + p1) * 0.5f;
        var toCenter = centroid - mid;
        if (Vector2.Dot(toCenter, perp) > 0f) perp = -perp;

        float inset = len * TabInset;
        var   q0    = p0 + inset * dir + TabDepth * perp;
        var   q1    = p1 - inset * dir + TabDepth * perp;

        return new GlueTab(faceId, edgeIdx, p0, p1, q1, q0);
    }
}
