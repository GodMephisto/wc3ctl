// src/Wc3.Commands/ContractCommand.cs
using System.Text.RegularExpressions;
using Wc3.Model;

namespace Wc3.Commands;

/// <summary>A registration call a target map expects for each unit it hosts.</summary>
public sealed record RosterRegistry(
    string Function,
    int CallCount,
    string Signature,
    string ExampleCall,
    IReadOnlyList<string> RegisteredRawcodes);

/// <summary>A per-player array a hero must appear in for the map's systems to see it.</summary>
public sealed record HeroArray(string Name, string Type, int AssignmentSites);

/// <summary>
/// A roster expressed as a repeating STATEMENT BLOCK rather than a single call. Some maps register
/// a hero with one function call; others need several statements (a slot assignment, a counter, a
/// hidden preview dummy, one call per ability). <see cref="TemplateLines"/> is a real existing
/// entry, so an installer can copy it and substitute rather than invent the shape.
/// </summary>
public sealed record RosterTemplate(
    string ArrayName,
    int Entries,
    int StartLine,
    int EndLine,
    IReadOnlyList<string> TemplateLines);

public sealed record ContractResult(
    string ScriptFile,
    IReadOnlyList<RosterRegistry> Registries,
    IReadOnlyList<string> SpellDispatchers,
    IReadOnlyList<HeroArray> HeroArrays,
    IReadOnlyList<RosterTemplate> Templates)
{
    public bool Any => Registries.Count > 0 || SpellDispatchers.Count > 0
        || HeroArrays.Count > 0 || Templates.Count > 0;
}

/// <summary>
/// Discovers a map's HERO INTEGRATION CONTRACT: what a unit must be registered with before the
/// map's own systems will treat it as a playable hero.
///
/// This is the piece nothing else in the ecosystem models, and it is where ported heroes actually
/// die. A hero can have correct object data, correct assets and a compiling script and still not
/// exist to the player, because the map builds its roster from its own script. GGGA's is
/// <c>RPB_AddHero(rawcode, role, portrait)</c> called 165 times inside
/// <c>RPB_SetHeroCardList</c>; a hero absent from that list has no card no matter how complete
/// everything else is. Finding it took reading a 200,000-line script by hand.
///
/// The generalisable signature is a function called many times whose first argument is a rawcode
/// literal. That is what a roster registration looks like in any map, whatever it is named.
/// </summary>
public static class ContractCommand
{
    /// <summary>Below this, repeated rawcode calls are a coincidence rather than a roster.</summary>
    private const int MinRegistrationCalls = 8;

    private static readonly Regex RawcodeCall =
        new(@"\bcall\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(\s*'([^']{4})'", RegexOptions.Compiled);
    private static readonly Regex Declaration =
        new(@"^function\s+([A-Za-z_][A-Za-z0-9_]*)\s+takes\s+(.*?)\s+returns", RegexOptions.Compiled);
    private static readonly Regex GlobalArray =
        new(@"^\s*(unit|integer|player)\s+array\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    public static ContractResult Run(MapDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var entry = doc.GetFile("war3map.j") ?? doc.GetFile("scripts\\war3map.j");
        if (entry is null)
            return new ContractResult("(none)", Array.Empty<RosterRegistry>(),
                Array.Empty<string>(), Array.Empty<HeroArray>(), Array.Empty<RosterTemplate>());

        var text = System.Text.Encoding.UTF8.GetString(entry.OverrideBytes ?? entry.RawBytes);
        var lines = text.Split('\n');

        var signatures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var l in lines)
            if (Declaration.Match(l) is { Success: true } m)
                signatures[m.Groups[1].Value] = m.Groups[2].Value.Trim();

        // A roster registry is a function called repeatedly with a rawcode as its first argument.
        var byFunction = new Dictionary<string, List<(string code, string call)>>(StringComparer.Ordinal);
        foreach (var l in lines)
        {
            if (RawcodeCall.Match(l) is not { Success: true } m) continue;
            var fn = m.Groups[1].Value;
            if (!byFunction.TryGetValue(fn, out var list))
                byFunction[fn] = list = new List<(string, string)>();
            list.Add((m.Groups[2].Value, l.Trim()));
        }

