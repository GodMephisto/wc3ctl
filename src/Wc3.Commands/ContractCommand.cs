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

public sealed record ContractResult(
    string ScriptFile,
    IReadOnlyList<RosterRegistry> Registries,
    IReadOnlyList<string> SpellDispatchers,
    IReadOnlyList<HeroArray> HeroArrays)
{
    public bool Any => Registries.Count > 0 || SpellDispatchers.Count > 0 || HeroArrays.Count > 0;
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
                Array.Empty<string>(), Array.Empty<HeroArray>());

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
            arrays.OrderByDescending(a => a.AssignmentSites).Take(15).ToList());
    }

    private static bool LooksLikeHeroArray(string name) =>
        name.Contains("Hero", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Player", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Champion", StringComparison.OrdinalIgnoreCase);

    private static int CountAssignments(string text, string name) =>
        Regex.Matches(text, @"\bset\s+" + Regex.Escape(name) + @"\s*\[").Count;

    private static string Shorten(string s) => s.Length <= 120 ? s : s[..120] + "...";
}
