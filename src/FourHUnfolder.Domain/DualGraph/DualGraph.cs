namespace FourHUnfolder.Domain.DualGraph;

/// <summary>
/// Face-adjacency graph: one node per mesh face, one edge per shared interior mesh edge.
/// </summary>
public sealed class DualGraph
{
    public List<GraphNode> Nodes { get; } = new();
    public List<GraphEdge> Edges { get; } = new();
}
