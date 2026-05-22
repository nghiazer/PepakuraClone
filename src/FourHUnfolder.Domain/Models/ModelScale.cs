namespace FourHUnfolder.Domain.Models;

public enum ScaleUnit { Mm, Cm, Inch }
public enum ScaleAxis { Width, Height, Depth, Longest }

public sealed class ModelScale
{
    public double    TargetValue { get; }
    public ScaleUnit Unit        { get; }
    public ScaleAxis Axis        { get; }

    public ModelScale(double targetValue, ScaleUnit unit, ScaleAxis axis)
    {
        TargetValue = targetValue;
        Unit        = unit;
        Axis        = axis;
    }

    public double TargetMm => Unit switch
    {
        ScaleUnit.Cm   => TargetValue * 10.0,
        ScaleUnit.Inch => TargetValue * 25.4,
        _              => TargetValue
    };

    public static ModelScale Default => new(200.0, ScaleUnit.Mm, ScaleAxis.Longest);
}
