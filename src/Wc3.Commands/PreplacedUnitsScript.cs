// src/Wc3.Commands/PreplacedUnitsScript.cs
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using War3Net.Build.Widget;      // MapUnits, UnitData
using War3Net.Common.Extensions; // ToRawcode
using Wc3.Model;

namespace Wc3.Commands;

/// <summary>
/// Generates the JASS that actually spawns a map's preplaced units and items at runtime.
///
/// <para>Warcraft III does NOT read war3mapUnits.doo directly for a map that ships a custom
/// war3map.j. For such a map the World Editor bakes a <c>CreateAllUnits</c> function (and a
/// <c>CreateAllItems</c> one) into the script and calls them from <c>main</c>; that generated
/// code is what brings preplaced widgets to life. A map whose .doo holds units but whose script
/// has no such function shows nothing in game, even though the editor still draws the widgets
/// from the .doo. wc3ctl and the Studio only wrote the .doo, so this closes the gap: after any
/// placement the creation script is regenerated and wired into <c>main</c>.</para>
///
/// <para>The generated code lives between two marker comments so a later sync can replace exactly
/// its own block and never touch a map's hand-written script. If a map already carries its own
/// (foreign) <c>CreateAllUnits</c> that we did not generate, the sync leaves the script untouched,
/// so we never double-create or clobber a real World-Editor map.</para>
/// </summary>
public static class PreplacedUnitsScript
{
    public const string ScriptFile = "war3map.j";

    private const string BeginMarker = "//=== wc3ctl preplaced widgets (generated, do not edit) ===";
    private const string EndMarker = "//=== end wc3ctl preplaced widgets ===";

    private const string UnitsFunc = "CreateAllUnits";
    private const string ItemsFunc = "CreateAllItems";
    private const string WireSpellsFunc = "wc3ctl_WirePlacedHeroSpells";

    public sealed record SyncResult(bool Ok, string Message, int Units, int Items);

    /// <summary>
    /// Regenerates the preplaced-widget creation script for <paramref name="doc"/> from its
    /// current war3mapUnits.doo and wires the calls into <c>main</c>. Idempotent, running it
    /// twice with the same placements yields the same script. Never throws for an ordinary map,
    /// a missing or foreign script is reported through <see cref="SyncResult"/> instead.
    /// </summary>
    public static SyncResult Sync(MapDocument doc)
    {
        var scriptEntry = doc.GetFile(ScriptFile);
        // The effective script is any pending raw override, else the original bytes. Reading
        // RawBytes alone would miss what an earlier sync in this same session already wrote,
        // which would leave a stale block behind when placements change (e.g. a phantom unit
        // after the last one is removed).
        byte[]? effective = scriptEntry?.OverrideBytes ?? scriptEntry?.RawBytes;
        if (scriptEntry is null || effective is null || effective.Length == 0)
            return new(false, "no war3map.j to wire (a scriptless melee map spawns preplaced widgets from the .doo directly)", 0, 0);

        var units = doc.GetFile(PlacementCommand.UnitsFile)?.Model as MapUnits;
        var spawnable = units?.Units.Where(IsSpawnableUnit).ToList() ?? new List<UnitData>();
        var items = units?.Units.Where(IsItem).ToList() ?? new List<UnitData>();

        // Latin1 is one byte per char, so the script round-trips byte-for-byte.
        var enc = Encoding.Latin1;
        string jass = enc.GetString(effective);
        string nl = jass.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        bool hasOurBlock = jass.Contains(BeginMarker, StringComparison.Ordinal);

        // A real World-Editor map owns its own unit creation. Do not touch it.
        if (!hasOurBlock && MentionsForeignFunction(jass, UnitsFunc))
            return new(false, "map already creates its preplaced units through its own script, left untouched", 0, 0);

        // Nothing to create and nothing of ours to clean up: leave the file byte-identical.
        if (spawnable.Count == 0 && items.Count == 0 && !hasOurBlock)
            return new(true, "no preplaced widgets to wire", 0, 0);

        // Rebuild from a clean slate: drop our old block, then splice a fresh one (if any) in
        // just before main so every function it defines exists before main calls it.
        // An arena registers each player's hero in a global unit array (Hero[GetPlayerId(p)]=u), and
        // its spell handlers only act on a unit that is in that array. A placed hero never runs the
        // arena's selection flow, so it is absent and its spells do nothing. Detecting that array lets
        // CreateAllUnits register the placed hero into it, a best-effort so a ported arena hero can cast.
        string? heroArray = DetectHeroArray(jass);
        var spellTriggers = DetectPerPlayerSpellTriggers(jass);

        jass = RemoveBlock(jass, nl);
        if (spawnable.Count > 0 || items.Count > 0)
        {
            string block = BuildBlock(spawnable, items, nl, heroArray, spellTriggers);
            jass = InsertBefore(jass, "function main takes nothing returns nothing", block + nl, nl);
        }

        // Wire (or unwire) the calls in main to match what the block now defines.
        jass = EnsureMainCall(jass, UnitsFunc, wanted: spawnable.Count > 0, nl);
        jass = EnsureMainCall(jass, ItemsFunc, wanted: items.Count > 0, nl);

        doc.AddOrReplaceRawFile(ScriptFile, enc.GetBytes(jass));
        return new(true, $"wired {spawnable.Count} unit(s) and {items.Count} item(s) into main()", spawnable.Count, items.Count);
    }

