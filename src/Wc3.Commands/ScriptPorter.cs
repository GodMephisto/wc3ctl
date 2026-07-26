// src/Wc3.Commands/ScriptPorter.cs
using System.Text;
using System.Text.RegularExpressions;
using War3Net.Common.Extensions;
using Wc3.Model;

namespace Wc3.Commands;

/// <summary>
/// Best-effort JASS script port: carries a unit's trigger-function closure (and the
/// globals it references) from the source map's war3map.j into the target's, renaming
/// any symbol that collides with the target, rewriting rawcode literals to follow the
/// object remap, and hooking carried InitTrig_* functions into the target's init.
///
/// This is text surgery over an untyped, Turing-complete language — it is deliberately
/// conservative and always reports what it did. Tightly-coupled maps (a shared spell
/// dispatcher) produce very large closures; the mechanism still applies but the notes
/// flag that a large fraction of the source script was carried.
/// </summary>
internal static class ScriptPorter
{
    private static readonly Regex Ident = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
    private static readonly Regex Rawcode = new(@"'(\\?.|[^'\\]{1,4})'", RegexOptions.Compiled);

    /// <summary>Byte-faithful codec (Latin1 is a bijection on all 256 byte values), so decoding
    /// then re-encoding war3map.j preserves a legacy-codepage target's bytes exactly.</summary>
    private static readonly Encoding ByteText = Encoding.Latin1;

