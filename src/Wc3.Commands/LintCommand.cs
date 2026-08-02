// src/Wc3.Commands/LintCommand.cs
using Wc3.Model;

namespace Wc3.Commands;

public enum LintSeverity { Ok, Warning, Error }

/// <summary>One lint rule's outcome. <see cref="Detail"/> lines name the offending items.</summary>
public sealed record LintCheck(
    string Name,
    LintSeverity Severity,
    string Summary,
    IReadOnlyList<string> Detail);

public sealed record LintResult(IReadOnlyList<LintCheck> Checks)
{
    public int Errors => Checks.Count(c => c.Severity == LintSeverity.Error);
    public int Warnings => Checks.Count(c => c.Severity == LintSeverity.Warning);
    public bool Ok => Errors == 0;
}

/// <summary>
/// Pre-flight checks on a map, aimed squarely at the failures that a green build, a passing
/// test and a clean <c>validate</c> all missed while a user was repeatedly unable to host or
/// load the map. Each rule here exists because its absence cost real debugging time:
/// <list type="bullet">
/// <item>A script that could not compile passed <c>validate</c>, because pjass's
/// uninitialized-variable errors were classified as advisory. The user's only symptom was
/// being kicked from their own lobby.</item>
/// <item>Assets were added to an archive without a matching <c>war3map.imp</c> entry.</item>
/// <item>A one-hero port copied hundreds of files and thousands of functions belonging to
/// other heroes, and nothing objected.</item>
/// <item>A port silently overwrote the TARGET's own same-named assets.</item>
/// </list>
/// This cannot predict Warcraft III's loader. It catches the classes we have actually been
/// burned by, before a human spends minutes loading a 250 MB map to find out.
/// </summary>
public static class LintCommand
{
    /// <summary>
    /// Extensions Warcraft III actually loads from a map archive. A carried path outside this
    /// set is reported, because the porter's closure walk picks paths out of script string
    /// literals and object fields and has no notion of whether the game can use them.
    /// </summary>
    private static readonly HashSet<string> LoadableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".blp", ".tga", ".dds", ".jpg", ".jpeg",          // textures
        ".mdx", ".mdl",                                   // models
        ".wav", ".mp3", ".ogg", ".flac",                  // audio
        ".slk", ".txt", ".ai", ".wpm", ".shd", ".w3e",    // data the editor/game reads
        ".doo", ".w3i", ".w3u", ".w3t", ".w3a", ".w3b",
        ".w3d", ".w3q", ".w3h", ".w3c", ".w3r", ".w3s",
        ".wts", ".wtg", ".wct", ".imp", ".j", ".lua",
        ".fdf", ".toc", ".mmp", ".pld", ".json",   // Reforged ships conversation.json
    };

    private static readonly System.Text.RegularExpressions.Regex ScriptStringLiteral =
        new("\"([^\"\\r\\n]{3,260})\"", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static LintResult Run(MapDocument doc, MapDocument? original = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var checks = new List<LintCheck>
        {
            ScriptCompiles(doc),
            ImportTableConsistent(doc),
            AssetReferencesResolve(doc),
            CarriedFileTypesLoadable(doc),
        };
        if (original is not null)
        {
            checks.Add(NoFilesLost(doc, original));
            checks.Add(NoTargetAssetsClobbered(doc, original));
        }
        return new LintResult(checks);
    }

    /// <summary>
    /// The check that matters most. An un-compilable <c>war3map.j</c> means <c>config()</c>
    /// never runs, so a hosted map shows no player slots and the host is kicked from their own
    /// lobby. Everything pjass calls an error is an error here, bar a missing-but-real native.
    /// </summary>
    private static LintCheck ScriptCompiles(MapDocument doc)
    {
        var script = doc.GetFile("war3map.j") ?? doc.GetFile("scripts\\war3map.j");
        if (script is null)
            return new("script-compiles", LintSeverity.Ok, "map has no JASS script", Array.Empty<string>());
        // Latin-1, NOT UTF-8. PjassGate writes the string back out with Latin1.GetBytes, and
        // Latin-1 cannot encode anything above U+00FF, so decoding as UTF-8 first turned every
        // such character into '?' and pjass reported "Unrecognized character ? (ASCII 63)". That
        // made this check fail on UNTOUCHED maps whose scripts are not pure ASCII, and worse, it
        // passed on a map an earlier install had already corrupted into that range, so a green
        // result meant nothing. Latin-1 both ways round-trips every byte 0..255 unchanged.
        var pj = PjassGate.Check(System.Text.Encoding.Latin1.GetString(script.OverrideBytes ?? script.RawBytes));
        if (!pj.Ran)
            return new("script-compiles", LintSeverity.Warning,
                "pjass did not run, so the script was not compile-checked", new[] { pj.Note });
        if (!pj.Passed)
            return new("script-compiles", LintSeverity.Error,
                $"war3map.j does not compile ({pj.Errors.Count} error(s)). A hosted map will show no "
                + "player slots. If any error names a variable the porter marked "
                + "\"(initializer dropped)\", 'wc3ctl script repair' is the fix.",
                pj.Errors.ToList());
        return new("script-compiles", LintSeverity.Ok, "war3map.j compiles", pj.Warnings.ToList());
    }

    /// <summary>
    /// Every imported asset in the archive should be listed in <c>war3map.imp</c>, and every
    /// listed path should exist. Adding an archive entry without its import entry is easy to do
    /// and invisible to a content diff.
    /// </summary>
    private static LintCheck ImportTableConsistent(MapDocument doc)
    {
        var imp = doc.GetFile("war3map.imp");
        if (imp is null)
            return new("import-table", LintSeverity.Ok, "map has no war3map.imp", Array.Empty<string>());

        var listed = ImportedPathsOf(doc);
        var problems = new List<string>();

        // An .imp routinely spells a separator differently from the archive (forward slash in the
        // table, backslash in the stored name), so resolve through the same spelling expansion the
        // loader effectively does rather than comparing the literal strings.
        foreach (var path in listed)
            if (!AssetPathCandidates.Expand(path).Any(c => doc.GetFile(c) is not null))
                problems.Add($"war3map.imp lists '{path}', which is not in the archive");

        var listedSpellings = listed.SelectMany(AssetPathCandidates.Expand)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in doc.Files)
        {
            if (entry.FileName is null) continue;
            if (!entry.FileName.StartsWith("war3mapImported", StringComparison.OrdinalIgnoreCase)) continue;
            if (!listedSpellings.Contains(entry.FileName))
                problems.Add($"'{entry.FileName}' is in the archive but not listed in war3map.imp");
        }

        return problems.Count == 0
            ? new("import-table", LintSeverity.Ok, $"war3map.imp agrees with the archive ({listed.Count} entries)", Array.Empty<string>())
            : new("import-table", LintSeverity.Error,
                $"war3map.imp and the archive disagree on {problems.Count} path(s)", Capped(problems));
    }

    /// <summary>
    /// Asset paths named by the map's own script and object data must resolve to a real entry.
    /// Uses the same un-escaping and spelling expansion the loader effectively does, so a
    /// doubled backslash in a JASS literal is not mistaken for a missing file.
    /// </summary>
    private static LintCheck AssetReferencesResolve(MapDocument doc)
    {
        var script = doc.GetFile("war3map.j") ?? doc.GetFile("scripts\\war3map.j")
            ?? doc.GetFile("war3map.lua") ?? doc.GetFile("scripts\\war3map.lua");
        if (script is null)
            return new("asset-references", LintSeverity.Ok, "no script to scan", Array.Empty<string>());

        var text = System.Text.Encoding.UTF8.GetString(script.OverrideBytes ?? script.RawBytes);
        var imported = ImportedPathsOf(doc);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in ScriptStringLiteral.Matches(text))
        {
            // Unescape first: these maps write one real separator as a doubled backslash, and
            // comparing the raw literal against stored names is how a present file was once
            // reported absent (see AssetPathCandidates.Unescape).
            var raw = AssetPathCandidates.Unescape(m.Groups[1].Value);
            if (!AssetPathCandidates.LooksLikeAssetPath(raw) || !seen.Add(raw)) continue;
            if (AssetPathCandidates.Expand(raw).Any(c => doc.GetFile(c) is not null)) continue;
            // A path the map never claimed to import is a base-game asset the archive is not
            // expected to hold, and flagging those buries the ones that matter. Only report a
            // path the map itself says it imports.
            if (!imported.Contains(raw) && !raw.StartsWith("war3mapImported", StringComparison.OrdinalIgnoreCase))
                continue;
            missing.Add(raw);
        }
        return missing.Count == 0
            ? new("asset-references", LintSeverity.Ok, "every referenced asset path resolves", Array.Empty<string>())
            : new("asset-references", LintSeverity.Warning,
                $"{missing.Count} referenced asset path(s) do not resolve. A base-game path is fine, "
                + "an imported one that is absent renders as nothing.", Capped(missing));
    }

    /// <summary>
    /// Flags archive entries whose extension the game does not load. The porter's closure walk
    /// harvests paths from script literals, so it can carry things that are not assets at all.
    /// </summary>
    private static LintCheck CarriedFileTypesLoadable(MapDocument doc)
    {
        var odd = doc.Files
            .Select(f => f.FileName)
            .Where(n => n is not null && !n.StartsWith("(", StringComparison.Ordinal))
            .Select(n => n!)
            .Where(n => Path.GetExtension(n) is { Length: > 0 } ext && !LoadableExtensions.Contains(ext))
            .ToList();

        return odd.Count == 0
            ? new("file-types", LintSeverity.Ok, "every archive entry has a loadable extension", Array.Empty<string>())
            : new("file-types", LintSeverity.Warning,
                $"{odd.Count} entry(ies) have an extension the game does not load", Capped(odd));
    }

    /// <summary>A rebuild must never drop a file. Bookkeeping is excluded, it is regenerated or dropped by design.</summary>
    private static LintCheck NoFilesLost(MapDocument doc, MapDocument original)
    {
        var have = doc.Files.Where(f => f.FileName is not null)
            .Select(f => f.FileName!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lost = original.Files
            .Where(f => f.FileName is not null && !IsBookkeeping(f.FileName))
            .Select(f => f.FileName!)
            .Where(n => !have.Contains(n))
            .ToList();

        return lost.Count == 0
            ? new("no-files-lost", LintSeverity.Ok, "no file from the original is missing", Array.Empty<string>())
            : new("no-files-lost", LintSeverity.Error, $"{lost.Count} file(s) present in the original are gone", Capped(lost));
    }

    /// <summary>
    /// A port must not replace the TARGET's own assets with same-named ones from the source.
    /// Those files belong to the target's other content, so overwriting them changes how
    /// unrelated units and doodads look, or stops them loading at all.
    /// </summary>
    private static LintCheck NoTargetAssetsClobbered(MapDocument doc, MapDocument original)
    {
        var before = original.Files
            .Where(f => f.FileName is not null && !IsBookkeeping(f.FileName) && !IsStructural(f.FileName))
            .ToDictionary(f => f.FileName!, f => f.RawBytes, StringComparer.OrdinalIgnoreCase);

        var clobbered = new List<string>();
        foreach (var f in doc.Files)
        {
            if (f.FileName is null || !before.TryGetValue(f.FileName, out var was)) continue;
            var now = f.OverrideBytes ?? f.RawBytes;
            if (!now.AsSpan().SequenceEqual(was)) clobbered.Add(f.FileName);
        }

        return clobbered.Count == 0
            ? new("no-assets-clobbered", LintSeverity.Ok, "no pre-existing asset was overwritten", Array.Empty<string>())
            : new("no-assets-clobbered", LintSeverity.Error,
                $"{clobbered.Count} of the target's own asset(s) were overwritten by same-named source files. "
                + "The target's other content renders with these.", Capped(clobbered));
    }

    private static HashSet<string> ImportedPathsOf(MapDocument doc)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (doc.GetFile("war3map.imp") is not { } imp) return set;
        var bytes = imp.OverrideBytes ?? imp.RawBytes;
        // Entries are a flag byte followed by a NUL-terminated path. Reading the paths out
        // directly keeps this independent of the header's declared count, which is what a
        // consistency check should do.
        var cur = new List<byte>();
        for (int i = 8; i < bytes.Length; i++)
        {
            if (bytes[i] != 0) { cur.Add(bytes[i]); continue; }
            if (cur.Count > 1)
            {
                var s = System.Text.Encoding.UTF8.GetString(cur.ToArray()).Trim();
                if (s.Length > 1) set.Add(s);
            }
            cur.Clear();
        }
        return set;
    }

    private static bool IsBookkeeping(string name) =>
        name is "(listfile)" or "(attributes)" or "(signature)";

    /// <summary>Structural files legitimately change on an edit, unlike assets.</summary>
    private static bool IsStructural(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is ".j" or ".lua" or ".imp" or ".wts"
            or ".w3i" or ".w3u" or ".w3t" or ".w3a" or ".w3b" or ".w3d" or ".w3q"
            or ".w3h" or ".w3c" or ".w3r" or ".w3s" or ".doo" or ".w3e" or ".wpm"
            or ".shd" or ".wtg" or ".wct" or ".mmp";

    private static IReadOnlyList<string> Capped(List<string> items) =>
        items.Count <= 25 ? items : items.Take(25).Append($"... and {items.Count - 25} more").ToList();
}
