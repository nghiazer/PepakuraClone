namespace FourHUnfolder.Domain.DualGraph;

public sealed class GraphEdge
{
    public int   Id               { get; }
    public int   FaceA            { get; }
    public int   FaceB            { get; }
    public int   SharedMeshEdgeId { get; }  // index into Mesh.Edges
    public float Weight           { get; }

    public GraphEdge(int id, int faceA, int faceB, int sharedMeshEdgeId, float weight)
    {
        Id               = id;
        FaceA            = faceA;
        FaceB            = faceB;
        SharedMeshEdgeId = sharedMeshEdgeId;
        Weight           = weight;
    }
}
