// src/Wc3.Commands/HeroInstallCommand.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Wc3.Model;

namespace Wc3.Commands;

public sealed record InstallResult(
    bool Ok,
    string Message,
    IReadOnlyDictionary<string, string> RawcodeRemap,
    int ObjectsCreated,
    int FieldsApplied,
    int AssetsWritten,
    int AssetsSkippedIdentical,
    IReadOnlyList<string> Collisions,
    IReadOnlyList<string> UnmetRequirements,
    IReadOnlyList<string> NextSteps,
    string? RegisteredWith,
    IReadOnlyList<string> Assumptions);

/// <summary>
/// Installs a <see cref="HeroDefinition"/> into any map, the <c>install</c> verb of the format.
///
/// Three rules here exist because their absence caused real, expensive failures:
///
/// ASSETS ARE NEVER CLOBBERED. The porter silently overwrote six of a target map's own textures
/// and a model with same-named files from the source, changing how unrelated heroes rendered. An
/// asset whose hash matches is skipped as already present; one that differs is REFUSED and
/// reported, never overwritten.
///
/// RAWCODES ARE REMAPPED, NOT ASSUMED. The target may already use the definition's codes, so each
/// object is created from its base and the resulting code recorded. The script is rewritten
/// through that map, so nothing depends on a code surviving.
///
/// REQUIREMENTS ARE CHECKED BEFORE WRITING. A hero can have perfect objects, assets and script and
/// still not exist to the player, because a map builds its roster from its own script. This looks
/// for the target's registration call and reports it as an unmet requirement rather than producing
/// a map that silently lacks the hero. That exact failure cost a full day.
/// </summary>
public static class HeroInstallCommand
{
    public static InstallResult Run(string definitionDirectory, MapDocument target,
        bool force = false, string? gameDir = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        var jsonPath = Path.Combine(definitionDirectory, "hero.json");
        if (!File.Exists(jsonPath))
            return Fail($"no hero.json in '{definitionDirectory}'");

        HeroDefinition? def;
        try { def = JsonSerializer.Deserialize<HeroDefinition>(File.ReadAllText(jsonPath)); }
        catch (JsonException ex) { return Fail($"hero.json could not be parsed: {ex.Message}"); }
        if (def is null) return Fail("hero.json could not be parsed");
        if (def.SchemaVersion > HeroDefinition.CurrentSchemaVersion)
            return Fail($"hero.json is schema v{def.SchemaVersion}, this build understands "
                        + $"v{HeroDefinition.CurrentSchemaVersion}");

        // 1. Assets first, because a refused collision should abort before anything is created.
        var collisions = new List<string>();
        var pending = new List<(string path, byte[] bytes)>();
        int skipped = 0;
        foreach (var a in def.Assets)
        {
            var disk = Path.Combine(definitionDirectory, "assets",
                a.Path.Replace('\\', Path.DirectorySeparatorChar));
            if (!File.Exists(disk)) { collisions.Add($"{a.Path}: missing from the definition folder"); continue; }
            var bytes = File.ReadAllBytes(disk);

            if (target.GetFile(a.Path) is { } existing)
            {
                var have = Convert.ToHexString(SHA256.HashData(existing.OverrideBytes ?? existing.RawBytes))
                    .ToLowerInvariant();
                if (have == a.Sha256) { skipped++; continue; }   // already exactly this file
                collisions.Add($"{a.Path}: the target has a DIFFERENT file at this path");
                continue;
            }
            pending.Add((a.Path, bytes));
        }
        if (collisions.Count > 0 && !force)
            return new(false,
                $"{collisions.Count} asset collision(s); the target's own files would be overwritten. "
                + "Re-run with --force only if you are certain, or rename the definition's assets.",
                new Dictionary<string, string>(), 0, 0, 0, skipped, collisions,
                Array.Empty<string>(), Array.Empty<string>(), null, Array.Empty<string>());

        // 2. Objects. Created from their base, so the target's existing codes are never disturbed.
        var remap = new Dictionary<string, string>(StringComparer.Ordinal);
        int created = 0, fields = 0;
        foreach (var o in def.Objects)
        {
            if (o.BaseRawcode is null) continue;
            if (!Enum.TryParse<ObjectKind>(o.Kind, ignoreCase: true, out var kind)) continue;
            var made = ObjectNewCommand.Execute(target, kind, o.BaseRawcode);
            if (!made.Ok || made.NewRawcode is null) continue;
            remap[o.Rawcode] = made.NewRawcode;
            created++;
            foreach (var f in o.Fields)
                if (ObjectSetCommand.Execute(target, kind, made.NewRawcode, f.Code, f.Value).Ok)
                    fields++;
        }

        // 3. Assets, now that nothing has been refused. Each one also gets a war3map.imp entry:
        // an archive file the import table does not declare is a real inconsistency, and 'lint'
        // reports it, so writing the bytes without the entry only moves the bug.
        foreach (var (path, bytes) in pending)
            FileEditCommand.AddOrReplace(target, path, bytes);
        if (pending.Count > 0) AddImportEntries(target, pending.Select(p => p.path));

        // 4. Script, rewritten through the remap so no carried code depends on an original code.
        if (def.ScriptFile is not null)
        {
            var scriptDisk = Path.Combine(definitionDirectory, def.ScriptFile);
            if (File.Exists(scriptDisk))
            {
                var body = File.ReadAllText(scriptDisk);
                foreach (var (from, to) in remap)
                    body = body.Replace($"'{from}'", $"'{to}'", StringComparison.Ordinal);
                var existing = target.GetFile("war3map.j");
                var head = existing is null ? "" :
                    ScriptBytes.GetString(existing.OverrideBytes ?? existing.RawBytes);
                // Globals must land INSIDE the target's own globals block; appended after it they
                // are a syntax error, and the carried functions that read them will not compile.
                head = InsertGlobals(head, def.Globals, remap);
                FileEditCommand.WriteText(target, "war3map.j",
                    head + "\n\n// ==== wc3ctl hero: " + def.Name + " (" + def.Id + ") ====\n" + body);
            }
        }

        // 5. Requirements. Reported, never silently ignored.
        var unmet = new List<string>();
        var next = new List<string>();
        var contract = ContractCommand.Run(target);
        // Call count and a near-1.0 rawcode ratio are NOT enough to identify a hero roster. On a
        // map with no roster at all they matched SetItemRelatives(integer itemId, ...) - an
        // item-relation table - and inserted a bogus call. The signature is the discriminator: a
        // hero roster names its first parameter for a unit (GGGA: RPB_AddHero(integer unitCode,
        // ...)), an item table names it for an item. Refusing to guess is the correct outcome when
        // no candidate looks unit-shaped; a wrong automatic edit is far worse than a manual step.
        var roster = contract.Registries.FirstOrDefault(r =>
            r.CallCount >= 8
            && r.RegisteredRawcodes.Count >= r.CallCount * 0.8
            && FirstParameterLooksLikeAUnit(r.Signature));
        string installedRoot = remap.TryGetValue(def.Id, out var rootCode) ? rootCode : def.Id;

        string? registeredWith = null;
        var assumptions = new List<string>();
        if (roster is null && contract.Templates.Count > 0)
        {
            // Block-style roster. The target registers each hero with several statements rather
            // than one call, so copy a REAL existing entry and substitute. Inventing the shape is
            // what fails here: the block carries a counter, a hidden preview dummy and one
            // UnitAddAbility per spell, and omitting any of those half-registers the hero.
            var tmpl = contract.Templates[0];
            var block = InstantiateTemplate(tmpl, installedRoot, def, remap, assumptions);
            if (block.Count > 0 && AppendAfterLine(target, tmpl.EndLine, block))
            {
                registeredWith = tmpl.ArrayName + "[" + tmpl.Entries + "] block, "
                    + block.Count + " statement(s) copied from the target's own convention";
                next.Add("registered automatically into " + tmpl.ArrayName
                         + " as entry " + tmpl.Entries);
                next.Add("NOT automated: the display name. This map sets it in a separate "
                         + "if/elseif chain (FRAME_PlayerPickString); add a branch for this hero.");
            }
            else
                unmet.Add("roster-registration: could not instantiate the " + tmpl.ArrayName
                          + " block; add an entry by hand using the template from 'wc3ctl contract'.");
        }
        else if (roster is null)
            unmet.Add("roster-registration: no hero roster call was detected in the target. If it "
                      + "has one, the hero must be added to it by hand or it will not be selectable.");
        else
        {
            // Automatic registration. The argument shape is learned from how the target already
            // registers every other hero, so this follows the map's own convention instead of a
            // guess at it. Anything inferred is reported, never hidden.
            var call = BuildRegistrationCall(roster, installedRoot, def, assumptions);
            if (InsertAfterLastRegistration(target, roster.Function, call))
            {
                registeredWith = call.Trim();
                next.Add($"registered automatically: {registeredWith}");
            }
            else
                unmet.Add($"roster-registration: could not place the call automatically; add "
                          + $"{call.Trim()} inside the target's registration function by hand.");
        }

        if (contract.SpellDispatchers.Count == 0)
            unmet.Add("spell-dispatch: no cast dispatcher detected; the hero's abilities may exist "
                      + "but do nothing.");

        return new(true,
            $"installed {def.Name} as '{installedRoot}'",
            remap, created, fields, pending.Count, skipped,
            collisions, unmet, next, registeredWith, assumptions);
    }

