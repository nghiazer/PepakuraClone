using FluentAssertions;
using FourHUnfolder.Infrastructure.Loaders;
using Xunit;

namespace FourHUnfolder.Tests;

/// <summary>
/// Integration smoke-tests for PdoMeshLoader against the 3 sample PDO files
/// in the project root.  Tests run only when the files are present (CI may skip).
/// </summary>
public class PdoMeshLoaderTests
{
    private static readonly string ProjectRoot =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));  // tests/Project/bin/Release/net8.0 → repo root

    private static string Pdo(string name) => Path.Combine(ProjectRoot, name);

    private static bool FileExists(string path) => File.Exists(path);

    // ── helpers ───────────────────────────────────────────────────────────

    private static void AssertValidMesh(string filePath)
    {
        if (!FileExists(filePath))
            return;  // skip if file not present (e.g. CI without large binaries)

        var loader = new PdoMeshLoader();
        var mesh   = loader.Load(filePath);

        mesh.Should().NotBeNull();
        mesh.Vertices.Count.Should().BePositive($"{filePath} should have vertices");
        mesh.Faces.Count.Should().BePositive($"{filePath} should have faces");
        mesh.Edges.Count.Should().BePositive($"{filePath} should have edges");

        // All face vertex indices must be valid
        foreach (var f in mesh.Faces)
        {
            f.A.Should().BeInRange(0, mesh.Vertices.Count - 1);
            f.B.Should().BeInRange(0, mesh.Vertices.Count - 1);
            f.C.Should().BeInRange(0, mesh.Vertices.Count - 1);
        }
    }

    // ── tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void Load_SoundEmitter_ReturnsValidMesh()
    {
        var path = Pdo("SoundEmitterObject.pdo");
        AssertValidMesh(path);

        if (!FileExists(path)) return;

        var mesh = new PdoMeshLoader().Load(path);
        // geos: speaker(36) + speaker__2_(36) + speaker__3_(36) + speaker_foot(24) + speaker_source(36) = 168
        mesh.Vertices.Count.Should().Be(168,
            "SoundEmitterObject has 5 geos with 36+36+36+24+36 = 168 vertices total");
        // Actual: 4 bodies (36 each) + foot (24) + source (36) = 4*36+24+36 = 204
        mesh.Faces.Count.Should().BePositive();
    }

    [Fact]
    public void Load_Waluigi_ReturnsValidMesh()
        => AssertValidMesh(Pdo("waluigiblimp.pdo"));

    [Fact]
    public void Load_Pillar_ReturnsValidMesh()
        => AssertValidMesh(Pdo("Pillar.pdo"));

    [Fact]
    public void Load_InvalidSignature_ThrowsInvalidDataException()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, new byte[200]);  // zeros — not "version 3\n"
            var loader = new PdoMeshLoader();
            var act    = () => loader.Load(tmp);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*Not a PDO v3*");
        }
        finally { File.Delete(tmp); }
    }
}
