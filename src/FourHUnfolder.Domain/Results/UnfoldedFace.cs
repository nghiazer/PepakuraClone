using System.Numerics;

namespace FourHUnfolder.Domain.Results;

/// <summary>
/// 2-D position of the three vertices of one unfolded triangle, with per-edge
/// type flags and optional UV texture coordinates.
///
/// Edge index convention: [0] = V0→V1, [1] = V1→V2, [2] = V2→V0.
/// </summary>
public sealed class UnfoldedFace
{
    public int     FaceId        { get; }
    public Vector2 V0            { get; }
    public Vector2 V1            { get; }
    public Vector2 V2            { get; }

    /// True when the edge is a fold edge (shared interior edge in the same piece).
    public bool[]  EdgeIsFold    { get; }

    /// True when the edge is a boundary edge (outer edge of the whole mesh — no adjacent face).
    public bool[]  EdgeIsBoundary { get; }

    /// UV texture coordinates for V0, V1, V2.  Null when the mesh has no UV data.
    public Vector2[]? UVCoords   { get; }

    public UnfoldedFace(int faceId,
                        Vector2 v0, Vector2 v1, Vector2 v2,
                        bool[]  edgeIsFold,
                        bool[]? edgeIsBoundary = null,
                        Vector2[]? uvCoords    = null)
    {
        FaceId         = faceId;
        V0             = v0; V1 = v1; V2 = v2;
        EdgeIsFold     = edgeIsFold;
        EdgeIsBoundary = edgeIsBoundary ?? [false, false, false];
        UVCoords       = uvCoords;
    }

    public Vector2[] Vertices => [V0, V1, V2];
}
