// src/Wc3.Studio/Controls/AssetCatalog.cs
using System.Collections.Concurrent;
using Wc3.Model;

namespace Wc3.Studio.Controls;

/// <summary>Media family an asset path field holds, dispatched from the field's metadata
/// type token exactly as HiveWE's editor dispatch does ('icon' and 'model').</summary>
public enum AssetFamily { Icon, Model }

/// <summary>
/// Path candidates for the icon and model pickers, the paths that actually exist rather
/// than whatever the user can type. Two sources, the open map's own imported files (their
/// entry names are exactly the strings an object field must hold) and, when a Warcraft III
/// install is reachable, the base game's files. Family membership is derived through
/// <see cref="AssetPathCandidates"/>' extension expansion instead of a second extension
/// table, so this class and the archive prober can never disagree about what counts as a
/// model or a texture. The base game side goes through GameDataContext.FindFiles, whose
/// first CASC listfile pass takes seconds, hence the task shape and the per-install cache.
/// </summary>
public static class AssetCatalog
{
    /// <summary>FindFiles cap. Reforged's listfile holds a few tens of thousands of models
    /// and icons per family, so this is unreachable in practice and exists only so a
    /// pathological listfile cannot balloon the picker without bound.</summary>
    private const int MaxGamePaths = 200_000;

    private static readonly ConcurrentDictionary<(string Dir, AssetFamily Family), Task<IReadOnlyList<string>>>
        GameCache = new();

    /// <summary>The picker family for a metadata type token, or null when the field is not
    /// an asset path. This is the wiki's widget mapping, model gets the model picker, icon
    /// gets the icon picker, and nothing else is guessed at.</summary>
    public static AssetFamily? FamilyForFieldType(string type) => type.ToLowerInvariant() switch
    {
        "icon" => AssetFamily.Icon,
        "model" => AssetFamily.Model,
        _ => null,
    };

    /// <summary>The open map's imported files of the family, sorted, duplicates folded.</summary>
    public static IReadOnlyList<string> MapPaths(MapDocument doc, AssetFamily family)
    {
        var exts = FamilyExtensions(family);
        return doc.Files
            .Select(f => f.FileName)
            .Where(n => n is not null && exts.Contains(Path.GetExtension(n), StringComparer.OrdinalIgnoreCase))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The base game's paths of the family, empty when no install answers, never throwing.
    /// Cached per (install, family) for the process lifetime, the first call pays one CASC
    /// listfile pass and every later field selection reuses it. Callers marshal back to the
    /// UI thread themselves.
    /// </summary>
    public static Task<IReadOnlyList<string>> GamePathsAsync(string? gameDir, AssetFamily family) =>
        GameCache.GetOrAdd((gameDir ?? "", family),
            key => Task.Run(() => LoadGamePaths(key.Dir.Length == 0 ? null : key.Dir, key.Family)));

    private static IReadOnlyList<string> LoadGamePaths(string? gameDir, AssetFamily family)
    {
        if (!GameData.GameData.TryOpen(gameDir, out var ctx, out _) || ctx is null)
            return Array.Empty<string>();

        // One listfile pass per family. Icons narrow by directory, every icon field the
        // game reads points into ReplaceableTextures\CommandButtons or its Disabled
        // sibling (both contain this needle). Models narrow by extension, the base game
        // ships binary .mdx only.
        var needle = family == AssetFamily.Icon
            ? @"replaceabletextures\commandbuttons"
            : ".mdx";
        var exts = FamilyExtensions(family);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var name in ctx.FindFiles(needle, MaxGamePaths))
        {
            // Storage paths carry w3mod layer prefixes ("war3.w3mod:_hd.w3mod:units\...").
            // An object field stores the logical path after the last prefix, and HD and SD
            // ship the same logical names, so the dedup folds the layers to one entry.
            var logical = name[(name.LastIndexOf(':') + 1)..];
            if (!exts.Contains(Path.GetExtension(logical), StringComparer.OrdinalIgnoreCase))
                continue;
            if (seen.Add(logical))
                result.Add(logical);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>
    /// The family's extensions, read out of <see cref="AssetPathCandidates.Expand"/> by
    /// expanding one seed name per family. Expand answers "which sibling spellings might
    /// this file exist under", which for a bare seed is precisely the extension family, so
    /// the path rules stay defined in one place.
    /// </summary>
    public static IReadOnlyList<string> FamilyExtensions(AssetFamily family) =>
        AssetPathCandidates.Expand(family == AssetFamily.Model ? "seed.mdx" : "seed.blp")
            .Select(Path.GetExtension)
            .Where(e => e is { Length: > 0 })
            .Select(e => e!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
