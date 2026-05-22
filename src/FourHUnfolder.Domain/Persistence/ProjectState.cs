namespace FourHUnfolder.Domain.Persistence;

/// <summary>
/// Serialisable snapshot of a FourHUnfolder editing session.
/// Saved as JSON with extension .pmc (FourHUnfolder).
/// </summary>
public sealed class ProjectState
{
    public int Version { get; set; } = 2;

    // ── File paths (stored relative to the .pmc file, absolute as fallback) ──
    public string? MeshPath    { get; set; }
    public string? TexturePath { get; set; }

    // ── Unfold settings ───────────────────────────────────────────────────────
    public double   ScaleMmPerUnit { get; set; } = 1.0;

    public PaperDto Paper { get; set; } = new();

    // ── Multi-page canvas layout ──────────────────────────────────────────────
    public int PagesWide { get; set; } = 1;
    public int PagesTall { get; set; } = 1;

    // ── User edge overrides (mesh edge id → "Fold" or "Cut") ─────────────────
    public Dictionary<int, string> EdgeOverrides { get; set; } = new();

    // ── Piece positions on the paper (one entry per connected component) ─────
    public List<PieceLayoutDto> Layouts { get; set; } = new();

    // ── Load-time warnings (not serialised) ──────────────────────────────────
    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> Warnings { get; } = new();

    // ── Nested DTOs ───────────────────────────────────────────────────────────

    public sealed class PaperDto
    {
        public string Name     { get; set; } = "A4";
        public double WidthMm  { get; set; } = 210;
        public double HeightMm { get; set; } = 297;
    }

    public sealed class PieceLayoutDto
    {
        public int    GroupId   { get; set; }
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public double Rotation  { get; set; }
    }
}
