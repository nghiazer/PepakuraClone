using System.Text.Json;
using FourHUnfolder.Domain.Persistence;

namespace FourHUnfolder.Application.Services;

/// <summary>
/// Saves and loads a <see cref="ProjectState"/> to/from a .pmc JSON file.
/// File paths are stored relative to the .pmc file for portability,
/// with an absolute fallback embedded in the value (separated by '|').
/// </summary>
public class ProjectSerializer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    public void Save(ProjectState state, string filePath)
    {
        var dir  = Path.GetDirectoryName(filePath) ?? string.Empty;
        var copy = Clone(state);

        // Relativize paths
        copy.MeshPath    = Relativize(state.MeshPath,    dir);
        copy.TexturePath = Relativize(state.TexturePath, dir);

        var json = JsonSerializer.Serialize(copy, JsonOpts);
        File.WriteAllText(filePath, json);
    }

    public ProjectState Load(string filePath)
    {
        var json  = File.ReadAllText(filePath);
        var state = JsonSerializer.Deserialize<ProjectState>(json, JsonOpts)
                    ?? throw new InvalidDataException("Invalid project file.");

        var dir = Path.GetDirectoryName(filePath) ?? string.Empty;

        // Resolve paths
        state.MeshPath    = Resolve(state.MeshPath,    dir);
        state.TexturePath = Resolve(state.TexturePath, dir);

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
