// src/Wc3.Commands/SynthDispatchBuilder.cs
using System.Text;
using System.Text.RegularExpressions;
using Wc3.Model;

namespace Wc3.Commands;

/// <summary>
/// Reads one hero's OWN branch out of a source map's shared spellcast dispatcher (the
/// "if GetUnitTypeId(c) == Hero_ID then if id == HeroQ_ID then call ... endif endif" shape a GUI
/// hero arena commonly compiles down to) and turns it into the calls a fresh, self-contained
/// dispatcher needs, verbatim.
///
/// Why this exists: the shared dispatcher a hero's own cast logic lives inside is written to
/// satisfy every gate the SOURCE map wired around it, a placed-hero registration array, a map rect
/// that stays null off the source map, a cooldown hashtable. Carrying that dispatcher whole
/// carries every one of those gates too, and a ported hero fails every one of them at once. The
/// hero's OWN branch names exactly which handler each of its abilities calls, so reading it (never
/// executing it) is enough to synthesize a minimal dispatcher that skips every gate entirely,
/// see <see cref="ScriptPorter"/> for where this is spliced in (opt-in, behind --synth-dispatch).
///
/// Pure text analysis, same idiom as <see cref="ScriptPorter"/> and <see cref="BundleCommand"/>,
/// deliberately conservative, never assumes the shape and reports null rather than guessing.
/// </summary>
internal static class SynthDispatchBuilder
{
    /// <summary>One ability the hero's own branch dispatches: its rawcode, and the exact
    /// "call Foo(...)" statement(s) that branch runs, copied byte-for-byte (arguments untouched)
    /// from the source. <see cref="Callees"/> are the function names those calls invoke, fed back
    /// into the porter's carry set so a combo ability granted only through a shared runtime helper
    /// (invisible to a plain call-graph walk) is still carried.</summary>
    public sealed record CastBranch(string AbilityRawcode, IReadOnlyList<string> Calls, IReadOnlyList<string> Callees);

    private static readonly Regex HeroGuardEq = new(
        @"GetUnitTypeId\s*\([^)]*\)\s*==\s*(?<tokA>'[^']{4}'|[A-Za-z_][A-Za-z0-9_]*)"
        + @"|(?<tokB>'[^']{4}'|[A-Za-z_][A-Za-z0-9_]*)\s*==\s*GetUnitTypeId\s*\([^)]*\)",
        RegexOptions.Compiled);

