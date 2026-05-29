using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourHUnfolder.Domain.Models;
using FourHUnfolder.Domain.Results;
using FourHUnfolder.Geometry.Algorithms;

namespace FourHUnfolder.App.ViewModels;

/// <summary>
/// ViewModel for the Assembly Animation window.
///
/// Animation — two phases per step
/// ─────────────────────────────────
/// Phase 1 (t ∈ [0, 0.5])  — Paper-fold: each face rotates around its shared
///   fold edge with its parent (BFS spanning tree per piece) from coplanar flat
///   to the correct 3-D dihedral angle, while the root face stays fixed in the
///   unfolded-layout plane.  Uses accumulated System.Numerics.Matrix4x4.
///
/// Phase 2 (t ∈ [0.5, 1.0]) — Fly-in: the fully-folded 3-D shape translates
///   (per-vertex lerp) from the flat-plane position to its final 3-D position
///   in the assembled model.
///
/// Texture
/// ───────
/// Assembled pieces and the current piece display the real mesh texture (per
/// material).  Current piece also gets a semi-transparent amber EmissiveMaterial
/// overlay so it visually "glows" on top of the texture.
/// Ghost (upcoming) pieces are rendered as a translucent solid — texture at
/// near-zero opacity would look bad.
/// </summary>
public sealed partial class AssemblyViewModel : ObservableObject, IDisposable
{
    // ── inner types ───────────────────────────────────────────────────────────

    /// Pre-computed per-piece geometry for all three animation states.
    private sealed class PieceAnimData
    {
        public required int            GroupId      { get; init; }
        public required int            StepIndex    { get; init; }
        public required bool           HasUVs       { get; init; }
        public required PieceFoldTree  FoldTree     { get; init; }
        /// mesh vertex ID → flat position on Y=baseY plane.
        public required IReadOnlyDictionary<int, Vector3> FlatVertexPos { get; init; }
        public required TriData[]      Tris         { get; init; }
    }

    /// Per-triangle data for all three animation states.
    private readonly struct TriData
    {
        public readonly int     FaceId;
        public readonly int     VA, VB, VC;              // mesh vertex IDs
        public readonly Vector3 FlatA,  FlatB,  FlatC;  // 2-D unfolded, on baseY plane
        public readonly Vector3 FoldA,  FoldB,  FoldC;  // folded to 3-D shape, still at flat plane
        public readonly Vector3 FinalA, FinalB, FinalC; // final 3-D world positions
        public readonly Point   UV_A,   UV_B,   UV_C;
        public readonly int     MaterialId;

        public TriData(int faceId, int va, int vb, int vc,
            Vector3 flatA,  Vector3 flatB,  Vector3 flatC,
            Vector3 foldA,  Vector3 foldB,  Vector3 foldC,
            Vector3 finalA, Vector3 finalB, Vector3 finalC,
            Point uvA, Point uvB, Point uvC, int matId)
        {
            FaceId = faceId; VA = va; VB = vb; VC = vc;
            FlatA  = flatA;  FlatB  = flatB;  FlatC  = flatC;
            FoldA  = foldA;  FoldB  = foldB;  FoldC  = foldC;
            FinalA = finalA; FinalB = finalB; FinalC = finalC;
            UV_A   = uvA;    UV_B   = uvB;    UV_C   = uvC;
            MaterialId = matId;
        }
    }

    private sealed class StepInfo
    {
        public required int StepIndex     { get; init; }
        public required int GroupId       { get; init; }
        public required int ParentGroupId { get; init; }
        public required int FaceCount     { get; init; }
    }

    // ── constants ─────────────────────────────────────────────────────────────
    private const double AnimDurationMs  = 1200;  // Phase 1: 0→600ms  Phase 2: 600→1200ms
    private const double PauseDurationMs = 500;
    private const double FoldPhaseEnd    = 0.5;   // t-fraction where Phase 1 ends

