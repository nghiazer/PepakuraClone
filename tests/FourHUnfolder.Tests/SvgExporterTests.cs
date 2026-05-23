using FluentAssertions;
using FourHUnfolder.Infrastructure.Exporters;
using Xunit;

namespace FourHUnfolder.Tests;

public class AffineTransformTests
{
    private const double Eps = 1e-9;

    // Helper: apply matrix to a UV point
    private static (double X, double Y) Map(double[] m, double u, double v) =>
        AffineTransformHelper.Apply(m, u, v);

    [Fact]
    public void Identity_UVEqualsScreenCoords()
    {
        // UV and screen triangles are the same → transform should be identity
        double[] src = [0, 0,  1, 0,  0, 1];
        double[] dst = [0, 0,  1, 0,  0, 1];

        var m = AffineTransformHelper.Compute(src, dst);

        m.Should().NotBeNull();
        var (x0, y0) = Map(m!, 0, 0);
        var (x1, y1) = Map(m!, 1, 0);
        var (x2, y2) = Map(m!, 0, 1);
        x0.Should().BeApproximately(0, Eps);
        y0.Should().BeApproximately(0, Eps);
        x1.Should().BeApproximately(1, Eps);
        y1.Should().BeApproximately(0, Eps);
        x2.Should().BeApproximately(0, Eps);
        y2.Should().BeApproximately(1, Eps);
    }

    [Fact]
    public void UniformScale_TransformsVerticesCorrectly()
    {
        // UV [0,1]² → screen [0,100]² (10× scale)
        double[] src = [0, 0,  1, 0,  0, 1];
        double[] dst = [0, 0,  100, 0,  0, 100];

        var m = AffineTransformHelper.Compute(src, dst);

        m.Should().NotBeNull();
        var (x1, y1) = Map(m!, 0.5, 0);    // midpoint of UV bottom edge
        var (x2, y2) = Map(m!, 0,   0.5);  // midpoint of UV left edge
        x1.Should().BeApproximately(50, Eps);
        y1.Should().BeApproximately(0,  Eps);
        x2.Should().BeApproximately(0,  Eps);
        y2.Should().BeApproximately(50, Eps);
    }

    [Fact]
    public void Translation_TransformsCorrectly()
    {
        // UV at origin → screen triangle shifted by (10, 20)
        double[] src = [0, 0,  1, 0,  0, 1];
        double[] dst = [10, 20,  11, 20,  10, 21];

        var m = AffineTransformHelper.Compute(src, dst);

        m.Should().NotBeNull();
        var (x0, y0) = Map(m!, 0, 0);
        var (x1, y1) = Map(m!, 1, 0);
        x0.Should().BeApproximately(10, Eps);
        y0.Should().BeApproximately(20, Eps);
        x1.Should().BeApproximately(11, Eps);
        y1.Should().BeApproximately(20, Eps);
    }

    [Fact]
    public void DegenerateTriangle_ReturnsNull()
    {
        // Three UV points on a line → degenerate (zero area)
        double[] src = [0, 0,  1, 1,  2, 2];
        double[] dst = [0, 0,  1, 0,  2, 0];

        var m = AffineTransformHelper.Compute(src, dst);

        m.Should().BeNull();
    }

    [Fact]
    public void MidpointPreserved_AfterArbitraryTransform()
    {
        // UV triangle centroid should map to screen triangle centroid
        double[] src = [0.1, 0.2,  0.8, 0.1,  0.4, 0.9];
        double[] dst = [50, 30,   200, 80,   100, 250];

        var m = AffineTransformHelper.Compute(src, dst);
        m.Should().NotBeNull();

        double ucx = (src[0] + src[2] + src[4]) / 3;
        double ucy = (src[1] + src[3] + src[5]) / 3;
        double dcx = (dst[0] + dst[2] + dst[4]) / 3;
        double dcy = (dst[1] + dst[3] + dst[5]) / 3;

        var (px, py) = Map(m!, ucx, ucy);
        px.Should().BeApproximately(dcx, 1e-6);
        py.Should().BeApproximately(dcy, 1e-6);
    }
}
