// src/Wc3.Commands/ImportsCommand.cs
using Wc3.Model;
using War3Net.Build.Import;

namespace Wc3.Commands;

/// <summary>One import row. <see cref="Path"/> is the manifest path when the file is listed
/// in war3map.imp, else the archive file name. SizeBytes is null when the file is only
/// listed in the manifest but missing from the archive.</summary>
public sealed record ImportEntry(string Path, bool InManifest, bool InArchive, int? SizeBytes);

/// <summary>Read-only imports listing. HasManifest is false when war3map.imp is absent
/// (or failed to parse — the listing then degrades to archive files only).</summary>
public sealed record ImportsListResult(bool HasManifest, IReadOnlyList<ImportEntry> Entries);

/// <summary>
/// Lists a map's imported files. Imports are represented twice in a .w3x: the actual
/// archive entries (arbitrary paths, e.g. "war3mapImported\foo.blp") plus a WorldEdit
/// manifest, war3map.imp (parsed by War3Net into <see cref="ImportedFiles"/>: FullPath +
/// flags per file). This command merges both views: manifest rows are checked against the
/// archive (also probing the default "war3mapImported\" prefix, which WorldEdit prepends
/// for non-custom-path imports), and archive files that are neither standard map files nor
/// manifest-listed appear as orphans (InManifest = false). Read-only by design.
/// </summary>
public static class ImportsCommand
{
    public const string ImpFileName = "war3map.imp";

    /// <summary>WorldEdit's default folder for imports whose manifest path is stored
    /// without it (flag-relative, non-custom-path imports).</summary>
    public const string DefaultImportPrefix = @"war3mapImported\";

    /// <summary>Archive entries that are never user imports: MPQ bookkeeping plus standard
    /// map files that MapFormatRegistry doesn't (yet) register.</summary>
    private static readonly HashSet<string> NonImportFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "(listfile)", "(attributes)", "(signature)",
        "war3map.shd", "war3map.mmp", "war3map.wai",
        "war3mapMap.blp", "war3mapMap.tga", "war3mapPreview.tga",
        "war3mapMisc.txt", "war3mapSkin.txt", "war3mapExtra.txt",
        @"scripts\war3map.j", @"scripts\war3map.lua",
    };

    public static ImportsListResult Execute(MapDocument doc)
    {
        var manifest = doc.GetFile(ImpFileName)?.Model as ImportedFiles;
        var manifestPaths = manifest?.Files.Select(f => f.FullPath).ToList()
                            ?? new List<string>();

        var archiveFiles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in doc.Files)
        {
            if (f.FileName is null || MapFormatRegistry.IsKnown(f.FileName) || NonImportFiles.Contains(f.FileName))
                continue;
            // Pending in-memory replacement wins over the original for the size. An override is
            // already in memory so its length is free, but the original's must come from RawSize,
            // since reading RawBytes here would decompress every imported asset in the map.
            archiveFiles[f.FileName] = f.OverrideBytes?.Length ?? f.RawSize;
        }

        return new ImportsListResult(manifest is not null, BuildEntries(manifestPaths, archiveFiles));
    }

    /// <summary>
    /// Pure merge of the manifest path list with the archive's candidate import files
    /// (name → size in bytes). Case sensitivity of the match follows
    /// <paramref name="archiveFiles"/>'s comparer (<see cref="Execute"/> passes
    /// OrdinalIgnoreCase, matching MPQ name hashing). Result is sorted by path; duplicate
    /// manifest paths are collapsed to one row.
    /// </summary>
    public static IReadOnlyList<ImportEntry> BuildEntries(
        IEnumerable<string> manifestPaths, IReadOnlyDictionary<string, int> archiveFiles)
    {
        var entries = new List<ImportEntry>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in manifestPaths)
        {
            if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
            string? archiveName = null;
            if (archiveFiles.ContainsKey(path)) archiveName = path;
            else if (archiveFiles.ContainsKey(DefaultImportPrefix + path)) archiveName = DefaultImportPrefix + path;
            if (archiveName is not null) claimed.Add(archiveName);
            entries.Add(new ImportEntry(
                path,
                InManifest: true,
                InArchive: archiveName is not null,
                SizeBytes: archiveName is not null ? archiveFiles[archiveName] : null));
        }

        foreach (var (name, size) in archiveFiles)
            if (!claimed.Contains(name))
                entries.Add(new ImportEntry(name, InManifest: false, InArchive: true, SizeBytes: size));

        entries.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return entries;
    }
}
