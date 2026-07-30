// src/Wc3.Commands/HeroWiringAudit.cs
using System.Text;
using System.Text.RegularExpressions;
using War3Net.Build.Widget;
using War3Net.Common.Extensions;
using Wc3.Model;

namespace Wc3.Commands;

/// <summary>Which link of the wiring chain is missing for one ability. <see cref="Ok"/> means every
/// prerequisite this tool can check statically is satisfied.</summary>
public enum WiringStatus
{
    Ok,
    /// <summary>The ability object itself is not in the map, so the hero can never hold it.</summary>
    AbilityObjectMissing,
    /// <summary>No code compares GetSpellAbilityId() against this ability, so a cast reaches nothing.</summary>
    NoDispatch,
    /// <summary>The handler exists but is not attached to any trigger.</summary>
    NotAttachedToTrigger,
    /// <summary>The trigger has no spell event at all, so it never fires.</summary>
    NoEventRegistered,
    /// <summary>The trigger's event is per-player and this hero's player is not among them.</summary>
    PlayerNotRegistered,
    /// <summary>The handler body is empty, or every statement in it was commented out by porting.</summary>
    EmptyOrTrimmedHandler,
    /// <summary>The handler only acts on the unit held in a per-player array this hero is absent from.</summary>
    IdentityArrayNotRegistered,
    /// <summary>Nothing calls the init that creates the trigger, so it is never built.</summary>
    InitNeverCalled,
    /// <summary>Two calls to the init build two triggers, so the ability fires twice.</summary>
    InitCalledTwice,
}

public sealed record AbilityWiring(string Ability, string? Name, WiringStatus Status, string Detail);

public sealed record HeroWiringResult(
    string Hero, string? Name, int OwnerId, IReadOnlyList<AbilityWiring> Abilities)
{
    public int Wired => Abilities.Count(a => a.Status == WiringStatus.Ok);
    public IReadOnlyList<AbilityWiring> Problems =>
        Abilities.Where(a => a.Status != WiringStatus.Ok).ToList();
}

/// <summary>
/// Verifies, for every ability of a placed hero, that the map actually wires it up.
///
/// This exists because a ported hero can fail to cast for many unrelated reasons (a missing ability
/// object, a dispatch that was never carried, a trigger with no event, an event registered for the
/// wrong player, a handler whose body was trimmed away, a per-player identity array the hero is
/// absent from, an init nobody calls, or one two callers build twice). Each of those was previously
/// found by playing the map and noticing a dead spell. All of them are decidable from the script, so
/// they are decided here instead, and the specific missing link is named.
/// </summary>
public static class HeroWiringAudit
{
    /// <summary>Audits every distinct hero type placed on the map.</summary>
    public static IReadOnlyList<HeroWiringResult> AuditPlacedHeroes(MapDocument doc)
    {
        var units = doc.GetFile(PlacementCommand.UnitsFile)?.Model as MapUnits;
        if (units is null) return Array.Empty<HeroWiringResult>();

        // A hero's rawcode starts uppercase by World Editor convention (Hpal/H000 vs hfoo/h000).
        var placed = units.Units
            .Where(u => u.OwnerId >= 0 && u.OwnerId < PlayerColors.NeutralHostileId)
            .Select(u => (Rawcode: u.TypeId.ToRawcode(), u.OwnerId))
            .Where(x => x.Rawcode.Length == 4 && char.IsUpper(x.Rawcode[0]))
            .GroupBy(x => x.Rawcode, StringComparer.Ordinal)
            .Select(g => (Rawcode: g.Key, OwnerId: g.First().OwnerId))
            .ToList();

        return placed.Select(p => Audit(doc, p.Rawcode, p.OwnerId)).ToList();
    }

    /// <summary>Audits one hero type as owned by <paramref name="ownerId"/>.</summary>
    public static HeroWiringResult Audit(MapDocument doc, string heroRawcode, int ownerId)
    {
        var entry = doc.GetFile(PreplacedUnitsScript.ScriptFile);
        byte[]? bytes = entry?.OverrideBytes ?? entry?.RawBytes;
        string jass = bytes is { Length: > 0 } ? Encoding.Latin1.GetString(bytes) : "";

        var ctx = new ScriptContext(jass, ownerId, doc);
        var abilityNames = AbilityNames(doc);
        var results = new List<AbilityWiring>();

        foreach (var ability in HeroAbilities(doc, heroRawcode, ctx))
        {
            abilityNames.TryGetValue(ability, out var name);
            results.Add(CheckAbility(ability, name, ctx));
        }

        string? heroName = UnitNames(doc).TryGetValue(heroRawcode, out var hn) ? hn : null;
        return new(heroRawcode, heroName, ownerId,
            results.OrderBy(r => r.Status == WiringStatus.Ok ? 1 : 0)
                   .ThenBy(r => r.Ability, StringComparer.Ordinal).ToList());
    }

