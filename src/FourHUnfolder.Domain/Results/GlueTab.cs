using System.Numerics;

namespace FourHUnfolder.Domain.Results;

/// <summary>
/// Trapezoidal glue tab on a cut edge.
/// P0, P1 are on the cut edge; P2, P3 are the inset/offset points.
/// FaceId + LocalEdgeIdx identify the originating face edge.
/// </summary>
public sealed class GlueTab
{
    public int    FaceId       { get; }
    public int    LocalEdgeIdx { get; }  // 0,1,2 → edge V0-V1, V1-V2, V2-V0

    public Vector2 P0 { get; }
    public Vector2 P1 { get; }
    public Vector2 P2 { get; }
    public Vector2 P3 { get; }

    public GlueTab(int faceId, int localEdgeIdx,
                   Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        FaceId       = faceId;
        LocalEdgeIdx = localEdgeIdx;
        P0 = p0; P1 = p1; P2 = p2; P3 = p3;
    }

    public Vector2[] Vertices => [P0, P1, P2, P3];
}