    // ── fields ────────────────────────────────────────────────────────────────
    private readonly PieceAnimData[]                         _pieceData;
    private readonly StepInfo[]                              _stepInfos;
    private readonly IReadOnlyDictionary<int, BitmapImage?>? _materialBitmaps;
    private readonly bool                                     _hasTexture;

    private readonly DispatcherTimer _timer;
    private DateTime _lastTick;
    private double   _animT          = 1.0;
    private double   _pauseRemaining = 0;
    private bool     _suppressStepRefresh = false;  // guard: timer sets step internally

    // ── observable properties ─────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepDescription))]
    [NotifyPropertyChangedFor(nameof(StepCountText))]
    [NotifyPropertyChangedFor(nameof(StepProgress))]
    [NotifyCanExecuteChangedFor(nameof(PrevStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToStartCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToEndCommand))]
    private int _currentStep = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseLabel))]
    private bool _isPlaying = false;

    // Called by source-generated setter when CurrentStep changes (e.g. Slider drag)
    partial void OnCurrentStepChanged(int value)
    {
        if (_suppressStepRefresh) return;
        _animT = 1.0;
        RefreshModel();
    }

    [ObservableProperty] private Model3DGroup? _assemblyModel;

    // ── computed properties ───────────────────────────────────────────────────
    public int    StepCount    => _stepInfos.Length;
    public int    StepMaxIndex => Math.Max(_stepInfos.Length - 1, 0);
    public double StepProgress => StepCount > 1 ? (double)CurrentStep / (StepCount - 1) * 100.0 : 100.0;
    public string PlayPauseLabel => IsPlaying ? "⏸ Pause" : "▶ Play";

    public string StepDescription
    {
        get
        {
            if (_stepInfos.Length == 0) return "No steps computed.";
            var s = _stepInfos[CurrentStep];
            return s.ParentGroupId < 0
                ? $"Step {CurrentStep + 1} / {_stepInfos.Length}  —  Place root piece #{s.GroupId} ({s.FaceCount} faces)"
                : $"Step {CurrentStep + 1} / {_stepInfos.Length}  —  Attach piece #{s.GroupId} ({s.FaceCount} faces) onto piece #{s.ParentGroupId}";
        }
    }

    public string StepCountText => $"{CurrentStep + 1} / {_stepInfos.Length}";

    // ── constructor ───────────────────────────────────────────────────────────

    public AssemblyViewModel(
        Mesh                                          mesh,
        UnfoldResult                                  unfoldResult,
        IReadOnlyList<PieceViewModel>                 pieces,
        double                                        scaleMmPerUnit,
        IReadOnlyDictionary<int, BitmapImage?>?       materialBitmaps = null)
    {
        _materialBitmaps = materialBitmaps;
        _hasTexture      = materialBitmaps?.Values.Any(b => b != null) == true;

        (_pieceData, _stepInfos) = BuildAssemblyData(mesh, unfoldResult, pieces, scaleMmPerUnit);

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)   // ≈ 60 fps
        };
        _timer.Tick += OnTimerTick;

        if (_stepInfos.Length > 0)
        {
            _animT = 0.0;
            RefreshModel();
        }
    }

    // ── commands ──────────────────────────────────────────────────────────────

    private bool CanGoBack()    => CurrentStep > 0;
    private bool CanGoForward() => CurrentStep < _stepInfos.Length - 1;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoToStart()
    {
        StopAnimation();
        CurrentStep = 0;
        _animT = 0.0;
        RefreshModel();
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void PrevStep()
    {
        StopAnimation();
        if (CurrentStep > 0) CurrentStep--;
        _animT = 1.0;
        RefreshModel();
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void NextStep()
    {
        StopAnimation();
        if (CurrentStep < _stepInfos.Length - 1) CurrentStep++;
        _animT = 0.0;
        RefreshModel();
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoToEnd()
    {
        StopAnimation();
        CurrentStep = _stepInfos.Length - 1;
        _animT = 1.0;
        RefreshModel();
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (IsPlaying) StopAnimation();
        else           StartAnimation();
    }

    // ── animation loop ────────────────────────────────────────────────────────

    private void StartAnimation()
    {
        if (CurrentStep >= _stepInfos.Length - 1 && _animT >= 1.0)
        {
            CurrentStep = 0;
            _animT = 0.0;
        }
        IsPlaying       = true;
        _pauseRemaining = 0;
        _lastTick       = DateTime.Now;
        _timer.Start();
    }

    private void StopAnimation()
    {
        IsPlaying = false;
        _timer.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var    now  = DateTime.Now;
        double dtMs = (now - _lastTick).TotalMilliseconds;
        _lastTick = now;

        if (_pauseRemaining > 0) { _pauseRemaining -= dtMs; return; }

        _animT += dtMs / AnimDurationMs;

        if (_animT >= 1.0)
        {
            _animT = 1.0;
            RefreshModel();

            if (CurrentStep < _stepInfos.Length - 1)
            {
                _pauseRemaining      = PauseDurationMs;
                _suppressStepRefresh = true;
                CurrentStep++;
                _suppressStepRefresh = false;
                _animT = 0.0;
            }
            else
            {
                StopAnimation();
            }
            return;
        }

        RefreshModel();
    }

    // ── model building ────────────────────────────────────────────────────────

    private void RefreshModel()
    {
        OnPropertyChanged(nameof(StepDescription));
        OnPropertyChanged(nameof(StepCountText));
        OnPropertyChanged(nameof(StepProgress));
        AssemblyModel = BuildFrame(CurrentStep, _animT);
    }

    /// Builds the 3-D Model3DGroup for the given step and raw animation progress t ∈ [0,1].
    private Model3DGroup BuildFrame(int stepIdx, double t)
    {
        var group = new Model3DGroup();

        // ── Split raw t into fold and fly sub-phases ──────────────────────────
        double tFold, tFly;
        if (t <= FoldPhaseEnd)
        {
            tFold = SmoothStep(t / FoldPhaseEnd);
            tFly  = 0.0;
        }
        else
        {
            tFold = 1.0;
            tFly  = SmoothStep((t - FoldPhaseEnd) / (1.0 - FoldPhaseEnd));
        }

        // Precompute fold transforms for the current piece (Phase 1 only)
        Dictionary<int, Matrix4x4>? foldTransforms = null;
        if (tFly == 0.0 && stepIdx < _pieceData.Length)
        {
            var cur = _pieceData[stepIdx];
            foldTransforms = ComputeFoldTransforms(cur.FoldTree, cur.FlatVertexPos, (float)tFold);
        }

        for (int i = 0; i < _pieceData.Length; i++)
        {
            var pd = _pieceData[i];
            if (pd.Tris.Length == 0) continue;

            if (pd.StepIndex > stepIdx)
                AppendGhost(group, pd);
            else if (pd.StepIndex < stepIdx)
                AppendStatic(group, pd, isAssembled: true);
            else
                AppendCurrent(group, pd, foldTransforms, tFold, tFly);
        }

        return group;
    }

    // ── ghost pieces (future) — translucent flat layout ───────────────────────

    private static void AppendGhost(Model3DGroup group, PieceAnimData pd)
    {
        var positions = new Point3DCollection(pd.Tris.Length * 3);
        var indices   = new Int32Collection(pd.Tris.Length * 3);
        int idx = 0;

        foreach (var tri in pd.Tris)
        {
            positions.Add(ToP3D(tri.FlatA));
            positions.Add(ToP3D(tri.FlatB));
            positions.Add(ToP3D(tri.FlatC));
            indices.Add(idx); indices.Add(idx + 1); indices.Add(idx + 2);
            idx += 3;
        }

        var geo = new MeshGeometry3D { Positions = positions, TriangleIndices = indices };
        var mat = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(30, 0xcc, 0xdd, 0xff)));
        group.Children.Add(new GeometryModel3D(geo, mat));
    }

    // ── assembled pieces — static, textured at final 3-D positions ───────────

    private void AppendStatic(Model3DGroup group, PieceAnimData pd, bool isAssembled)
    {
        foreach (var mg in pd.Tris.GroupBy(t => t.MaterialId))
        {
            var tris = mg;

            var (positions, indices, normals, uvCoords, bmp) =
                BuildGeometryBuffers(tris, pd, (tri) =>
                    isAssembled ? (tri.FinalA, tri.FinalB, tri.FinalC)
                                : (tri.FlatA,  tri.FlatB,  tri.FlatC));

            var geo  = MakeGeometry(positions, indices, normals, uvCoords);
            var mat  = MakeAssembledMaterial(bmp, uvCoords != null);
            var back = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(200, 0x40, 0x50, 0x78)));
            group.Children.Add(new GeometryModel3D(geo, mat) { BackMaterial = back });
        }
    }

    // ── current piece — animated (fold + fly), textured with amber glow ───────

    private void AppendCurrent(
        Model3DGroup               group,
        PieceAnimData              pd,
        Dictionary<int, Matrix4x4>? foldTransforms,
        double                     tFold,
        double                     tFly)
    {
        foreach (var mg in pd.Tris.GroupBy(t => t.MaterialId))
        {
            var tris = mg;

            var (positions, indices, normals, uvCoords, bmp) =
                BuildGeometryBuffers(tris, pd, (tri) =>
                {
                    Vector3 a, b, c;

                    if (tFly > 0.0)
                    {
                        // Phase 2: lerp fold shape → final 3-D position
                        a = LerpV(tri.FoldA,  tri.FinalA, tFly);
                        b = LerpV(tri.FoldB,  tri.FinalB, tFly);
                        c = LerpV(tri.FoldC,  tri.FinalC, tFly);
                    }
                    else if (foldTransforms != null &&
                             foldTransforms.TryGetValue(tri.FaceId, out var T))
                    {
                        // Phase 1: fold via accumulated transform
                        a = Vector3.Transform(tri.FlatA, T);
                        b = Vector3.Transform(tri.FlatB, T);
                        c = Vector3.Transform(tri.FlatC, T);
                    }
                    else
                    {
                        // Fallback: flat positions
                        a = tri.FlatA; b = tri.FlatB; c = tri.FlatC;
                    }

                    return (a, b, c);
                });

            var geo  = MakeGeometry(positions, indices, normals, uvCoords);
            var mat  = MakeCurrentMaterial(bmp, uvCoords != null);
            var back = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(255, 0xcc, 0x88, 0x10)));
            group.Children.Add(new GeometryModel3D(geo, mat) { BackMaterial = back });
        }
    }

    // ── geometry helpers ──────────────────────────────────────────────────────

    private (Point3DCollection positions,
             Int32Collection   indices,
             Vector3DCollection normals,
             PointCollection?  uvCoords,
             BitmapImage?      bmp)
        BuildGeometryBuffers(
            IEnumerable<TriData>                      tris,
            PieceAnimData                             pd,
            Func<TriData, (Vector3 a, Vector3 b, Vector3 c)> getPos)
    {
        var triList   = tris.ToList();
        var positions = new Point3DCollection(triList.Count * 3);
        var indices   = new Int32Collection(triList.Count * 3);
        var normals   = new Vector3DCollection(triList.Count * 3);

        // Resolve texture for this material group
        int matId = triList.Count > 0 ? triList[0].MaterialId : -1;
        BitmapImage? bmp = null;
        if (_hasTexture && _materialBitmaps != null)
        {
            _materialBitmaps.TryGetValue(matId, out bmp);
            bmp ??= _materialBitmaps.Values.FirstOrDefault(b => b != null);
        }

        bool        useUV    = bmp != null && pd.HasUVs;
        PointCollection? uvCoords = useUV ? new PointCollection(triList.Count * 3) : null;

        int idx = 0;
        foreach (var tri in triList)
        {
            var (a, b, c) = getPos(tri);

            positions.Add(ToP3D(a));
            positions.Add(ToP3D(b));
            positions.Add(ToP3D(c));
            indices.Add(idx); indices.Add(idx + 1); indices.Add(idx + 2);

            var n = Vector3.Cross(b - a, c - a);
            float nl = n.Length();
            if (nl > 1e-12f) n /= nl;
            normals.Add(ToV3D(n)); normals.Add(ToV3D(n)); normals.Add(ToV3D(n));

            uvCoords?.Add(tri.UV_A);
            uvCoords?.Add(tri.UV_B);
            uvCoords?.Add(tri.UV_C);
            idx += 3;
        }

        return (positions, indices, normals, uvCoords, bmp);
    }

    private static MeshGeometry3D MakeGeometry(
        Point3DCollection  positions,
        Int32Collection    indices,
        Vector3DCollection normals,
        PointCollection?   uvCoords)
    {
        var geo = new MeshGeometry3D
        {
            Positions       = positions,
            TriangleIndices = indices,
            Normals         = normals
        };
        if (uvCoords != null) geo.TextureCoordinates = uvCoords;
        return geo;
    }

    /// Assembled piece material: texture if available, else neutral blue-grey.
    private static Material MakeAssembledMaterial(BitmapImage? bmp, bool hasUV)
    {
        if (bmp != null && hasUV)
            return new DiffuseMaterial(MakeImageBrush(bmp));

        return new DiffuseMaterial(
            new SolidColorBrush(Color.FromArgb(220, 0x80, 0x98, 0xc8)));
    }

    /// Current piece material: texture + amber emissive overlay if textured,
    /// else solid amber.
    private static Material MakeCurrentMaterial(BitmapImage? bmp, bool hasUV)
    {
        if (bmp != null && hasUV)
        {
            var mg = new MaterialGroup();
            mg.Children.Add(new DiffuseMaterial(MakeImageBrush(bmp)));
            // Semi-transparent amber glow so texture is still visible
            mg.Children.Add(new EmissiveMaterial(
                new SolidColorBrush(Color.FromArgb(90, 0xff, 0xcc, 0x00))));
            return mg;
        }

        return new DiffuseMaterial(
            new SolidColorBrush(Color.FromArgb(255, 0xff, 0xcc, 0x30)));
    }

    private static ImageBrush MakeImageBrush(BitmapImage bmp) => new(bmp)
    {
        ViewportUnits = BrushMappingMode.Absolute,
        Viewport      = new Rect(0, 0, 1, 1),
        TileMode      = TileMode.Tile,
        Stretch       = Stretch.Fill
    };

    // ── fold transforms ───────────────────────────────────────────────────────

    /// <summary>
    /// Computes per-face accumulated transform matrices at fold progress <paramref name="tFold"/> ∈ [0,1].
    ///
    /// Convention (System.Numerics row-major): "apply A then B" = A * B.
    /// Rotation around point P: T(-P) * R * T(+P).
    /// </summary>
    private static Dictionary<int, Matrix4x4> ComputeFoldTransforms(
        PieceFoldTree                     tree,
        IReadOnlyDictionary<int, Vector3> flatVertexPos,
        float                             tFold)
    {
        var transforms = new Dictionary<int, Matrix4x4>(tree.BFSOrder.Count);
        transforms[tree.Root.FaceId] = Matrix4x4.Identity;

        foreach (var node in tree.BFSOrder.Skip(1))
        {
            // Orphaned face (shouldn't occur in valid meshes)
            if (node.SharedEdgeVA < 0)
            {
                transforms[node.FaceId] =
                    transforms.GetValueOrDefault(node.ParentFaceId, Matrix4x4.Identity);
                continue;
            }

            if (!transforms.TryGetValue(node.ParentFaceId, out var parentT))
                parentT = Matrix4x4.Identity;

            var flatA = flatVertexPos.GetValueOrDefault(node.SharedEdgeVA, Vector3.Zero);
            var flatB = flatVertexPos.GetValueOrDefault(node.SharedEdgeVB, Vector3.Zero);

            // Fold edge in world space (after parent's accumulated transform)
            var worldA   = Vector3.Transform(flatA, parentT);
            var worldB   = Vector3.Transform(flatB, parentT);
            var axisVec  = worldB - worldA;
            float axisLen = axisVec.Length();

            if (axisLen < 1e-7f)
            {
                transforms[node.FaceId] = parentT;
                continue;
            }

            var  axis  = axisVec / axisLen;

            // TargetTheta was signed using the 3-D mesh edge direction (VA→VB).
            // After parent accumulated transform the flat-space axis may be
            // antiparallel to that 3-D direction → flip the angle sign to match.
            float signCorr = Vector3.Dot(axis, node.EdgeDir3D) >= 0f ? 1f : -1f;
            var  q     = System.Numerics.Quaternion.CreateFromAxisAngle(axis, signCorr * node.TargetTheta * tFold);
            var  R     = Matrix4x4.CreateFromQuaternion(q);

            // Rotation around worldA:  T(-worldA) * R * T(+worldA)
            var R_adj = Matrix4x4.CreateTranslation(-worldA)
                      * R
                      * Matrix4x4.CreateTranslation( worldA);

            transforms[node.FaceId] = parentT * R_adj;
        }

        return transforms;
    }

    // ── static helpers ────────────────────────────────────────────────────────

    private static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    private static Vector3 LerpV(Vector3 from, Vector3 to, double t) =>
        from + (float)t * (to - from);

    private static Point3D   ToP3D(Vector3 v) => new(v.X, v.Y, v.Z);
    private static Vector3D  ToV3D(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Point ToWpfUV(Mesh mesh, int idx)
    {
        if (idx < 0 || idx >= mesh.UVs.Count) return default;
        var uv = mesh.UVs[idx];
        return new Point(uv.X, 1.0 - uv.Y);
    }

    // ── assembly data builder ─────────────────────────────────────────────────

    private static (PieceAnimData[], StepInfo[]) BuildAssemblyData(
        Mesh                          mesh,
        UnfoldResult                  unfoldResult,
        IReadOnlyList<PieceViewModel> pieces,
        double                        scaleMmPerUnit)
    {
        if (pieces.Count == 0 || scaleMmPerUnit <= 0) return ([], []);

        // 1. Determine assembly order via AssemblyPlanner
        var pieceGroups = pieces
            .Select(p => (p.GroupId, p.Faces.Select(f => f.FaceId).ToArray()))
            .ToArray();
        var steps = AssemblyPlanner.Build(mesh, pieceGroups);
        if (steps.Count == 0) return ([], []);

        // 2. UnfoldedFace lookup (faceId → UnfoldedFace)
        var unfoldMap = unfoldResult.Faces.ToDictionary(f => f.FaceId);

        // 3. Flat-plane parameters (centre the unfolded layout under the 3-D model)
        float modelMinY = mesh.Vertices.Count > 0 ? mesh.Vertices.Min(v => v.Position.Y) : 0f;
        float modelMaxY = mesh.Vertices.Count > 0 ? mesh.Vertices.Max(v => v.Position.Y) : 1f;
        float modelCx   = mesh.Vertices.Count > 0 ? mesh.Vertices.Average(v => v.Position.X) : 0f;
        float modelCz   = mesh.Vertices.Count > 0 ? mesh.Vertices.Average(v => v.Position.Z) : 0f;
        float modelH    = Math.Max(modelMaxY - modelMinY, 0.001f);
        float baseY     = modelMinY - modelH * 0.7f;  // flat plane well below model

        double sumX = 0, sumZ = 0; int vtxN = 0;
        foreach (var uf in unfoldResult.Faces)
        {
            sumX += uf.V0.X + uf.V1.X + uf.V2.X;
            sumZ += uf.V0.Y + uf.V1.Y + uf.V2.Y;
            vtxN += 3;
        }
        double patCx = vtxN > 0 ? sumX / vtxN / scaleMmPerUnit : 0;
        double patCz = vtxN > 0 ? sumZ / vtxN / scaleMmPerUnit : 0;

        Vector3 ToFlatV(float u, float v) => new(
            (float)(u / scaleMmPerUnit - patCx + modelCx),
            baseY,
            (float)(v / scaleMmPerUnit - patCz + modelCz));

        // 4. Build per-step data
        var pieceDataList = new List<PieceAnimData>(steps.Count);
        var stepInfoList  = new List<StepInfo>(steps.Count);

        foreach (var step in steps)
        {
            var faceIds = step.FaceIds.ToArray();

            // 4a. Flat vertex positions (mesh vertex ID → Vector3 on baseY plane)
            var flatVertexPos = new Dictionary<int, Vector3>(faceIds.Length * 3);
            foreach (var fid in faceIds)
            {
                if (!unfoldMap.TryGetValue(fid, out var uf)) continue;
                var mf = mesh.Faces[fid];
                flatVertexPos[mf.A] = ToFlatV(uf.V0.X, uf.V0.Y);
                flatVertexPos[mf.B] = ToFlatV(uf.V1.X, uf.V1.Y);
                flatVertexPos[mf.C] = ToFlatV(uf.V2.X, uf.V2.Y);
            }

            // 4b. Build fold spanning tree
            var foldTree = PieceFoldTree.Build(mesh, faceIds);

            // 4c. Pre-compute fold positions at t=1.0 (fully folded, at flat plane)
            var foldTransformsAtOne = ComputeFoldTransforms(foldTree, flatVertexPos, 1.0f);

            // 4d. Per-face TriData
            bool pieceHasUVs = false;
            var  triList     = new List<TriData>(faceIds.Length);

            foreach (var fid in faceIds)
            {
                if (fid < 0 || fid >= mesh.Faces.Count) continue;
                var mf  = mesh.Faces[fid];
                var va3 = mesh.Vertices[mf.A].Position;
                var vb3 = mesh.Vertices[mf.B].Position;
                var vc3 = mesh.Vertices[mf.C].Position;

                // Flat positions
                var flatA = flatVertexPos.GetValueOrDefault(mf.A, va3);
                var flatB = flatVertexPos.GetValueOrDefault(mf.B, vb3);
                var flatC = flatVertexPos.GetValueOrDefault(mf.C, vc3);

                // Fold positions at t=1
                Vector3 foldA, foldB, foldC;
                if (foldTransformsAtOne.TryGetValue(fid, out var fT))
                {
                    foldA = Vector3.Transform(flatA, fT);
                    foldB = Vector3.Transform(flatB, fT);
                    foldC = Vector3.Transform(flatC, fT);
                }
                else { foldA = flatA; foldB = flatB; foldC = flatC; }

                // UV coords (from mesh UV table, same as BuildWpfModel)
                Point uvA = default, uvB = default, uvC = default;
                if (mesh.HasUVs && fid < mesh.FaceUVs.Count)
                {
                    var (ua, ub, uc) = mesh.FaceUVs[fid];
                    uvA = ToWpfUV(mesh, ua);
                    uvB = ToWpfUV(mesh, ub);
                    uvC = ToWpfUV(mesh, uc);
                    if (ua >= 0 || ub >= 0 || uc >= 0) pieceHasUVs = true;
                }

                triList.Add(new TriData(
                    fid, mf.A, mf.B, mf.C,
                    flatA,  flatB,  flatC,
                    foldA,  foldB,  foldC,
                    va3,    vb3,    vc3,
                    uvA, uvB, uvC, mf.MaterialId));
            }

            pieceDataList.Add(new PieceAnimData
            {
                GroupId      = step.GroupId,
                StepIndex    = step.StepIndex,
                HasUVs       = pieceHasUVs,
                FoldTree     = foldTree,
                FlatVertexPos = flatVertexPos,
                Tris         = [.. triList]
            });

            stepInfoList.Add(new StepInfo
            {
                StepIndex     = step.StepIndex,
                GroupId       = step.GroupId,
                ParentGroupId = step.ParentGroupId,
                FaceCount     = step.FaceIds.Count
            });
        }

        return ([.. pieceDataList], [.. stepInfoList]);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
