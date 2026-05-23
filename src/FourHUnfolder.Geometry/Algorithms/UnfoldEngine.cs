using System.Numerics;
using FourHUnfolder.Domain.Models;
using FourHUnfolder.Domain.Results;

namespace FourHUnfolder.Geometry.Algorithms;

using static FourHUnfolder.Geometry.GeometryConstants;

/// <summary>
/// BFS unfold. Accepts a set of fold-edge mesh IDs (from Kruskal MST ± user overrides)
/// and places each triangle in 2-D, preserving 3-D edge lengths.
/// </summary>
public class UnfoldEngine
{
    public UnfoldResult Unfold(Mesh mesh, IReadOnlyCollection<int> foldEdgeIds)
    {
        if (mesh.Faces.Count == 0)
            return new UnfoldResult([], [], false);

        var foldSet = new HashSet<int>(foldEdgeIds);
        var adj     = BuildAdjacency(mesh, foldSet);

        var placed  = new Dictionary<int, Vector2[]>(mesh.Faces.Count);
        var visited = new HashSet<int>(mesh.Faces.Count);

        // Find the root of each connected component and BFS from it
        for (int startFace = 0; startFace < mesh.Faces.Count; startFace++)
        {
            if (visited.Contains(startFace)) continue;

            placed[startFace] = PlaceRootFace(mesh, mesh.Faces[startFace]);
            visited.Add(startFace);

            var queue = new Queue<(int faceId, int parentId, int sharedEdgeId)>();
            foreach (var (nbId, eId) in adj[startFace])
                queue.Enqueue((nbId, startFace, eId));

            while (queue.Count > 0)
            {
                var (faceId, parentId, sharedEdgeId) = queue.Dequeue();
                if (visited.Contains(faceId)) continue;
                visited.Add(faceId);

                try
                {
                    placed[faceId] = PlaceChildFace(
                        mesh,
                        mesh.Faces[faceId],
                        mesh.Faces[parentId],
                        placed[parentId],
                        mesh.Edges[sharedEdgeId]);
                }
                catch (InvalidOperationException)
                {
                    // Malformed topology: skip this face, place it at origin as fallback
                    placed[faceId] = [Vector2.Zero, Vector2.One, new Vector2(0f, 1f)];
                }

                foreach (var (nbId, eId) in adj[faceId])
                    if (!visited.Contains(nbId))
                        queue.Enqueue((nbId, faceId, eId));
            }
        }

        var unfoldedFaces = new List<UnfoldedFace>(placed.Count);
        bool hasUV = mesh.HasUVs;

        foreach (var (faceId, pos) in placed)
        {
            var face         = mesh.Faces[faceId];
            var edgeIsFold   = face.EdgeIds.Select(eid => mesh.Edges[eid].Type == EdgeType.Fold).ToArray();
            var edgeIsBound  = face.EdgeIds.Select(eid => mesh.Edges[eid].Type == EdgeType.Boundary).ToArray();

            // Copy UV coordinates for this face (null when mesh has no UVs)
            Vector2[]? uvCoords = null;
            if (hasUV && mesh.FaceUVs.Count > faceId)
            {
                var (ua, ub, uc) = mesh.FaceUVs[faceId];
                uvCoords = [
                    ua >= 0 && ua < mesh.UVs.Count ? mesh.UVs[ua] : Vector2.Zero,
                    ub >= 0 && ub < mesh.UVs.Count ? mesh.UVs[ub] : Vector2.Zero,
                    uc >= 0 && uc < mesh.UVs.Count ? mesh.UVs[uc] : Vector2.Zero
                ];
            }

            unfoldedFaces.Add(new UnfoldedFace(
                faceId, pos[0], pos[1], pos[2],
                edgeIsFold, edgeIsBound, uvCoords));
        }

        return new UnfoldResult(unfoldedFaces, [], false);
    }

    // ── root / child placement ────────────────────────────────────────────────

    private static Vector2[] PlaceRootFace(Mesh mesh, Face face)
    {
        var a3 = mesh.Vertices[face.A].Position;
        var b3 = mesh.Vertices[face.B].Position;
        var c3 = mesh.Vertices[face.C].Position;

        float ab = Vector3.Distance(a3, b3);
        float ac = Vector3.Distance(a3, c3);
        float bc = Vector3.Distance(b3, c3);

        var p0 = Vector2.Zero;
        var p1 = new Vector2(ab, 0f);
        var p2 = TriangleApex(p0, p1, ac, bc, apexAbove: true);

        return [p0, p1, p2];
    }

