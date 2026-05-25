using System.Numerics;
using FourHUnfolder.Domain.Models;

namespace FourHUnfolder.Geometry.Algorithms;

/// <summary>
/// Spanning tree of fold edges for a single unfolded piece.
///
/// Used by <c>AssemblyViewModel</c> to drive Phase-1 of the assembly animation:
/// each face rotates around its shared edge with its parent (BFS order)
/// from flat (all coplanar, t=0) to the correct 3-D dihedral angle (t=1),
/// while the root face stays fixed in the flat layout plane.
///
/// Fold angle convention
/// ─────────────────────
/// <see cref="FoldNode.TargetTheta"/> is the signed angle (radians) needed to
/// rotate the child face's normal from "same direction as parent" (flat = 0)
/// to the dihedral it has in the 3-D mesh.  Positive/negative encodes which
/// side of the fold axis the child faces.
/// </summary>
public sealed class PieceFoldTree
{
    // ── inner node ────────────────────────────────────────────────────────────

    public sealed class FoldNode
    {
        /// Face ID in <see cref="Mesh.Faces"/>.
        public int FaceId           { get; init; }

        /// Parent face in the spanning tree.  -1 for the root.
        public int ParentFaceId     { get; init; }

        /// Canonical mesh vertex IDs (V1 ≤ V2) of the shared fold edge.
        /// Both are -1 for orphaned faces (degenerate, shouldn't normally occur).
        public int SharedEdgeVA     { get; init; }
        public int SharedEdgeVB     { get; init; }

        /// Signed fold angle in radians to reach the 3-D dihedral from flat.
        public float TargetTheta    { get; init; }

        /// Normalised 3-D direction of the shared edge (VA → VB) in the original
        /// mesh space, as used when computing <see cref="TargetTheta"/>.
        /// AssemblyViewModel uses this to reconcile sign when the flat-layout
        /// edge direction is antiparallel to the 3-D mesh edge direction.
        public Vector3 EdgeDir3D    { get; init; }

        /// Children in BFS order.
        public List<FoldNode> Children { get; } = [];
    }

    // ── public api ────────────────────────────────────────────────────────────

    public FoldNode                Root     { get; }

    /// All nodes in BFS order (root at index 0).  Iterating this list and
    /// applying parent→child rotations in order gives correct accumulated
    /// transforms for the whole piece.
    public IReadOnlyList<FoldNode> BFSOrder { get; }

    // ── ctor (private) ────────────────────────────────────────────────────────

    PieceFoldTree(FoldNode root, List<FoldNode> bfsOrder)
    {
        Root     = root;
        BFSOrder = bfsOrder;
    }

    // ── factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a fold spanning tree for the given face IDs (all from the same piece).
    /// </summary>
    public static PieceFoldTree Build(Mesh mesh, int[] faceIds)
    {
        if (faceIds.Length == 0)
        {
            var dummy = new FoldNode { FaceId = -1, ParentFaceId = -1 };
            return new PieceFoldTree(dummy, [dummy]);
        }

        var faceSet = new HashSet<int>(faceIds);

        // Build adjacency: faceId → [(neighborFace, sharedVA, sharedVB)]
        var adj = new Dictionary<int, List<(int Face, int VA, int VB)>>(faceIds.Length);
        foreach (var id in faceIds) adj[id] = [];

        foreach (var faceId in faceIds)
        {
            foreach (var edgeId in mesh.Faces[faceId].EdgeIds)
            {
                var e = mesh.Edges[edgeId];
                if (!e.ConnectsFaces) continue;

                int nb = e.FaceA == faceId ? e.FaceB : e.FaceA;
                if (nb >= 0 && faceSet.Contains(nb))
                    adj[faceId].Add((nb, e.V1, e.V2));
            }
        }

        // BFS spanning tree (deterministic: neighbours sorted by face ID)
        var root     = new FoldNode { FaceId = faceIds[0], ParentFaceId = -1 };
        var bfsOrder = new List<FoldNode>(faceIds.Length) { root };
        var visited  = new HashSet<int> { faceIds[0] };
        var queue    = new Queue<FoldNode>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            // Sort for deterministic output
            var neighbours = adj[parent.FaceId];
            neighbours.Sort((a, b) => a.Face.CompareTo(b.Face));

            foreach (var (nb, va, vb) in neighbours)
            {
                if (!visited.Add(nb)) continue;

                var child = new FoldNode
                {
                    FaceId       = nb,
                    ParentFaceId = parent.FaceId,
                    SharedEdgeVA = va,
                    SharedEdgeVB = vb,
                    TargetTheta  = ComputeFoldAngle(mesh, parent.FaceId, nb, va, vb),
                    EdgeDir3D    = Vector3.Normalize(
                                       mesh.Vertices[vb].Position - mesh.Vertices[va].Position)
                };
                parent.Children.Add(child);
                bfsOrder.Add(child);
                queue.Enqueue(child);
            }
        }

        // Attach any disconnected faces as orphaned nodes (shouldn't occur in valid meshes)
        foreach (var id in faceIds)
        {
            if (!visited.Contains(id))
                bfsOrder.Add(new FoldNode
                {
                    FaceId       = id,
                    ParentFaceId = root.FaceId,
                    SharedEdgeVA = -1,
                    SharedEdgeVB = -1
                });
        }

        return new PieceFoldTree(root, bfsOrder);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Signed angle (radians) to rotate the child face from "coplanar with parent"
    /// (flat state, θ=0) to the 3-D dihedral angle it has in the mesh.
    ///
    /// Formula: θ = atan2( dot(cross(nP,nC), e), dot(nP,nC) )
    /// where e is the normalised edge direction from VA to VB.
    /// </summary>
    static float ComputeFoldAngle(Mesh mesh, int parentId, int childId, int va, int vb)
    {
        var nP = FaceNormal(mesh, parentId);
        var nC = FaceNormal(mesh, childId);
        var e  = Vector3.Normalize(mesh.Vertices[vb].Position - mesh.Vertices[va].Position);

        float sinTheta = Vector3.Dot(Vector3.Cross(nP, nC), e);
        float cosTheta = Vector3.Dot(nP, nC);
        return MathF.Atan2(sinTheta, cosTheta);
    }

    static Vector3 FaceNormal(Mesh mesh, int faceId)
    {
        var f = mesh.Faces[faceId];
        var a = mesh.Vertices[f.A].Position;
        var b = mesh.Vertices[f.B].Position;
        var c = mesh.Vertices[f.C].Position;
        var n = Vector3.Cross(b - a, c - a);
        float len = n.Length();
        return len > 1e-10f ? n / len : Vector3.UnitY;
    }
}
