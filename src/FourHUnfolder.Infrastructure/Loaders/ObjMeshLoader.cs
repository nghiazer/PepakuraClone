using System.Globalization;
using System.Numerics;
using FourHUnfolder.Application.Interfaces;
using FourHUnfolder.Domain.Models;

namespace FourHUnfolder.Infrastructure.Loaders;

/// <summary>
/// Parses Wavefront OBJ files including UV texture coordinates.
/// Supports "v/vt", "v/vt/vn", "v//vn" face tokens and fan-triangulates n-gons.
/// Also reads the companion MTL file (if present) to extract the diffuse texture path.
/// </summary>
public class ObjMeshLoader : IMeshLoader
{
    public Mesh Load(string filePath)
    {
        var mesh  = new Mesh();
        var lines = File.ReadAllLines(filePath);

        foreach (var rawLine in lines)
        {
            var line  = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case "v" when parts.Length >= 4:
                    ParseVertex(mesh, parts);
                    break;

                case "vt" when parts.Length >= 3:
                    ParseUV(mesh, parts);
                    break;

                case "f" when parts.Length >= 4:
                    ParseFace(mesh, parts);
                    break;

                case "mtllib" when parts.Length >= 2:
                    TryLoadMtl(mesh, filePath, parts[1]);
                    break;
            }
        }

        return mesh;
    }

    // ── vertex / UV parsing ──────────────────────────────────────────────

    private static void ParseVertex(Mesh mesh, string[] parts)
    {
        float x = F(parts[1]);
        float y = F(parts[2]);
        float z = F(parts[3]);
        mesh.AddVertex(new Vertex(mesh.Vertices.Count, new Vector3(x, y, z)));
    }

    private static void ParseUV(Mesh mesh, string[] parts)
    {
        float u = F(parts[1]);
        float v = F(parts[2]);
        mesh.UVs.Add(new Vector2(u, v));
    }

    // ── face parsing ─────────────────────────────────────────────────────

    private static void ParseFace(Mesh mesh, string[] parts)
    {
        var tokens = parts.Skip(1).ToArray();

        // Each token may be: "v", "v/vt", "v/vt/vn", "v//vn"
        var posIdx = tokens.Select(t => SlotIndex(t, 0)).ToArray();
        var uvIdx  = tokens.Select(t => SlotIndex(t, 1)).ToArray();
        int vCount = mesh.Vertices.Count;

        // Fan triangulation — skip faces with out-of-bounds vertex references
        for (int i = 1; i < posIdx.Length - 1; i++)
        {
            int a = posIdx[0], b = posIdx[i], c = posIdx[i + 1];
            if (a < 0 || a >= vCount || b < 0 || b >= vCount || c < 0 || c >= vCount)
                continue;
            mesh.AddFace(a, b, c, uvIdx[0], uvIdx[i], uvIdx[i + 1]);
        }
    }

    /// Parses one "v/vt/vn" token and returns the 0-based index for the given slot
    /// (0=position, 1=uv, 2=normal).  Returns -1 when absent or empty.
    private static int SlotIndex(string token, int slot)
    {
        var segs = token.Split('/');
        if (slot >= segs.Length || string.IsNullOrEmpty(segs[slot])) return -1;
        int idx = int.Parse(segs[slot], CultureInfo.InvariantCulture);
        // OBJ is 1-based; negative indices count from the end — treat them as absent
        return idx > 0 ? idx - 1 : -1;
    }

    // ── MTL loading ───────────────────────────────────────────────────────

    private static void TryLoadMtl(Mesh mesh, string objPath, string mtlName)
    {
        var dir     = Path.GetDirectoryName(objPath) ?? string.Empty;
        var mtlPath = Path.Combine(dir, mtlName);
        if (!File.Exists(mtlPath)) return;

        foreach (var rawLine in File.ReadAllLines(mtlPath))
        {
            var line  = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            // map_Kd is the diffuse texture map
            if (!parts[0].Equals("map_Kd", StringComparison.OrdinalIgnoreCase)) continue;

            var texFile = parts[1].Trim();
            var texPath = Path.IsPathRooted(texFile)
                ? texFile
                : Path.Combine(dir, texFile);

            if (File.Exists(texPath))
            {
                mesh.SuggestedTexturePath = texPath;
                return;
            }
        }
    }

    private static float F(string s) =>
        float.TryParse(s, System.Globalization.NumberStyles.Float,
                       CultureInfo.InvariantCulture, out var v) ? v : 0f;
}
