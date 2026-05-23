using System.Globalization;
using System.Numerics;
using FourHUnfolder.Application.Services;
using FourHUnfolder.Domain.Results;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FourHUnfolder.Infrastructure.Exporters;

/// <summary>
/// Exports an <see cref="UnfoldResult"/> to a multi-page PDF file.
/// Each canvas page becomes one PDF page.
/// </summary>
public class PdfExporter
{
    private readonly SettingsService _settings;

    public PdfExporter(SettingsService settings) => _settings = settings;

    public void Export(UnfoldResult result, string filePath,
                       double paperWidthMm, double paperHeightMm,
                       int pagesWide = 1, int pagesToll = 1,
                       double pageSepMm = 20)
    {
        var p = _settings.Current.Print;

        using var doc = new PdfDocument();
        doc.Info.Title = System.IO.Path.GetFileNameWithoutExtension(filePath);

        // Points per mm: 1 pt = 1/72 inch, 1 inch = 25.4 mm → 1 mm = 72/25.4 pt
        const double PtPerMm = 72.0 / 25.4;

        double pageWidthPt  = paperWidthMm  * PtPerMm;
        double pageHeightPt = paperHeightMm * PtPerMm;

        for (int row = 0; row < pagesToll; row++)
        for (int col = 0; col < pagesWide; col++)
        {
            var page = doc.AddPage();
            page.Width  = XUnit.FromPoint(pageWidthPt);
            page.Height = XUnit.FromPoint(pageHeightPt);

            using var gfx = XGraphics.FromPdfPage(page);

            // Origin offset for this page in model-mm coords
            double oxMm = col * (paperWidthMm + pageSepMm);
            double oyMm = row * (paperHeightMm + pageSepMm);

            // Convert mm to PDF points (Y axis flipped: PDF origin is top-left)
            double MmToX(double x) => (x - oxMm) * PtPerMm;
            double MmToY(double y) => pageHeightPt - (y - oyMm) * PtPerMm;

            // Pens & brushes
            string foldHex = p.GrayscaleOutput ? "#555555" : p.FoldLineColor;
            string cutHex  = p.GrayscaleOutput ? "#000000" : p.CutLineColor;

            var foldPen = new XPen(HexToColor(foldHex), p.FoldLineWidth);
            if (!p.FoldLineDash.Equals("Solid", StringComparison.OrdinalIgnoreCase))
                foldPen.DashStyle = XDashStyle.Dash;

            var cutPen      = new XPen(HexToColor(cutHex), p.CutLineWidth);
            var boundPen    = new XPen(XColors.DimGray, 0.6);
            var faceBrush   = p.GrayscaleOutput
                ? new XSolidBrush(XColor.FromArgb(240, 240, 240))
                : new XSolidBrush(XColor.FromArgb(255, 253, 231));
            var tabBrush    = p.GrayscaleOutput
                ? new XSolidBrush(XColor.FromArgb(200, 200, 200))
                : new XSolidBrush(XColor.FromArgb(100, 80, 200, 80));

            // ── face fills ─────────────────────────────────────────────────
            foreach (var face in result.Faces)
            {
                if (!IsOnPage(face, oxMm, oyMm, paperWidthMm, paperHeightMm)) continue;
                var pts = ToPoints(face.Vertices, MmToX, MmToY);
                gfx.DrawPolygon(faceBrush, pts, XFillMode.Winding);
            }

            // ── glue tabs ──────────────────────────────────────────────────
            if (p.IncludeGlueTabs)
            {
                foreach (var tab in result.GlueTabs)
                {
                    if (!IsTabOnPage(tab, oxMm, oyMm, paperWidthMm, paperHeightMm)) continue;
                    var pts = ToPoints(tab.Vertices, MmToX, MmToY);
                    var tabPen = new XPen(XColor.FromArgb(46, 125, 50), 0.6);
                    gfx.DrawPolygon(tabPen, tabBrush, pts, XFillMode.Winding);
                }
            }

            // ── fold / cut / boundary lines ────────────────────────────────
            var drawn = new HashSet<(float, float, float, float)>();
            foreach (var face in result.Faces)
            {
                if (!IsOnPage(face, oxMm, oyMm, paperWidthMm, paperHeightMm)) continue;
                var verts = face.Vertices;
                for (int i = 0; i < 3; i++)
                {
                    bool isFold     = face.EdgeIsFold[i];
                    bool isBoundary = face.EdgeIsBoundary[i];
                    if (isFold && !p.PrintFoldLines) continue;
                    if (!isFold && !isBoundary && !p.PrintCutLines) continue;

                    var va = verts[i]; var vb = verts[(i + 1) % 3];
                    var key = va.X <= vb.X || (va.X == vb.X && va.Y <= vb.Y)
                        ? (va.X, va.Y, vb.X, vb.Y)
                        : (vb.X, vb.Y, va.X, va.Y);
                    if (!drawn.Add(key)) continue;

                    var pen = isBoundary ? boundPen : (isFold ? foldPen : cutPen);
                    gfx.DrawLine(pen,
                        MmToX(va.X), MmToY(va.Y),
                        MmToX(vb.X), MmToY(vb.Y));
                }
            }

            // ── page label ─────────────────────────────────────────────────
            if (p.IncludePageLabel)
            {
                int pageNum = row * pagesWide + col + 1;
                string label = (pagesWide * pagesToll > 1)
                    ? $"{System.IO.Path.GetFileNameWithoutExtension(filePath)}  p.{pageNum}"
                    : System.IO.Path.GetFileNameWithoutExtension(filePath);
                gfx.DrawString(label, new XFont("Helvetica", 8), XBrushes.Gray,
                               new XRect(4, 4, pageWidthPt, 14), XStringFormats.TopLeft);
            }
        }

        doc.Save(filePath);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static XPoint[] ToPoints(IReadOnlyList<Vector2> verts,
                                     Func<double, double> toX,
                                     Func<double, double> toY)
        => verts.Select(v => new XPoint(toX(v.X), toY(v.Y))).ToArray();

    private static bool IsOnPage(UnfoldedFace face,
                                  double oxMm, double oyMm, double wMm, double hMm)
    {
        var vs = face.Vertices;
        return vs.Any(v => v.X >= oxMm && v.X <= oxMm + wMm
                        && v.Y >= oyMm && v.Y <= oyMm + hMm);
    }

    private static bool IsTabOnPage(GlueTab tab,
                                     double oxMm, double oyMm, double wMm, double hMm)
        => tab.Vertices.Any(v => v.X >= oxMm && v.X <= oxMm + wMm
                               && v.Y >= oyMm && v.Y <= oyMm + hMm);

    private static XColor HexToColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8) // AARRGGBB
        {
            byte a = Convert.ToByte(hex[0..2], 16);
            byte r = Convert.ToByte(hex[2..4], 16);
            byte g = Convert.ToByte(hex[4..6], 16);
            byte b = Convert.ToByte(hex[6..8], 16);
            return XColor.FromArgb(a, r, g, b);
        }
        if (hex.Length == 6)
        {
            byte r = Convert.ToByte(hex[0..2], 16);
            byte g = Convert.ToByte(hex[2..4], 16);
            byte b = Convert.ToByte(hex[4..6], 16);
            return XColor.FromArgb(r, g, b);
        }
        return XColors.Black;
    }
}
