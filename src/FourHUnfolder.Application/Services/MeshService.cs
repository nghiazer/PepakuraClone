using FourHUnfolder.Application.Interfaces;
using FourHUnfolder.Domain.Models;

namespace FourHUnfolder.Application.Services;

public class MeshService
{
    private readonly IMeshLoader _loader;

    public MeshService(IMeshLoader loader) => _loader = loader;

    public Mesh LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Mesh file not found: {filePath}");

        return _loader.Load(filePath);
    }
}
