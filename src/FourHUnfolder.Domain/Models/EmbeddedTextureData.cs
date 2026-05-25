namespace FourHUnfolder.Domain.Models;

/// <summary>
/// Raw pixel data for a texture embedded directly in a mesh file (e.g. PDO format).
/// Pixel data is uncompressed RGB24 (R, G, B byte order), top-to-bottom scan order,
/// tightly packed (no row padding).  Width × Height × 3 == Rgb24Bytes.Length.
/// </summary>
public sealed record EmbeddedTextureData(
    string Name,
    int    Width,
    int    Height,
    byte[] Rgb24Bytes);
