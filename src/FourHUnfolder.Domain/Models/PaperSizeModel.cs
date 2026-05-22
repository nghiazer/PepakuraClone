namespace FourHUnfolder.Domain.Models;

public sealed class PaperSizeModel
{
    public string Name     { get; }
    public double WidthMm  { get; }
    public double HeightMm { get; }

    public PaperSizeModel(string name, double widthMm, double heightMm)
    {
        Name     = name;
        WidthMm  = widthMm;
        HeightMm = heightMm;
    }

    public PaperSizeModel Landscape() => new(Name + " (L)", HeightMm, WidthMm);
    public PaperSizeModel Portrait()  => new(Name,           WidthMm,  HeightMm);

    public static readonly PaperSizeModel A4     = new("A4",     210.0,  297.0);
    public static readonly PaperSizeModel A3     = new("A3",     297.0,  420.0);
    public static readonly PaperSizeModel A2     = new("A2",     420.0,  594.0);
    public static readonly PaperSizeModel A1     = new("A1",     594.0,  841.0);
    public static readonly PaperSizeModel Letter = new("Letter", 215.9,  279.4);
    public static readonly PaperSizeModel Legal  = new("Legal",  215.9,  355.6);

    public static PaperSizeModel Custom(double w, double h) => new("Custom", w, h);

    public static PaperSizeModel[] Presets =>
        [A4, A3, A2, A1, Letter, Legal];

    public override string ToString() => $"{Name}  ({WidthMm:F0}×{HeightMm:F0} mm)";
}
