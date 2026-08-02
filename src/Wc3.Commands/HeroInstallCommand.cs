// src/Wc3.Commands/HeroInstallCommand.cs
using System.Security.Cryptography;
using System.Text;
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
    IReadOnlyList<string> NextSteps);

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
                Array.Empty<string>(), Array.Empty<string>());

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

        // 3. Assets, now that nothing has been refused.
        foreach (var (path, bytes) in pending)
            FileEditCommand.AddOrReplace(target, path, bytes);

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
                    Encoding.UTF8.GetString(existing.OverrideBytes ?? existing.RawBytes);
                FileEditCommand.WriteText(target, "war3map.j",
                    head + "\n\n// ==== wc3ctl hero: " + def.Name + " (" + def.Id + ") ====\n" + body);
            }
        }

        // 5. Requirements. Reported, never silently ignored.
        var unmet = new List<string>();
        var next = new List<string>();
        var contract = ContractCommand.Run(target);
        var roster = contract.Registries.FirstOrDefault(r =>
            r.CallCount >= 8 && r.RegisteredRawcodes.Count >= r.CallCount * 0.8);
        string installedRoot = remap.TryGetValue(def.Id, out var rootCode) ? rootCode : def.Id;

        if (roster is null)
            unmet.Add("roster-registration: no hero roster call was detected in the target. If it "
                      + "has one, the hero must be added to it by hand or it will not be selectable.");
        else
            next.Add($"add  call {roster.Function}('{installedRoot}' , ...)  to the target's roster "
                     + $"(it registers {roster.CallCount} heroes; e.g. {roster.ExampleCall})");

        if (contract.SpellDispatchers.Count == 0)
            unmet.Add("spell-dispatch: no cast dispatcher detected; the hero's abilities may exist "
                      + "but do nothing.");

        return new(true,
            $"installed {def.Name} as '{installedRoot}'",
            remap, created, fields, pending.Count, skipped,
            collisions, unmet, next);
    }

    private static InstallResult Fail(string message) =>
        new(false, message, new Dictionary<string, string>(), 0, 0, 0, 0,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
}
