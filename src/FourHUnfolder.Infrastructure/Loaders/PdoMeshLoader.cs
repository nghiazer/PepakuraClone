using System.IO.Compression;
using System.Numerics;
using System.Text;
using FourHUnfolder.Application.Interfaces;
using FourHUnfolder.Domain.Models;

namespace FourHUnfolder.Infrastructure.Loaders;

/// <summary>
/// Loads Pepakura Designer (.pdo) v3 / PD6 files.
///
/// Format notes (empirically verified against 3 sample files):
/// ┌──────────────────────────────────────────────────────────────────────────┐
/// │  abs 0-9    : "version 3\n" (ASCII signature)                           │
/// │  abs 10-13  : uint32 locked  (6 for PD6)                                │
/// │  abs 14-17  : uint32 unk1                                                │
/// │  abs 18-21  : uint32 version                                             │
/// │  abs 22-25  : uint32 localeLen  (bytes)                                  │
/// │  abs 26-..  : localeLen bytes UTF-16LE locale  (RAW, no cipher)          │
/// │  abs 66-69  : uint32 cipherKey  (subtraction: decoded=(raw-key+256)%256) │
/// │  abs 70-73  : uint32 commentLen (bytes, always 306 for PD6)              │
/// │  abs 74-..  : commentLen bytes cipher-encoded comment  (skipped)         │
/// │  abs 380-499: 120 bytes pre-geometry settings           (skipped)        │
/// │  abs 500-.. : uint32 geoCount + geometry data                            │
/// └──────────────────────────────────────────────────────────────────────────┘
///
/// Geometry per geo: wstr name + bool unk8 + vertices (raw doubles) + shapes +
///                   edge data (skipped).
/// wstr: uint32 byteLen (raw) + byteLen bytes cipher-encoded UTF-16LE.
/// Per shape: int32 unk11 + uint32 part + 4×double + uint32 ptCount +
///            ptCount × 85-byte point records.
/// Per point (85 bytes): uint32 vtxIdx + 2×double coord (2D paper pos, skipped) +
///                       2×double UV (texture coords) + bool unk14 +
///                       3×double unk15 + 3×uint32 + 3×float.
/// Per edge entry (22 bytes): 4×uint32 + 2×bool + 1×uint32.
///
/// Texture section (after all geometry):
///   uint32 texCount → per texture: wstr name + 5×(4floats) + bool hasImage
///   if hasImage: uint32 w, uint32 h, uint32 csize + zlib-compressed RGB24 bytes
///
/// Polygons are fan-triangulated into the output Mesh.
/// UV coordinates (texture coords, not paper-layout coords) are extracted and
/// stored in mesh.UVs / mesh.FaceUVs.
/// Embedded textures are decompressed and stored in mesh.EmbeddedTextures.
/// </summary>
public sealed class PdoMeshLoader : IMeshLoader
{
    // Byte sizes of fixed-layout records
    private const int PerPointBytes = 4 + 16 + 16 + 1 + 24 + 24; // = 85
    private const int PerEdgeBytes  = 16 + 2 + 4;                 // = 22

