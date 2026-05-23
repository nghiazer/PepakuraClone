using Assimp;
using FourHUnfolder.Application.Interfaces;
using DomainMesh = FourHUnfolder.Domain.Models.Mesh;
using DomainVertex = FourHUnfolder.Domain.Models.Vertex;

namespace FourHUnfolder.Infrastructure.Loaders;

/// <summary>
/// Loads 3D files via Assimp (3DS, STL, DXF, LWO/LWS, FBX, COLLADA, etc.)
/// </summary>
public class AssimpMeshLoader : IMeshLoader
{
    public static readonly string[] SupportedExtensions =
        [".3ds", ".stl", ".dxf", ".lwo", ".lws", ".fbx", ".dae", ".ply", ".x"];

    public DomainMesh Load(string filePath)
    {
        using var ctx = new AssimpContext();

        var postProcess =
            PostProcessSteps.Triangulate          |
            PostProcessSteps.JoinIdenticalVertices |
            PostProcessSteps.GenerateNormals       |
            PostProcessSteps.FlipUVs;

        Scene scene;
        try
        {
            scene = ctx.ImportFile(filePath, postProcess);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Assimp could not load '{Path.GetFileName(filePath)}': {ex.Message}", ex);
        }

        if (!scene.HasMeshes)
            throw new InvalidOperationException($"No meshes found in '{Path.GetFileName(filePath)}'.");

        var mesh = new DomainMesh();

        int vertexOffset = 0;
        foreach (var aMesh in scene.Meshes)
        {
            bool hasUV = aMesh.HasTextureCoords(0);

            foreach (var v in aMesh.Vertices)
                mesh.AddVertex(new DomainVertex(mesh.Vertices.Count, new System.Numerics.Vector3(v.X, v.Y, v.Z)));

            int uvOffset = mesh.UVs.Count;
            if (hasUV)
                foreach (var uv in aMesh.TextureCoordinateChannels[0])
                    mesh.UVs.Add(new System.Numerics.Vector2(uv.X, uv.Y));

            foreach (var face in aMesh.Faces)
            {
                if (face.IndexCount != 3) continue;
                int a = face.Indices[0] + vertexOffset;
                int b = face.Indices[1] + vertexOffset;
                int c = face.Indices[2] + vertexOffset;

                if (hasUV)
                    mesh.AddFace(a, b, c, face.Indices[0] + uvOffset, face.Indices[1] + uvOffset, face.Indices[2] + uvOffset);
                else
                    mesh.AddFace(a, b, c);
            }

            vertexOffset += aMesh.VertexCount;
        }

        if (mesh.Faces.Count == 0)
            throw new InvalidOperationException($"No valid triangles found in '{Path.GetFileName(filePath)}'.");

        return mesh;
    }
}
