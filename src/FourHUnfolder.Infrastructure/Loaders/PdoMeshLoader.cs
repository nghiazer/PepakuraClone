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
/// │  abs 66-69  : uint32 cipherKey  (subtraction: decoded = (raw-key+256)%256)│
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
/// Per point (85 bytes): uint32 vtxIdx + 2×double UV + 2×double unk13 +
///                       bool unk14 + 3×double unk15 + 3×uint32 + 3×float.
/// Per edge entry (22 bytes): 4×uint32 + 2×bool + 1×uint32.
///
/// Polygons are fan-triangulated into the output Mesh.
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

        // ── 3. Skip pre-geometry settings (120 bytes, fixed for PD6) ──────
        // Verified across 3 sample PDO files: geo_count always sits at
        // abs 74 + commentLen + 120  (= abs 500 when commentLen = 306).
        reader.BaseStream.Seek(120, SeekOrigin.Current);

        // ── 4. Geometry ───────────────────────────────────────────────────
        var  mesh     = new Mesh();
        uint geoCount = reader.ReadUInt32();

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
                reader.ReadInt32();              // unk11
                reader.ReadUInt32();             // part (2-D part index)
                reader.BaseStream.Seek(32, SeekOrigin.Current); // 4×double unk12

                uint ptCount = reader.ReadUInt32();
                var  indices = new int[ptCount];

                for (uint pi = 0; pi < ptCount; pi++)
                {
                    // vertex index (0-based within this geo → add vtxBase for global)
                    indices[pi] = (int)reader.ReadUInt32() + vtxBase;

                    // Skip remaining 81 bytes of per-point extras
                    // (2×double UV  + 2×double unk13 + bool unk14 +
                    //  3×double unk15 + 3×uint32 unk16a + 3×float unk16b)
                    reader.BaseStream.Seek(PerPointBytes - 4, SeekOrigin.Current);
                }

                // Fan-triangulate polygon: (v0,v1,v2), (v0,v2,v3), …
                if (ptCount >= 3)
                {
                    for (int ti = 1; ti < (int)ptCount - 1; ti++)
                        mesh.AddFace(indices[0], indices[ti], indices[ti + 1]);
                }
            }

            // ── Skip unk17 edge data (22 bytes per entry) ─────────────────
            uint edgeCount = reader.ReadUInt32();
            reader.BaseStream.Seek((long)edgeCount * PerEdgeBytes, SeekOrigin.Current);
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
}