    public Mesh Load(string filePath)
    {
        using var fs     = File.OpenRead(filePath);
        using var reader = new BinaryReader(fs, Encoding.ASCII, leaveOpen: false);

        // ── 1. Signature ───────────────────────────────────────────────────
        var sig = Encoding.ASCII.GetString(reader.ReadBytes(10));
        if (!sig.StartsWith("version 3\n", StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Not a PDO v3 file (bad signature): '{sig.TrimEnd()}'");

        // ── 2. Fixed header ────────────────────────────────────────────────
        /*uint locked  =*/ reader.ReadUInt32();   // abs 10
        /*uint unk1   =*/ reader.ReadUInt32();    // abs 14
        /*uint version=*/ reader.ReadUInt32();    // abs 18

        uint localeLen = reader.ReadUInt32();     // abs 22 — byte count
        reader.BaseStream.Seek(localeLen, SeekOrigin.Current); // skip locale

        // abs 26 + localeLen = abs 66  (for standard PD6 localeLen = 40)
        uint key        = reader.ReadUInt32();    // abs 66 — subtraction cipher key
        uint commentLen = reader.ReadUInt32();    // abs 70 — byte count
        reader.BaseStream.Seek(commentLen, SeekOrigin.Current); // skip comment

        // ── 3. Skip pre-geometry settings block ───────────────────────────
        // TD-PDO-3: use absolute offset derived from header field trace instead of
        // a relative "skip 120 bytes" that depends on the stream position being exact.
        //
        // Header byte-trace (all offsets absolute from file start):
        //   abs  0 : signature "version 3\n"           — 10 bytes
        //   abs 10 : locked(4) + unk1(4) + version(4)  — 12 bytes
        //   abs 22 : localeLen(4) + locale(localeLen)
        //   abs 26+localeLen : key(4) + commentLen(4) + comment(commentLen)
        //   abs 34+localeLen+commentLen : 120-byte pre-geo settings block
        //   abs 154+localeLen+commentLen : geoCount ← Seek here
        //
        // (Previous hardcoded Seek(120, Current) assumed localeLen=40 which is standard
        // for PD6 but not guaranteed; this formula is valid for any localeLen.)
        //
        // The 120-byte block's internal structure is not yet reverse-engineered;
        // it contains display/page settings not needed for geometry parsing.
        long geoStart = 154L + localeLen + commentLen;
        reader.BaseStream.Seek(geoStart, SeekOrigin.Begin);

        // ── 4. Geometry ───────────────────────────────────────────────────
        var  mesh      = new Mesh();
        var  pdoLayout = new PdoLayout();
        uint geoCount  = reader.ReadUInt32();

        for (uint gi = 0; gi < geoCount; gi++)
        {
            int vtxBase = mesh.Vertices.Count;   // vertex-index offset for this geo

            ReadWStr(reader, key);               // geo name — discard
            reader.ReadByte();                   // unk8 bool — discard

            // ── Vertices (raw doubles, NO cipher) ─────────────────────────
            uint vtxCount = reader.ReadUInt32();
            for (uint vi = 0; vi < vtxCount; vi++)
            {
                float x = (float)reader.ReadDouble();
                float y = (float)reader.ReadDouble();
                float z = (float)reader.ReadDouble();
                mesh.AddVertex(new Vertex(mesh.Vertices.Count, new Vector3(x, y, z)));
            }

            // ── Shapes → fan-triangulated faces ───────────────────────────
            uint shapeCount = reader.ReadUInt32();
            for (uint si = 0; si < shapeCount; si++)
            {
                int  materialId = reader.ReadInt32();  // unk11 = material/texture index
                int  partIndex  = (int)reader.ReadUInt32(); // 2-D piece group (Phase C)
                reader.BaseStream.Seek(32, SeekOrigin.Current); // 4×double unk12

                uint ptCount   = reader.ReadUInt32();
                var  indices   = new int[ptCount];
                var  uvIndices = new int[ptCount];
                var  coords2D  = new Vector2[ptCount]; // Phase C: paper-space coords (mm)

                for (uint pi = 0; pi < ptCount; pi++)
                {
                    // vertex index (0-based within this geo → add vtxBase for global)
                    indices[pi] = (int)reader.ReadUInt32() + vtxBase; // 4 bytes

                    // coord: 2D paper layout (mm) — extract for Phase C/D
                    float cx = (float)reader.ReadDouble();             // 8 bytes
                    float cy = (float)reader.ReadDouble();             // 8 bytes
                    coords2D[pi] = new Vector2(cx, cy);

                    // unk13: texture UV
                    // PDO stores UV with Y=0 at top; WPF/OpenGL expect Y=0 at bottom → flip V
                    float u = (float)reader.ReadDouble();              // 8 bytes
                    float v = 1.0f - (float)reader.ReadDouble();       // 8 bytes — Y-flip
                    mesh.UVs.Add(new Vector2(u, v));
                    uvIndices[pi] = mesh.UVs.Count - 1;

                    // skip: unk14(1) + unk15(24) + unk16(24) = 49 bytes
                    reader.BaseStream.Seek(49, SeekOrigin.Current);
                    // per-point total: 4+16+16+49 = 85 ✓
                }

                // Fan-triangulate polygon: (v0,v1,v2), (v0,v2,v3), …
                if (ptCount >= 3)
                {
                    for (int ti = 1; ti < (int)ptCount - 1; ti++)
                    {
                        int faceIdx = mesh.Faces.Count;
                        mesh.AddFace(indices[0], indices[ti], indices[ti + 1],
                                     uvIndices[0], uvIndices[ti], uvIndices[ti + 1],
                                     materialId);
                        // Record 2D layout for this triangle
                        pdoLayout.Faces.Add(new PdoFace(
                            faceIdx, partIndex,
                            coords2D[0], coords2D[ti], coords2D[ti + 1]));
                    }
                }
            }

            // ── Skip unk17 edge data (22 bytes per entry) ─────────────────
            // Phase D will parse these for fold/cut topology
            uint edgeCount = reader.ReadUInt32();
            reader.BaseStream.Seek((long)edgeCount * PerEdgeBytes, SeekOrigin.Current);
        }

        // Attach layout if any 2-D data was gathered
        if (pdoLayout.Faces.Count > 0)
            mesh.PdoLayout = pdoLayout;

        // ── 5. Texture section ────────────────────────────────────────────
        // texCount → per texture: wstr name + 80 bytes (5×4floats) + bool hasImage
        // If hasImage: uint32 w + uint32 h + uint32 csize + csize bytes zlib(RGB24)
        try
        {
            uint texCount = reader.ReadUInt32();
            for (uint ti = 0; ti < texCount; ti++)
            {
                var texName = ReadWStr(reader, key);
                reader.BaseStream.Seek(80, SeekOrigin.Current); // skip 5×(4 floats)

                bool hasImage = reader.ReadByte() != 0;
                if (!hasImage) continue;

                uint w     = reader.ReadUInt32();
                uint h     = reader.ReadUInt32();
                uint csize = reader.ReadUInt32();

                var compressed = reader.ReadBytes((int)csize);
                var rgb = DecompressZlib(compressed);

                if (rgb.Length == (int)(w * h * 3))
                    mesh.EmbeddedTextures.Add(new EmbeddedTextureData(texName, (int)w, (int)h, rgb));
            }
        }
        catch
        {
            // Texture section is optional; silently ignore parse errors.
        }

        // ── 6. Populate material names from embedded textures ─────────────
        // Faces carry materialId = unk11 (0-based texture index).
        // Setting MaterialNames lets RebuildMaterialSlots create per-texture slots
        // that map materialId → EmbeddedTextures[i] correctly.
        foreach (var tex in mesh.EmbeddedTextures)
        {
            mesh.MaterialNames.Add(tex.Name);
            mesh.MaterialTexturePaths.Add(null); // embedded — no file path
        }

        return mesh;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a PDO wide-string: uint32 byte-count (raw) + byte-count bytes of
    /// cipher-encoded UTF-16LE.  Applies subtraction cipher: (raw - key + 256) % 256.
    /// </summary>
    private static string ReadWStr(BinaryReader reader, uint key)
    {
        uint byteLen = reader.ReadUInt32();    // raw (no cipher)
        if (byteLen == 0) return string.Empty;

        var raw = reader.ReadBytes((int)byteLen);
        byte k  = (byte)(key & 0xFF);

        for (int i = 0; i < raw.Length; i++)
            raw[i] = (byte)((raw[i] - k + 256) & 0xFF);

        return Encoding.Unicode.GetString(raw).TrimEnd('\0');
    }

    /// <summary>Decompresses a zlib-framed (RFC 1950) byte array.</summary>
    private static byte[] DecompressZlib(byte[] data)
    {
        // ZLibStream is the RFC 1950 (zlib) wrapper; available in .NET 6+.
        using var ms  = new MemoryStream(data);
        using var zs  = new ZLibStream(ms, CompressionMode.Decompress);
        using var out_= new MemoryStream();
        zs.CopyTo(out_);
        return out_.ToArray();
    }
}
