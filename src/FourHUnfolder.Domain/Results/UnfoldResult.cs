namespace FourHUnfolder.Domain.Results;

public sealed class UnfoldResult
{
    public IReadOnlyList<UnfoldedFace>       Faces           { get; }
    public IReadOnlyList<GlueTab>            GlueTabs        { get; }
    public bool                              HasOverlaps     { get; }
    /// Maps mesh edge ID → 1-based sequential pair number for every cut edge.
    public IReadOnlyDictionary<int, int>     CutEdgePairIds  { get; }

    public UnfoldResult(
        IReadOnlyList<UnfoldedFace>      faces,
        IReadOnlyList<GlueTab>           glueTabs,
        bool                             hasOverlaps,
        IReadOnlyDictionary<int, int>?   cutEdgePairIds = null)
    {
        Faces          = faces;
        GlueTabs       = glueTabs;
        HasOverlaps    = hasOverlaps;
        CutEdgePairIds = cutEdgePairIds ?? new Dictionary<int, int>();
    }
}