    private static Vector2[] PlaceChildFace(
        Mesh      mesh,
        Face      face,
        Face      parentFace,
        Vector2[] parentPos,
        Edge      sharedEdge)
    {
        var fv  = face.VertexIds;
        var pv  = parentFace.VertexIds;

        int[] ls  = FindSharedLocalIndices(fv, sharedEdge);
        int   la  = 3 - ls[0] - ls[1];
        int[] lsp = FindSharedLocalIndices(pv, sharedEdge);

        Vector2 sv1_2d, sv2_2d;
        if (pv[lsp[0]] == fv[ls[0]])
        { sv1_2d = parentPos[lsp[0]]; sv2_2d = parentPos[lsp[1]]; }
        else
        { sv1_2d = parentPos[lsp[1]]; sv2_2d = parentPos[lsp[0]]; }

        var v3_apex = mesh.Vertices[fv[la]].Position;
        var v3_sv1  = mesh.Vertices[fv[ls[0]]].Position;
        var v3_sv2  = mesh.Vertices[fv[ls[1]]].Position;

        float da = Vector3.Distance(v3_apex, v3_sv1);
        float db = Vector3.Distance(v3_apex, v3_sv2);

        var parentCentroid = (parentPos[0] + parentPos[1] + parentPos[2]) / 3f;
        var apexPos        = ReconstructApex(sv1_2d, sv2_2d, da, db, parentCentroid);

        var positions = new Vector2[3];
        positions[ls[0]] = sv1_2d;
        positions[ls[1]] = sv2_2d;
        positions[la]    = apexPos;
        return positions;
    }

    // ── geometry helpers ──────────────────────────────────────────────────────

    private static Vector2 TriangleApex(Vector2 p1, Vector2 p2,
                                        float da, float db, bool apexAbove)
    {
        var ab    = p2 - p1;
        float len = ab.Length();
        if (len < GeometryConstants.DegenerateEdge) return p1 + new Vector2(da, 0f);
        float t  = (da * da - db * db + len * len) / (2f * len * len);
        var   ft = p1 + t * ab;
        float h  = MathF.Sqrt(MathF.Max(0f, da * da - t * t * len * len));
        var abN  = ab / len;
        var perp = new Vector2(-abN.Y, abN.X);
        return apexAbove ? ft + h * perp : ft - h * perp;
    }

    private static Vector2 ReconstructApex(Vector2 sv1, Vector2 sv2,
                                           float da, float db, Vector2 parentCentroid)
    {
        var ab    = sv2 - sv1;
        float len = ab.Length();
        if (len < DegenerateEdge) return sv1 + new Vector2(da, 0f);
        float t  = (da * da - db * db + len * len) / (2f * len * len);
        var   ft = sv1 + t * ab;
        float h  = MathF.Sqrt(MathF.Max(0f, da * da - t * t * len * len));
        var abN  = ab / len;
        var perp = new Vector2(-abN.Y, abN.X);
        var c1   = ft + h * perp;
        var c2   = ft - h * perp;

        float CrossSign(Vector2 p) =>
            (sv2.X - sv1.X) * (p.Y - sv1.Y) - (sv2.Y - sv1.Y) * (p.X - sv1.X);

        return Math.Sign(CrossSign(parentCentroid)) == Math.Sign(CrossSign(c1)) ? c2 : c1;
    }

    private static int[] FindSharedLocalIndices(int[] vids, Edge edge)
    {
        var r = new int[2]; int n = 0;
        for (int i = 0; i < 3 && n < 2; i++)
            if (vids[i] == edge.V1 || vids[i] == edge.V2) r[n++] = i;
        if (n < 2)
            throw new InvalidOperationException(
                $"Mesh topology error: edge ({edge.V1},{edge.V2}) shares fewer than 2 vertices with face [{string.Join(",", vids)}].");
        return r;
    }

    private static Dictionary<int, List<(int, int)>> BuildAdjacency(
        Mesh mesh, HashSet<int> foldEdgeIds)
    {
        var adj = new Dictionary<int, List<(int, int)>>(mesh.Faces.Count);
        foreach (var f in mesh.Faces) adj[f.Id] = [];
        foreach (var eid in foldEdgeIds)
        {
            var e = mesh.Edges[eid];
            if (!e.ConnectsFaces) continue;
            adj[e.FaceA].Add((e.FaceB, eid));
            adj[e.FaceB].Add((e.FaceA, eid));
        }
        return adj;
    }
}
