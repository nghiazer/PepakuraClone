using System.Text.Json;
using FourHUnfolder.Domain.Settings;

namespace FourHUnfolder.Application.Services;

/// <summary>
/// Singleton that owns the application settings.
/// Persists to %AppData%\FourHUnfolder\settings.json.
/// Raises <see cref="SettingsChanged"/> after every <see cref="Apply"/> call.
/// </summary>
public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "4H-Unfolder", "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AppSettings Current { get; private set; } = new();

    public event EventHandler? SettingsChanged;

    // ── lifecycle ─────────────────────────────────────────────────────────────

    /// Called once on startup.  Silently falls back to defaults on any error.
    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var json = File.ReadAllText(SettingsPath);
            Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new();
        }
        catch
        {
            Current = new();
        }
    }

    /// Replaces current settings, persists, and notifies listeners.
    public void Apply(AppSettings settings)
    {
        Current = settings ?? throw new ArgumentNullException(nameof(settings));
        Save();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// Resets to defaults, persists, and notifies listeners.
    public void ResetToDefaults() => Apply(new AppSettings());

    // ── persistence ───────────────────────────────────────────────────────────

    /// Persists the current settings object as-is (no event fired).
    /// Use when a single field was mutated directly and only persistence is needed.
    public void SaveCurrent() => Save();

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, JsonOpts));
        }
        catch { /* non-fatal */ }
    }
}