    private static readonly Regex TokenEq = new(
        @"(?<a>'[^']{4}'|[A-Za-z_][A-Za-z0-9_]*)\s*==\s*(?<b>'[^']{4}'|[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    private static readonly Regex CalleeName = new(
        @"^call\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// Every ability branch found under <paramref name="heroRawcode"/>'s own
    /// "GetUnitTypeId(...) == HeroId" guard(s), anywhere in <paramref name="sourceJass"/>, merged
    /// and de-duplicated by ability rawcode (first occurrence wins). Null when the script has no
    /// such guard for this hero at all (an unsupported dispatch shape, or the hero casts through
    /// something other than a shared id-comparison dispatcher) — the caller must not synthesize
    /// anything in that case, not guess. An empty (non-null) result never happens; a guard with
    /// zero resolvable ability branches is treated the same as "no guard found".
    /// </summary>
    public static IReadOnlyList<CastBranch>? ExtractHeroCastBranches(string sourceJass, string heroRawcode)
    {
        if (string.IsNullOrEmpty(sourceJass)) return null;

        var aliases = JassRawcodeAliases.Parse(sourceJass);
        var lines = sourceJass.Replace("\r\n", "\n").Split('\n');
        var functions = JassFunctionIndex.Parse(sourceJass);

        var result = new List<CastBranch>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var f in functions)
        {
            int start = f.StartLine - 1, end = Math.Min(f.EndLine, lines.Length);
            var openIfs = new Stack<int>(); // line index of each currently-open "if", for "elseif"

            for (int i = start; i < end; i++)
            {
                var head = lines[i].TrimStart();
                if (StartsWithWord(head, "if"))
                {
                    openIfs.Push(i);
                    if (TryFindHeroBranch(lines, i, i, heroRawcode, aliases) is { } branch)
                        foreach (var cb in ExtractAbilityBranches(lines, branch.BodyStart, branch.BodyEnd, aliases))
                            if (seen.Add(cb.AbilityRawcode)) result.Add(cb);
                }
                else if (StartsWithWord(head, "elseif"))
                {
                    if (openIfs.Count > 0
                        && TryFindHeroBranch(lines, openIfs.Peek(), i, heroRawcode, aliases) is { } branch)
                        foreach (var cb in ExtractAbilityBranches(lines, branch.BodyStart, branch.BodyEnd, aliases))
                            if (seen.Add(cb.AbilityRawcode)) result.Add(cb);
                }
                else if (StartsWithWord(head, "endif"))
                {
                    if (openIfs.Count > 0) openIfs.Pop();
                }
            }
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>The bare "trigger gg_trg_..." declaration <paramref name="triggerGlobal"/> needs in
    /// the target's globals block (no initializer, JASS convention for every gg_trg_* the World
    /// Editor itself emits, and how HeroWiringAudit's static analysis recognizes an attached
    /// trigger, its own name is not enough on its own). The caller adds it alongside the other
    /// carried globals.</summary>
    public static string BuildGlobalDeclaration(string triggerGlobal) => "trigger " + triggerGlobal;

    /// <summary>Builds the raw (pre-rename, pre-rawcode-remap) text of the synthesized cast
    /// dispatcher: a fresh function testing <c>GetSpellAbilityId()</c> against each branch's
    /// literal rawcode and running its call(s) verbatim, plus a companion init function that
    /// assigns <paramref name="triggerGlobal"/> its own trigger and registers it for every
    /// player's spell-effect event (an any-unit registration, so it needs no per-player wiring and
    /// no placed-hero registration array). Named with the gg_trg_ prefix (not a plain local), the
    /// same convention every World-Editor-generated trigger uses, so this reads as a real,
    /// end-to-end wired trigger to anything that looks for one, this codebase's own
    /// HeroWiringAudit included, not just to a human reading the script.
    /// The caller is expected to run this through the SAME rename/rawcode-remap pass every other
    /// carried body gets, so a collision-driven rename of a callee or a remap of an ability's
    /// rawcode (from the object port) is picked up exactly like anywhere else.</summary>
    public static string BuildRawText(string dispatchFunctionName, string initFunctionName,
        string triggerGlobal, IReadOnlyList<CastBranch> branches)
    {
        var sb = new StringBuilder();
        sb.Append("function ").Append(dispatchFunctionName).Append(" takes nothing returns nothing\n");
        sb.Append("    local unit c = GetSpellAbilityUnit()\n");
        sb.Append("    local integer id = GetSpellAbilityId()\n");
        sb.Append("    local real x = GetSpellTargetX()\n");
        sb.Append("    local real y = GetSpellTargetY()\n");
        sb.Append("    local unit td = GetSpellTargetUnit()\n");
        for (int i = 0; i < branches.Count; i++)
        {
            sb.Append("    ").Append(i == 0 ? "if" : "elseif").Append(" id == '")
              .Append(branches[i].AbilityRawcode).Append("' then\n");
            foreach (var call in branches[i].Calls)
                sb.Append("        ").Append(call).Append('\n');
        }
        if (branches.Count > 0) sb.Append("    endif\n");
        sb.Append("    set c = null\n");
        sb.Append("    set td = null\n");
        sb.Append("endfunction\n");

        sb.Append("function ").Append(initFunctionName).Append(" takes nothing returns nothing\n");
        sb.Append("    set ").Append(triggerGlobal).Append(" = CreateTrigger()\n");
        sb.Append("    call TriggerAddAction(").Append(triggerGlobal).Append(", function ")
          .Append(dispatchFunctionName).Append(")\n");
        sb.Append("    call TriggerRegisterAnyUnitEventBJ(").Append(triggerGlobal)
          .Append(", EVENT_PLAYER_UNIT_SPELL_EFFECT)\n");
        sb.Append("endfunction\n");
        return sb.ToString();
    }

    /// <summary>A safe JASS identifier fragment out of a rawcode (or anything else): non
    /// alphanumeric bytes become underscores, and a leading digit gets an underscore in front
    /// (JASS identifiers cannot start with one).</summary>
    public static string SanitizeIdentifier(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }

    // ---- one hero guard --------------------------------------------------------------------

    private sealed record HeroBranch(int BodyStart, int BodyEnd);

    /// <summary>When the "if"/"elseif" at <paramref name="conditionLine"/> guards on
    /// <paramref name="heroRawcode"/>, splits the chain it opens (starting at
    /// <paramref name="chainStart"/>) and returns that one branch's body range. Null otherwise, or
    /// when the chain never closes (malformed input).</summary>
    private static HeroBranch? TryFindHeroBranch(
        IReadOnlyList<string> lines, int chainStart, int conditionLine, string heroRawcode,
        IReadOnlyDictionary<string, string> aliases)
    {
        var condition = ConditionOf(lines[conditionLine]);
        if (!condition.Contains("GetUnitTypeId", StringComparison.Ordinal)) return null;
        var m = HeroGuardEq.Match(condition);
        if (!m.Success) return null;
        var tok = m.Groups["tokA"].Success ? m.Groups["tokA"].Value : m.Groups["tokB"].Value;
        if (!string.Equals(Resolve(tok, aliases), heroRawcode, StringComparison.Ordinal)) return null;

        if (SplitIfChain(lines, chainStart) is not { } chain) return null;
        var mine = chain.Branches.FirstOrDefault(b => b.ConditionLine == conditionLine);
        return mine is null ? null : new HeroBranch(mine.BodyStart, mine.BodyEnd);
    }

    /// <summary>Every ability sub-branch inside a hero's own guard body: every depth-0 if/elseif
    /// chain whose OWN condition resolves an ability rawcode, merged and de-duplicated. Depth-0
    /// here means relative to the branch body (an "if" nested one level deeper, inside an ability's
    /// own branch, is not split further — its calls are read as part of that ability's body by
    /// <see cref="ExtractCalls"/>, which scans the whole range flatly).</summary>
    private static List<CastBranch> ExtractAbilityBranches(
        IReadOnlyList<string> lines, int bodyStart, int bodyEnd, IReadOnlyDictionary<string, string> aliases)
    {
        var result = new List<CastBranch>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int depth = 0;
        for (int i = bodyStart; i < bodyEnd; i++)
        {
            var head = lines[i].TrimStart();
            if (StartsWithWord(head, "if"))
            {
                if (depth == 0 && ResolveAbilityRawcode(ConditionOf(lines[i]), aliases) is not null
                    && SplitIfChain(lines, i) is { } chain)
                    foreach (var cb in BuildCastBranches(lines, chain.Branches, aliases))
                        if (seen.Add(cb.AbilityRawcode)) result.Add(cb);
                depth++;
            }
            else if (StartsWithWord(head, "endif"))
            {
                depth--;
            }
        }
        return result;
    }

    private static List<CastBranch> BuildCastBranches(
        IReadOnlyList<string> lines, IReadOnlyList<IfBranch> branches, IReadOnlyDictionary<string, string> aliases)
    {
        var result = new List<CastBranch>();
        foreach (var b in branches)
        {
            if (b.Condition.Length == 0) continue; // a trailing "else" names no ability
            var rawcode = ResolveAbilityRawcode(b.Condition, aliases);
            if (rawcode is null) continue;
            var calls = ExtractCalls(lines, b.BodyStart, b.BodyEnd);
            if (calls.Count == 0) continue; // nothing to synthesize (e.g. bookkeeping only)
            var callees = calls.Select(CalleeOf).Where(c => c is not null).Select(c => c!)
                .Distinct(StringComparer.Ordinal).ToList();
            result.Add(new CastBranch(rawcode, calls, callees));
        }
        return result;
    }

    // ---- statement extraction ---------------------------------------------------------------

    /// <summary>Every "call Foo(...)" statement in [start, end), flat (nested if/loop structure
    /// inside the range is not split further, every call in it still counts). Revives a line this
    /// tool itself commented out on some EARLIER port (its own <see cref="JassScriptCheck.TrimMarker"/>
    /// prefix, which only ever wraps the exact original statement, never deletes it), but leaves a
    /// genuinely author-commented line alone — reviving the map author's own intentionally disabled
    /// code would not be ours to do.</summary>
    private static List<string> ExtractCalls(IReadOnlyList<string> lines, int start, int end)
    {
        var result = new List<string>();
        for (int i = start; i < end; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith(JassScriptCheck.TrimMarker, StringComparison.Ordinal))
                trimmed = trimmed[JassScriptCheck.TrimMarker.Length..].TrimStart();
            else if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue; // genuinely disabled by the map author, not ours to revive
            if (!trimmed.StartsWith("call ", StringComparison.Ordinal)) continue;
            if (ExtractOneCall(trimmed) is { } call) result.Add(call);
        }
        return result;
    }

    /// <summary>"call Foo(a, b)  // trailing comment" to "call Foo(a, b)", parens balanced so a
    /// nested call in an argument (Player(0)) does not truncate early. Null when the call's parens
    /// never balance on this line (a call that spans multiple lines is not handled, matching this
    /// codebase's existing single-line assumption for JASS statements, e.g. BundleCommand.CallArgs).</summary>
    private static string? ExtractOneCall(string trimmedLine)
    {
        int open = trimmedLine.IndexOf('(');
        if (open < 0) return null;
        int depth = 0;
        for (int i = open; i < trimmedLine.Length; i++)
        {
            if (trimmedLine[i] == '(') depth++;
            else if (trimmedLine[i] == ')')
            {
                depth--;
                if (depth == 0) return trimmedLine[..(i + 1)];
            }
        }
        return null;
    }

    private static string? CalleeOf(string callStatement) =>
        CalleeName.Match(callStatement) is { Success: true } m ? m.Groups[1].Value : null;

    private static string? ResolveAbilityRawcode(string condition, IReadOnlyDictionary<string, string> aliases)
    {
        var m = TokenEq.Match(condition);
        if (!m.Success) return null;
        return Resolve(m.Groups["a"].Value, aliases) ?? Resolve(m.Groups["b"].Value, aliases);
    }

    private static string? Resolve(string token, IReadOnlyDictionary<string, string> aliases) =>
        token.Length == 6 && token[0] == '\'' ? token[1..^1] : aliases.GetValueOrDefault(token);

    // ---- generic if/elseif/else/endif splitting ----------------------------------------------

    /// <summary>One sibling branch of an if/elseif/.../else/endif chain: the condition text of its
    /// own "if"/"elseif" line (empty for a trailing "else"), that line's index, and the [BodyStart,
    /// BodyEnd) range of lines up to the next sibling (or the chain's own "endif"), at the SAME
    /// nesting depth. A nested if/endif pair inside a branch is left whole, part of that branch's
    /// range, never split further.</summary>
    private sealed record IfBranch(string Condition, int ConditionLine, int BodyStart, int BodyEnd);

    /// <summary>Splits the if/elseif/.../else/endif chain opening at <paramref name="start"/>
    /// (lines[start]'s trimmed text must begin with the word "if") into its sibling branches, or
    /// null when the chain never closes within <paramref name="lines"/> (malformed/truncated input,
    /// never assumed).</summary>
    private static (List<IfBranch> Branches, int EndLine)? SplitIfChain(IReadOnlyList<string> lines, int start)
    {
        if (!StartsWithWord(lines[start].TrimStart(), "if")) return null;

        var branches = new List<IfBranch>();
        int depth = 0;
        int branchStart = start;
        string branchCondition = ConditionOf(lines[start]);

        for (int i = start + 1; i < lines.Count; i++)
        {
            var head = lines[i].TrimStart();
            if (StartsWithWord(head, "if")) { depth++; continue; }
            if (depth > 0)
            {
                if (StartsWithWord(head, "endif")) depth--;
                continue;
            }
            if (StartsWithWord(head, "elseif"))
            {
                branches.Add(new IfBranch(branchCondition, branchStart, branchStart + 1, i));
                branchStart = i;
                branchCondition = ConditionOf(lines[i]);
            }
            else if (StartsWithWord(head, "else"))
            {
                branches.Add(new IfBranch(branchCondition, branchStart, branchStart + 1, i));
                branchStart = i;
                branchCondition = "";
            }
            else if (StartsWithWord(head, "endif"))
            {
                branches.Add(new IfBranch(branchCondition, branchStart, branchStart + 1, i));
                return (branches, i + 1);
            }
        }
        return null; // never closed
    }

    /// <summary>The text of an "if"/"elseif" line with the leading keyword and trailing "then"
    /// stripped, and any line comment removed. "if id == AstaQ_ID then // note" -> "id == AstaQ_ID".</summary>
    private static string ConditionOf(string ifOrElseifLine)
    {
        var t = StripLineComment(ifOrElseifLine).Trim();
        int sp = t.IndexOf(' ');
        t = sp < 0 ? "" : t[(sp + 1)..].Trim();
        if (t.EndsWith("then", StringComparison.Ordinal)) t = t[..^4].TrimEnd();
        return t;
    }

    private static string StripLineComment(string line)
    {
        int i = line.IndexOf("//", StringComparison.Ordinal);
        return i >= 0 ? line[..i] : line;
    }

    private static bool StartsWithWord(string s, string word)
    {
        if (!s.StartsWith(word, StringComparison.Ordinal)) return false;
        if (s.Length == word.Length) return true;
        char c = s[word.Length];
        return !(char.IsLetterOrDigit(c) || c == '_');
    }
}
