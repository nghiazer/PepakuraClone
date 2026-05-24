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

        // TD-22-5: removed PostProcessSteps.FlipUVs — ToWpfUV in MainViewModel already
        //          applies (1.0 - uv.Y); having both flips cancelled out (net = no flip),
        //          which produced wrong UV mapping for WPF's top-left origin convention.
        var postProcess =
            PostProcessSteps.Triangulate          |
            PostProcessSteps.JoinIdenticalVertices |
            PostProcessSteps.GenerateNormals;

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

        var mesh       = new DomainMesh();
        var modelDir   = Path.GetDirectoryName(filePath) ?? string.Empty;

        // TD-22-1: extract material names + texture paths from Assimp scene
        if (scene.HasMaterials)
        {
            foreach (var mat in scene.Materials)
            {
                var matName = !string.IsNullOrEmpty(mat.Name) ? mat.Name : $"Material_{mesh.MaterialNames.Count}";
                mesh.MaterialNames.Add(matName);

                string? texPath = null;
                if (mat.HasTextureDiffuse && !string.IsNullOrEmpty(mat.TextureDiffuse.FilePath))
                {
                    var rawPath = mat.TextureDiffuse.FilePath;
                    // Resolve relative to the model file; keep absolute paths as-is
                    var candidate = Path.IsPathRooted(rawPath)
                        ? rawPath
                        : Path.GetFullPath(Path.Combine(modelDir, rawPath));
                    texPath = File.Exists(candidate) ? candidate : null;
                }
                mesh.MaterialTexturePaths.Add(texPath);
            }

            // Populate SuggestedTexturePath with the first available texture (for single-slot fallback)
            mesh.SuggestedTexturePath ??= mesh.MaterialTexturePaths.FirstOrDefault(p => p != null);
        }

        int vertexOffset = 0;
        foreach (var aMesh in scene.Meshes)
        {
            bool hasUV     = aMesh.HasTextureCoords(0);
            int  matId     = aMesh.MaterialIndex;   // TD-22-1: sub-mesh → domain material index

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
                    mesh.AddFace(a, b, c,
                        face.Indices[0] + uvOffset,
                        face.Indices[1] + uvOffset,
                        face.Indices[2] + uvOffset,
                        matId);
                else
                    mesh.AddFace(a, b, c, materialId: matId);
            }

            vertexOffset += aMesh.VertexCount;
        }

        if (mesh.Faces.Count == 0)
            throw new InvalidOperationException($"No valid triangles found in '{Path.GetFileName(filePath)}'.");

        return mesh;
    }
}
