using System.Numerics;

namespace FourHUnfolder.Domain.Models;

/// <summary>
/// Indexed triangle mesh.  Vertices, Edges, and Faces are stored in lists
/// whose index equals the element's Id, giving O(1) lookup everywhere.
/// </summary>
public sealed class Mesh
{
    public List<Vertex> Vertices { get; } = new();
    public List<Edge>   Edges    { get; } = new();
    public List<Face>   Faces    { get; } = new();

    // UV texture coordinates parsed from file (may be empty)
    public List<Vector2> UVs { get; } = new();

    // Per-face UV indices, always same count as Faces.
    // Each tuple maps to (face.A, face.B, face.C); -1 means no UV for that vertex.
    private readonly List<(int UA, int UB, int UC)> _faceUVs = new();
    public IReadOnlyList<(int UA, int UB, int UC)> FaceUVs => _faceUVs;

    /// Texture path suggested by the associated material file (e.g. from MTL).
    public string? SuggestedTexturePath { get; set; }

    /// <summary>
    /// Raw RGB24 texture data embedded directly in the file (e.g. PDO format).
    /// Each element is one texture layer (multi-texture PDOs have multiple entries).
    /// Width × Height × 3 bytes, R-G-B order, top-to-bottom scan order.
    /// </summary>
    public List<EmbeddedTextureData> EmbeddedTextures { get; } = new();

    /// Material names in order; index matches Face.MaterialId.
    public List<string> MaterialNames        { get; } = new();
    /// Suggested texture path per material (parallel to MaterialNames). Null = no texture.
    public List<string?> MaterialTexturePaths { get; } = new();

    /// <summary>
    /// Pre-computed 2-D paper layout from a PDO file (null for all other formats).
    /// When present, the paper-space positions stored here can be used to reconstruct
    /// the unfolded state without running the UnfoldEngine.
    /// </summary>
    public PdoLayout? PdoLayout { get; set; }

    // Maps canonical (min,max) vertex pair → edge index
    private readonly Dictionary<(int, int), int> _edgeMap = new();

    public void AddVertex(Vertex v) => Vertices.Add(v);

    /// <summary>
    /// Applies a rigid-body (or any affine) transform to all vertex positions in-place.
    /// Call this immediately after loading before building the dual graph.
    /// </summary>
    public void ApplyTransform(System.Numerics.Matrix4x4 transform)
    {
        for (int i = 0; i < Vertices.Count; i++)
        {
            var newPos = System.Numerics.Vector3.Transform(Vertices[i].Position, transform);
            Vertices[i] = new Vertex(i, newPos);
        }
    }

    /// <summary>Flips the V coordinate of all UV entries: V = 1 - V.</summary>
    public void FlipUVsVertical()
    {
        for (int i = 0; i < UVs.Count; i++)
            UVs[i] = new System.Numerics.Vector2(UVs[i].X, 1.0f - UVs[i].Y);
    }

    /// <param name="ua">UV index for vertex A (-1 if no UV)</param>
    /// <param name="materialId">Material index for this face (-1 = no material).</param>
    public void AddFace(int a, int b, int c, int ua = -1, int ub = -1, int uc = -1, int materialId = -1)
    {
        var faceId = Faces.Count;
        var face   = new Face(faceId, a, b, c) { MaterialId = materialId };
        Faces.Add(face);
        _faceUVs.Add((ua, ub, uc));

        face.EdgeIds.Add(GetOrAddEdge(a, b, faceId));
        face.EdgeIds.Add(GetOrAddEdge(b, c, faceId));
        face.EdgeIds.Add(GetOrAddEdge(c, a, faceId));
    }

    public bool HasUVs => UVs.Count > 0 && _faceUVs.Count == Faces.Count;

    private int GetOrAddEdge(int v1, int v2, int faceId)
    {
        var key = (Math.Min(v1, v2), Math.Max(v1, v2));

        if (_edgeMap.TryGetValue(key, out var edgeId))
        {
            // Only assign FaceB if not yet set; a 3rd+ face sharing this edge
            // indicates non-manifold topology — silently ignore the extra face
            // rather than overwriting and corrupting the dual graph.
            if (Edges[edgeId].FaceB < 0)
                Edges[edgeId].FaceB = faceId;
            return edgeId;
        }

        edgeId = Edges.Count;
        var edge = new Edge(edgeId, v1, v2) { FaceA = faceId };
        Edges.Add(edge);
        _edgeMap[key] = edgeId;
        return edgeId;
    }
}
