namespace FourHUnfolder.Domain.DualGraph;

public sealed class GraphNode
{
    public int FaceId { get; }

    // Indices into DualGraph.Edges
    public List<int> GraphEdgeIds { get; } = new();

    public GraphNode(int faceId) => FaceId = faceId;
}
