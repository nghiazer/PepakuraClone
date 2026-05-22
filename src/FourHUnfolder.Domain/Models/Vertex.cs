using System.Numerics;

namespace FourHUnfolder.Domain.Models;

public sealed class Vertex
{
    public int Id { get; }
    public Vector3 Position { get; }

    public Vertex(int id, Vector3 position)
    {
        Id = id;
        Position = position;
    }
}