    /// <summary>
    /// Splices the function closure into <paramref name="target"/> (mutated via
    /// AddOrReplaceRawFile) — or, with <paramref name="apply"/> false, computes the exact
    /// same <see cref="ScriptPortInfo"/> without touching the target (dry-run preview;
    /// one code path so preview and port cannot drift). <paramref name="codeRemap"/> maps
    /// source rawcode → target rawcode (as 4-char strings) for the objects that were
    /// remapped on collision; <paramref name="markerLabel"/> names the port in the spliced
    /// block's BEGIN/END comments (e.g. "Raiden Ei (H000)"). Returns null when there is
    /// nothing to port (no functions, or no target script).
    /// </summary>
    public static ScriptPortInfo? PortScript(
        MapDocument source, MapDocument target,
        IReadOnlyList<BundleFunction> functions, string markerLabel,
        IReadOnlyDictionary<string, string> codeRemap, bool apply = true)
    {
        var notes = new List<string>();
        if (functions.Count == 0) return null;

        var srcEntry = ScriptEntry(source);
        var tgtEntry = ScriptEntry(target);
        if (srcEntry is null) { notes.Add("source has no war3map.j — script not ported."); return new ScriptPortInfo(0, 0, 0, false, notes); }
        if (tgtEntry is null) { notes.Add("target has no war3map.j — script not ported."); return new ScriptPortInfo(0, 0, 0, false, notes); }

        // Byte-faithful codec: war3map.j is often authored in a legacy codepage (GBK/Big5 on
        // Chinese maps, exactly this corpus). UTF8 with its replacement fallback would turn every
        // non-ASCII byte in the UNTOUCHED target script into U+FFFD on re-encode, corrupting names
        // and messages the port never meant to touch. Latin1 maps all 256 byte values one-to-one,
        // so decode then re-encode round-trips the original bytes exactly, and our inserted ASCII
        // is identical either way.
        string srcJ = ByteText.GetString(srcEntry.RawBytes);
        string tgtJ = ByteText.GetString(tgtEntry.RawBytes);
        var srcLines = srcJ.Replace("\r\n", "\n").Split('\n');

        // Idempotency: the object layer reuses an identical prior port rather than duplicating it,
        // and the MCP tool advertises port_unit as idempotent. The script layer must match. Splicing
        // again would append a second copy of every carried function under _p1 names and wire a
        // second call into InitCustomTriggers, so each ported spell would fire twice. If the target
        // already carries this exact port (its BEGIN marker is present), leave the script untouched.
        string marker = $"wc3ctl ported: {markerLabel}";
        if (tgtJ.Contains($"BEGIN {marker} ====", StringComparison.Ordinal))
        {
            notes.Add("target already carries this port (marker present) — script left unchanged to stay idempotent.");
            return new ScriptPortInfo(0, 0, 0, false, notes);
        }

        // Every function defined in the source, by name (first declaration wins).
        var allByName = new Dictionary<string, JassFunction>(StringComparer.Ordinal);
        foreach (var f in JassFunctionIndex.Parse(srcJ)) allByName.TryAdd(f.Name, f);
        var allSourceFns = allByName.Keys.ToHashSet(StringComparer.Ordinal);

        // Self-contain the carried script. A shared spellcast dispatcher that names our hero also
        // names every OTHER hero, so its body calls their handlers, which the closure excluded.
        // Carrying it verbatim would call functions that were never carried, and the target would
        // fail to compile (map will not load). We comment out each SAFE statement (call/set/local)
        // that invokes a non-carried function, leaving the hero's own branches intact. A non-carried
        // function named in a STRUCTURAL line (an if/loop condition or a return) cannot be commented
        // without breaking block nesting, so we carry it too, iterating to a fixpoint until the
        // carried set is closed. The net effect, only other heroes' plain call statements are cut,
        // and the ported script only ever calls carried functions or natives.
        var carried = new HashSet<string>(functions.Select(f => f.Name), StringComparer.Ordinal);

        // Also carry each InitTrig_* that turns on one of the hero's spells, plus the handlers it wires.
        // The closure only reaches functions the hero's DATA references; a spell's InitTrig (CreateTrigger
        // + TriggerAddAction of the handler) is reached only from the source's InitCustomTriggers, never
        // from the hero, so without this the spell comes across but is never registered — defined and dead
        // in the target, exactly the "abilities do nothing" break. An init counts as the hero's when it
        // wires a carried function, OR its handler tests a rawcode a carried function uses (an on-cast
        // trigger for a combo ability the hero adds at runtime, e.g. QShikiOne firing on 'A1BP' which the
        // carried learn handler grants). Spell ability ids are hero-unique, so this stays precise. Seeding
        // them lets the existing globals step pull in each gg_trg_* and the init hook call InitTrig_* in
        // the target. Fixpoint, bounded by the function count.
        CarryRegisteringInits(carried, allByName, srcLines, codeRemap.Keys);

        var residual = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, string> bodies;
        for (int guard = 0; ; guard++)
        {
            var dropped = new HashSet<string>(allSourceFns, StringComparer.Ordinal);
            dropped.ExceptWith(carried);
            residual.Clear();
            bodies = carried.Where(allByName.ContainsKey).ToDictionary(
                n => n, n => Trim(BodyText(srcLines, allByName[n]), dropped, residual), StringComparer.Ordinal);
            var toAdd = residual.Where(r => !carried.Contains(r) && allByName.ContainsKey(r)).ToList();
            if (toAdd.Count == 0 || guard > allSourceFns.Count) break; // converges, bounded by the function count
            foreach (var r in toAdd) carried.Add(r);
        }

        var srcFns = carried.Where(allByName.ContainsKey)
            .Select(n => allByName[n]).OrderBy(f => f.StartLine).ToList();
        if (srcFns.Count == 0) return new ScriptPortInfo(0, 0, 0, false, notes);

        int trimmedCalls = bodies.Values.Sum(CountTrimMarkers);
        if (trimmedCalls > 0)
            notes.Add($"trimmed {trimmedCalls} call(s) to non-carried functions out of the carried bodies "
                + "(other heroes' branches of a shared dispatcher), so the ported script is self-contained.");
        int pulledForStructure = srcFns.Count - functions.Count;
        if (pulledForStructure > 0)
            notes.Add($"carried {pulledForStructure} extra helper function(s) that the closure referenced from a "
                + "condition or return, so no call is left dangling.");
        if (srcFns.Count > 400)
            notes.Add($"{srcFns.Count} functions carried — the source script is highly coupled (likely a shared " +
                      "spell dispatcher), so a large fraction of it came along. Verify the ported map in-game.");

        // Globals referenced by the carried (trimmed) functions.
        var (srcGlobals, srcGlobalOrder) = ParseGlobals(srcLines);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in srcFns)
            foreach (Match m in Ident.Matches(bodies[f.Name]))
                if (srcGlobals.ContainsKey(m.Value)) used.Add(m.Value);
        // A used global's initializer can itself reference another global (constant chains),
        // so expand to a fixpoint over the used declarations. The set only grows and is
        // bounded by the global count, so the loop terminates.
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var g in used.ToList())
                foreach (Match m in Ident.Matches(srcGlobals[g]))
                    if (srcGlobals.ContainsKey(m.Value) && used.Add(m.Value)) grew = true;
        }
        var carriedGlobals = srcGlobalOrder.Where(used.Contains).ToList();

        // Symbols the target already defines (functions + its own globals).
        var targetSymbols = JassFunctionIndex.Parse(tgtJ).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var g in ParseGlobals(tgtJ.Replace("\r\n", "\n").Split('\n')).Item1.Keys) targetSymbols.Add(g);

        // Rename any carried symbol that collides with the target (or with common.j-ish
        // reserved names we can't see); rewrite references within the ported block only.
        var rename = new Dictionary<string, string>(StringComparer.Ordinal);
        var carriedSymbols = srcFns.Select(f => f.Name).Concat(carriedGlobals).ToList();
        var taken = new HashSet<string>(targetSymbols, StringComparer.Ordinal);
        foreach (var name in carriedSymbols)
        {
            if (!taken.Contains(name)) { taken.Add(name); continue; }
            string fresh = name;
            int n = 1;
            while (taken.Contains(fresh)) fresh = $"{name}_p{n++}";
            rename[name] = fresh;
            taken.Add(fresh);
        }

        string Rewrite(string code) => RewriteRawcodes(ApplyRenames(code, rename, carriedSymbols), codeRemap);

        // Build the ported globals + functions text.
        var portedGlobals = new StringBuilder();
        foreach (var g in carriedGlobals)
            portedGlobals.Append("    ").Append(Rewrite(srcGlobals[g].Trim())).Append('\n');

        var portedFns = new StringBuilder();
        foreach (var f in srcFns)
            portedFns.Append(Rewrite(bodies[f.Name])).Append('\n');

        // A non-carried function named in a condition or return could not be safely commented out
        // without breaking block structure, so it was left in place. Flag it (best-effort port).
        if (residual.Count > 0)
            notes.Add($"{residual.Count} reference(s) to non-carried function(s) remain in a condition or "
                + $"return and were left in place, verify the ported map ({string.Join(", ", residual.Take(8))}).");

        // Splice into the target: globals into its globals block, functions after endglobals.
        string merged = Splice(tgtJ, portedGlobals.ToString(), portedFns.ToString(), marker, notes);

        // Best-effort init hook: call carried InitTrig_* functions from InitCustomTriggers.
        var initFns = srcFns.Select(f => f.Name)
            .Where(nm => nm.StartsWith("InitTrig_", StringComparison.Ordinal))
            .Select(nm => rename.GetValueOrDefault(nm, nm)).ToList();
        bool hooked = false;
        if (initFns.Count > 0)
        {
            merged = HookInit(merged, initFns, marker, out hooked);
            notes.Add(hooked
                ? $"wired {initFns.Count} InitTrig_* function(s) into the target's InitCustomTriggers."
                : "carried InitTrig_* functions but could not find InitCustomTriggers in the target — " +
                  "trigger registration may need to be wired manually.");
        }
        else
        {
            notes.Add("no InitTrig_* init functions in the closure — if the spells register via a custom " +
                      "init path, verify it runs in the target.");
        }

        if (apply)
            target.AddOrReplaceRawFile(tgtEntry.FileName!, ByteText.GetBytes(merged));
        return new ScriptPortInfo(srcFns.Count, carriedGlobals.Count, rename.Count, hooked, notes);
    }

    private const string TrimMarker = "//[wc3ctl trimmed] ";

    /// <summary>Comments out each safe statement (call/set/local/debug) that invokes or references a
    /// source function we are NOT carrying, so a carried body only ever calls carried functions or
    /// natives. Structural lines (an if/loop condition or a return) that name a non-carried function
    /// are left in place to preserve block nesting, and recorded in <paramref name="residual"/>.</summary>
    private static string Trim(string body, IReadOnlySet<string> dropped, HashSet<string> residual)
    {
        if (dropped.Count == 0) return body;
        var sb = new StringBuilder();
        foreach (var line in body.Split('\n'))
        {
            var refs = DroppedRefs(StripComment(line), dropped);
            if (refs.Count == 0) { sb.Append(line).Append('\n'); continue; }
            var head = line.TrimStart();
            bool safe = head.StartsWith("call ", StringComparison.Ordinal)
                || head.StartsWith("set ", StringComparison.Ordinal)
                || head.StartsWith("local ", StringComparison.Ordinal)
                || head.StartsWith("debug ", StringComparison.Ordinal);
            if (safe)
            {
                sb.Append(TrimMarker).Append(line).Append('\n');
            }
            else
            {
                foreach (var r in refs) residual.Add(r);
                sb.Append(line).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static int CountTrimMarkers(string body)
    {
        int count = 0, i = 0;
        while ((i = body.IndexOf(TrimMarker, i, StringComparison.Ordinal)) >= 0) { count++; i += TrimMarker.Length; }
        return count;
    }

    /// <summary>Names of dropped functions this line invokes ("Foo(") or references ("function Foo").</summary>
    private static List<string> DroppedRefs(string code, IReadOnlySet<string> dropped)
    {
        var found = new List<string>();
        Match? prev = null;
        foreach (Match m in Ident.Matches(code))
        {
            if (dropped.Contains(m.Value) && (FollowedByOpenParen(code, m) || prev is { Value: "function" }))
                found.Add(m.Value);
            prev = m;
        }
        return found;
    }

    private static string StripComment(string line)
    {
        int i = line.IndexOf("//", StringComparison.Ordinal);
        return i >= 0 ? line[..i] : line;
    }

    private static bool FollowedByOpenParen(string s, Match m)
    {
        int i = m.Index + m.Length;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
        return i < s.Length && s[i] == '(';
    }

    /// <summary>
    /// Adds to <paramref name="carried"/> every <c>InitTrig_*</c> that turns on one of the hero's spells,
    /// together with the handler functions it registers. An init qualifies when it wires a carried
    /// function, or when a handler it wires tests a rawcode that a carried function references (the
    /// on-cast trigger for a combo ability the hero grants at runtime, which the data-driven closure
    /// cannot reach because nothing but the init names it). Handlers pulled in this way feed their own
    /// rawcodes back into the live set, so a combo chain is followed to a fixpoint. Bounded by the
    /// function count; spell ability ids are hero-unique, so unrelated heroes' inits are not pulled in.
    /// </summary>
    private static void CarryRegisteringInits(
        HashSet<string> carried, IReadOnlyDictionary<string, JassFunction> allByName, string[] srcLines,
        IEnumerable<string> seedRawcodes)
    {
        var rawcodesOf = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        HashSet<string> RawcodesOf(string n) => rawcodesOf.TryGetValue(n, out var s)
            ? s
            : rawcodesOf[n] = RawcodeLiterals(BodyText(srcLines, allByName[n]));

        // Each InitTrig_* -> the source functions it references (the handlers it registers).
        var initHandlers = allByName.Values
            .Where(f => f.Name.StartsWith("InitTrig_", StringComparison.Ordinal))
            .ToDictionary(
                f => f.Name,
                f => ReferencedNames(BodyText(srcLines, f)).Where(allByName.ContainsKey).Distinct().ToList(),
                StringComparer.Ordinal);

        // Rawcodes tied to THIS hero: the ported objects, plus every rawcode the hero's own carried
        // functions name (its abilities and the combo abilities its handlers grant at runtime, like
        // 'A1BP'). Fixed on purpose, we do NOT fold in rawcodes from initializers pulled in below.
        // Doing so cascades in a dense arena map (one hero's init shares a rawcode with another's, and
        // the set snowballs to the whole map). Every combo ability the hero uses is granted by a base
        // closure handler, so it is already here without the cascade.
        var live = new HashSet<string>(seedRawcodes, StringComparer.Ordinal);
        foreach (var n in carried.Where(allByName.ContainsKey))
            live.UnionWith(RawcodesOf(n));

        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var (init, handlers) in initHandlers)
            {
                if (carried.Contains(init)) continue;
                // The hero's own init: it wires a handler we already carry, or wires a handler that
                // fires on one of the hero's rawcodes. Unrelated heroes' inits fire on their own
                // (different) ability ids, so they are not pulled in.
                bool wanted = handlers.Any(carried.Contains)
                    || handlers.Any(h => RawcodesOf(h).Overlaps(live));
                if (!wanted) continue;
                carried.Add(init);
                grew = true;
                // Carry the handlers this init registers so the trigger is not left dangling. Their
                // rawcodes are deliberately NOT added to the live set (see above).
                foreach (var h in handlers)
                    if (carried.Add(h)) grew = true;
            }
        }
    }

    /// <summary>The 4-character rawcode literals ('A1BP') a snippet names.</summary>
    private static HashSet<string> RawcodeLiterals(string code)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Rawcode.Matches(code))
            if (m.Groups[1].Value.Length == 4) set.Add(m.Groups[1].Value);
        return set;
    }

    /// <summary>Identifiers used as a function reference in a snippet ("Foo(" or "function Foo").</summary>
    private static IEnumerable<string> ReferencedNames(string code)
    {
        Match? prev = null;
        foreach (Match m in Ident.Matches(code))
        {
            if (FollowedByOpenParen(code, m) || prev is { Value: "function" }) yield return m.Value;
            prev = m;
        }
    }

    private static MapFileEntry? ScriptEntry(MapDocument doc) =>
        doc.GetFile("war3map.j") ?? doc.GetFile("scripts\\war3map.j");

    private static string BodyText(string[] lines, JassFunction f)
    {
        var sb = new StringBuilder();
        for (int i = f.StartLine - 1; i < f.EndLine && i < lines.Length; i++)
            sb.Append(lines[i]).Append('\n');
        return sb.ToString();
    }

    /// <summary>Global name → its full declaration line, plus declaration order.</summary>
    private static (Dictionary<string, string>, List<string>) ParseGlobals(string[] lines)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();
        bool inBlock = false;
        foreach (var raw in lines)
        {
            var t = raw.Trim();
            if (!inBlock) { if (t == "globals") inBlock = true; continue; }
            if (t == "endglobals") { inBlock = false; continue; }
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal)) continue;
            var name = GlobalName(t);
            if (name is not null && !map.ContainsKey(name)) { map[name] = t; order.Add(name); }
        }
        return (map, order);
    }

    private static string? GlobalName(string decl)
    {
        var toks = decl.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        if (i < toks.Length && toks[i] == "constant") i++;
        i++; // type
        if (i < toks.Length && toks[i] == "array") i++;
        if (i >= toks.Length) return null;
        return toks[i].Split('=')[0].Trim();
    }

    private static string ApplyRenames(string code, IReadOnlyDictionary<string, string> rename, IEnumerable<string> _)
    {
        if (rename.Count == 0) return code;
        return Ident.Replace(code, m => rename.TryGetValue(m.Value, out var r) ? r : m.Value);
    }

    private static string RewriteRawcodes(string code, IReadOnlyDictionary<string, string> remap)
    {
        if (remap.Count == 0) return code;
        return Rawcode.Replace(code, m =>
        {
            var inner = m.Groups[1].Value;
            return inner.Length == 4 && remap.TryGetValue(inner, out var to) ? $"'{to}'" : m.Value;
        });
    }

    private static string Splice(string tgtJ, string portedGlobals, string portedFns, string marker, List<string> notes)
    {
        string nl = tgtJ.Contains("\r\n") ? "\r\n" : "\n";
        var text = tgtJ.Replace("\r\n", "\n");
        string block = $"\n// ==== BEGIN {marker} ====\n{portedFns}// ==== END {marker} ====\n";

        int endGlobals = FindLine(text, "endglobals");
        if (endGlobals >= 0 && portedGlobals.Length > 0)
        {
            int insertAt = text.LastIndexOf('\n', endGlobals);
            string gblock = $"\n    // ==== {marker} (globals) ====\n{portedGlobals}";
            text = text.Insert(insertAt < 0 ? endGlobals : insertAt, gblock);
            endGlobals = FindLine(text, "endglobals"); // shifted
        }
        else if (portedGlobals.Length > 0)
        {
            // No globals block in target: prepend one.
            text = $"globals\n{portedGlobals}endglobals\n" + text;
            notes.Add("target had no globals block — one was created for the ported globals.");
            endGlobals = FindLine(text, "endglobals");
        }

        // Functions go right after endglobals (so target code can call them; JASS is top-down).
        int after = endGlobals >= 0 ? text.IndexOf('\n', endGlobals) + 1 : 0;
        text = text.Insert(after, block);
        return text.Replace("\n", nl);
    }

    private static string HookInit(string mergedJ, IReadOnlyList<string> initFns, string marker, out bool hooked)
    {
        hooked = false;
        string nl = mergedJ.Contains("\r\n") ? "\r\n" : "\n";
        var text = mergedJ.Replace("\r\n", "\n");
        var idx = JassFunctionIndex.Parse(text)
            .FirstOrDefault(f => f.Name == "InitCustomTriggers");
        if (idx is null) return mergedJ;

        var lines = text.Split('\n').ToList();
        var calls = initFns.Select(fn => $"    call {fn}() // {marker}").ToList();
        lines.InsertRange(idx.EndLine - 1, calls); // before the "endfunction" line
        hooked = true;
        return string.Join('\n', lines).Replace("\n", nl);
    }

    private static int FindLine(string text, string exactTrimmed)
    {
        int pos = 0;
        foreach (var line in text.Split('\n'))
        {
            if (line.Trim() == exactTrimmed) return pos;
            pos += line.Length + 1;
        }
        return -1;
    }
}