    /// <summary>A spawnable unit is a real unit (not an item slot) and not a start location
    /// (those become <c>DefineStartLocation</c> in config, never a created unit).</summary>
    private static bool IsSpawnableUnit(UnitData u) =>
        u.OwnerId != PlacementCommand.ItemOwnerId && u.TypeId != StartLocationTypeId;

    private static bool IsItem(UnitData u) => u.OwnerId == PlacementCommand.ItemOwnerId;

    private static readonly int StartLocationTypeId = PlacementCommand.StartLocationRawcode.FromRawcode();

    /// <summary>
    /// Finds the arena's per-player hero array, a global <c>unit array</c> that the script indexes by
    /// <c>[GetPlayerId(...)]</c> (spell handlers test it to decide a cast belongs to that player's hero).
    /// Returns the array indexed that way most often, or null when the map has none.
    /// </summary>
    private static string? DetectHeroArray(string jass)
    {
        string? best = null;
        int bestCount = 0;
        foreach (Match decl in Regex.Matches(jass, @"\bunit\s+array\s+([A-Za-z_][A-Za-z0-9_]*)"))
        {
            string name = decl.Groups[1].Value;
            int c = Regex.Matches(jass, Regex.Escape(name) + @"\s*\[\s*GetPlayerId\s*\(").Count;
            if (c > bestCount) { bestCount = c; best = name; }
        }
        return bestCount > 0 ? best : null;
    }

