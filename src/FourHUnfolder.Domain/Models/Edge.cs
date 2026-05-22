namespace FourHUnfolder.Domain.Models;

public sealed class Edge
{
    public int Id { get; }

    // Always stored as min/max so (V1,V2) is canonical
    public int V1 { get; }
    public int V2 { get; }

    public int FaceA { get; set; } = -1;
    public int FaceB { get; set; } = -1;

    public EdgeType Type { get; set; } = EdgeType.Unknown;

    public bool IsBoundary => FaceB == -1;
    public bool ConnectsFaces => FaceA >= 0 && FaceB >= 0;

    public Edge(int id, int v1, int v2)
    {
        Id = id;
        V1 = Math.Min(v1, v2);
        V2 = Math.Max(v1, v2);
    }
}
