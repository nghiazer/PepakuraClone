using FourHUnfolder.Domain.Models;

namespace FourHUnfolder.Geometry.Algorithms;

/// <summary>
/// Computes connected components of the fold graph.
/// Each component = one independent paper piece.
/// The component key is the smallest face ID in the group.
/// </summary>
public class PieceComputer
{
    public List<List<int>> ComputePieces(Mesh mesh)
    {
        var parent = Enumerable.Range(0, mesh.Faces.Count).ToArray();
        var rank   = new int[mesh.Faces.Count];

        foreach (var edge in mesh.Edges)
        {
            if (edge.Type != EdgeType.Fold || !edge.ConnectsFaces) continue;
            Union(parent, rank, edge.FaceA, edge.FaceB);
        }

        return mesh.Faces
            .GroupBy(f => Find(parent, f.Id))
            .OrderBy(g => g.Key)
            .Select(g => g.Select(f => f.Id).ToList())
            .ToList();
    }

    private static int Find(int[] p, int x)
    {
        if (p[x] != x) p[x] = Find(p, p[x]);
        return p[x];
    }

    private static void Union(int[] p, int[] rank, int a, int b)
    {
        a = Find(p, a); b = Find(p, b);
        if (a == b) return;
        if (rank[a] < rank[b]) (a, b) = (b, a);
        p[b] = a;
        if (rank[a] == rank[b]) rank[a]++;
    }
}