    /// <summary>
    /// Builds the registration call by copying the target's OWN convention: same function, same
    /// argument count, our rawcode first. A string argument that looks like an asset path is
    /// filled from the definition's icon; every other argument reuses the value an existing
    /// registration used, so the call is always well-formed even on a map we have never seen.
    /// </summary>
    private static string BuildRegistrationCall(RosterRegistry roster, string rawcode,
        HeroDefinition def, List<string> assumptions)
    {
        var args = SplitArguments(roster.ExampleCall);
        if (args.Count == 0) return $"    call {roster.Function}('{rawcode}')";

        var built = new List<string> { $"'{rawcode}'" };
        string? icon = def.Assets.FirstOrDefault(a =>
            a.Category.Equals("icon", StringComparison.OrdinalIgnoreCase))?.Path;

        for (int i = 1; i < args.Count; i++)
        {
            var sample = args[i].Trim();
            if (icon is not null && LooksLikeAssetArgument(sample))
            {
                built.Add("\"" + icon.Replace("\\", "\\\\") + "\"");
                assumptions.Add($"argument {i + 1} (portrait) set to the definition's icon: {icon}");
                icon = null;
            }
            else
            {
                built.Add(sample);
                assumptions.Add($"argument {i + 1} copied from an existing registration: {sample}");
            }
        }
        return $"    call {roster.Function}({string.Join(" , ", built)})";
    }

