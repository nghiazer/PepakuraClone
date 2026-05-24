using System.Globalization;
using System.Numerics;
using System.Text;
using FourHUnfolder.Application.Interfaces;
using FourHUnfolder.Application.Services;
using FourHUnfolder.Domain.Results;
using FourHUnfolder.Domain.Settings;

namespace FourHUnfolder.Infrastructure.Exporters;

/// <summary>
/// Exports an <see cref="UnfoldResult"/> to a standalone SVG file.
///
/// Edge types:
///   .fold     — dashed, settings colour (fold edges)
///   .cut      — solid,  settings colour (cut edges between pieces)
///   .boundary — thin dark grey (outer mesh boundary, no glue)
///
/// When texture data is supplied and faces carry UV coords, each face's texture
/// is embedded as a base-64 data URI (one per distinct material) and affine-mapped.
/// Per-material textures (<paramref name="perMaterialTextures"/>) take precedence
/// over the single fallback <paramref name="texturePath"/>.
/// </summary>
public class SvgExporter : IExporter
{
    private readonly SettingsService _settings;

    public SvgExporter(SettingsService settings) => _settings = settings;

    public void Export(UnfoldResult result, string filePath,
                       string? texturePath = null,
                       IReadOnlyDictionary<int, string?>? perMaterialTextures = null)
    {
        var p = _settings.Current.Print;

        float scale  = (float)p.SvgScaleFactor;
        float margin = (float)p.MarginMm * scale;

        var allVerts = result.Faces.SelectMany(f => f.Vertices).ToList();
        if (allVerts.Count == 0) return;

        float minX = allVerts.Min(v => v.X) - (float)p.MarginMm;
        float minY = allVerts.Min(v => v.Y) - (float)p.MarginMm;
        float maxX = allVerts.Max(v => v.X) + (float)p.MarginMm;
        float maxY = allVerts.Max(v => v.Y) + (float)p.MarginMm;

        float W = (maxX - minX) * scale + 2 * margin;
        float H = (maxY - minY) * scale + 2 * margin;

        string Sx(float x) => F((x - minX) * scale + margin);
        string Sy(float y) => F((y - minY) * scale + margin);
        string Pt(Vector2 v) => $"{Sx(v.X)},{Sy(v.Y)}";

        // ── colours ────────────────────────────────────────────────────────────
        string foldColor = p.GrayscaleOutput ? "#555555" : Clamp(p.FoldLineColor,  "#4169e1");
        string cutColor  = p.GrayscaleOutput ? "#000000" : Clamp(p.CutLineColor,   "#ff0000");
        string tabFill   = p.GrayscaleOutput ? "#cccccc" : "rgba(80,200,80,0.4)";
        string faceFill  = p.GrayscaleOutput ? "#eeeeee" : "#fffde7";

        string foldDash = p.FoldLineDash.Equals("Solid", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $" stroke-dasharray=\"{p.FoldLineDash}\"";

        // ── TD-22-3: build per-material data URI dictionary ────────────────────
        // Key: MaterialId (-1 = default/fallback). Value: data URI string.
        var matDataUris = new Dictionary<int, string>();

        // Fallback / single-texture mode
        if (!string.IsNullOrEmpty(texturePath) && File.Exists(texturePath))
        {
            var uri = TryBuildDataUri(texturePath!);
            if (uri != null) matDataUris[-1] = uri;
        }

        // Per-material textures (override or supplement)
        if (perMaterialTextures != null)
        {
            foreach (var (matId, path) in perMaterialTextures)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path!))
                {
                    var uri = TryBuildDataUri(path!);
                    if (uri != null) matDataUris[matId] = uri;
                }
            }
        }

        bool hasTexture = matDataUris.Count > 0 && result.Faces.Any(f => f.UVCoords != null);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" " +
                      $"width=\"{F(W)}\" height=\"{F(H)}\" viewBox=\"0 0 {F(W)} {F(H)}\">");
        sb.AppendLine("  <defs>");
        sb.AppendLine("    <style>");
        sb.AppendLine($"      .face     {{ fill:{faceFill}; stroke:none; }}");
        sb.AppendLine($"      .fold     {{ stroke:{foldColor}; stroke-width:{F((float)p.FoldLineWidth)}{foldDash}; fill:none; }}");
        sb.AppendLine($"      .cut      {{ stroke:{cutColor};  stroke-width:{F((float)p.CutLineWidth)};  fill:none; }}");
        sb.AppendLine( "      .boundary { stroke:#505050;      stroke-width:0.6; fill:none; }");
        sb.AppendLine($"      .tab      {{ fill:{tabFill}; stroke:#2e7d32; stroke-width:0.6; }}");
        sb.AppendLine( "      .label    { font-family:sans-serif; font-size:8px; fill:#888; }");
        sb.AppendLine("    </style>");

        // Clip paths for textured faces
        if (hasTexture)
        {
            foreach (var face in result.Faces.Where(f => f.UVCoords != null && ResolveUri(f, matDataUris) != null))
            {
                sb.AppendLine($"    <clipPath id=\"cp{face.FaceId}\">" +
                              $"<polygon points=\"{Pt(face.V0)} {Pt(face.V1)} {Pt(face.V2)}\"/></clipPath>");
            }
        }
        sb.AppendLine("  </defs>");

        if (p.IncludePageLabel)
            sb.AppendLine($"  <text x=\"{F(margin)}\" y=\"{F(margin - 4)}\" class=\"label\">{Path.GetFileNameWithoutExtension(filePath)}</text>");

        // ── face polygons ──────────────────────────────────────────────────────
        foreach (var face in result.Faces)
        {
            string pts = $"{Pt(face.V0)} {Pt(face.V1)} {Pt(face.V2)}";
            sb.AppendLine($"  <polygon points=\"{pts}\" class=\"face\"/>");
        }

        // ── TD-22-3: texture overlay (affine-mapped per face, per material) ────
        if (hasTexture)
        {
            sb.AppendLine("  <!-- UV-mapped texture overlay (per material) -->");
            foreach (var face in result.Faces.Where(f => f.UVCoords != null))
            {
                var faceUri = ResolveUri(face, matDataUris);
                if (faceUri == null) continue;

                var uv  = face.UVCoords!;
                var svg = face.Vertices;

                double[] src = [uv[0].X, uv[0].Y, uv[1].X, uv[1].Y, uv[2].X, uv[2].Y];
                double[] dst = [
                    (svg[0].X - minX) * scale + margin,
                    (svg[0].Y - minY) * scale + margin,
                    (svg[1].X - minX) * scale + margin,
                    (svg[1].Y - minY) * scale + margin,
                    (svg[2].X - minX) * scale + margin,
                    (svg[2].Y - minY) * scale + margin
                ];

                var m = AffineTransform(src, dst);
                if (m == null) continue;

                sb.AppendLine(
                    $"  <image href=\"{faceUri}\" width=\"1\" height=\"1\" " +
                    $"preserveAspectRatio=\"none\" " +
                    $"clip-path=\"url(#cp{face.FaceId})\" " +
                    $"transform=\"matrix({Dm(m[0])},{Dm(m[1])},{Dm(m[2])},{Dm(m[3])},{Dm(m[4])},{Dm(m[5])})\" " +
                    $"opacity=\"0.85\"/>");
            }
        }

        // ── fold / cut / boundary lines ────────────────────────────────────────
        // TD-22-4: use rounded-coordinate edge key to avoid float equality issues
        var drawnEdges = new HashSet<(float, float, float, float)>();

        foreach (var face in result.Faces)
        {
            var verts = face.Vertices;
            for (int i = 0; i < 3; i++)
            {
                bool isFold     = face.EdgeIsFold[i];
                bool isBoundary = face.EdgeIsBoundary[i];

                if (isFold && !p.PrintFoldLines) continue;
                if (!isFold && !isBoundary && !p.PrintCutLines) continue;

                var pa  = verts[i];
                var pb  = verts[(i + 1) % 3];

                // TD-22-4: round coordinates to 3 decimal places before hashing
                var key = EdgeKey(pa, pb);
                if (!drawnEdges.Add(key)) continue;

                string cls = isBoundary ? "boundary" : (isFold ? "fold" : "cut");
                sb.AppendLine($"  <line x1=\"{Sx(pa.X)}\" y1=\"{Sy(pa.Y)}\" " +
                              $"x2=\"{Sx(pb.X)}\" y2=\"{Sy(pb.Y)}\" class=\"{cls}\"/>");
            }
        }

        // ── glue tabs ──────────────────────────────────────────────────────────
        if (p.IncludeGlueTabs)
        {
            foreach (var tab in result.GlueTabs)
            {
                string pts = string.Join(" ", tab.Vertices.Select(v => Pt(v)));
                sb.AppendLine($"  <polygon points=\"{pts}\" class=\"tab\"/>");
            }
        }

        sb.AppendLine("</svg>");
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// TD-22-3: resolve the data URI for a face — materialId first, then -1 fallback.
    private static string? ResolveUri(UnfoldedFace face, Dictionary<int, string> uris)
    {
        if (uris.TryGetValue(face.MaterialId, out var uri)) return uri;
        if (uris.TryGetValue(-1, out uri))                  return uri;
        return null;
    }

    private static string? TryBuildDataUri(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var ext   = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            var mime  = ext switch { "jpg" or "jpeg" => "image/jpeg",
                                     "bmp"           => "image/bmp",
                                     _               => "image/png" };
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch { return null; }
    }

    /// TD-22-4: canonical edge key using 3-dp rounded coordinates, order-independent.
    private static (float, float, float, float) EdgeKey(Vector2 a, Vector2 b)
    {
        float ax = MathF.Round(a.X, 3), ay = MathF.Round(a.Y, 3);
        float bx = MathF.Round(b.X, 3), by = MathF.Round(b.Y, 3);
        return (ax < bx || (ax == bx && ay <= by)) ? (ax, ay, bx, by) : (bx, by, ax, ay);
    }

    private static double[]? AffineTransform(double[] src, double[] dst) =>
        AffineTransformHelper.Compute(src, dst);

    private static string F(float  v) => v.ToString("F2", CultureInfo.InvariantCulture);
    private static string Dm(double v) => v.ToString("F6", CultureInfo.InvariantCulture);

    private static string Clamp(string hex, string fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        return hex.StartsWith('#') ? hex : '#' + hex;
    }
}
