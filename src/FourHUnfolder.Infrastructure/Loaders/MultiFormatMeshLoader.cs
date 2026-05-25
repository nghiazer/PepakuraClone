using FourHUnfolder.Application.Interfaces;
using DomainMesh = FourHUnfolder.Domain.Models.Mesh;

namespace FourHUnfolder.Infrastructure.Loaders;

/// <summary>
/// Routes mesh loading to the appropriate loader by file extension.
/// OBJ → ObjMeshLoader; PDO → PdoMeshLoader; everything else → AssimpMeshLoader.
/// </summary>
public class MultiFormatMeshLoader : IMeshLoader
{
    private readonly ObjMeshLoader    _obj    = new();
    private readonly PdoMeshLoader    _pdo    = new();
    private readonly AssimpMeshLoader _assimp = new();

    public DomainMesh Load(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".obj" => _obj.Load(filePath),
            ".pdo" => _pdo.Load(filePath),
            _      => _assimp.Load(filePath),
        };
    }
}
