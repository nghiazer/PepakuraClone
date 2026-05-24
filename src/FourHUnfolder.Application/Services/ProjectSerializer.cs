using System.IO.Compression;
using System.Text.Json;
using FourHUnfolder.Domain.Persistence;

namespace FourHUnfolder.Application.Services;

/// <summary>
/// Saves and loads a <see cref="ProjectState"/> to/from a .pmc JSON file or a self-contained .4hu ZIP bundle.
/// </summary>
public class ProjectSerializer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    // ── .4hu bundle constants ─────────────────────────────────────────────────
    public const string BundleExtension = ".4hu";
    private const string StateEntry     = "state.json";
    private const string MeshEntry      = "mesh.obj";
    private const string TexturePrefix  = "texture";

    // ── .4hu: save self-contained bundle ─────────────────────────────────────

    /// <summary>
    /// Creates a .4hu ZIP bundle: embeds the mesh file, optional per-material textures,
    /// and project state.  The resulting file is binary — not readable by text editors.
    /// </summary>
    public void SaveBundle(ProjectState state, string meshPath, string? texturePath,
                           string outputPath,
                           IReadOnlyDictionary<int, string?>? perMaterialTextures = null)
    {
        if (!File.Exists(meshPath))
            throw new FileNotFoundException($"Mesh file not found: {meshPath}");

        var textureExt = string.IsNullOrEmpty(texturePath)
            ? null
            : Path.GetExtension(texturePath).TrimStart('.').ToLowerInvariant();

        var copy = Clone(state);
        copy.MeshPath                = null;
        copy.TexturePath             = null;
        copy.MaterialTexturePaths    = new();        // cleared; paths are embedded as files
        copy.BundledTextureExt       = textureExt;
        copy.BundledMaterialTextureExts = new();

        // TD-22-2: record extension for each per-material texture that we're embedding
        if (perMaterialTextures != null)
        {
            foreach (var (matId, path) in perMaterialTextures)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path!))
                {
                    var ext = Path.GetExtension(path!).TrimStart('.').ToLowerInvariant();
                    copy.BundledMaterialTextureExts[matId] = ext;
                }
                else
                {
                    copy.BundledMaterialTextureExts[matId] = null;
                }
            }
        }

        using var fs      = File.Create(outputPath);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        // state.json
        var stateEntry = archive.CreateEntry(StateEntry, CompressionLevel.Fastest);
        using (var sw = new StreamWriter(stateEntry.Open()))
            sw.Write(JsonSerializer.Serialize(copy, JsonOpts));

        // mesh.obj
        AddFileToArchive(archive, meshPath, MeshEntry);

        // fallback / legacy single texture (optional)
        if (!string.IsNullOrEmpty(texturePath) && !string.IsNullOrEmpty(textureExt) && File.Exists(texturePath))
            AddFileToArchive(archive, texturePath, $"{TexturePrefix}.{textureExt}");

        // TD-22-2: per-material textures embedded as texture_<matId>.<ext>
        if (perMaterialTextures != null)
        {
            foreach (var (matId, path) in perMaterialTextures)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path!))
                {
                    var ext = Path.GetExtension(path!).TrimStart('.').ToLowerInvariant();
                    AddFileToArchive(archive, path!, $"{TexturePrefix}_{matId}.{ext}");
                }
            }
        }
    }

    private static void AddFileToArchive(ZipArchive archive, string sourcePath, string entryName)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var src  = File.OpenRead(sourcePath);
        using var dest = entry.Open();
        src.CopyTo(dest);
    }

    // ── .4hu: load bundle ─────────────────────────────────────────────────────

    /// <summary>
    /// Loads a .4hu bundle by extracting embedded files to a temporary directory.
    /// The caller owns <paramref name="tempDirOut"/> and must delete it when finished.
    /// </summary>
    public ProjectState LoadBundle(string inputPath, out string tempDirOut)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "4hu_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        tempDirOut = tempDir;

        using (var archive = ZipFile.OpenRead(inputPath))
            archive.ExtractToDirectory(tempDir, overwriteFiles: true);

        var statePath = Path.Combine(tempDir, StateEntry);
        if (!File.Exists(statePath))
            throw new InvalidDataException("Invalid .4hu file: missing state.json.");

        var state = JsonSerializer.Deserialize<ProjectState>(File.ReadAllText(statePath), JsonOpts)
                    ?? throw new InvalidDataException("Invalid .4hu file: state.json could not be parsed.");

        if (state.Version > CurrentVersion)
            throw new InvalidDataException(
                $"Project was saved by a newer version (file v{state.Version}, supported up to v{CurrentVersion}).");

        state.MeshPath = File.Exists(Path.Combine(tempDir, MeshEntry))
            ? Path.Combine(tempDir, MeshEntry)
            : null;

        // Fallback / legacy single texture
        if (!string.IsNullOrEmpty(state.BundledTextureExt))
        {
            var texPath = Path.Combine(tempDir, $"{TexturePrefix}.{state.BundledTextureExt}");
            state.TexturePath = File.Exists(texPath) ? texPath : null;
        }

        // TD-22-2: restore per-material texture paths from embedded files
        state.MaterialTexturePaths = new();
        foreach (var (matId, ext) in state.BundledMaterialTextureExts)
        {
            if (!string.IsNullOrEmpty(ext))
            {
                var texPath = Path.Combine(tempDir, $"{TexturePrefix}_{matId}.{ext}");
                state.MaterialTexturePaths[matId] = File.Exists(texPath) ? texPath : null;
            }
            else
            {
                state.MaterialTexturePaths[matId] = null;
            }
        }

        if (state.MeshPath == null)
            state.Warnings.Add("Embedded mesh not found in project bundle.");

        return state;
    }

    // ── .pmc: legacy JSON format ──────────────────────────────────────────────

    public void Save(ProjectState state, string filePath)
    {
        var dir  = Path.GetDirectoryName(filePath) ?? string.Empty;
        var copy = Clone(state);

        // Relativize paths
        copy.MeshPath    = Relativize(state.MeshPath,    dir);
        copy.TexturePath = Relativize(state.TexturePath, dir);

        // TD-22-2: relativize per-material texture paths
        copy.MaterialTexturePaths = new();
        foreach (var (matId, path) in state.MaterialTexturePaths)
            copy.MaterialTexturePaths[matId] = Relativize(path, dir);

        var json = JsonSerializer.Serialize(copy, JsonOpts);
        File.WriteAllText(filePath, json);
    }

    public const int CurrentVersion = 2;

    public ProjectState Load(string filePath)
    {
        var json  = File.ReadAllText(filePath);
        var state = JsonSerializer.Deserialize<ProjectState>(json, JsonOpts)
                    ?? throw new InvalidDataException("Invalid project file.");

        if (state.Version > CurrentVersion)
            throw new InvalidDataException(
                $"Project was saved by a newer version of 4H-Unfolder (file version {state.Version}, " +
                $"supported up to {CurrentVersion}). Please update the application.");

        var dir = Path.GetDirectoryName(filePath) ?? string.Empty;

        // Resolve paths — record a warning if a saved path can no longer be found
        string? rawMesh    = state.MeshPath;
        string? rawTexture = state.TexturePath;
        state.MeshPath    = Resolve(state.MeshPath,    dir);
        state.TexturePath = Resolve(state.TexturePath, dir);

        if (!string.IsNullOrEmpty(rawMesh)    && state.MeshPath    == null)
            state.Warnings.Add($"Mesh file not found: {rawMesh}");
        if (!string.IsNullOrEmpty(rawTexture) && state.TexturePath == null)
            state.Warnings.Add($"Texture file not found: {rawTexture}");

        // TD-22-2: resolve per-material texture paths
        var resolvedMat = new Dictionary<int, string?>();
        foreach (var (matId, encoded) in state.MaterialTexturePaths)
            resolvedMat[matId] = Resolve(encoded, dir);
        state.MaterialTexturePaths = resolvedMat;

        return state;
    }

    // ── path helpers ─────────────────────────────────────────────────────────

    /// Stores "relative|absolute" so either can be used when the folder moves.
    private static string? Relativize(string? path, string baseDir)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            var rel = Path.GetRelativePath(baseDir, path);
            return $"{rel}|{path}";
        }
        catch { return $"|{path}"; }
    }

    private static string? Resolve(string? encoded, string baseDir)
    {
        if (string.IsNullOrEmpty(encoded)) return null;

        var sep = encoded.IndexOf('|');
        if (sep < 0) return TryExist(encoded, baseDir);

        var rel = encoded[..sep];
        var abs = encoded[(sep + 1)..];

        if (!string.IsNullOrEmpty(rel))
        {
            var full = Path.GetFullPath(Path.Combine(baseDir, rel));
            if (File.Exists(full)) return full;
        }
        return File.Exists(abs) ? abs : null;
    }

    private static string? TryExist(string path, string baseDir)
    {
        if (File.Exists(path)) return path;
        var full = Path.GetFullPath(Path.Combine(baseDir, path));
        return File.Exists(full) ? full : null;
    }

    private static ProjectState Clone(ProjectState s)
    {
        var json = JsonSerializer.Serialize(s, JsonOpts);
        return JsonSerializer.Deserialize<ProjectState>(json, JsonOpts)!;
    }
}
