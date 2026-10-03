using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Wc3.Studio.Controls;

/// <summary>
/// One shared entry point for every OS file dialog in the app. Wraps the top-level
/// <see cref="IStorageProvider"/> and remembers the last folder the user browsed to (across
/// ALL pickers - open map, import, save, the FilePathBox Browse buttons), so a dialog reopens
/// where they left off instead of the home folder every time. The picker plumbing lives here
/// once rather than being re-written in every panel (Principle #3).
/// </summary>
public static class FilePicker
{
    /// <summary>Last directory a pick landed in, shared by every dialog for the session.</summary>
    private static string? _lastDirectory;

    /// <summary>A named file-type filter, e.g. ("Audio", ["*.mp3","*.wav"]).</summary>
    public readonly record struct Filter(string Name, string[] Patterns);

    /// <summary>Shows an open-file dialog (single selection) starting at the remembered folder;
    /// returns the chosen local path, or null if cancelled. Updates the remembered folder.</summary>
    public static async Task<string?> PickOpenAsync(Visual owner, string title, params Filter[] filters)
    {
        var storage = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (storage is null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolderAsync(storage),
        };
        if (filters.Length > 0)
            options.FileTypeFilter = filters
                .Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns })
                .Append(FilePickerFileTypes.All)
                .ToList();

        var files = await storage.OpenFilePickerAsync(options);
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
        {
            Remember(path);
            return path;
        }
        return null;
    }

    /// <summary>Shows a save-file dialog starting at the remembered folder; returns the chosen
    /// local path, or null if cancelled. Updates the remembered folder.</summary>
    public static async Task<string?> PickSaveAsync(
        Visual owner, string title, string? suggestedName = null, string? defaultExtension = null)
    {
        var storage = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (storage is null) return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = defaultExtension,
            SuggestedStartLocation = await StartFolderAsync(storage),
        });
        if (file?.TryGetLocalPath() is { } path)
        {
            Remember(path);
            return path;
        }
        return null;
    }

    /// <summary>Shows a folder-picker dialog starting at the remembered folder; returns the
    /// chosen folder path, or null if cancelled. Remembers the picked folder itself, so the
    /// next dialog opens there (used for the Warcraft III and map folder settings).</summary>
    public static async Task<string?> PickFolderAsync(Visual owner, string title)
    {
        var storage = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (storage is null) return null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolderAsync(storage),
        });
        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path)
        {
            _lastDirectory = path;
            return path;
        }
        return null;
    }

    /// <summary>Seeds the remembered folder, e.g. from saved settings on startup, so the first
    /// Open Map opens at the user's map folder. A missing directory is ignored.</summary>
    public static void SeedLastDirectory(string? dir)
    {
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            _lastDirectory = dir;
    }

    private static async Task<IStorageFolder?> StartFolderAsync(IStorageProvider storage) =>
        _lastDirectory is not null ? await storage.TryGetFolderFromPathAsync(_lastDirectory) : null;

    private static void Remember(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) _lastDirectory = dir;
    }
}
