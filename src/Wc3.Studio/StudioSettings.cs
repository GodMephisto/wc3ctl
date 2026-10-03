using System.IO;
using System.Text.Json;

namespace Wc3.Studio;

/// <summary>
/// Small persisted app settings, saved as JSON under the user's application-data folder so the
/// choices survive a restart. GameDir points the app at a Warcraft III install for base-game
/// data (overriding auto-detection). MapsDir is the folder Open Map starts in. Both are picked
/// in the Settings dialog. Loading and saving never throw, so a missing or bad file just yields
/// defaults and the app keeps running.
/// </summary>
public sealed class StudioSettings
{
    /// <summary>The Warcraft III install folder, or null to auto-detect.</summary>
    public string? GameDir { get; set; }

    /// <summary>The folder Open Map starts in, or null.</summary>
    public string? MapsDir { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "wc3ctl", "studio-settings.json");

    public static StudioSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(FilePath))
                    ?? new StudioSettings();
        }
        catch { /* corrupt or unreadable, fall back to defaults */ }
        return new StudioSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort, never crash the app over settings */ }
    }
}
