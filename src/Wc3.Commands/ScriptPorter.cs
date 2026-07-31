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

    /// <summary>An <c>ExecuteFunc("Name")</c> call. The name is a STRING, so no identifier scan can
    /// see it, yet this is how a World Editor main invokes hand-written system initializers.</summary>
    private static readonly Regex ExecuteFuncLiteral =
        new(@"ExecuteFunc\s*\(\s*""([A-Za-z_][A-Za-z0-9_]*)""\s*\)", RegexOptions.Compiled);
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
    ///
    /// <paramref name="synthDispatchHero"/> is opt-in (null by default, so ordinary ports are
    /// byte-for-byte unchanged): the rawcode of the unit being ported, ROOT one, so the source's
    /// shared cast dispatcher's OWN branch for it can be read and turned into a fresh, minimal,
    /// self-contained dispatcher that bypasses every gate the shared one carries (a placed-hero
    /// registration array, a map rect that reads null off the source map, a cooldown hashtable,
    /// the hero-type-id check itself). See <see cref="SynthDispatchBuilder"/>. This does not
    /// replace the ordinary carried closure, a spell handler still needs its own carried body
    /// (timers, dummies, per-player state), it only replaces the SHARED entry point into it.
    ///
    /// <paramref name="bootstrapState"/> is opt-in too (false by default, so an ordinary port is
    /// unaffected): every carried global this port's own carried code reads but assigns nowhere in
    /// the spliced text (no InitGlobals ever carried it, no differently named helper Init ever
    /// reached it) is a global stuck at its type default, null for a handle. For the types that
    /// have a safe, universal, argument-free constructor (timer, group, hashtable, trigger, rect,
    /// force) a fresh generated function builds one, wired into the target's main before
    /// InitCustomTriggers runs, the same call site InitGlobals already uses, so the state exists
    /// before any carried code could read it. Every other type is left alone and reported instead,
    /// see <see cref="BootstrapStateBuilder"/> for the full reasoning (a rect is built EMPTY, never
    /// guessed at, and why that is the right tradeoff for a gate).
    /// </summary>
    public static ScriptPortInfo? PortScript(
        MapDocument source, MapDocument target,
        IReadOnlyList<BundleFunction> functions, string markerLabel,
        IReadOnlyDictionary<string, string> codeRemap, bool apply = true,
        string? synthDispatchHero = null, bool bootstrapState = false)
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

        // Opt-in: read the hero's OWN branch of the source's shared cast dispatcher and seed every
        // function it calls into the carry set, BEFORE the ordinary closure rules below run, so
        // those handlers come along even when the ordinary closure's scoping missed them. That is
        // not hypothetical: a combo ability a hero grants at runtime through a shared "start a
        // timer, then add this ability" helper is invisible to a plain call-graph walk (the helper
        // receives the ability id as a plain argument, not a callback reference), exactly the trap
        // that silently drops a hero's second-tier abilities from a port that only trusted its
        // object data. See SynthDispatchBuilder for what "the hero's own branch" means and how it
        // is found; the actual dispatcher function is built and spliced in further down, once the
        // rename/rawcode-remap machinery below is available to rewrite it consistently with every
        // other carried body.
        IReadOnlyList<SynthDispatchBuilder.CastBranch>? synthBranches = null;
        if (synthDispatchHero is not null)
        {
            synthBranches = SynthDispatchBuilder.ExtractHeroCastBranches(srcJ, synthDispatchHero);
            if (synthBranches is { Count: > 0 })
                foreach (var branch in synthBranches)
                    foreach (var callee in branch.Callees)
                        carried.Add(callee);
            else
                notes.Add("--synth-dispatch requested but no per-hero cast branch was found in the "
                    + "source's shared dispatcher (unsupported script shape) — no synthesized "
                    + "dispatcher was added, the ordinary carried closure (if any) is unaffected.");
        }

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

        // Carry the GUI-generated child callbacks of a carried trigger, to a fixpoint. A spell that hits
        // an area does its damage inside an iterator callback, for example inside Trig_Spell_Actions the
        // World Editor emits call ForGroupBJ(GetHostileGroup(...), function Trig_Spell_Func016A), and that
        // Func016A callback IS the code that deals the damage. Nothing but this argument names it and it
        // is not a structural (if/loop/return) reference, so without this the whole ForGroupBJ statement
        // is trimmed as a dropped reference and the ability silently deals no area damage on every port.
        //
        // The carry is deliberately scoped to callbacks that belong to the SAME GUI trigger as the
        // carried body that names them (they share the Trig_<TriggerName> stem, so Trig_Spell_Actions may
        // pull Trig_Spell_Func016A but nothing else). Following "function Foo" arguments WITHOUT that
        // scope is not safe here, measured on GGGA it dragged in 2446 functions belonging to dozens of
        // OTHER heroes (Shinon, Zion, Archer, ...), because the shared event systems this hero also uses
        // (damage, kill, death) name every hero's callback the same way. Restricting to same-trigger
        // children recovers the hero's own area effects without following those shared systems into the
        // rest of the roster. A carried callback that in turn calls foreign code still has that inner call
        // trimmed by the loop below, so carrying it can never leave a dangling reference.
        CarryCallbackReferences(carried, allByName, srcLines);

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
        // The set the last fixpoint pass trimmed against, kept around for the synthesized dispatcher
        // below: it must go through the exact same discipline (a call to something not actually
        // carried gets commented out, never left calling into the void) as every other carried body.
        var finalDropped = new HashSet<string>(allSourceFns, StringComparer.Ordinal);
        finalDropped.ExceptWith(carried);

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
        var carriedGlobals = srcGlobalOrder.Where(used.Contains).ToList();  // recomputed below after the initializers

        // Carry the map's own global-state initializer(s) too: the conventional InitGlobals (the
        // JASS entry point a GUI map's Variable Editor values are normally assigned through), plus
        // any differently named helper InitCustomTriggers calls before it wires a single trigger.
        // The second case is not a hypothetical, on a real corpus map InitGlobals is EMPTY and every
        // hero's timers and unit groups are instead allocated once inside one such helper, called as
        // the first statement of InitCustomTriggers. Without carrying it, a global a carried spell
        // reads (a per-player timer or group the map allocates once, not a plain literal) stays at
        // its JASS type default in the target, so the spell's trigger fires but every
        // TimerStart/GroupAddUnit on that still-null handle silently does nothing. Filtered to only
        // the assignments that touch a global the closure above actually needs, so a map with
        // thousands of OTHER heroes' state does not bloat the port.
        var residualGlobalRefs = new HashSet<string>(StringComparer.Ordinal);
        var initResidualFns = new HashSet<string>(StringComparer.Ordinal);
        var droppedForInits = allByName.Keys.Where(n => !carried.Contains(n)).ToHashSet(StringComparer.Ordinal);
        var globalInitBodies = CarryGlobalInitializers(allByName, srcLines, srcGlobals, used, carried,
            residualGlobalRefs, droppedForInits, initResidualFns);

        // A global these bodies still READ in a structural line has to be DECLARED, or the script does
        // not compile. TrimToGlobals correctly comments out "set RINQ_registered=true" but cannot
        // touch the "if RINQ_registered then" guarding it, so the reference survives. Declaring an
        // extra global costs one line and no behaviour, leaving it undeclared kills the whole script,
        // so this is strictly the safer direction. Only the DECLARATION is added, never the
        // assignment, so no foreign hero's state gets set up.
        // Only the ones stuck in a structural line, which is exactly what residualGlobalRefs holds.
        // Scanning the whole body instead would also declare globals whose assignment was correctly
        // trimmed, and a declaration carries its own initial value, so an unrelated global would come
        // back to life through the globals block.
        foreach (var g in residualGlobalRefs)
            if (srcGlobals.ContainsKey(g)) used.Add(g);
        carriedGlobals = srcGlobalOrder.Where(used.Contains).ToList();
        int carriedInitializers = globalInitBodies.Values.Sum(CountKeptAssignments);
        if (globalInitBodies.Count > 0)
            notes.Add(carriedInitializers > 0
                ? $"carried {carriedInitializers} global-initializer assignment(s) from "
                    + $"{string.Join(", ", globalInitBodies.Keys)}, so the ported spells' timers, unit "
                    + "groups, and other non-default globals are set up before they run."
                : $"wired {string.Join(", ", globalInitBodies.Keys)} but it carried 0 assignments for "
                    + "any global the closure needs (nothing there sets one up, or it was already "
                    + "carried whole by the ordinary closure) — verify the ported map in-game.");
        if (residualGlobalRefs.Count > 0)
            notes.Add($"{residualGlobalRefs.Count} reference(s) to a non-carried global remain in a "
                + "condition or return inside the map's own global initializer and were left in "
                + $"place, verify the ported map ({string.Join(", ", residualGlobalRefs.Take(8))}).");

        // Symbols the target already defines (functions + its own globals).
        var targetSymbols = JassFunctionIndex.Parse(tgtJ).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var g in ParseGlobals(tgtJ.Replace("\r\n", "\n").Split('\n')).Item1.Keys) targetSymbols.Add(g);

        // Rename any carried symbol that collides with the target (or with common.j-ish
        // reserved names we can't see); rewrite references within the ported block only.
        var rename = new Dictionary<string, string>(StringComparer.Ordinal);
        var carriedSymbols = srcFns.Select(f => f.Name).Concat(carriedGlobals)
            .Concat(globalInitBodies.Keys).ToList();
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

        // Names for the synthesized dispatcher (see the seeding step above), reserved now against
        // the SAME collision-avoidance set the ordinary rename pass just finished with, so its
        // trigger global can be declared alongside the other carried globals below.
        string? synthDispatchFn = null, synthInitFn = null, synthTriggerGlobal = null;
        if (synthBranches is { Count: > 0 })
        {
            string baseName = "wc3ctl_SynthCast_" + SynthDispatchBuilder.SanitizeIdentifier(synthDispatchHero!);
            synthDispatchFn = UniqueName(baseName, taken);
            synthInitFn = UniqueName(baseName + "Init", taken);
            synthTriggerGlobal = UniqueName("gg_trg_" + baseName, taken);
        }

        // Build the ported globals + functions text.
        var portedGlobals = new StringBuilder();
        foreach (var g in carriedGlobals)
            portedGlobals.Append("    ").Append(Rewrite(srcGlobals[g].Trim())).Append('\n');
        if (synthTriggerGlobal is not null)
            portedGlobals.Append("    ").Append(SynthDispatchBuilder.BuildGlobalDeclaration(synthTriggerGlobal)).Append('\n');

        var portedFns = new StringBuilder();
        foreach (var f in srcFns)
            portedFns.Append(Rewrite(bodies[f.Name])).Append('\n');
        foreach (var name in globalInitBodies.Keys)
            portedFns.Append(Rewrite(globalInitBodies[name])).Append('\n');

        // The synthesized dispatcher's function bodies: built from the hero's own extracted
        // branches, run through the exact same Trim discipline as every other carried body (so a
        // callee this port could not actually carry — for any reason — is commented out here too,
        // rather than left calling into the void), then through the SAME Rewrite every other
        // carried body gets, so a renamed callee or a remapped ability rawcode is picked up
        // consistently.
        string? synthInitToHook = null;
        if (synthBranches is { Count: > 0 })
        {
            string raw = SynthDispatchBuilder.BuildRawText(synthDispatchFn!, synthInitFn!, synthTriggerGlobal!, synthBranches);
            var synthResidual = new HashSet<string>(StringComparer.Ordinal);
            raw = Trim(raw, finalDropped, synthResidual);
            int synthTrimmed = CountTrimMarkers(raw);
            portedFns.Append(Rewrite(raw)).Append('\n');
            notes.Add($"synthesized {synthDispatchFn}(), a fresh minimal cast dispatcher for {markerLabel} "
                + $"covering {synthBranches.Count} ability branch(es) read from the source's own per-hero "
                + "dispatch section, wired through its own trigger (any-unit spell-effect event) so it "
                + "never touches the shared dispatcher's gates (map rects, the hero-type-id check, the "
                + "cooldown hashtable, any placed-hero registration array).");
            if (synthTrimmed > 0)
                notes.Add($"{synthTrimmed} call(s) inside the synthesized dispatcher named a function this "
                    + "port could not carry and were commented out — those ability branch(es) will not "
                    + "cast, verify the ported map.");
            synthInitToHook = synthInitFn;
        }

        // Opt-in: find every carried global this port's own spliced-in text reads but never
        // assigns anywhere, and construct the ones whose type has a safe, universal constructor.
        // See BootstrapStateBuilder for the full reasoning and the type-to-constructor mapping.
        // Declarations are keyed by their FINAL (post rename) name, since that is the name the
        // spliced text (already renamed) actually uses, the ORIGINAL decl text still describes the
        // right type and initializer shape, renaming never touches either.
        string? bootstrapFnToHook = null;
        if (bootstrapState)
        {
            var declaredByFinalName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var g in carriedGlobals)
                declaredByFinalName[rename.GetValueOrDefault(g, g)] = srcGlobals[g];
            if (synthTriggerGlobal is not null)
                declaredByFinalName[synthTriggerGlobal] =
                    SynthDispatchBuilder.BuildGlobalDeclaration(synthTriggerGlobal);

            var candidates = BootstrapStateBuilder.FindUnassigned(declaredByFinalName, portedFns.ToString());
            var constructible = candidates.Where(c => c.Constructor is not null).ToList();
            var left = candidates.Where(c => c.Constructor is null).ToList();

            if (constructible.Count > 0)
            {
                string bootstrapFn = UniqueName(
                    "wc3ctl_BootstrapState_" + SynthDispatchBuilder.SanitizeIdentifier(markerLabel), taken);
                portedFns.Append(BootstrapStateBuilder.BuildRawText(bootstrapFn, constructible)).Append('\n');
                bootstrapFnToHook = bootstrapFn;
                notes.Add($"synthesized {bootstrapFn}(), constructing {constructible.Count} global(s) that "
                    + "this hero's own carried code reads but nothing in the port ever assigns "
                    + $"({string.Join(", ", constructible.Select(c => $"{c.Name} ({c.Type})"))}), so each "
                    + "reads as a fresh, real handle instead of null.");
                if (constructible.Any(c => c.Type == "rect"))
                    notes.Add("a bootstrapped rect is built EMPTY, bounds (0,0) to (0,0), never a guess "
                        + "at the source's real region, for a GATE that is strictly safer (it can never "
                        + "falsely block). The tradeoff, a handler that used the rect to pick a location "
                        + "now reads (0,0), verify the ported map.");
            }
            if (left.Count > 0)
            {
                var leftDescriptions = left.Select(c => c.Name + " (" + (c.IsArray ? c.Type + " array" : c.Type) + ")");
                notes.Add($"{left.Count} global(s) are read by this hero's own carried code but assigned "
                    + "nowhere in the port, and were deliberately left untouched rather than guessed at "
                    + $"({string.Join(", ", leftDescriptions)}), verify the ported map.");
            }
        }

        // A non-carried function named in a condition or return could not be safely commented out
        // without breaking block structure, so it was left in place. Flag it (best-effort port).
        if (residual.Count > 0)
            notes.Add($"{residual.Count} reference(s) to non-carried function(s) remain in a condition or "
                + $"return and were left in place, verify the ported map ({string.Join(", ", residual.Take(8))}).");

        // Splice into the target: globals into its globals block, functions after endglobals.
        string merged = Splice(tgtJ, portedGlobals.ToString(), portedFns.ToString(), marker, notes);

        // Best-effort init hook: call carried InitTrig_* functions from InitCustomTriggers.
        //
        // An init that a carried aggregator ALREADY calls must not be hooked a second time. Calling
        // InitTrig_X twice builds two triggers on the same event with the same action, so every spell
        // it registers fires twice (doubled damage, doubled effects, doubled dummies). These scripts
        // commonly have one InitTrig_* that calls dozens of others, and it gets carried too, so
        // hooking every carried init blindly double-registers most of them.
        //
        // Only calls made from another carried InitTrig_* count, because those are exactly the
        // functions this hook will run. A call from some other carried function is no guarantee that
        // it executes at init, so those inits are still hooked here.
        var hookedByAggregator = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in srcFns.Where(f => f.Name.StartsWith("InitTrig_", StringComparison.Ordinal)))
            foreach (Match m in Regex.Matches(bodies[f.Name], @"\bcall\s+(InitTrig_[A-Za-z0-9_]+)"))
                if (m.Groups[1].Value != f.Name) hookedByAggregator.Add(m.Groups[1].Value);

        var initFns = srcFns.Select(f => f.Name)
            .Where(nm => nm.StartsWith("InitTrig_", StringComparison.Ordinal))
            .Where(nm => !hookedByAggregator.Contains(nm))
            .Select(nm => rename.GetValueOrDefault(nm, nm)).ToList();
        if (hookedByAggregator.Count > 0)
            notes.Add($"skipped hooking {hookedByAggregator.Count} InitTrig_* function(s) that a carried "
                + "init already calls, so their triggers register once instead of twice (a double "
                + "registration makes every affected spell fire twice).");
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

        // Hook the synthesized dispatcher's own init (creates its own trigger, so it is wired
        // unconditionally, independent of whether the closure carried any ordinary InitTrig_*).
        if (synthInitToHook is not null)
        {
            merged = HookInit(merged, new[] { synthInitToHook }, marker, out bool synthHooked);
            notes.Add(synthHooked
                ? $"wired {synthInitToHook}() into the target's InitCustomTriggers, registering the "
                  + "synthesized dispatcher's trigger."
                : "synthesized a cast dispatcher but could not find InitCustomTriggers in the target — " +
                  "its trigger registration may need to be wired manually.");
        }

        // Call the carried global initializer(s), in the same relative order the source used: the
        // literal InitGlobals from the target's main (before InitCustomTriggers, the JASS convention
        // the source itself follows, globals before triggers), any other helper from the FRONT of the
        // target's InitCustomTriggers (before the InitTrig_* calls just hooked above), matching how
        // the source calls it as the first statement there, ahead of any trigger wiring.
        if (globalInitBodies.ContainsKey("InitGlobals"))
        {
            string fn = rename.GetValueOrDefault("InitGlobals", "InitGlobals");
            merged = HookMain(merged, fn, marker, out bool hookedMain);
            notes.Add(hookedMain
                ? $"wired {fn}() into the target's main, before InitCustomTriggers."
                : "carried InitGlobals but could not find the target's main — call it manually.");
        }
        var otherInitHelpers = globalInitBodies.Keys
            .Where(nm => nm != "InitGlobals")
            .Select(nm => rename.GetValueOrDefault(nm, nm)).ToList();
        if (otherInitHelpers.Count > 0)
        {
            merged = HookInit(merged, otherInitHelpers, marker, out bool hookedHelpers, atFront: true);
            notes.Add(hookedHelpers
                ? $"wired {otherInitHelpers.Count} global-initializer helper(s) into the front of the "
                  + "target's InitCustomTriggers, ahead of the trigger wiring above."
                : "carried a global-initializer helper but could not find InitCustomTriggers in the " +
                  "target — call it manually, before any trigger registration.");
        }

        // The synthesized bootstrap function, same call site as InitGlobals (before
        // InitCustomTriggers), so every global it constructs exists before any carried code,
        // including the synthesized dispatcher above, could ever read it.
        if (bootstrapFnToHook is not null)
        {
            merged = HookMain(merged, bootstrapFnToHook, marker, out bool hookedBootstrap);
            notes.Add(hookedBootstrap
                ? $"wired {bootstrapFnToHook}() into the target's main, before InitCustomTriggers, so "
                  + "every bootstrapped global exists before any carried code could read it."
                : "synthesized a bootstrap-state function but could not find the target's main, call it "
                  + "manually, before anything else runs.");
        }

        // The compile gate. Trimming a call to a function we did not carry can leave a variable
        // undeclared, and in JASS that single error fails the whole war3map.j, so config() never runs
        // and the hosted map shows no player slots. That used to save silently and only surface in a
        // lobby, so the merged script is now verified here, repaired if it is mechanically fixable,
        // and simply not written if it still would not compile.
        var issues = JassScriptCheck.Check(merged);
        if (!JassScriptCheck.IsCompilable(issues))
        {
            merged = JassScriptCheck.Repair(merged, out int repaired);
            if (repaired > 0)
                notes.Add($"repaired {repaired} declaration(s) whose initializer was dropped, so the "
                    + "ported script compiles (those variables take their type default).");
            issues = JassScriptCheck.Check(merged);
        }

        bool written = JassScriptCheck.IsCompilable(issues);
        if (!written)
        {
            var errors = issues
                .Where(i => i.Severity == DiagnosticSeverity.Error && JassScriptCheck.BlocksCompilation(i.Kind))
                .Take(10)
                .Select(i => i.Line > 0 ? $"line {i.Line}: {i.Message}" : i.Message);
            notes.Add("REFUSED to write the ported script: it would not compile, which would leave the "
                + "map unhostable (an empty lobby). The map keeps its original working script, so the "
                + "object and asset port still applies. Errors: " + string.Join(" | ", errors));
        }
        else if (apply)
        {
            target.AddOrReplaceRawFile(tgtEntry.FileName!, ByteText.GetBytes(merged));
        }
        return new ScriptPortInfo(srcFns.Count, carriedGlobals.Count, rename.Count, hooked, notes, written);
    }

    /// <summary>Owned by <see cref="JassScriptCheck"/> so the writer of this marker and the checker
    /// that detects (and repairs) its damage can never drift apart.</summary>
    private const string TrimMarker = JassScriptCheck.TrimMarker;

    /// <summary>Comments out each safe statement (call/set/local/debug) that invokes or references a
    /// source function we are NOT carrying, so a carried body only ever calls carried functions or
    /// natives. Structural lines (an if/loop condition or a return) that name a non-carried function
    /// are left in place to preserve block nesting, and recorded in <paramref name="residual"/>.</summary>
    private static string Trim(string body, IReadOnlySet<string> dropped, HashSet<string> residual)
    {
        if (dropped.Count == 0) return body;
        return TrimLines(body, code => DroppedRefs(code, dropped), residual);
    }

    /// <summary>Comments out each safe statement that reads or writes a global NOT in
    /// <paramref name="keep"/>, the line-by-line trim discipline of <see cref="Trim"/> but keyed on
    /// global references instead of function calls. Narrows a map-wide initializer (InitGlobals, or a
    /// differently named helper playing the same role) down to only the assignments the ported
    /// closure actually needs. See <see cref="CarryGlobalInitializers"/>.</summary>
    private static string TrimToGlobals(
        string body, IReadOnlyDictionary<string, string> allGlobals, IReadOnlySet<string> keep,
        HashSet<string> residual) =>
        TrimLines(body, code => DroppedGlobalRefs(code, allGlobals, keep), residual);

    /// <summary>Shared line classifier behind <see cref="Trim"/> and <see cref="TrimToGlobals"/>:
    /// for each line, <paramref name="badRefsInLine"/> decides whether it names something we are not
    /// carrying (a dropped function, or a dropped global). A clean line passes through unchanged. A
    /// bad local initializer is stripped but the declaration kept (an undeclared variable is a
    /// compile error that fails the whole script). A bad call/set/debug statement is commented out. A
    /// bad structural line (an if/loop condition or a return) cannot be commented without breaking
    /// block nesting, so it is left in place and its names recorded in <paramref name="residual"/>.
    /// </summary>
    private static string TrimLines(string body, Func<string, List<string>> badRefsInLine, HashSet<string> residual)
    {
        var sb = new StringBuilder();
        foreach (var line in body.Split('\n'))
        {
            var refs = badRefsInLine(StripComment(line));
            if (refs.Count == 0) { sb.Append(line).Append('\n'); continue; }
            var head = line.TrimStart();
            if (head.StartsWith("local ", StringComparison.Ordinal))
            {
                // Keep the variable DECLARED, drop only the initializer. Commenting the whole line
                // out would undeclare a variable the rest of the function still reads ("return ok",
                // "call SaveReal(HH, id, ...)"), and an undefined variable is a compile error that
                // kills the entire script (config never runs, so the host lobby cannot build slots).
                // The variable keeps its type default instead.
                sb.Append(TrimLocalInitializer(line)).Append('\n');
            }
            else if (head.StartsWith("call ", StringComparison.Ordinal)
                || head.StartsWith("set ", StringComparison.Ordinal)
                || head.StartsWith("debug ", StringComparison.Ordinal))
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

    /// <summary>Keeps a local's declaration but drops its initializer, so a variable whose value came
    /// from a dropped function stays declared (its later reads compile, defaulting to null/0/false).
    /// "  local integer id= NewTimerRU(hId)" becomes "  local integer id". The first '=' is the
    /// assignment (JASS identifiers and types carry no '='), so any '==' in the dropped expression
    /// sits safely after it.</summary>
    private static string TrimLocalInitializer(string line)
    {
        int eq = line.IndexOf('=');
        if (eq < 0) return line; // no initializer (e.g. a bare or array declaration): nothing to drop
        return line[..eq].TrimEnd() + " " + TrimMarker + "(initializer dropped)";
    }

    private static int CountTrimMarkers(string body)
    {
        int count = 0, i = 0;
        while ((i = body.IndexOf(TrimMarker, i, StringComparison.Ordinal)) >= 0) { count++; i += TrimMarker.Length; }
        return count;
    }

    /// <summary>Live (not commented out) "set" statements a <see cref="TrimToGlobals"/>-filtered
    /// global-initializer body still contains, reported so a port that ends up carrying zero
    /// assignments is visible rather than silently looking the same as one that carried plenty.
    /// </summary>
    private static int CountKeptAssignments(string body) =>
        body.Split('\n').Count(l => l.TrimStart().StartsWith("set ", StringComparison.Ordinal));

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

    /// <summary>Names of declared globals this line mentions that are NOT in <paramref name="keep"/>,
    /// whether read or written. Unlike <see cref="DroppedRefs"/> there is no "followed by paren" or
    /// "function" gate, a bare read ("set X= udg_Y") counts exactly as much as an argument
    /// ("call SetSoundDuration(gg_snd_Y, 100)") or the assignment target itself.</summary>
    private static List<string> DroppedGlobalRefs(
        string code, IReadOnlyDictionary<string, string> allGlobals, IReadOnlySet<string> keep)
    {
        var found = new List<string>();
        foreach (Match m in Ident.Matches(code))
            if (allGlobals.ContainsKey(m.Value) && !keep.Contains(m.Value))
                found.Add(m.Value);
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

        // Each initializer -> the source functions it references (the handlers it registers).
        //
        // InitTrig_* is the World Editor's form, but a hand-written system names its own initializer
        // and the suffix form was missing here. Acnologia's AcnoG_Init was carried by the ordinary
        // closure, so none of the safety below applied to it, and its four registered handlers were
        // never carried. The ported script then referenced functions that do not exist, which is a
        // compile error, so the map could not host at all.
        //
        // Measured on Anime Choice Arena porting H0DA, adding the suffix costs 27 more carried
        // functions (1827 to 1854) and one more wired init, and takes pjass from 11 errors to 7.
        var initHandlers = allByName.Values
            .Where(f => f.Name.StartsWith("InitTrig_", StringComparison.Ordinal)
                || f.Name.EndsWith("_Init", StringComparison.Ordinal))
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
                if (carried.Contains(init))
                {
                    // Already carried by the ordinary closure, which used to mean this loop skipped
                    // it and its handlers were never carried, so the registration named a function
                    // that was never emitted. However the init got here, if it is in the port then
                    // its handlers have to be too.
                    foreach (var h in handlers)
                        if (carried.Add(h)) grew = true;
                    continue;
                }
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

    /// <summary>
    /// Adds to <paramref name="carried"/> every GUI child callback (a "function Foo" argument) that a
    /// carried body names AND that belongs to the SAME GUI trigger as that body (same Trig_&lt;Name&gt;
    /// stem), iterating to a fixpoint bounded by the function count. See the call site in
    /// <see cref="PortScript"/> for why this is necessary (the callback applies a spell's area effect)
    /// and why the same-trigger scope is required (an unscoped walk follows shared event systems into
    /// every other hero's callbacks).
    /// </summary>
    private static void CarryCallbackReferences(
        HashSet<string> carried, IReadOnlyDictionary<string, JassFunction> allByName, string[] srcLines)
    {
        // Bounded by the function count: each pass only adds names, and there are finitely many.
        for (int guard = 0; guard <= allByName.Count; guard++)
        {
            var toAdd = new List<string>();
            foreach (var n in carried)
            {
                if (!allByName.TryGetValue(n, out var f)) continue;
                var stem = TriggerStem(n);
                if (!stem.StartsWith("Trig_", StringComparison.Ordinal)) continue; // only GUI triggers have Func children
                foreach (var cb in CallbackNames(BodyText(srcLines, f)))
                    if (allByName.ContainsKey(cb) && !carried.Contains(cb) && TriggerStem(cb) == stem)
                        toAdd.Add(cb);
            }
            if (toAdd.Count == 0) break;
            foreach (var r in toAdd) carried.Add(r);
        }
    }

    /// <summary>
    /// The map's global-state initializer function(s): the conventional <c>InitGlobals</c> (the JASS
    /// entry point a GUI map's Variable Editor values are normally assigned through) and any function
    /// <c>InitCustomTriggers</c> calls before it starts wiring individual triggers that is NOT itself
    /// an <c>InitTrig_*</c> constructor. The second shape is not hypothetical, on a real corpus map
    /// InitGlobals is EMPTY and every hero's per-player timers and unit groups are instead allocated
    /// once inside a differently named helper called as the very first statement of
    /// InitCustomTriggers, before any trigger gets wired. Both are carried the same way: each body is
    /// run through <see cref="TrimToGlobals"/> so only the assignments <paramref name="keepGlobals"/>
    /// actually needs survive, keyed by function name so <see cref="PortScript"/> can call the
    /// conventional one from the target's main and any other one from the front of the target's
    /// InitCustomTriggers (see the call site there for why the two need different call sites).
    /// </summary>
    private static Dictionary<string, string> CarryGlobalInitializers(
        IReadOnlyDictionary<string, JassFunction> allByName, string[] srcLines,
        IReadOnlyDictionary<string, string> allGlobals, IReadOnlySet<string> keepGlobals,
        IReadOnlySet<string> alreadyCarried, HashSet<string> residual,
        IReadOnlySet<string> droppedFunctions, HashSet<string> residualFunctions)
    {
        // Already carried (verbatim, by the ordinary closure) means it will already be emitted once
        // from srcFns; adding it here too would define the same function twice and fail to compile.
        var names = new List<string>();
        if (allByName.ContainsKey("InitGlobals") && !alreadyCarried.Contains("InitGlobals"))
            names.Add("InitGlobals");
        if (allByName.TryGetValue("InitCustomTriggers", out var ict))
            // BodyText includes the "function InitCustomTriggers takes..." signature line itself,
            // which ReferencedNames always "finds" (a function's own signature has the identical
            // token shape as a "function Foo" callback reference), so the anchor's own name is
            // explicitly excluded rather than relying on ReferencedNames to know better.
            foreach (var name in ReferencedNames(BodyText(srcLines, ict)).Distinct())
                if (name != ict.Name && !name.StartsWith("InitTrig_", StringComparison.Ordinal)
                    && allByName.ContainsKey(name) && !alreadyCarried.Contains(name) && !names.Contains(name))
                    names.Add(name);

        // Also the initializers main invokes as ExecuteFunc("Name") STRING LITERALS. A real World
        // Editor main does exactly this for hand-written systems, and a string is invisible to
        // identifier based reference scanning, so these were never carried at all. Measured on Anime
        // WOS2, `function Init` (source line 26612) is the only thing that creates GearTimer03,
        // GearTimer05 and GearTimer10 plus their callbacks, and main reaches it solely through
        // `call ExecuteFunc("Init")`. Without it those timers stay null, so every timed part of a
        // ported spell silently does nothing while its instant part works, which reads in game as
        // "casts but half the spell is missing". The name also matches no init pattern, it is
        // literally `Init`, so nothing else here would have found it either.
        if (allByName.TryGetValue("main", out var mainFn))
            foreach (Match m in ExecuteFuncLiteral.Matches(BodyText(srcLines, mainFn)))
            {
                var name = m.Groups[1].Value;
                if (allByName.ContainsKey(name) && !alreadyCarried.Contains(name) && !names.Contains(name))
                    names.Add(name);
            }

        // Keep an initializer ONLY if, after trimming, it still assigns a global this hero needs.
        // That is this feature's whole stated purpose, and carrying the rest is actively harmful.
        // InitCustomTriggers on a dense arena names every hand-written system's initializer for the
        // WHOLE map (AcnoG_Init, BelR_Init, RINQ_Register, ...), and TrimToGlobals is keyed on
        // globals, so a foreign init arrived with two kinds of dangling reference. Its
        // "function BelR_OnCast" callback survived untouched, because no bad GLOBAL appears on that
        // line, naming a function that was never carried. And a trimmed "set RINQ_registered=true"
        // left its guarding "if RINQ_registered then" in place, since commenting a structural line
        // would break block nesting, reading a global nobody carried. Both are compile errors, and a
        // script that does not compile means config() never runs and the host lobby shows no slots.
        //
        // Measured on Anime Choice Arena porting H0DA, that regression took the port from 0 pjass
        // errors to 11. An initializer with nothing left to set up has no reason to be in the port,
        // and dropping it removes the dangling references with it.
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var candidateResidual = new HashSet<string>(StringComparer.Ordinal);
            var trimmed = TrimToGlobals(
                BodyText(srcLines, allByName[name]), allGlobals, keepGlobals, candidateResidual);
            // InitGlobals is exempt. It is the conventional entry point the target's main calls, and
            // an empty one is harmless, whereas dropping it leaves the target with no InitGlobals at
            // all, which the runtime-readiness check correctly flags. The rule exists to keep FOREIGN
            // system initializers out, not to remove the map's own convention.
            if (name != "InitGlobals" && CountKeptAssignments(trimmed) == 0) continue;
            // Then the SAME function trim every other carried body gets. Without it a
            // "function BelR_OnCast" callback survives here naming a function nothing carried,
            // because that line holds no bad global for TrimToGlobals to catch.
            trimmed = Trim(trimmed, droppedFunctions, residualFunctions);
            bodies[name] = trimmed;
            residual.UnionWith(candidateResidual);
        }
        return bodies;
    }

    /// <summary>The GUI-trigger stem of a function name, the shared Trig_&lt;TriggerName&gt; prefix the
    /// World Editor gives a trigger's Conditions, Actions and Func&lt;n&gt; children. Two functions with
    /// the same stem belong to the same GUI trigger. A name that is not a Trig_ GUI function is returned
    /// unchanged (so it matches only itself).</summary>
    private static string TriggerStem(string name)
    {
        var m = Regex.Match(name, @"^(Trig_.+?)(_Conditions|_Actions|_Func\d)");
        return m.Success ? m.Groups[1].Value : name;
    }

    /// <summary>Identifiers referenced as a function-pointer argument ("function Foo") in a snippet, the
    /// JASS form for passing a callback to an iterator (ForGroup, Condition, Filter, TimerStart, enum).
    /// Unlike <see cref="ReferencedNames"/> this deliberately EXCLUDES the plain-call form "Foo(", so it
    /// never follows the call edges that would reach another hero's handler through a shared dispatcher.
    /// Comment text is stripped so a name that survives only in a comment is not pulled.</summary>
    private static IEnumerable<string> CallbackNames(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var code = StripComment(line);
            Match? prev = null;
            foreach (Match m in Ident.Matches(code))
            {
                if (prev is { Value: "function" }) yield return m.Value;
                prev = m;
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

    /// <summary>Global name → its full declaration line, plus declaration order. Delegates to the
    /// shared parser so the porter and <see cref="JassScriptCheck"/> agree on what a global is.</summary>
    private static (Dictionary<string, string>, List<string>) ParseGlobals(string[] lines)
    {
        var (byName, order) = JassGlobals.Parse(lines);
        return (byName, order);
    }

    /// <summary>A name not already in <paramref name="taken"/>, reserving it there (numbered
    /// suffixes, same convention as the ordinary collision rename above) — used for the synthesized
    /// dispatcher's own function names, which have no source name to collide under, only a freshly
    /// chosen one that must not collide with anything already spoken for.</summary>
    private static string UniqueName(string baseName, HashSet<string> taken)
    {
        string candidate = baseName;
        int n = 1;
        while (!taken.Add(candidate)) candidate = $"{baseName}_{n++}";
        return candidate;
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

    /// <summary>Splices a <c>call Fn()</c> for each of <paramref name="initFns"/> into the target's
    /// <c>InitCustomTriggers</c>. By default they land right before <c>endfunction</c> (registration
    /// order among sibling InitTrig_* calls does not matter). <paramref name="atFront"/> instead
    /// inserts them right after the function's own opening line, for a global-state helper that must
    /// run BEFORE any trigger gets wired (see the call site in <see cref="PortScript"/>), matching
    /// where the source itself calls it.</summary>
    private static string HookInit(
        string mergedJ, IReadOnlyList<string> initFns, string marker, out bool hooked, bool atFront = false)
    {
        hooked = false;
        string nl = mergedJ.Contains("\r\n") ? "\r\n" : "\n";
        var text = mergedJ.Replace("\r\n", "\n");
        var idx = JassFunctionIndex.Parse(text)
            .FirstOrDefault(f => f.Name == "InitCustomTriggers");
        if (idx is null) return mergedJ;

        var lines = text.Split('\n').ToList();
        var calls = initFns.Select(fn => $"    call {fn}() // {marker}").ToList();
        int at = atFront ? idx.StartLine : idx.EndLine - 1; // after "function ..." / before "endfunction"
        lines.InsertRange(at, calls);
        hooked = true;
        return string.Join('\n', lines).Replace("\n", nl);
    }

    /// <summary>Splices a <c>call Fn()</c> into the target's <c>main</c>, right before its call to
    /// InitCustomTriggers (the JASS convention the source itself follows, globals initialized before
    /// any trigger wiring), falling back to the end of main when InitCustomTriggers is not called
    /// there. Used to wire a carried InitGlobals, see the call site in <see cref="PortScript"/>.
    /// </summary>
    private static string HookMain(string mergedJ, string functionName, string marker, out bool hooked)
    {
        hooked = false;
        string nl = mergedJ.Contains("\r\n") ? "\r\n" : "\n";
        var text = mergedJ.Replace("\r\n", "\n");
        var main = JassFunctionIndex.Parse(text).FirstOrDefault(f => f.Name == "main");
        if (main is null) return mergedJ;

        var lines = text.Split('\n').ToList();
        int at = -1;
        for (int i = main.StartLine; i < main.EndLine - 1 && i < lines.Count; i++)
            if (Regex.IsMatch(lines[i], @"\bcall\s+InitCustomTriggers\s*\(")) { at = i; break; }
        if (at < 0) at = main.EndLine - 1; // no InitCustomTriggers call found: just before endfunction

        lines.Insert(at, $"    call {functionName}() // {marker}");
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
