using System.Globalization;
using System.Numerics;
using System.Text;
using PepakuraClone.Application.Interfaces;
using PepakuraClone.Application.Services;
using PepakuraClone.Domain.Results;
using PepakuraClone.Domain.Settings;

namespace PepakuraClone.Infrastructure.Exporters;

/// <summary>
/// Exports an <see cref="UnfoldResult"/> to a standalone SVG file.
///
/// Edge types:
///   .fold     — dashed, settings colour (fold edges)
///   .cut      — solid,  settings colour (cut edges between pieces)
///   .boundary — thin dark grey (outer mesh boundary, no glue)
///
/// When <paramref name="texturePath"/> is supplied and faces carry UV coords,
/// the texture is embedded as a base-64 data URI and affine-mapped per face.
/// </summary>
public class SvgExporter : IExporter
{
    private readonly SettingsService _settings;

    public SvgExporter(SettingsService settings) => _settings = settings;

    public void Export(UnfoldResult result, string filePath, string? texturePath = null)
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

        // ── texture ────────────────────────────────────────────────────────────
        bool hasTexture = !string.IsNullOrEmpty(texturePath)
            && File.Exists(texturePath)
            && result.Faces.Any(f => f.UVCoords != null);

        string? texDataUri  = null;
        if (hasTexture)
        {
            try
            {
                var bytes = File.ReadAllBytes(texturePath!);
                var ext   = Path.GetExtension(texturePath!).TrimStart('.').ToLowerInvariant();
                var mime  = ext switch { "jpg" or "jpeg" => "image/jpeg",
                                         "bmp"           => "image/bmp",
                                         _               => "image/png" };
                texDataUri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            }
            catch { hasTexture = false; }
        }

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
            foreach (var face in result.Faces.Where(f => f.UVCoords != null))
            {
                sb.AppendLine($"    <clipPath id=\"cp{face.FaceId}\">" +
                              $"<polygon points=\"{Pt(face.V0)} {Pt(face.V1)} {Pt(face.V2)}\"/></clipPath>");
            }
        }
        sb.AppendLine("  </defs>");

        if (p.IncludePageLabel)
            sb.AppendLine($"  <text x=\"{F(margin)}\" y=\"{F(margin - 4)}\" class=\"label\">PepakuraClone Export</text>");

        // ── face polygons ──────────────────────────────────────────────────────
        foreach (var face in result.Faces)
        {
            string pts = $"{Pt(face.V0)} {Pt(face.V1)} {Pt(face.V2)}";
            sb.AppendLine($"  <polygon points=\"{pts}\" class=\"face\"/>");
        }

        // ── texture overlay (affine-mapped per face) ───────────────────────────
        if (hasTexture && texDataUri != null)
        {
            sb.AppendLine("  <!-- UV-mapped texture overlay -->");
            foreach (var face in result.Faces.Where(f => f.UVCoords != null))
            {
                var uv  = face.UVCoords!;
                var svg = face.Vertices;

                // Affine transform: UV [0,1]² → SVG pixel space
                // Source: UV (u,v) coordinates for each vertex
                // Dest:   SVG pixel (x,y) for each vertex
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
                    $"  <image href=\"{texDataUri}\" width=\"1\" height=\"1\" " +
                    $"preserveAspectRatio=\"none\" " +
                    $"clip-path=\"url(#cp{face.FaceId})\" " +
                    $"transform=\"matrix({Dm(m[0])},{Dm(m[1])},{Dm(m[2])},{Dm(m[3])},{Dm(m[4])},{Dm(m[5])})\" " +
                    $"opacity=\"0.85\"/>");
            }
        }

        // ── fold / cut / boundary lines ────────────────────────────────────────
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

                var key = pa.X <= pb.X || (pa.X == pb.X && pa.Y <= pb.Y)
                    ? (pa.X, pa.Y, pb.X, pb.Y)
                    : (pb.X, pb.Y, pa.X, pa.Y);
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

    // ── affine transform helpers ───────────────────────────────────────────────

    /// <summary>
    /// Computes 6 affine coefficients [a,b,c,d,e,f] mapping UV triangle to SVG triangle.
    /// src = [u0,v0, u1,v1, u2,v2], dst = [x0,y0, x1,y1, x2,y2].
    /// Returns null for degenerate triangles.
    /// </summary>
    private static double[]? AffineTransform(double[] src, double[] dst)
    {
        // Build 3×3 source matrix from UV coords
        double u0 = src[0], v0 = src[1];
        double u1 = src[2], v1 = src[3];
        double u2 = src[4], v2 = src[5];

        double det = u0 * (v1 - v2) - u1 * (v0 - v2) + u2 * (v0 - v1);
        if (Math.Abs(det) < 1e-12) return null;

        // Inverse of 3×3 source matrix (adjugate / det)
        double i00 = (v1 - v2) / det,  i01 = (u2 - u1) / det,  i02 = (u1 * v2 - u2 * v1) / det;
        double i10 = (v2 - v0) / det,  i11 = (u0 - u2) / det,  i12 = (u2 * v0 - u0 * v2) / det;
        double i20 = (v0 - v1) / det,  i21 = (u1 - u0) / det,  i22 = (u0 * v1 - u1 * v0) / det;

        // M = dst_matrix (2×3) × inv_src (3×3)
        double x0 = dst[0], y0 = dst[1];
        double x1 = dst[2], y1 = dst[3];
        double x2 = dst[4], y2 = dst[5];

        double a = x0 * i00 + x1 * i10 + x2 * i20;
        double c = x0 * i01 + x1 * i11 + x2 * i21;
        double e = x0 * i02 + x1 * i12 + x2 * i22;
        double b = y0 * i00 + y1 * i10 + y2 * i20;
        double d = y0 * i01 + y1 * i11 + y2 * i21;
        double f = y0 * i02 + y1 * i12 + y2 * i22;

        return [a, b, c, d, e, f];
    }

    // ── string helpers ─────────────────────────────────────────────────────────

    private static string F(float  v) => v.ToString("F2", CultureInfo.InvariantCulture);
    private static string Dm(double v) => v.ToString("F6", CultureInfo.InvariantCulture);

    private static string Clamp(string hex, string fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        return hex.StartsWith('#') ? hex : '#' + hex;
    }
}
