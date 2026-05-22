using CommunityToolkit.Mvvm.ComponentModel;
using FourHUnfolder.Domain.Settings;

namespace FourHUnfolder.App.ViewModels;

/// <summary>
/// Editable clone of <see cref="AppSettings"/> used exclusively by
/// <see cref="Dialogs.SettingsDialog"/>.
/// Call <see cref="LoadFrom"/> to populate from the current settings,
/// then <see cref="ToSettings"/> to get the updated object back.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    // ── 3D View ───────────────────────────────────────────────────────────────
    [ObservableProperty] private string _background3D          = "#0d0d1a";
    [ObservableProperty] private bool   _showCoordSystem       = true;
    [ObservableProperty] private bool   _showViewCube          = true;
    [ObservableProperty] private string _displayMode3D         = "Solid";
    [ObservableProperty] private string _faceColor3D           = "#64a0dc";
    [ObservableProperty] private string _backFaceColor3D       = "#3c6496";
    [ObservableProperty] private double _faceOpacity           = 1.0;
    [ObservableProperty] private string _edgeOverlayColor      = "#ffffffff";
    [ObservableProperty] private double _edgeOverlayThickness  = 0.5;
    [ObservableProperty] private double _ambientIntensity      = 0.3;
    [ObservableProperty] private double _directionalIntensity  = 0.85;
    [ObservableProperty] private double _cameraFOV             = 45.0;
    [ObservableProperty] private double _cameraNearPlane       = 0.01;
    [ObservableProperty] private double _cameraFarPlane        = 5000.0;

    // ── 2D View ───────────────────────────────────────────────────────────────
    [ObservableProperty] private string _canvasBackground      = "#3a3a5a";
    [ObservableProperty] private string _paperColor            = "#ffffff";
    [ObservableProperty] private bool   _showGrid              = true;
    [ObservableProperty] private double _gridSizeMm            = 10.0;
    [ObservableProperty] private string _gridColor             = "#28000080";
    [ObservableProperty] private string _faceFillColor         = "#c8fffde2";
    [ObservableProperty] private string _foldLineColor2D       = "#4169e1";
    [ObservableProperty] private double _foldLineWidth2D       = 0.8;
    [ObservableProperty] private string _foldLineDash2D        = "4,2";
    [ObservableProperty] private string _cutLineColor2D        = "#ff0000";
    [ObservableProperty] private double _cutLineWidth2D        = 1.0;
    [ObservableProperty] private bool   _showGlueTabs          = true;
    [ObservableProperty] private string _glueTabColor          = "#7850c850";
    [ObservableProperty] private bool   _showFaceNumbers       = false;
    [ObservableProperty] private string _faceNumberColor       = "#888888";
    [ObservableProperty] private double _pieceGapMm            = 10.0;
    [ObservableProperty] private bool   _snapToGrid            = false;
    [ObservableProperty] private double _defaultPixelsPerMm    = 3.0;
    [ObservableProperty] private string _edgeHoverColor        = "#ffff9900";
    [ObservableProperty] private string _defaultPaperSizeName = "A4";

    public IReadOnlyList<string> PaperSizeNames { get; } =
        FourHUnfolder.Domain.Models.PaperSizeModel.Presets
            .Select(p => p.Name).ToArray();

    // ── General ───────────────────────────────────────────────────────────────
    [ObservableProperty] private string _displayUnit = "mm";
    // Values must match AppSettings.GeneralSettings.DisplayUnit exactly ("mm" | "inch")
    public IReadOnlyList<string> DisplayUnits { get; } = ["mm", "inch"];

    // ── Print ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private double _glueTabDepthMm         = 4.0;
    [ObservableProperty] private double _glueTabInsetRatio      = 0.15;
    [ObservableProperty] private double _marginMm              = 10.0;
    [ObservableProperty] private double _bleedMm               = 3.0;
    [ObservableProperty] private bool   _includeGlueTabs       = true;
    [ObservableProperty] private bool   _printFoldLines        = true;
    [ObservableProperty] private bool   _printCutLines         = true;
    [ObservableProperty] private bool   _includePageLabel      = true;
    [ObservableProperty] private string _printFoldColor        = "#4169e1";
    [ObservableProperty] private double _printFoldWidth        = 1.0;
    [ObservableProperty] private string _printFoldDash         = "4,2";
    [ObservableProperty] private string _printCutColor         = "#ff0000";
    [ObservableProperty] private double _printCutWidth         = 1.2;
    [ObservableProperty] private double _svgScaleFactor        = 10.0;
    [ObservableProperty] private bool   _grayscaleOutput       = false;

    // ── Static option lists (for ComboBoxes) ──────────────────────────────────
    public IReadOnlyList<string> DisplayModes   { get; } = ["Solid", "SolidEdges", "Wireframe"];
    public IReadOnlyList<string> DashPatterns   { get; } = ["4,2", "2,2", "8,2", "6,3", "Solid"];

    // ── Conversion helpers ────────────────────────────────────────────────────

    public void LoadFrom(AppSettings s)
    {
        // 3D
        Background3D         = s.View3D.BackgroundColor;
        ShowCoordSystem      = s.View3D.ShowCoordinateSystem;
        ShowViewCube         = s.View3D.ShowViewCube;
        DisplayMode3D        = s.View3D.DisplayMode;
        FaceColor3D          = s.View3D.FaceColor;
        BackFaceColor3D      = s.View3D.BackFaceColor;
        FaceOpacity          = s.View3D.FaceOpacity;
        EdgeOverlayColor     = s.View3D.EdgeOverlayColor;
        EdgeOverlayThickness = s.View3D.EdgeOverlayThickness;
        AmbientIntensity     = s.View3D.AmbientIntensity;
        DirectionalIntensity = s.View3D.DirectionalIntensity;
        CameraFOV            = s.View3D.CameraFOV;
        CameraNearPlane      = s.View3D.CameraNearPlane;
        CameraFarPlane       = s.View3D.CameraFarPlane;

        // 2D
        CanvasBackground   = s.View2D.CanvasBackground;
        PaperColor         = s.View2D.PaperColor;
        ShowGrid           = s.View2D.ShowGrid;
        GridSizeMm         = s.View2D.GridSizeMm;
        GridColor          = s.View2D.GridColor;
        FaceFillColor      = s.View2D.FaceFillColor;
        FoldLineColor2D    = s.View2D.FoldLineColor;
        FoldLineWidth2D    = s.View2D.FoldLineWidth;
        FoldLineDash2D     = s.View2D.FoldLineDash;
        CutLineColor2D     = s.View2D.CutLineColor;
        CutLineWidth2D     = s.View2D.CutLineWidth;
        ShowGlueTabs       = s.View2D.ShowGlueTabs;
        GlueTabColor       = s.View2D.GlueTabColor;
        ShowFaceNumbers    = s.View2D.ShowFaceNumbers;
        FaceNumberColor    = s.View2D.FaceNumberColor;
        PieceGapMm         = s.View2D.PieceGapMm;
        SnapToGrid         = s.View2D.SnapToGrid;
        DefaultPixelsPerMm = s.View2D.DefaultPixelsPerMm;
        EdgeHoverColor         = s.View2D.EdgeHoverColor;
        DefaultPaperSizeName   = s.View2D.DefaultPaperSizeName;

        // General
        DisplayUnit = s.General.DisplayUnit;

        // Print
        GlueTabDepthMm    = s.Print.GlueTabDepthMm;
        GlueTabInsetRatio = s.Print.GlueTabInsetRatio;
        MarginMm        = s.Print.MarginMm;
        BleedMm         = s.Print.BleedMm;
        IncludeGlueTabs = s.Print.IncludeGlueTabs;
        PrintFoldLines  = s.Print.PrintFoldLines;
        PrintCutLines   = s.Print.PrintCutLines;
        IncludePageLabel = s.Print.IncludePageLabel;
        PrintFoldColor  = s.Print.FoldLineColor;
        PrintFoldWidth  = s.Print.FoldLineWidth;
        PrintFoldDash   = s.Print.FoldLineDash;
        PrintCutColor   = s.Print.CutLineColor;
        PrintCutWidth   = s.Print.CutLineWidth;
        SvgScaleFactor  = s.Print.SvgScaleFactor;
        GrayscaleOutput = s.Print.GrayscaleOutput;
    }

    public AppSettings ToSettings() => new()
    {
        View3D = new()
        {
            BackgroundColor      = Background3D,
            ShowCoordinateSystem = ShowCoordSystem,
            ShowViewCube         = ShowViewCube,
            DisplayMode          = DisplayMode3D,
            FaceColor            = FaceColor3D,
            BackFaceColor        = BackFaceColor3D,
            FaceOpacity          = FaceOpacity,
            EdgeOverlayColor     = EdgeOverlayColor,
            EdgeOverlayThickness = EdgeOverlayThickness,
            AmbientIntensity     = AmbientIntensity,
            DirectionalIntensity = DirectionalIntensity,
            CameraFOV            = CameraFOV,
            CameraNearPlane      = CameraNearPlane,
            CameraFarPlane       = CameraFarPlane
        },
        View2D = new()
        {
            CanvasBackground  = CanvasBackground,
            PaperColor        = PaperColor,
            ShowGrid          = ShowGrid,
            GridSizeMm        = GridSizeMm,
            GridColor         = GridColor,
            FaceFillColor     = FaceFillColor,
            FoldLineColor     = FoldLineColor2D,
            FoldLineWidth     = FoldLineWidth2D,
            FoldLineDash      = FoldLineDash2D,
            CutLineColor      = CutLineColor2D,
            CutLineWidth      = CutLineWidth2D,
            ShowGlueTabs      = ShowGlueTabs,
            GlueTabColor      = GlueTabColor,
            ShowFaceNumbers    = ShowFaceNumbers,
            FaceNumberColor    = FaceNumberColor,
            PieceGapMm         = PieceGapMm,
            SnapToGrid         = SnapToGrid,
            DefaultPixelsPerMm = DefaultPixelsPerMm,
            EdgeHoverColor         = EdgeHoverColor,
            DefaultPaperSizeName   = DefaultPaperSizeName
        },
        Print = new()
        {
            GlueTabDepthMm    = GlueTabDepthMm,
            GlueTabInsetRatio = GlueTabInsetRatio,
            MarginMm        = MarginMm,
            BleedMm         = BleedMm,
            IncludeGlueTabs = IncludeGlueTabs,
            PrintFoldLines  = PrintFoldLines,
            PrintCutLines   = PrintCutLines,
            IncludePageLabel = IncludePageLabel,
            FoldLineColor   = PrintFoldColor,
            FoldLineWidth   = PrintFoldWidth,
            FoldLineDash    = PrintFoldDash,
            CutLineColor    = PrintCutColor,
            CutLineWidth    = PrintCutWidth,
            SvgScaleFactor  = SvgScaleFactor,
            GrayscaleOutput = GrayscaleOutput
        },
        General = new()
        {
            DisplayUnit = DisplayUnit  // "mm" or "inch" — already stored as the canonical value
        }
    };
}
