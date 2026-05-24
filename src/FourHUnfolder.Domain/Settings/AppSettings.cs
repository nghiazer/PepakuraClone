namespace FourHUnfolder.Domain.Settings;

/// <summary>
/// All user-configurable application settings, serialised to
/// %AppData%\FourHUnfolder\settings.json.
/// Every property has a sensible default so the class can be
/// instantiated without loading a file.
/// </summary>
public sealed class AppSettings
{
    public View3DSettings  View3D { get; set; } = new();
    public View2DSettings  View2D { get; set; } = new();
    public PrintSettings   Print  { get; set; } = new();

    // ── 3D Viewport ───────────────────────────────────────────────────────────
    public sealed class View3DSettings
    {
        // Background
        public string BackgroundColor      { get; set; } = "#0d0d1a";

        // Viewport chrome
        public bool   ShowCoordinateSystem { get; set; } = true;
        public bool   ShowViewCube         { get; set; } = true;

        // Display mode: "Solid" | "SolidEdges" | "Wireframe"
        public string DisplayMode          { get; set; } = "Solid";

        // Mesh surface
        public string FaceColor            { get; set; } = "#64a0dc";
        public string BackFaceColor        { get; set; } = "#3c6496";
        public double FaceOpacity          { get; set; } = 1.0;

        // Optional edge overlay (shown when DisplayMode = SolidEdges)
        public string EdgeOverlayColor     { get; set; } = "#ffffffff";
        public double EdgeOverlayThickness { get; set; } = 0.5;

        // 3D edge-hover highlight colors (detachable fold = red, attachable cut = green)
        public string EdgeHoverDetachColor { get; set; } = "#ff3333";
        public string EdgeHoverAttachColor { get; set; } = "#33cc33";

        // Lighting
        public double AmbientIntensity     { get; set; } = 0.3;
        public double DirectionalIntensity { get; set; } = 0.85;

        // Camera
        public double CameraFOV        { get; set; } = 45.0;   // degrees
        public double CameraNearPlane  { get; set; } = 0.01;
        public double CameraFarPlane   { get; set; } = 5000.0;
    }

    // ── 2D Pattern Canvas ─────────────────────────────────────────────────────
    public sealed class View2DSettings
    {
        // Canvas backdrop
        public string CanvasBackground  { get; set; } = "#3a3a5a";
        public string PaperColor        { get; set; } = "#ffffff";

        // Grid
        public bool   ShowGrid          { get; set; } = true;
        public double GridSizeMm        { get; set; } = 10.0;
        public string GridColor         { get; set; } = "#28000080";

        // Face fill
        public string FaceFillColor     { get; set; } = "#c8fffde2";

        // Fold edge
        public string FoldLineColor     { get; set; } = "#4169e1";   // RoyalBlue
        public double FoldLineWidth     { get; set; } = 0.8;
        public string FoldLineDash      { get; set; } = "4,2";       // "Solid" or "n,m"

        // Cut edge
        public string CutLineColor      { get; set; } = "#ff0000";
        public double CutLineWidth      { get; set; } = 1.0;

        // Glue tabs
        public bool   ShowGlueTabs      { get; set; } = true;
        public string GlueTabColor      { get; set; } = "#7850c850";

        // Annotations
        public bool   ShowFaceNumbers   { get; set; } = false;
        public string FaceNumberColor   { get; set; } = "#888888";

        // Piece spacing used during auto-arrange
        public double PieceGapMm        { get; set; } = 10.0;

        // Snap pieces to grid intersections when dragging
        public bool   SnapToGrid        { get; set; } = false;

        // Default zoom when canvas first opens
        public double DefaultPixelsPerMm { get; set; } = 3.0;

        // Default paper size name; matched against PaperSizeModel.Presets on apply
        public string DefaultPaperSizeName { get; set; } = "A4";

        // Highlight color for hoverable edges in Edge-Edit mode (ARGB hex)
        public string EdgeHoverColor    { get; set; } = "#ffff9900";

        // Edge ID annotations on cut edges
        public bool   ShowEdgeIds       { get; set; } = true;
        public string EdgeIdColor       { get; set; } = "#cc333333";
    }

    // ── Print / SVG Export ────────────────────────────────────────────────────
    public sealed class PrintSettings
    {
        // Layout
        public double MarginMm          { get; set; } = 10.0;
        public double BleedMm           { get; set; } = 3.0;

        // Content switches
        public bool   IncludeGlueTabs   { get; set; } = true;
        public bool   PrintFoldLines    { get; set; } = true;
        public bool   PrintCutLines     { get; set; } = true;
        public bool   IncludePageLabel  { get; set; } = true;

        // Line appearance (SVG units)
        public string FoldLineColor     { get; set; } = "#4169e1";
        public double FoldLineWidth     { get; set; } = 1.0;
        public string CutLineColor      { get; set; } = "#ff0000";
        public double CutLineWidth      { get; set; } = 1.2;
        public string FoldLineDash      { get; set; } = "4,2";

        // Glue tab geometry
        public double GlueTabDepthMm      { get; set; } = 5.0;   // perpendicular depth of tab (mm)
        public double GlueTabSideAngleDeg { get; set; } = 45.0;  // angle of side wall vs edge (1–90°)
        // Tab shape: "Trapezoid" | "Rectangle" | "Triangle"
        public string GlueTabShape        { get; set; } = "Trapezoid";
        // Alternate flap placement: only one face per cut edge pair gets a tab
        public bool   AlternateFlaps      { get; set; } = false;

        // Output quality
        public double SvgScaleFactor    { get; set; } = 10.0;  // model-mm → SVG px
        public bool   GrayscaleOutput   { get; set; } = false;
    }

    // ── General ───────────────────────────────────────────────────────────────
    public GeneralSettings General { get; set; } = new();

    public sealed class GeneralSettings
    {
        /// Display unit used throughout the UI: "mm" or "inch"
        public string DisplayUnit { get; set; } = "mm";
    }
}
