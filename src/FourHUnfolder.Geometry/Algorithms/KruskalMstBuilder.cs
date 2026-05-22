using FourHUnfolder.Domain.DualGraph;

namespace FourHUnfolder.Geometry.Algorithms;

/// <summary>
/// Kruskal's algorithm with path-compressed Union-Find.
/// Returns n-1 edges that form a minimum spanning tree of the dual graph.
/// MST edges → Fold; non-MST interior edges → Cut.
/// </summary>
public class KruskalMstBuilder
{
    public IReadOnlyList<GraphEdge> Build(DualGraph graph)
    {
        if (graph.Nodes.Count == 0) return Array.Empty<GraphEdge>();

        var sortedEdges = graph.Edges.OrderBy(e => e.Weight).ToList();

        // Map face IDs to contiguous array indices for Union-Find
        var faceIds  = graph.Nodes.Select(n => n.FaceId).ToList();
        var faceIdx  = faceIds.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => t.i);
        var n        = faceIds.Count;
        var parent   = Enumerable.Range(0, n).ToArray();
        var rank     = new int[n];

        var mst = new List<GraphEdge>(n - 1);

        foreach (var edge in sortedEdges)
        {
            var ra = Find(parent, faceIdx[edge.FaceA]);
            var rb = Find(parent, faceIdx[edge.FaceB]);

            if (ra == rb) continue;   // same component → would form a cycle

            mst.Add(edge);
            Union(parent, rank, ra, rb);

            if (mst.Count == n - 1) break;  // MST complete
        }

        return mst;
    }

    // Path-compressed find
    private static int Find(int[] parent, int x)
    {
        if (parent[x] != x) parent[x] = Find(parent, parent[x]);
        return parent[x];
    }

    // Union by rank
    private static void Union(int[] parent, int[] rank, int a, int b)
    {
        if (rank[a] < rank[b]) (a, b) = (b, a);
        parent[b] = a;
        if (rank[a] == rank[b]) rank[a]++;
    }
}
