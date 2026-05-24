using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FourHUnfolder.App.ViewModels;

/// <summary>
/// Drives the ModelOrientationDialog — lets the user choose which axis of the
/// loaded mesh should point upward (+Y in world space) and which should point
/// toward the camera (+Z in world space), plus an optional UV vertical flip.
/// </summary>
public partial class ModelOrientationViewModel : ObservableObject
{
    // ── available axis options ────────────────────────────────────────────────
    public IReadOnlyList<string> AxisOptions { get; } =
        ["+X", "-X", "+Y", "-Y", "+Z", "-Z"];

    // ── selections ────────────────────────────────────────────────────────────

    /// Which axis of the mesh model should face upward in the world (+Y).
    [ObservableProperty] private string _upAxis = "+Y";

    /// Which axis of the mesh model should face forward / toward the camera (+Z).
    [ObservableProperty] private string _frontAxis = "+Z";

    /// When true, flip the V component of all UV coords (mirrors texture vertically).
    [ObservableProperty] private bool _flipUV = false;

    // ── computed transform ────────────────────────────────────────────────────

    /// <summary>
    /// Returns the rotation Matrix4x4 (System.Numerics, row-major) that re-orients
    /// the mesh so that the user's chosen up-axis maps to world +Y and the user's
    /// chosen front-axis maps to world +Z.
    /// If up and front are parallel (invalid), returns Identity.
    /// </summary>
    public Matrix4x4 ComputeRotation()
    {
        var up    = ParseAxis(UpAxis);
        var front = ParseAxis(FrontAxis);

        // Reject degenerate input (parallel axes)
        var cross = Vector3.Cross(front, up);
        if (cross.LengthSquared() < 1e-6f)
            return Matrix4x4.Identity;

        // Orthonormal basis: right = front × up, then re-derive up so it's truly perpendicular
        var right  = Vector3.Normalize(cross);
        var upOrtho = Vector3.Normalize(Vector3.Cross(right, front));

        // We want the rotation R such that:
        //   R * right  = (1, 0, 0)  [→ world +X]
        //   R * upOrtho = (0, 1, 0) [→ world +Y]
        //   R * front  = (0, 0, 1)  [→ world +Z]
        //
        // That is:  new_X = dot(v, right),  new_Y = dot(v, up),  new_Z = dot(v, front)
        //
        // In row-major System.Numerics layout:
        //   V3.Transform(v, M) = (v.X*M11 + v.Y*M21 + v.Z*M31, ...)
        // So M[col] = [right | up | front] written into rows 1-3:
        return new Matrix4x4(
            right.X,   upOrtho.X, front.X,  0f,
            right.Y,   upOrtho.Y, front.Y,  0f,
            right.Z,   upOrtho.Z, front.Z,  0f,
            0f,        0f,        0f,        1f);
    }

    /// <summary>True if no orientation change is needed (up=+Y and front=+Z).</summary>
    public bool IsIdentity =>
        UpAxis == "+Y" && FrontAxis == "+Z" && !FlipUV;

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Vector3 ParseAxis(string s) => s switch
    {
        "+X" => new Vector3( 1, 0, 0),
        "-X" => new Vector3(-1, 0, 0),
        "+Y" => new Vector3( 0, 1, 0),
        "-Y" => new Vector3( 0,-1, 0),
        "+Z" => new Vector3( 0, 0, 1),
        "-Z" => new Vector3( 0, 0,-1),
        _    => Vector3.UnitY
    };
}