    // ---- the chain -----------------------------------------------------------

    private static AbilityWiring CheckAbility(string ability, string? name, ScriptContext ctx)
    {
        AbilityWiring Fail(WiringStatus s, string detail) => new(ability, name, s, detail);

        // 1. The object must exist, else the hero cannot even hold the ability. This is the class
        //    that hid a dash-back ability the script adds at runtime.
        if (!ctx.CustomAbilities.Contains(ability) && !ctx.BaseLikeAbility(ability))
            return Fail(WiringStatus.AbilityObjectMissing,
                "the ability object is not in this map, port it from the source");

        // 2. Something must dispatch on it, by literal or through an id global.
        var dispatch = ctx.FindDispatch(ability);
        if (dispatch is null)
            return Fail(WiringStatus.NoDispatch,
                "no code compares GetSpellAbilityId() against this ability, its handler was not carried");

        // 3. The dispatch function must hang off a trigger.
        var trigger = ctx.TriggerFor(dispatch);
        if (trigger is null)
            return Fail(WiringStatus.NotAttachedToTrigger,
                $"handler '{dispatch}' is never attached to a trigger");

        // 4. That trigger needs a spell event that covers this hero's player.
        var (hasEvent, coversPlayer, eventDetail) = ctx.EventCoverage(trigger);
        if (!hasEvent)
            return Fail(WiringStatus.NoEventRegistered, $"trigger '{trigger}' has no spell event");
        if (!coversPlayer)
            return Fail(WiringStatus.PlayerNotRegistered,
                $"trigger '{trigger}' registers its spell event per player and player {ctx.OwnerId} is not one of them ({eventDetail})");

        // 5. The code that runs must actually do something.
        if (!ctx.HasLiveBody(trigger, dispatch))
            return Fail(WiringStatus.EmptyOrTrimmedHandler,
                $"every statement behind trigger '{trigger}' is empty or was commented out by porting");

        // 6. If the handler gates on "this player's hero", the hero must be in that array.
        if (ctx.IdentityGate(dispatch) is { } gate && !ctx.RegisteredArrays.Contains(gate))
            return Fail(WiringStatus.IdentityArrayNotRegistered,
                $"handler '{dispatch}' only acts on the unit in {gate}[...], which this hero is never put into");

        // 7. The trigger has to be built exactly once. Zero means it never exists, two means the
        //    ability fires twice (doubled damage and effects).
        int calls = ctx.InitCallCount(trigger);
        if (calls == 0)
            return Fail(WiringStatus.InitNeverCalled,
                $"nothing calls the init that creates trigger '{trigger}'");
        if (calls > 1)
            return Fail(WiringStatus.InitCalledTwice,
                $"the init for trigger '{trigger}' is called {calls} times, so this ability fires {calls} times");

        return new(ability, name, WiringStatus.Ok, $"dispatched by '{dispatch}' on '{trigger}'");
    }

    // ---- the hero's ability set ---------------------------------------------

    /// <summary>The hero's abilities from its object fields (normal + hero + spellbook contents),
    /// plus any the script grants at runtime with <c>UnitAddAbility</c>. That last source is the one
    /// object data alone cannot show, and it is where a combo or dash-back ability hides.</summary>
    private static IEnumerable<string> HeroAbilities(MapDocument doc, string heroRawcode, ScriptContext ctx)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddList(string? csv)
        {
            foreach (var raw in (csv ?? "").Split(','))
            {
                var t = raw.Trim();
                if (t.Length == 4 && seen.Add(t)) found.Add(t);
            }
        }

        var unitFields = Fields(doc, ObjectKind.Unit, heroRawcode);
        AddList(unitFields.GetValueOrDefault("uabi"));
        AddList(unitFields.GetValueOrDefault("uhab"));

