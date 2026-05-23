namespace FourHUnfolder.Infrastructure.Exporters;

/// <summary>
/// Computes an affine transform mapping a UV triangle to a screen-space triangle.
/// Used by SvgExporter for per-face texture mapping and exposed for unit testing.
/// </summary>
public static class AffineTransformHelper
{
    /// <summary>
    /// Computes the 6-element affine matrix [a,b,c,d,e,f] such that:
    ///   x' = a*u + c*v + e
    ///   y' = b*u + d*v + f
    /// where (u,v) are UV source coords and (x',y') are destination coords.
    /// </summary>
    /// <param name="src">6 values: u0,v0, u1,v1, u2,v2  (UV triangle)</param>
    /// <param name="dst">6 values: x0,y0, x1,y1, x2,y2  (screen triangle)</param>
    /// <returns>Matrix coefficients [a,b,c,d,e,f], or null if triangle is degenerate.</returns>
    public static double[]? Compute(double[] src, double[] dst)
    {
        double u0 = src[0], v0 = src[1];
        double u1 = src[2], v1 = src[3];
        double u2 = src[4], v2 = src[5];

        double det = u0 * (v1 - v2) - u1 * (v0 - v2) + u2 * (v0 - v1);
        if (Math.Abs(det) < 1e-12) return null;

        double i00 = (v1 - v2) / det,  i01 = (u2 - u1) / det,  i02 = (u1 * v2 - u2 * v1) / det;
        double i10 = (v2 - v0) / det,  i11 = (u0 - u2) / det,  i12 = (u2 * v0 - u0 * v2) / det;
        double i20 = (v0 - v1) / det,  i21 = (u1 - u0) / det,  i22 = (u0 * v1 - u1 * v0) / det;

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

    /// Applies the 6-element matrix to a UV point, returning the screen point.
    public static (double X, double Y) Apply(double[] m, double u, double v) =>
        (m[0] * u + m[2] * v + m[4],
         m[1] * u + m[3] * v + m[5]);
}