    private static bool LooksLikeAssetArgument(string literal) =>
        literal.StartsWith("\"", StringComparison.Ordinal)
        && (literal.Contains(".blp", StringComparison.OrdinalIgnoreCase)
            || literal.Contains(".tga", StringComparison.OrdinalIgnoreCase)
            || literal.Contains(".dds", StringComparison.OrdinalIgnoreCase));

    /// <summary>Top-level arguments of one call, so nested parens and commas in strings survive.</summary>
    private static List<string> SplitArguments(string callLine)
    {
        int open = callLine.IndexOf('(');
        int close = callLine.LastIndexOf(')');
        if (open < 0 || close <= open) return new List<string>();
        var inner = callLine[(open + 1)..close];
        var parts = new List<string>();
        int depth = 0, start = 0; bool inString = false;
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '\"') inString = !inString;
            else if (!inString && c == '(') depth++;
            else if (!inString && c == ')') depth--;
            else if (!inString && c == ',' && depth == 0)
            { parts.Add(inner[start..i]); start = i + 1; }
        }
        parts.Add(inner[start..]);
        return parts;
    }

    /// <summary>
    /// Places the call immediately after the LAST existing registration, so it lands inside the
    /// same function and after whatever setup those calls depend on.
    /// </summary>
    private static bool InsertAfterLastRegistration(MapDocument target, string function, string call)
    {
        var entry = target.GetFile("war3map.j");
        if (entry is null) return false;
        var text = ScriptBytes.GetString(entry.OverrideBytes ?? entry.RawBytes);
        var lines = text.Split('\n').ToList();
        int last = -1;
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].Contains("call " + function + "(", StringComparison.Ordinal)) last = i;
        if (last < 0) return false;
        lines.Insert(last + 1, call);
        FileEditCommand.AddOrReplace(target, entry.FileName!, ScriptBytes.GetBytes(string.Join("\n", lines)));
        return true;
    }

    /// <summary>
    /// Appends paths to <c>war3map.imp</c>, bumping its declared count. The table is a version and
    /// a count followed by (flag byte, NUL-terminated path) records; the flag is copied from the
    /// map's own first entry rather than guessed.
    /// </summary>
    private static void AddImportEntries(MapDocument target, IEnumerable<string> paths)
    {
        var entry = target.GetFile("war3map.imp");
        var existing = entry is null ? Array.Empty<byte>() : (entry.OverrideBytes ?? entry.RawBytes);
        byte flag = existing.Length > 8 ? existing[8] : (byte)0x0D;

        uint version = 1, count = 0;
        var body = new List<byte>();
        if (existing.Length >= 8)
        {
            version = BitConverter.ToUInt32(existing, 0);
            count = BitConverter.ToUInt32(existing, 4);
            body.AddRange(existing.Skip(8));
        }

        foreach (var p in paths)
        {
            body.Add(flag);
            body.AddRange(Encoding.UTF8.GetBytes(p));
            body.Add(0);
            count++;
        }

        var rebuilt = new List<byte>();
        rebuilt.AddRange(BitConverter.GetBytes(version));
        rebuilt.AddRange(BitConverter.GetBytes(count));
        rebuilt.AddRange(body);
        FileEditCommand.AddOrReplace(target, "war3map.imp", rebuilt.ToArray());
    }

    /// <summary>
    /// Splices the definition's globals in just before the target's <c>endglobals</c>, rewriting
    /// rawcode literals through the install remap so a carried declaration cannot reference a code
    /// that was renamed. A name the target already declares is skipped rather than duplicated.
    /// </summary>
    private static string InsertGlobals(string script, IReadOnlyList<string> globals,
        IReadOnlyDictionary<string, string> remap)
    {
        if (globals.Count == 0) return script;
        var lines = script.Split('\n').ToList();
        int end = lines.FindIndex(l => l.Trim() == "endglobals");
        if (end < 0) return script;

        var already = new HashSet<string>(StringComparer.Ordinal);
        var name = new System.Text.RegularExpressions.Regex(
            @"^\s*(?:constant\s+)?[A-Za-z_][A-Za-z0-9_]*\s+(?:array\s+)?([A-Za-z_][A-Za-z0-9_]*)");
        for (int i = 0; i < end; i++)
        {
            var m = name.Match(lines[i]);
            if (m.Success) already.Add(m.Groups[1].Value);
        }

        var add = new List<string>();
        foreach (var g in globals)
        {
            var m = name.Match(g);
            if (m.Success && already.Contains(m.Groups[1].Value)) continue;
            var line = g;
            foreach (var (from, to) in remap)
                line = line.Replace($"'{from}'", $"'{to}'", StringComparison.Ordinal);
            add.Add(line);
        }
        if (add.Count == 0) return script;

        lines.InsertRange(end, add);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// True when the registration's FIRST parameter names a unit rather than an item, ability or
    /// anything else. Deliberately conservative: an unrecognised shape returns false, so install
    /// reports the requirement as unmet instead of writing a call it cannot justify.
    /// </summary>
    private static bool FirstParameterLooksLikeAUnit(string signature)
    {
        var first = signature.Split(',')[0];
        var name = first.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (name is null) return false;
        if (name.Contains("item", StringComparison.OrdinalIgnoreCase)) return false;
        return name.Contains("unit", StringComparison.OrdinalIgnoreCase)
            || name.Contains("hero", StringComparison.OrdinalIgnoreCase)
            || name.Contains("char", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Copies a real roster entry, substituting this hero's rawcode for the sample's and her
    /// abilities for the sample's ability ids. Everything else is preserved verbatim so the new
    /// entry matches the map's own convention exactly, including the preview dummy and the counter.
    /// </summary>
    private static List<string> InstantiateTemplate(RosterTemplate tmpl, string rawcode,
        HeroDefinition def, IReadOnlyDictionary<string, string> remap, List<string> assumptions)
    {
        // What "set Arr[n]=X" assigns in the sample, i.e. the sample hero's id.
        var slot = new Regex(@"^\s*set\s+" + Regex.Escape(tmpl.ArrayName)
                             + @"\s*\[[^\]]*\]\s*=\s*([A-Za-z_][A-Za-z0-9_]*)");
        string? sampleHeroId = null;
        foreach (var l in tmpl.TemplateLines)
        {
            var m = slot.Match(l);
            if (m.Success) { sampleHeroId = m.Groups[1].Value; break; }
        }
        if (sampleHeroId is null) return new List<string>();

        var addAbility = new Regex(@"UnitAddAbility\s*\([^,]*,\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)");
        int sampleAbilityCount = tmpl.TemplateLines.Count(l => addAbility.IsMatch(l));

        // Ours: the abilities the definition carries, in declaration order, after install's remap.
        var ours = def.Objects
            .Where(o => o.Kind.Equals("ability", StringComparison.OrdinalIgnoreCase))
            .Select(o => remap.TryGetValue(o.Rawcode, out var to) ? to : o.Rawcode)
            .ToList();

        var outLines = new List<string>();
        int abilityIndex = 0;
        foreach (var raw in tmpl.TemplateLines)
        {
            var line = raw;

            // This hero goes after every existing entry.
            line = Regex.Replace(line, @"^(\s*set\s+n\s*=\s*)-?\d+", "${1}" + tmpl.Entries);
            line = line.Replace(sampleHeroId, "'" + rawcode + "'", StringComparison.Ordinal);

            var am = addAbility.Match(line);
            if (am.Success)
            {
                // Fewer abilities than the sample: drop the surplus line rather than emit a
                // reference to an ability this hero does not have.
                if (abilityIndex >= ours.Count) continue;
                line = line.Replace(am.Groups[1].Value, "'" + ours[abilityIndex] + "'",
                    StringComparison.Ordinal);
                abilityIndex++;
            }
            outLines.Add(line);
        }

        assumptions.Add("roster block copied from " + tmpl.ArrayName + " entry at line " + tmpl.StartLine);
        assumptions.Add(abilityIndex + " of the sample's " + sampleAbilityCount
                        + " ability slot(s) filled from " + ours.Count
                        + " carried ability object(s), in declaration order");
        return outLines;
    }

    /// <summary>Inserts lines directly after a 1-based line number in war3map.j.</summary>
    private static bool AppendAfterLine(MapDocument target, int oneBasedLine, List<string> block)
    {
        var entry = target.GetFile("war3map.j");
        if (entry is null) return false;
        var text = ScriptBytes.GetString(entry.OverrideBytes ?? entry.RawBytes);
        var lines = text.Split('\n').ToList();
        if (oneBasedLine < 1 || oneBasedLine > lines.Count) return false;
        lines.InsertRange(oneBasedLine, block);
        FileEditCommand.AddOrReplace(target, entry.FileName!, ScriptBytes.GetBytes(string.Join("\n", lines)));
        return true;
    }

    /// <summary>
    /// Byte-preserving codec for script text. A map's war3map.j is NOT necessarily UTF-8: WOS2
    /// carries bytes that are not valid UTF-8, and decoding then re-encoding them as UTF-8 replaces
    /// each with '?', which pjass reports as "Unrecognized character ? (ASCII 63)" and which
    /// silently corrupts author names and localised strings. Latin-1 maps every byte 0..255 to the
    /// same code point and back, so a read/modify/write round-trip is lossless whatever the real
    /// encoding was.
    /// </summary>
    private static readonly Encoding ScriptBytes = Encoding.Latin1;

    private static InstallResult Fail(string message) =>
        new(false, message, new Dictionary<string, string>(), 0, 0, 0, 0,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null,
            Array.Empty<string>());
}
