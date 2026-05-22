namespace FourHUnfolder.Domain.Results;

public sealed class UnfoldResult
{
    public IReadOnlyList<UnfoldedFace> Faces      { get; }
    public IReadOnlyList<GlueTab>      GlueTabs   { get; }
    public bool                        HasOverlaps { get; }

    public UnfoldResult(
        IReadOnlyList<UnfoldedFace> faces,
        IReadOnlyList<GlueTab>      glueTabs,
        bool                        hasOverlaps)
    {
        Faces       = faces;
        GlueTabs    = glueTabs;
        HasOverlaps = hasOverlaps;
    }
}
