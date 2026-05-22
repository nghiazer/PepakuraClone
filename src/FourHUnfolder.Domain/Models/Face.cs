namespace FourHUnfolder.Domain.Models;

public sealed class Face
{
    public int Id { get; }

    // Global vertex indices into Mesh.Vertices
    public int A { get; }
    public int B { get; }
    public int C { get; }

    // Global edge indices into Mesh.Edges; order: AB, BC, CA
    public List<int> EdgeIds { get; } = new();

    public Face(int id, int a, int b, int c)
    {
        Id = id;
        A = a;
        B = b;
        C = c;
    }

    public int[] VertexIds => [A, B, C];
}
