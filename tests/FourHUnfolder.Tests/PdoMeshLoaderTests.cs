using FluentAssertions;
using FourHUnfolder.Infrastructure.Loaders;
using Xunit;

namespace FourHUnfolder.Tests;

/// <summary>
/// Integration smoke-tests for PdoMeshLoader against the 3 sample PDO files
/// in the project root.  Tests silently skip if the files are absent (CI).
/// </summary>
public class PdoMeshLoaderTests
{
    private static readonly string ProjectRoot =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));  // tests/Project/bin/Release/net8.0 → repo root

    private static string Pdo(string name) => Path.Combine(ProjectRoot, name);
    private static bool   Exists(string p)  => File.Exists(p);

    // ── helpers ───────────────────────────────────────────────────────────

    private static void AssertValidMesh(string path)
    {
        if (!Exists(path)) return;  // skip in CI

        var mesh = new PdoMeshLoader().Load(path);

        mesh.Should().NotBeNull();
        mesh.Vertices.Count.Should().BePositive($"vertices in {Path.GetFileName(path)}");
        mesh.Faces.Count.Should().BePositive($"faces in {Path.GetFileName(path)}");
        mesh.Edges.Count.Should().BePositive($"edges in {Path.GetFileName(path)}");

        // All face vertex indices must be in-bounds
        foreach (var f in mesh.Faces)
        {
            f.A.Should().BeInRange(0, mesh.Vertices.Count - 1);
            f.B.Should().BeInRange(0, mesh.Vertices.Count - 1);
            f.C.Should().BeInRange(0, mesh.Vertices.Count - 1);
        }

        // Every face's UV indices must be valid (Phase 2: UVs extracted)
        if (mesh.HasUVs)
        {
            mesh.UVs.Count.Should().BePositive("UV list should be non-empty when HasUVs");
            foreach (var (ua, ub, uc) in mesh.FaceUVs)
            {
                ua.Should().BeInRange(0, mesh.UVs.Count - 1, "face UV A in-bounds");
                ub.Should().BeInRange(0, mesh.UVs.Count - 1, "face UV B in-bounds");
                uc.Should().BeInRange(0, mesh.UVs.Count - 1, "face UV C in-bounds");
            }

            // UV values must be finite (NaN/Inf indicate a parse error)
            foreach (var uv in mesh.UVs)
            {
                float.IsFinite(uv.X).Should().BeTrue("UV.X must be finite");
                float.IsFinite(uv.Y).Should().BeTrue("UV.Y must be finite");
            }
        }
    }

    // ── SoundEmitter ─────────────────────────────────────────────────────

    [Fact]
    public void SoundEmitter_ReturnsValidMesh()
    {
        var path = Pdo("SoundEmitterObject.pdo");
        AssertValidMesh(path);

        if (!Exists(path)) return;
        var mesh = new PdoMeshLoader().Load(path);

        // 5 geos: speaker(36) + speaker__2_(36) + speaker__3_(36) + speaker_foot(24) + speaker_source(36)
        mesh.Vertices.Count.Should().Be(168, "168 vertices across 5 geos");
        mesh.HasUVs.Should().BeTrue("SoundEmitter has a texture, so UVs should be populated");
    }

    [Fact]
    public void SoundEmitter_HasEmbeddedTexture()
    {
        var path = Pdo("SoundEmitterObject.pdo");
        if (!Exists(path)) return;

        var mesh = new PdoMeshLoader().Load(path);

        mesh.EmbeddedTextures.Should().HaveCount(1);
        var tex = mesh.EmbeddedTextures[0];
        tex.Name.Should().Be("soundemitter");
        tex.Width.Should().Be(128);
        tex.Height.Should().Be(128);
        tex.Rgb24Bytes.Should().HaveCount(128 * 128 * 3, "128×128 × 3 RGB bytes");
    }

    // ── Waluigi ──────────────────────────────────────────────────────────

    [Fact]
    public void Waluigi_ReturnsValidMesh()
        => AssertValidMesh(Pdo("waluigiblimp.pdo"));

    [Fact]
    public void Waluigi_HasEmbeddedTexture()
    {
        var path = Pdo("waluigiblimp.pdo");
        if (!Exists(path)) return;

        var mesh = new PdoMeshLoader().Load(path);
        mesh.EmbeddedTextures.Should().HaveCount(1);
        var tex = mesh.EmbeddedTextures[0];
        tex.Width.Should().Be(2048);
        tex.Height.Should().Be(2048);
        tex.Rgb24Bytes.Should().HaveCount(2048 * 2048 * 3);
    }

    // ── Pillar ───────────────────────────────────────────────────────────

    [Fact]
    public void Pillar_ReturnsValidMesh()
        => AssertValidMesh(Pdo("Pillar.pdo"));

    [Fact]
    public void Pillar_HasTwoEmbeddedTextures()
    {
        var path = Pdo("Pillar.pdo");
        if (!Exists(path)) return;

        var mesh = new PdoMeshLoader().Load(path);
        mesh.EmbeddedTextures.Should().HaveCount(2, "Pillar has 2 material textures");
        mesh.EmbeddedTextures[0].Width.Should().Be(2048);
        mesh.EmbeddedTextures[0].Height.Should().Be(2048);
        mesh.EmbeddedTextures[1].Width.Should().Be(1440);
        mesh.EmbeddedTextures[1].Height.Should().Be(2880);
    }

    // ── Phase B: material ID + UV-flip tests ─────────────────────────────

    [Fact]
    public void SoundEmitter_AllFacesHaveMaterialId0()
    {
        var path = Pdo("SoundEmitterObject.pdo");
        if (!Exists(path)) return;

        var mesh = new PdoMeshLoader().Load(path);

        // unk11=0 for every shape → all triangles must carry MaterialId=0
        mesh.Faces.Should().OnlyContain(f => f.MaterialId == 0,
            "SoundEmitter has one texture; every shape has unk11=0");
    }

    [Fact]
    public void SoundEmitter_MaterialNamesMatchEmbeddedTexture()
    {
        var path = Pdo("SoundEmitterObject.pdo");
        if (!Exists(path)) return;

        var mesh = new PdoMeshLoader().Load(path);

        mesh.MaterialNames.Should().HaveCount(1,
            "one embedded texture → one material name");
        mesh.MaterialNames[0].Should().Be("soundemitter");
        mesh.MaterialTexturePaths.Should().HaveCount(1);
        mesh.MaterialTexturePaths[0].Should().BeNull("embedded texture has no file path");
    }

    [Fact]
    public void Pillar_FacesHaveBothMaterialIds()
    {
        var path = Pdo("Pillar.pdo");
        if (!Exists(path)) return;

        var mesh = new PdoMeshLoader().Load(path);

        // Probe confirmed: unk11=0 (12 shapes) and unk11=1 (240 shapes)
        var ids = mesh.Faces.Select(f => f.MaterialId).Distinct().Order().ToList();
        ids.Should().Equal(new[] { 0, 1 }, "Pillar has 2 materials (unk11 values 0 and 1)");

        mesh.MaterialNames.Should().HaveCount(2, "two embedded textures → two material names");
    }

    [Fact]
    public void AllFiles_UVsAreFiniteAfterFlip()
    {
        // After Y-flip (v = 1-v), UVs may go outside [0,1] for tiling textures
        // but must always be finite (no NaN/Inf).
        foreach (var name in new[] { "SoundEmitterObject.pdo", "waluigiblimp.pdo", "Pillar.pdo" })
        {
            var path = Pdo(name);
            if (!Exists(path)) continue;

            var mesh = new PdoMeshLoader().Load(path);
            foreach (var uv in mesh.UVs)
            {
                float.IsFinite(uv.X).Should().BeTrue($"UV.X finite in {name}");
                float.IsFinite(uv.Y).Should().BeTrue($"UV.Y finite in {name} after Y-flip");
            }
        }
    }

    // ── Invalid signature guard ──────────────────────────────────────────

    [Fact]
    public void InvalidSignature_ThrowsInvalidDataException()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, new byte[200]);
            var act = () => new PdoMeshLoader().Load(tmp);
            act.Should().Throw<InvalidDataException>().WithMessage("*Not a PDO v3*");
        }
        finally { File.Delete(tmp); }
    }
}