        var registries = byFunction
            .Where(kv => kv.Value.Count >= MinRegistrationCalls)
            .Select(kv => new RosterRegistry(kv.Key, kv.Value.Count,
                signatures.TryGetValue(kv.Key, out var s) ? s : "(unknown)",
                Shorten(kv.Value[0].call),
                kv.Value.Select(v => v.code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList()))
            .OrderByDescending(r => r.CallCount)
            .ToList();

        // A spell dispatcher decides which handler runs for a cast. A ported hero whose ability id
        // never reaches one of these can be selected but will do nothing.
        var dispatchers = new List<string>();
        string current = "(top level)";
        foreach (var l in lines)
        {
            if (Declaration.Match(l) is { Success: true } dm) current = dm.Groups[1].Value;
            if (l.Contains("GetSpellAbilityId", StringComparison.Ordinal) && !dispatchers.Contains(current))
                dispatchers.Add(current);
        }

        // Per-player hero arrays. A hero missing from these is invisible to the map's own systems.
        var arrays = new List<HeroArray>();
        foreach (var l in lines)
        {
            if (GlobalArray.Match(l) is not { Success: true } gm) continue;
            var name = gm.Groups[2].Value;
            if (!LooksLikeHeroArray(name)) continue;
            int assignments = CountAssignments(text, name);
            if (assignments > 0) arrays.Add(new HeroArray(name, gm.Groups[1].Value, assignments));
        }

        return new ContractResult(entry.FileName ?? "war3map.j", registries,
            dispatchers.Take(20).ToList(),
            arrays.OrderByDescending(a => a.AssignmentSites).Take(15).ToList(),
            FindRosterTemplates(lines));
    }


    /// <summary>Assignment of an array slot from an id-shaped global, e.g. set Hero_ID0[n]=Raiden_ID.</summary>
    private static readonly Regex RosterSlotAssign =
        new(@"^\s*set\s+([A-Za-z_][A-Za-z0-9_]*)\s*\[[^\]]*\]\s*=\s*([A-Za-z_][A-Za-z0-9_]*_ID)\b",
            RegexOptions.Compiled);

    /// <summary>
    /// Finds rosters built as a REPEATING BLOCK instead of a single call.
    /// </summary>
    /// <remarks>
    /// Searching for a registration FUNCTION found nothing on a map that plainly has hero
    /// selection, because its roster is an array of ability-id globals, not rawcode literals:
    /// <c>set Hero_ID0[n]=Raiden_ID</c>, followed by a counter bump, a hidden preview dummy and one
    /// UnitAddAbility per spell. Five statements per hero, repeated 56 times, with the display name
    /// set in a separate if/elseif chain.
    ///
    /// So a contract cannot be reduced to a signature. The map demonstrates its own shape dozens of
    /// times, and the reliable move is to capture one real entry as a template that an installer
    /// copies and substitutes into. A single-call roster is simply a one-line template, so this
    /// generalises both forms rather than special-casing either.
    /// </remarks>
    private static IReadOnlyList<RosterTemplate> FindRosterTemplates(string[] lines)
    {
        var sites = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < lines.Length; i++)
        {
            var m = RosterSlotAssign.Match(lines[i]);
            if (!m.Success) continue;
            var name = m.Groups[1].Value;
            if (!sites.TryGetValue(name, out var l)) sites[name] = l = new List<int>();
            l.Add(i);
        }

        var result = new List<RosterTemplate>();
        foreach (var (name, at) in sites.OrderByDescending(kv => kv.Value.Count))
        {
            if (at.Count < 3) continue;   // a handful is a coincidence, not a roster
            // The pattern alone is not enough: an item table matches it too
            // (ItemsCurrentItem_ID). The same discipline as identifying a registration function -
            // the NAME has to indicate a unit, or we report something the caller cannot use.
            if (!LooksLikeHeroArray(name)) continue;

            // The block for one entry runs from its slot assignment to just before the next one,
            // which is how the map itself delimits them.
            int start = at[0];
            int end = at.Count > 1 ? at[1] - 1 : Math.Min(at[0] + 8, lines.Length - 1);
            // Include the immediately preceding counter/bump lines, which belong to the entry.
            while (start > 0 && lines[start - 1].TrimStart().StartsWith("set ", StringComparison.Ordinal)
                   && !RosterSlotAssign.IsMatch(lines[start - 1]))
                start--;

            var block = new List<string>();
            for (int i = start; i <= end && block.Count < 24; i++)
            {
                var t = lines[i].TrimEnd('\r');
                if (t.Trim().Length > 0) block.Add(t);
            }
            result.Add(new RosterTemplate(name, at.Count, start + 1, end + 1, block));
            if (result.Count == 6) break;
        }
        return result;
    }

    private static bool LooksLikeHeroArray(string name) =>
        name.Contains("Hero", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Player", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Champion", StringComparison.OrdinalIgnoreCase);

    private static int CountAssignments(string text, string name) =>
        Regex.Matches(text, @"\bset\s+" + Regex.Escape(name) + @"\s*\[").Count;

    private static string Shorten(string s) => s.Length <= 120 ? s : s[..120] + "...";
}
