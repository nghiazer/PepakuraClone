using PepakuraClone.Domain.Results;

namespace PepakuraClone.Application.Interfaces;

public interface IExporter
{
    /// <param name="texturePath">
    /// Optional path to the diffuse texture image.
    /// When provided and the result contains UV coordinates, the texture
    /// will be embedded in the exported file.
    /// </param>
    void Export(UnfoldResult result, string filePath, string? texturePath = null);
}
