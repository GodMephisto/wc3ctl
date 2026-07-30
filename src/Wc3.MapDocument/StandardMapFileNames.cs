// src/Wc3.MapDocument/StandardMapFileNames.cs
namespace Wc3.Model;

/// <summary>
/// The candidate list <see cref="MapDocument.Load"/> probes into an archive's hash table
/// when its (listfile) is missing, stripped, or curated to hide most files, a common shape
/// for protected maps. An MPQ locates a file by hashing its name, not by a stored name, so
/// the hash table still resolves a standard file even with no listfile at all, probing is
/// only a matter of trying every name a WC3 map is known to carry.
/// </summary>
public static class StandardMapFileNames
{
    // Standard WC3 map files with no War3Net parser (images, ini-style text, and the
    // scripts\ prefixed paths some compilers write the script/AI script under instead of
    // the archive root). Kept separate from MapFormatRegistry, adding these here does not
    // make MapDocument treat them as IsKnown or parse them into a model, it only recovers
    // their name so they show up in ls/extract instead of staying invisible.
    private static readonly string[] Extra =
    {
        "scripts\\war3map.j",
        "scripts\\war3map.lua",
        "war3mapMap.blp",
        "war3mapMap.tga",
        "war3mapPreview.tga",
        "war3mapPreview.blp",
        "war3map.mmp",
        "war3map.shd",
        "war3mapMisc.txt",
        "war3mapSkin.txt",
        "war3mapExtra.txt",
    };

    /// <summary>
    /// Every name worth probing for, every name <see cref="MapFormatRegistry"/> parses
    /// plus <see cref="Extra"/>. Built from the registry rather than duplicated by hand so
    /// the two lists cannot drift apart as parsers are added.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
        MapFormatRegistry.KnownFileNames.Concat(Extra).ToArray();
}