    /// <summary>
    /// Global dispatch triggers (<c>gg_trg_*</c>) whose spell-effect event the arena registers per
    /// player (<c>TriggerRegisterPlayerUnitEvent(trg, somePlayer, EVENT_PLAYER_UNIT_SPELL_EFFECT, ...)</c>),
    /// normally during hero creation. A placed hero never triggers that registration, so its casts do
    /// not reach the dispatcher, CreateAllUnits registers the placed hero's player on each instead.
    /// Registering an unrelated dispatcher is harmless, its condition just filters the cast out.
    /// </summary>
    private static List<string> DetectPerPlayerSpellTriggers(string jass)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(jass,
            @"TriggerRegisterPlayerUnitEvent\s*\(\s*(gg_trg_[A-Za-z0-9_]+)\s*,[^,]+,\s*EVENT_PLAYER_UNIT_SPELL_EFFECT"))
            set.Add(m.Groups[1].Value);
        return set.ToList();
    }

    /// <summary>Builds the marker-wrapped block of creation functions. When <paramref name="heroArray"/>
    /// is the arena's per-player hero array, each placed hero registers itself into it (first hero per
    /// player wins) so the ported spell handlers recognise it.</summary>
    private static string BuildBlock(List<UnitData> units, List<UnitData> items, string nl,
        string? heroArray = null, IReadOnlyList<string>? spellTriggersOrNull = null)
    {
        var spellTriggers = spellTriggersOrNull ?? Array.Empty<string>();
        // Players that own a placed unit (a placed hero's owner is among them, neutral slots excluded).
        // The arena's spell-dispatch triggers get their spell-effect event registered for these players
        // so a placed hero's casts reach the handlers. This is deferred, not done in CreateAllUnits,
        // because CreateAllUnits runs BEFORE InitCustomTriggers in main(), so the gg_trg_* dispatch
        // triggers do not exist yet at unit-creation time (registering then would silently hit null).
        var heroOwners = units.Select(u => u.OwnerId)
            .Where(o => o >= 0 && o < PlayerColors.NeutralHostileId)
            .Distinct().OrderBy(o => o).ToList();
        bool wireSpells = spellTriggers.Count > 0 && heroOwners.Count > 0;

        var sb = new StringBuilder();
        sb.Append(BeginMarker).Append(nl);

        if (units.Count > 0)
        {
            if (wireSpells)
            {
                // Fired by a 0-second timer from CreateAllUnits, so it runs once map init has finished
                // and every gg_trg_* dispatch trigger has been created by InitCustomTriggers.
                sb.Append("function ").Append(WireSpellsFunc).Append(" takes nothing returns nothing").Append(nl);
                foreach (var trg in spellTriggers)
                    foreach (var owner in heroOwners)
                        sb.Append("    call TriggerRegisterPlayerUnitEvent(").Append(trg)
                          .Append(", Player(").Append(owner).Append("), EVENT_PLAYER_UNIT_SPELL_EFFECT, null)").Append(nl);
                sb.Append("    call DestroyTimer(GetExpiredTimer())").Append(nl);
                sb.Append("endfunction").Append(nl);
            }

            sb.Append("function ").Append(UnitsFunc).Append(" takes nothing returns nothing").Append(nl);
            sb.Append("    local unit u").Append(nl);
            foreach (var u in units)
            {
                float faceDeg = u.Rotation * 180f / MathF.PI;
                sb.Append("    set u = CreateUnit(Player(").Append(u.OwnerId).Append("), '")
                  .Append(Rawcode(u.TypeId)).Append("', ")
                  .Append(Real(u.Position.X)).Append(", ").Append(Real(u.Position.Y)).Append(", ")
                  .Append(Real(faceDeg)).Append(')').Append(nl);
                // Editor-set overrides, applied so a placed unit arrives exactly as the panel showed it
                // (a scripted map spawns from here, not from war3mapUnits.doo, so these must be re-applied).
                if (u.Scale.X is > 0f and not 1f)
                    sb.Append("    call SetUnitScale(u, ").Append(Real(u.Scale.X)).Append(", ")
                      .Append(Real(u.Scale.Y)).Append(", ").Append(Real(u.Scale.Z)).Append(')').Append(nl);
                if (u.HeroLevel > 1)
                    sb.Append("    call SetHeroLevel(u, ").Append(u.HeroLevel).Append(", false)").Append(nl);
                if (u.HeroStrength > 0)
                    sb.Append("    call SetHeroStr(u, ").Append(u.HeroStrength).Append(", true)").Append(nl);
                if (u.HeroAgility > 0)
                    sb.Append("    call SetHeroAgi(u, ").Append(u.HeroAgility).Append(", true)").Append(nl);
                if (u.HeroIntelligence > 0)
                    sb.Append("    call SetHeroInt(u, ").Append(u.HeroIntelligence).Append(", true)").Append(nl);
                if (u.GoldAmount > 0)
                    sb.Append("    call SetResourceAmount(u, ").Append(u.GoldAmount).Append(')').Append(nl);
                if (u.TargetAcquisition >= 0f)
                    sb.Append("    call SetUnitAcquireRange(u, ").Append(Real(u.TargetAcquisition)).Append(')').Append(nl);
                // Current life / mana as a percent of the now-correct (leveled) maximum.
                if (u.HP is >= 0 and <= 100)
                    sb.Append("    call SetUnitState(u, UNIT_STATE_LIFE, GetUnitState(u, UNIT_STATE_MAX_LIFE) * ")
                      .Append(Real(u.HP / 100f)).Append(')').Append(nl);
                if (u.MP is >= 0 and <= 100)
                    sb.Append("    call SetUnitState(u, UNIT_STATE_MANA, GetUnitState(u, UNIT_STATE_MAX_MANA) * ")
                      .Append(Real(u.MP / 100f)).Append(')').Append(nl);
                // Best-effort arena integration for a placed hero (see Sync): register it into the
                // per-player hero array so handlers that test Hero[pid] recognise it. Safe here because
                // it is a plain global-array write (the array exists from map load), unlike the spell
                // trigger registration, which must wait for the triggers and is done via the timer below.
                if (heroArray is not null)
                    sb.Append("    if IsUnitType(u, UNIT_TYPE_HERO) and ").Append(heroArray)
                      .Append("[GetPlayerId(GetOwningPlayer(u))] == null then").Append(nl)
                      .Append("        set ").Append(heroArray)
                      .Append("[GetPlayerId(GetOwningPlayer(u))] = u").Append(nl)
                      .Append("    endif").Append(nl);
            }
            if (wireSpells)
                sb.Append("    call TimerStart(CreateTimer(), 0., false, function ")
                  .Append(WireSpellsFunc).Append(')').Append(nl);
            sb.Append("    set u = null").Append(nl);
            sb.Append("endfunction").Append(nl);
        }

        if (items.Count > 0)
        {
            sb.Append("function ").Append(ItemsFunc).Append(" takes nothing returns nothing").Append(nl);
            foreach (var it in items)
                sb.Append("    call CreateItem('").Append(Rawcode(it.TypeId)).Append("', ")
                  .Append(Real(it.Position.X)).Append(", ").Append(Real(it.Position.Y)).Append(')').Append(nl);
            sb.Append("endfunction").Append(nl);
        }

        sb.Append(EndMarker);
        return sb.ToString();
    }

    /// <summary>Removes a previously generated block (markers included) plus the one trailing
    /// newline we inserted with it, leaving unrelated script untouched.</summary>
    private static string RemoveBlock(string jass, string nl)
    {
        int start = jass.IndexOf(BeginMarker, StringComparison.Ordinal);
        if (start < 0) return jass;
        int end = jass.IndexOf(EndMarker, start, StringComparison.Ordinal);
        if (end < 0) return jass;
        end += EndMarker.Length;
        if (jass.AsSpan(end).StartsWith(nl)) end += nl.Length;
        return jass.Remove(start, end - start);
    }

    /// <summary>Inserts <paramref name="text"/> immediately before the first line that begins
    /// with <paramref name="anchor"/>. Falls back to appending if the anchor is absent.</summary>
    private static string InsertBefore(string jass, string anchor, string text, string nl)
    {
        int at = jass.IndexOf(anchor, StringComparison.Ordinal);
        if (at < 0) return jass.EndsWith(nl, StringComparison.Ordinal) ? jass + text : jass + nl + text;
        // Back up to the start of the anchor's line so we do not split it.
        int lineStart = jass.LastIndexOf('\n', at) + 1;
        return jass.Insert(lineStart, text);
    }

    /// <summary>Adds or removes a <c>call Func(  )</c> statement inside main so the calls always
    /// match the functions the block defines. Inserts right before InitCustomTriggers (World
    /// Editor order, widgets before triggers), falling back to after InitBlizzard, then to the
    /// end of main.</summary>
    private static string EnsureMainCall(string jass, string func, bool wanted, string nl)
    {
        int mainAt = jass.IndexOf("function main takes nothing returns nothing", StringComparison.Ordinal);
        if (mainAt < 0) return jass;
        int mainEnd = jass.IndexOf(nl + "endfunction", mainAt, StringComparison.Ordinal);
        if (mainEnd < 0) mainEnd = jass.Length;

        string callToken = "call " + func + "(";
        int existing = jass.IndexOf(callToken, mainAt, StringComparison.Ordinal);
        bool present = existing >= 0 && existing < mainEnd;

        if (wanted && !present)
        {
            int anchor = jass.IndexOf("call InitCustomTriggers", mainAt, StringComparison.Ordinal);
            if (anchor < 0 || anchor > mainEnd)
            {
                anchor = jass.IndexOf("call InitBlizzard", mainAt, StringComparison.Ordinal);
                // After InitBlizzard's line rather than before it.
                if (anchor >= 0 && anchor < mainEnd)
                    anchor = jass.IndexOf('\n', anchor) + 1;
            }
            if (anchor < 0 || anchor > mainEnd) anchor = mainEnd; // last resort: end of main
            int lineStart = jass.LastIndexOf('\n', Math.Min(anchor, jass.Length - 1)) + 1;
            string indent = LeadingWhitespace(jass, lineStart);
            string stmt = indent + "call " + func + "(  )" + nl;
            return jass.Insert(lineStart, stmt);
        }
        if (!wanted && present)
        {
            int lineStart = jass.LastIndexOf('\n', existing) + 1;
            int lineEnd = jass.IndexOf('\n', existing);
            if (lineEnd < 0) lineEnd = jass.Length; else lineEnd += 1;
            return jass.Remove(lineStart, lineEnd - lineStart);
        }
        return jass;
    }

    /// <summary>True if the script defines <paramref name="func"/> outside any block of ours.</summary>
    private static bool MentionsForeignFunction(string jass, string func) =>
        jass.Contains("function " + func + " ", StringComparison.Ordinal) ||
        jass.Contains("function " + func + "\t", StringComparison.Ordinal);

    private static string LeadingWhitespace(string s, int lineStart)
    {
        int i = lineStart;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
        return s.Substring(lineStart, i - lineStart);
    }

    private static string Rawcode(int id) => id.ToRawcode();

    /// <summary>A JASS real literal, invariant, always with a decimal point (JASS does not
    /// promote an integer literal to a real argument).</summary>
    private static string Real(float v)
    {
        string s = v.ToString("0.0###", CultureInfo.InvariantCulture);
        return s;
    }
}