        // Spellbooks hold their real abilities in spb1, so follow one level (that is how these maps
        // hang a hero's kit off a single granted book).
        foreach (var ability in found.ToList())
            foreach (var kv in Fields(doc, ObjectKind.Ability, ability))
                if (kv.Key.StartsWith("spb1", StringComparison.OrdinalIgnoreCase))
                    AddList(kv.Value);

        // Runtime grants: UnitAddAbility(u, 'A1R6') or UnitAddAbility(u, SomeQ2_ID).
        foreach (Match m in Regex.Matches(ctx.CodeText,
            @"UnitAddAbility\s*\([^,]+,\s*(?:'([^']{4})'|([A-Za-z_][A-Za-z0-9_]*))\s*\)"))
        {
            string? id = m.Groups[1].Success ? m.Groups[1].Value
                : ctx.Aliases.GetValueOrDefault(m.Groups[2].Value);
            // Only the ones this hero's own kit references, so a map-wide scan does not pull in
            // every other hero's grants. An id global named after the hero counts, as does an
            // ability already in the set.
            if (id is null || !seen.Add(id)) continue;
            if (ctx.CustomAbilities.Contains(id) && ctx.SharesNamingGroup(heroRawcode, m.Groups[2].Value))
                found.Add(id);
            else seen.Remove(id);
        }
        return found;
    }

    private static Dictionary<string, string> Fields(MapDocument doc, ObjectKind kind, string rawcode)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var info = ObjectKinds.Info(kind);
        int id = rawcode.FromRawcode();
        foreach (var e in ObjectKinds.MergedEntries(doc, info))
        {
            if (e.Id != id) continue;
            foreach (var mod in e.Mods) result[mod.Key] = mod.Value;
        }
        return result;
    }

    private static Dictionary<string, string> AbilityNames(MapDocument doc) => Names(doc, ObjectKind.Ability);
    private static Dictionary<string, string> UnitNames(MapDocument doc) => Names(doc, ObjectKind.Unit);

    private static Dictionary<string, string> Names(MapDocument doc, ObjectKind kind)
    {
        var info = ObjectKinds.Info(kind);
        var strings = MapStrings.From(doc);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in ObjectKinds.MergedEntries(doc, info))
            if (ObjectKinds.DeltaName(ObjectKinds.ModsToDict(e.Mods), info, strings) is { } n)
                result[e.Id.ToRawcode()] = n;
        return result;
    }

    // ---- script analysis ----------------------------------------------------

    /// <summary>Everything the checks need from the script, parsed once per hero.</summary>
    private sealed class ScriptContext
    {
        public int OwnerId { get; }
        public string CodeText { get; }
        public Dictionary<string, string> Aliases { get; }
        public HashSet<string> CustomAbilities { get; }
        public HashSet<string> RegisteredArrays { get; }

        private readonly string[] _code;                       // comment-stripped, same indices
        private readonly IReadOnlyList<JassFunction> _functions;
        private readonly Dictionary<string, string> _bodies;    // function -> comment-stripped body
        private readonly Dictionary<string, HashSet<string>> _abilityRefs; // ability -> dispatch fns

        public ScriptContext(string jass, int ownerId, MapDocument doc)
        {
            OwnerId = ownerId;
            _functions = JassFunctionIndex.Parse(jass);
            Aliases = JassRawcodeAliases.Parse(jass);

            var lines = jass.Replace("\r\n", "\n").Split('\n');
            _code = StripComments(lines);
            CodeText = string.Join("\n", _code);

            _bodies = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in _functions)
            {
                var sb = new StringBuilder();
                for (int i = f.StartLine - 1; i < f.EndLine && i < _code.Length; i++)
                    sb.Append(_code[i]).Append('\n');
                _bodies.TryAdd(f.Name, sb.ToString());
            }

            CustomAbilities = ObjectKinds.MergedEntries(doc, ObjectKinds.Info(ObjectKind.Ability))
                .Select(e => e.Id.ToRawcode()).ToHashSet(StringComparer.Ordinal);

            // Which per-player arrays the generated block actually writes this hero into. The audit
            // compares a handler's identity gate against exactly this, rather than guessing.
            RegisteredArrays = Regex.Matches(CodeText, @"set\s+([A-Za-z_][A-Za-z0-9_]*)\s*\[[^\]]*\]\s*=\s*u\b")
                .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

            _abilityRefs = BuildAbilityRefs();
        }

        /// <summary>Base-game abilities are present in any map, so a 4-char code the map does not
        /// define is treated as base rather than missing (no game data needed for that call).</summary>
        public bool BaseLikeAbility(string ability) =>
            ability.Length == 4 && !ability.StartsWith("A0", StringComparison.Ordinal)
                               && !ability.StartsWith("A1", StringComparison.Ordinal)
                               && !ability.StartsWith("A2", StringComparison.Ordinal);

        /// <summary>Maps each ability to the functions that dispatch on it, resolving id globals.</summary>
        private Dictionary<string, HashSet<string>> BuildAbilityRefs()
        {
            var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var spellId = new Regex(
                @"GetSpellAbilityId\s*\(\s*\)\s*==\s*(?:'([^']{4})'|([A-Za-z_][A-Za-z0-9_]*))");
            foreach (var f in _functions)
            {
                if (!_bodies.TryGetValue(f.Name, out var body)) continue;
                foreach (Match m in spellId.Matches(body))
                {
                    string? id = m.Groups[1].Success ? m.Groups[1].Value
                        : Aliases.GetValueOrDefault(m.Groups[2].Value);
                    if (id is null) continue;
                    if (!map.TryGetValue(id, out var set))
                        map[id] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(f.Name);
                }
            }
            return map;
        }

        public string? FindDispatch(string ability) =>
            _abilityRefs.TryGetValue(ability, out var fns) ? fns.OrderBy(x => x, StringComparer.Ordinal).First() : null;

        /// <summary>The gg_trg_* the function is attached to, as a condition or an action.</summary>
        public string? TriggerFor(string function)
        {
            var m = Regex.Match(CodeText,
                @"TriggerAdd(?:Condition|Action)\s*\(\s*(gg_trg_[A-Za-z0-9_]+)\s*,[^)]*\b"
                + Regex.Escape(function) + @"\b");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Whether the trigger has a spell event, and whether it reaches this hero's player.
        /// An any-unit registration covers everyone, a per-player one must name our player.</summary>
        public (bool HasEvent, bool CoversPlayer, string Detail) EventCoverage(string trigger)
        {
            string esc = Regex.Escape(trigger);
            bool anyUnit = Regex.IsMatch(CodeText,
                @"TriggerRegisterAnyUnitEventBJ\s*\(\s*" + esc + @"\s*,\s*EVENT_PLAYER_UNIT_SPELL");
            if (anyUnit) return (true, true, "any-unit event");

            // Per-UNIT registration, TriggerRegisterUnitEvent(trg, udg_X_Caster, EVENT_UNIT_SPELL_EFFECT).
            // These arenas bind a hero's triggers to the single unit held in its caster global, set by
            // the map's own hero setup routine, which the generated block now calls for a placed hero.
            // The bound unit is a runtime value so coverage cannot be proven statically, but the
            // registration plainly exists, and reporting it as "no event" was simply wrong.
            var perUnit = Regex.Matches(CodeText,
                @"TriggerRegisterUnitEvent\s*\(\s*" + esc + @"\s*,\s*([^,]+),\s*EVENT_UNIT_SPELL");
            if (perUnit.Count > 0)
                return (true, true,
                    $"per-unit event bound to {perUnit[0].Groups[1].Value.Trim()} by the map's hero setup");

            var perPlayer = Regex.Matches(CodeText,
                @"TriggerRegisterPlayerUnitEvent\s*\(\s*" + esc + @"\s*,\s*([^,]+),\s*EVENT_PLAYER_UNIT_SPELL");
            if (perPlayer.Count == 0)
            {
                bool otherSpellEvent = Regex.IsMatch(CodeText,
                    @"TriggerRegister[A-Za-z]*Event[A-Za-z]*\s*\(\s*" + esc + @"\s*,[^)]*EVENT_PLAYER_UNIT_SPELL");
                return (otherSpellEvent, otherSpellEvent, otherSpellEvent ? "spell event" : "none");
            }

            // Our generated block registers with a literal Player(N); the arena's own registrations
            // use a runtime expression, which we cannot resolve, so treat those as not proven.
            bool covered = perPlayer.Cast<Match>().Any(m =>
                Regex.IsMatch(m.Groups[1].Value, @"\bPlayer\s*\(\s*" + OwnerId + @"\s*\)"));
            return (true, covered, $"{perPlayer.Count} per-player registration(s)");
        }

        /// <summary>True when something behind the trigger still has executable statements. A ported
        /// handler whose every call was commented out compiles but does nothing.</summary>
        public bool HasLiveBody(string trigger, string dispatch)
        {
            var names = new HashSet<string>(StringComparer.Ordinal) { dispatch };
            foreach (Match m in Regex.Matches(CodeText,
                @"TriggerAdd(?:Condition|Action)\s*\(\s*" + Regex.Escape(trigger)
                + @"\s*,[^)]*?\bfunction\s+([A-Za-z_][A-Za-z0-9_]*)"))
                names.Add(m.Groups[1].Value);

            foreach (var n in names)
            {
                if (!_bodies.TryGetValue(n, out var body)) continue;
                foreach (var line in body.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.Length == 0) continue;
                    if (t.StartsWith("function", StringComparison.Ordinal)
                        || t.StartsWith("endfunction", StringComparison.Ordinal)
                        || t.StartsWith("local ", StringComparison.Ordinal)) continue;
                    if (t.StartsWith("call ", StringComparison.Ordinal)
                        || t.StartsWith("set ", StringComparison.Ordinal)
                        || t.StartsWith("return ", StringComparison.Ordinal)
                        || t.StartsWith("if ", StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }

        /// <summary>The per-player array a handler gates on ("... == Hero[GetPlayerId(p)]"), if any.</summary>
        public string? IdentityGate(string dispatch)
        {
            if (!_bodies.TryGetValue(dispatch, out var body)) return null;
            var m = Regex.Match(body,
                @"(?:GetTriggerUnit\(\)|GetSpellAbilityUnit\(\))\s*==\s*([A-Za-z_][A-Za-z0-9_]*)\s*\["
                + @"|([A-Za-z_][A-Za-z0-9_]*)\s*\[[^\]]*\]\s*==\s*(?:GetTriggerUnit\(\)|GetSpellAbilityUnit\(\))");
            if (!m.Success) return null;
            return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        }

        /// <summary>How many times the init that creates this trigger is called.</summary>
        public int InitCallCount(string trigger)
        {
            var creator = _functions.FirstOrDefault(f =>
                _bodies.TryGetValue(f.Name, out var b)
                && Regex.IsMatch(b, @"set\s+" + Regex.Escape(trigger) + @"\s*=\s*CreateTrigger"));
            if (creator is null)
                return Regex.IsMatch(CodeText, @"set\s+" + Regex.Escape(trigger) + @"\s*=\s*CreateTrigger") ? 1 : 0;
            return Regex.Matches(CodeText, @"\bcall\s+" + Regex.Escape(creator.Name) + @"\s*\(").Count;
        }

        /// <summary>True when an id global's name shares the hero's naming stem, the convention these
        /// maps use to group one hero's ids (DarkShiki_ID, DarkShikiQ_ID, DarkShikiQ2_ID).</summary>
        public bool SharesNamingGroup(string heroRawcode, string globalName)
        {
            if (string.IsNullOrEmpty(globalName)) return false;
            var heroGlobal = Aliases.FirstOrDefault(kv =>
                string.Equals(kv.Value, heroRawcode, StringComparison.Ordinal)).Key;
            if (heroGlobal is null) return false;
            string stem = heroGlobal.EndsWith("_ID", StringComparison.OrdinalIgnoreCase)
                ? heroGlobal[..^3] : heroGlobal;
            return stem.Length >= 4 && globalName.StartsWith(stem, StringComparison.Ordinal);
        }

        private static string[] StripComments(string[] lines)
        {
            var result = new string[lines.Length];
            bool inBlock = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var sb = new StringBuilder(lines[i].Length);
                var line = lines[i];
                for (int c = 0; c < line.Length; c++)
                {
                    if (inBlock)
                    {
                        if (c + 1 < line.Length && line[c] == '*' && line[c + 1] == '/') { inBlock = false; c++; }
                        continue;
                    }
                    if (c + 1 < line.Length && line[c] == '/' && line[c + 1] == '/') break;
                    if (c + 1 < line.Length && line[c] == '/' && line[c + 1] == '*') { inBlock = true; c++; continue; }
                    sb.Append(line[c]);
                }
                result[i] = sb.ToString();
            }
            return result;
        }
    }
}
