// src/Wc3.Commands/PortCommand.cs
using War3Net.Build.Import;
using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.Model;

namespace Wc3.Commands;

/// <summary>
/// Ports a unit (its custom-object closure + imported assets, with referenced strings
/// inlined) from a source map into a target map. Rawcodes that collide with the target
/// are auto-remapped and every in-bundle reference is rewritten to match — no manual
/// configuration. This is the reliable "Tier 1" port (object data + assets + strings);
/// the JASS script closure is layered on separately.
/// </summary>
public static class PortCommand
{
    /// <summary>Neutral form of one object-data modification (Level = 0 for non-leveled).</summary>
    private sealed record PortMod(int Level, int Id, ObjectDataType Type, object? Value);

    /// <summary>Neutral form of one object entry across the three War3Net shapes.</summary>
    private sealed class PortGroup
    {
        public required int OldId { get; init; }
        public required int NewId { get; init; }         // 0 = modifies a standard object
        public List<PortMod> Mods { get; } = new();
    }

    /// <summary>
    /// Injects the bundle's custom objects + assets into <paramref name="target"/> (mutated
    /// in place). The caller saves the target. Base-game deps and missing assets are skipped
    /// (they already exist in any target). Returns a full report of what was done.
    /// </summary>
    public static PortResult PortUnit(
        MapDocument source, UnitBundle bundle, MapDocument target, bool includeScript = true)
        => PortCore(source, bundle, target, RawcodeAllocator.UsedRawcodes(target),
            alreadyPorted: null, pendingCopies: null, includeScript, apply: true);

    /// <summary>
    /// Computes exactly the report <see cref="PortUnit"/> would produce — the rawcode
    /// remap plan, the objects, which asset files would copy vs skip, the string-inline
    /// count and the script closure summary — WITHOUT mutating <paramref name="target"/>
    /// or writing anything. Same code path as the real port behind a single apply switch,
    /// so preview and port cannot drift.
    /// </summary>
    public static PortResult PreviewPort(
        MapDocument source, UnitBundle bundle, MapDocument target, bool includeScript = true)
        => PortCore(source, bundle, target, RawcodeAllocator.UsedRawcodes(target),
            alreadyPorted: null,
            pendingCopies: new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase),
            includeScript, apply: false);

    /// <summary>
    /// Ports several units into the SAME target in one operation. The used-rawcode set
    /// grows across bundles so later units can never collide with earlier ports, objects
    /// shared between bundles (a common custom ability) are ported once and reused, and
    /// the script closures are merged and spliced once (a shared spell dispatcher is
    /// carried a single time, with every unit's rawcode remap applied). The caller saves
    /// the target once at the end.
    /// </summary>
    public static BatchPortResult PortUnits(
        MapDocument source, IReadOnlyList<UnitBundle> bundles, MapDocument target, bool includeScript = true)
        => BatchCore(source, bundles, target, includeScript, apply: true);

    /// <summary>The combined report <see cref="PortUnits"/> would produce, writing nothing.</summary>
    public static BatchPortResult PreviewPorts(
        MapDocument source, IReadOnlyList<UnitBundle> bundles, MapDocument target, bool includeScript = true)
        => BatchCore(source, bundles, target, includeScript, apply: false);

    private static BatchPortResult BatchCore(
        MapDocument source, IReadOnlyList<UnitBundle> bundles, MapDocument target,
        bool includeScript, bool apply)
    {
        var used = RawcodeAllocator.UsedRawcodes(target);
        var alreadyPorted = new Dictionary<int, int>();
        // In preview mode the target never mutates, so files "copied" by earlier bundles
        // are simulated here to keep later bundles' copy-vs-skip decisions identical.
        var pendingCopies = apply ? null : new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var results = new List<PortResult>();
        foreach (var bundle in bundles)
            results.Add(PortCore(source, bundle, target, used, alreadyPorted, pendingCopies,
                includeScript: false, apply));

        // One combined script port: the union of the closures (shared dispatcher functions
        // carried once) with the union of every unit's rawcode remap.
        ScriptPortInfo? script = null;
        var warnings = new List<string>();
        if (includeScript)
        {
            try
            {
                var functions = bundles.SelectMany(b => b.Functions)
                    .GroupBy(f => f.Name, StringComparer.Ordinal)
                    .Select(g => g.First())
                    .OrderBy(f => f.StartLine)
                    .ToList();
                var codeRemap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var r in results)
                    foreach (var m in r.Remaps)
                        codeRemap.TryAdd(m.From, m.To);
                var label = string.Join(" + ", results.Select(r => $"{r.RootName ?? r.RootRawcode} ({r.RootRawcode})"));
                script = ScriptPorter.PortScript(source, target, functions, label, codeRemap, apply);
            }
            catch (Exception ex)
            {
                warnings.Add($"script port skipped (object/asset port is unaffected): {ex.Message}");
            }
        }

        return new BatchPortResult(results, script, warnings);
    }

    /// <summary>
    /// The single shared port pipeline. <paramref name="apply"/> true mutates the target;
    /// false computes the identical report without touching it. <paramref name="used"/>
    /// accumulates allocated rawcodes across a batch; <paramref name="alreadyPorted"/>
    /// (batch only) maps source ids already ported by an earlier bundle to their target
    /// ids so shared dependencies are reused, not duplicated; <paramref name="pendingCopies"/>
    /// (preview only) simulates the asset copies a real port would have made.
    /// </summary>
    private static PortResult PortCore(
        MapDocument source, UnitBundle bundle, MapDocument target,
        HashSet<int> used, Dictionary<int, int>? alreadyPorted,
        Dictionary<string, byte[]>? pendingCopies,
        bool includeScript, bool apply)
    {
        var warnings = new List<string>();
        var diagnostics = new List<string>(bundle.Diagnostics);

        var customs = bundle.Objects.Where(o => o.CustomToMap).ToList();

        // 1) Rawcode remap: reassign any custom rawcode already defined in the target.
        //    Objects an earlier bundle in this batch already ported are reused as-is.
        var remap = new Dictionary<int, int>();       // source id -> target id (identity when free)
        var remapReport = new List<RawcodeRemap>();
        var sharedIds = new HashSet<int>();           // ported by an earlier bundle — don't re-inject
        foreach (var o in customs)
        {
            int id = o.Rawcode.FromRawcode();
            if (remap.ContainsKey(id)) continue;
            if (alreadyPorted is not null && alreadyPorted.TryGetValue(id, out var prior))
            {
                remap[id] = prior;
                sharedIds.Add(id);
                if (prior != id)
                    remapReport.Add(new RawcodeRemap(o.Kind, o.Rawcode, prior.ToRawcode()));
                diagnostics.Add($"{o.Kind} {o.Rawcode} already ported by an earlier unit in this batch — reusing {prior.ToRawcode()}.");
                continue;
            }
            if (used.Contains(id))
            {
                int fresh = RawcodeAllocator.Allocate(o.Rawcode, used);
                remap[id] = fresh;
                used.Add(fresh);
                remapReport.Add(new RawcodeRemap(o.Kind, o.Rawcode, fresh.ToRawcode()));
            }
            else
            {
                remap[id] = id;
                used.Add(id);
            }
        }

        // 2) Inject object data, per kind, with references rewritten.
        var srcStrings = MapStrings.From(source);
        int inlinedStrings = 0;
        var portedObjects = new List<PortedObject>();

        foreach (var byKind in customs.GroupBy(o => o.Kind))
        {
            var kind = byKind.Key;
            var wantIds = byKind.Select(o => o.Rawcode.FromRawcode()).ToHashSet();

            // Merge the map layer with the Reforged skin layer (skin wins per field).
            var groups = new Dictionary<int, PortGroup>();
            foreach (var file in new[] { ObjectKinds.Info(kind).MapFile, ObjectKinds.Info(kind).SkinFile })
                foreach (var g in ExtractGroups(source.GetFile(file)?.Model, wantIds))
                    groups[g.NewId != 0 ? g.NewId : g.OldId] = Merge(groups.GetValueOrDefault(KeyOf(g)), g);

            foreach (var g in groups.Values)
            {
                if (sharedIds.Contains(g.NewId != 0 ? g.NewId : g.OldId))
                    continue; // an earlier bundle in this batch already injected it
                var remapped = RemapGroup(g, remap, srcStrings, ref inlinedStrings);
                if (apply)
                    InjectGroup(target, kind, remapped);
                var node = byKind.First(o => o.Rawcode.FromRawcode() == (g.NewId != 0 ? g.NewId : g.OldId));
                string ownId = (remapped.NewId != 0 ? remapped.NewId : remapped.OldId).ToRawcode();
                portedObjects.Add(new PortedObject(kind, ownId, node.Name, remapped.NewId == 0));
                if (remapped.NewId == 0)
                    warnings.Add($"{kind} {ownId} modifies a standard object — it will change that object in the target too.");
            }
        }

        // 3) Copy imported assets (present-in-source only) + register in war3map.imp.
        var copied = new List<string>();
        var skipped = new List<string>();
        var srcImports = (source.GetFile("war3map.imp")?.Model as ImportedFiles);
        var tgtImports = (target.GetFile("war3map.imp")?.Model as ImportedFiles)
                         ?? new ImportedFiles(ImportedFilesFormatVersion.v1);
        bool importsChanged = false;

        foreach (var f in bundle.Files)
        {
            if (!f.PresentInMap) { skipped.Add($"{f.Path} (base-game/not imported)"); continue; }
            var srcEntry = FindFile(source, f.Path);
            if (srcEntry is null || srcEntry.RawBytes.Length == 0) { skipped.Add($"{f.Path} (unreadable in source)"); continue; }

            // What the target holds at that path — including files "copied" by an earlier
            // bundle of a preview batch (pendingCopies simulates the real port's mutation).
            var tgtBytes = FindFile(target, f.Path)?.RawBytes;
            if (tgtBytes is null && pendingCopies is not null
                && pendingCopies.TryGetValue(NormalizePath(f.Path), out var pending))
                tgtBytes = pending;

            if (tgtBytes is not null && tgtBytes.SequenceEqual(srcEntry.RawBytes))
            { skipped.Add($"{f.Path} (already in target)"); continue; }
            if (tgtBytes is not null)
                warnings.Add($"{f.Path} already exists in target with different bytes — overwritten.");

            if (apply)
            {
                target.AddOrReplaceRawFile(srcEntry.FileName!, srcEntry.RawBytes);

                if (!tgtImports.Files.Any(i => PathEq(i.FullPath, srcEntry.FileName!)))
                {
                    var flags = srcImports?.Files.FirstOrDefault(i => PathEq(i.FullPath, srcEntry.FileName!))?.Flags
                                ?? (ImportedFileFlags)10; // standard custom-path import
                    tgtImports.Files.Add(new ImportedFile { Flags = flags, FullPath = srcEntry.FileName! });
                    importsChanged = true;
                }
            }
            else
            {
                pendingCopies![NormalizePath(srcEntry.FileName!)] = srcEntry.RawBytes;
            }
            copied.Add(srcEntry.FileName!);
        }
        if (apply && importsChanged)
            target.AddOrReplaceModelFile("war3map.imp", tgtImports);

        // 4) Best-effort JASS script closure append (defensive — never breaks the port).
        ScriptPortInfo? scriptInfo = null;
        if (includeScript)
        {
            try
            {
                var codeRemap = remap.Where(kv => kv.Key != kv.Value)
                    .ToDictionary(kv => kv.Key.ToRawcode(), kv => kv.Value.ToRawcode(), StringComparer.Ordinal);
                scriptInfo = ScriptPorter.PortScript(source, target, bundle.Functions,
                    $"{bundle.RootName ?? bundle.RootRawcode} ({bundle.RootRawcode})", codeRemap, apply);
            }
            catch (Exception ex)
            {
                warnings.Add($"script port skipped (object/asset port is unaffected): {ex.Message}");
            }
        }

        // Batch bookkeeping: later bundles reuse everything this one ported (identity
        // mappings included — a later bundle must not treat them as fresh collisions).
        if (alreadyPorted is not null)
            foreach (var kv in remap)
                alreadyPorted[kv.Key] = kv.Value;

        string rootPortedTo = remap.TryGetValue(bundle.RootRawcode.FromRawcode(), out var rid)
            ? rid.ToRawcode() : bundle.RootRawcode;

        return new PortResult(
            bundle.RootRawcode, rootPortedTo, bundle.RootName,
            remapReport, portedObjects, copied, skipped, inlinedStrings, warnings, diagnostics, scriptInfo);
    }

    // ---- neutral group extraction / injection ------------------------------

    private static int KeyOf(PortGroup g) => g.NewId != 0 ? g.NewId : g.OldId;

    private static PortGroup Merge(PortGroup? existing, PortGroup incoming)
    {
        if (existing is null) return incoming;
        // skin (incoming, seen second) wins per (Level, Id).
        foreach (var m in incoming.Mods)
        {
            existing.Mods.RemoveAll(x => x.Level == m.Level && x.Id == m.Id);
            existing.Mods.Add(m);
        }
        return existing;
    }

    private static IEnumerable<PortGroup> ExtractGroups(object? model, HashSet<int> wantIds)
    {
        IEnumerable<PortGroup> FromSimple(IEnumerable<SimpleObjectModification> gs) =>
            gs.Where(g => wantIds.Contains(g.NewId != 0 ? g.NewId : g.OldId)).Select(g =>
            {
                var pg = new PortGroup { OldId = g.OldId, NewId = g.NewId };
                foreach (var m in g.Modifications) pg.Mods.Add(new PortMod(0, m.Id, m.Type, m.Value));
                return pg;
            });
        IEnumerable<PortGroup> FromLevel(IEnumerable<LevelObjectModification> gs) =>
            gs.Where(g => wantIds.Contains(g.NewId != 0 ? g.NewId : g.OldId)).Select(g =>
            {
                var pg = new PortGroup { OldId = g.OldId, NewId = g.NewId };
                foreach (var m in g.Modifications) pg.Mods.Add(new PortMod(m.Level, m.Id, m.Type, m.Value));
                return pg;
            });
        IEnumerable<PortGroup> FromVar(IEnumerable<VariationObjectModification> gs) =>
            gs.Where(g => wantIds.Contains(g.NewId != 0 ? g.NewId : g.OldId)).Select(g =>
            {
                var pg = new PortGroup { OldId = g.OldId, NewId = g.NewId };
                foreach (var m in g.Modifications) pg.Mods.Add(new PortMod(m.Variation, m.Id, m.Type, m.Value));
                return pg;
            });

        return model switch
        {
            UnitObjectData m => FromSimple(m.BaseUnits.Concat(m.NewUnits)),
            ItemObjectData m => FromSimple(m.BaseItems.Concat(m.NewItems)),
            DestructableObjectData m => FromSimple(m.BaseDestructables.Concat(m.NewDestructables)),
            BuffObjectData m => FromSimple(m.BaseBuffs.Concat(m.NewBuffs)),
            AbilityObjectData m => FromLevel(m.BaseAbilities.Concat(m.NewAbilities)),
            UpgradeObjectData m => FromLevel(m.BaseUpgrades.Concat(m.NewUpgrades)),
            DoodadObjectData m => FromVar(m.BaseDoodads.Concat(m.NewDoodads)),
            _ => Enumerable.Empty<PortGroup>(),
        };
    }

    private static PortGroup RemapGroup(
        PortGroup g, IReadOnlyDictionary<int, int> remap, MapStrings srcStrings, ref int inlinedStrings)
    {
        int NewId = g.NewId != 0 && remap.TryGetValue(g.NewId, out var n) ? n : g.NewId;
        int OldId = g.OldId != 0 && remap.TryGetValue(g.OldId, out var o) ? o : g.OldId;
        var ng = new PortGroup { OldId = OldId, NewId = NewId };
        foreach (var m in g.Mods)
        {
            object? value = m.Value;
            if (m.Type == ObjectDataType.String && m.Value is string s)
            {
                if (s.Contains("TRIGSTR_", StringComparison.Ordinal))
                {
                    var resolved = srcStrings.Resolve(s);
                    if (!ReferenceEquals(resolved, s) && resolved != s) inlinedStrings++;
                    value = resolved;
                }
                else
                {
                    value = RewriteRawcodeList(s, remap);
                }
            }
            else if (m.Type == ObjectDataType.Int && m.Value is int iv && remap.TryGetValue(iv, out var ri) && ri != iv)
            {
                value = ri;
            }
            ng.Mods.Add(m with { Value = value });
        }
        return ng;
    }

    private static string RewriteRawcodeList(string s, IReadOnlyDictionary<int, int> remap)
    {
        if (!s.Contains(',') && s.Length != 4) return s;
        var parts = s.Split(',');
        bool changed = false;
        for (int i = 0; i < parts.Length; i++)
        {
            var tok = parts[i].Trim();
            if (tok.Length == 4 && remap.TryGetValue(tok.FromRawcode(), out var to) && to != tok.FromRawcode())
            { parts[i] = to.ToRawcode(); changed = true; }
        }
        return changed ? string.Join(",", parts) : s;
    }

    private static void InjectGroup(MapDocument target, ObjectKind kind, PortGroup g)
    {
        var info = ObjectKinds.Info(kind);
        var version = SourceVersion(target, info.MapFile);

        switch (kind)
        {
            case ObjectKind.Unit:
            case ObjectKind.Item:
            case ObjectKind.Destructable:
            case ObjectKind.Buff:
            {
                var mod = new SimpleObjectModification { OldId = g.OldId, NewId = g.NewId };
                foreach (var m in g.Mods)
                    mod.Modifications.Add(new SimpleObjectDataModification { Id = m.Id, Type = m.Type, Value = m.Value! });
                AddSimple(target, kind, info.MapFile, version, mod, g.NewId != 0);
                break;
            }
            case ObjectKind.Ability:
            case ObjectKind.Upgrade:
            {
                var mod = new LevelObjectModification { OldId = g.OldId, NewId = g.NewId };
                foreach (var m in g.Mods)
                    mod.Modifications.Add(new LevelObjectDataModification { Level = m.Level, Pointer = 0, Id = m.Id, Type = m.Type, Value = m.Value! });
                AddLevel(target, kind, info.MapFile, version, mod, g.NewId != 0);
                break;
            }
            case ObjectKind.Doodad:
            {
                var mod = new VariationObjectModification { OldId = g.OldId, NewId = g.NewId };
                foreach (var m in g.Mods)
                    mod.Modifications.Add(new VariationObjectDataModification { Variation = m.Level, Pointer = 0, Id = m.Id, Type = m.Type, Value = m.Value! });
                var model = (DoodadObjectData?)target.GetFile(info.MapFile)?.Model ?? new DoodadObjectData(version);
                (g.NewId != 0 ? model.NewDoodads : model.BaseDoodads).Add(mod);
                target.AddOrReplaceModelFile(info.MapFile, model);
                break;
            }
        }
    }

    private static void AddSimple(MapDocument target, ObjectKind kind, string file, ObjectDataFormatVersion v,
        SimpleObjectModification mod, bool isNew)
    {
        switch (kind)
        {
            case ObjectKind.Unit:
            {
                var m = (UnitObjectData?)target.GetFile(file)?.Model ?? new UnitObjectData(v);
                (isNew ? m.NewUnits : m.BaseUnits).Add(mod); target.AddOrReplaceModelFile(file, m); break;
            }
            case ObjectKind.Item:
            {
                var m = (ItemObjectData?)target.GetFile(file)?.Model ?? new ItemObjectData(v);
                (isNew ? m.NewItems : m.BaseItems).Add(mod); target.AddOrReplaceModelFile(file, m); break;
            }
            case ObjectKind.Destructable:
            {
                var m = (DestructableObjectData?)target.GetFile(file)?.Model ?? new DestructableObjectData(v);
                (isNew ? m.NewDestructables : m.BaseDestructables).Add(mod); target.AddOrReplaceModelFile(file, m); break;
            }
            case ObjectKind.Buff:
            {
                var m = (BuffObjectData?)target.GetFile(file)?.Model ?? new BuffObjectData(v);
                (isNew ? m.NewBuffs : m.BaseBuffs).Add(mod); target.AddOrReplaceModelFile(file, m); break;
            }
        }
    }

    private static void AddLevel(MapDocument target, ObjectKind kind, string file, ObjectDataFormatVersion v,
        LevelObjectModification mod, bool isNew)
    {
        if (kind == ObjectKind.Ability)
        {
            var m = (AbilityObjectData?)target.GetFile(file)?.Model ?? new AbilityObjectData(v);
            (isNew ? m.NewAbilities : m.BaseAbilities).Add(mod); target.AddOrReplaceModelFile(file, m);
        }
        else
        {
            var m = (UpgradeObjectData?)target.GetFile(file)?.Model ?? new UpgradeObjectData(v);
            (isNew ? m.NewUpgrades : m.BaseUpgrades).Add(mod); target.AddOrReplaceModelFile(file, m);
        }
    }

    private static ObjectDataFormatVersion SourceVersion(MapDocument target, string file) => target.GetFile(file)?.Model switch
    {
        UnitObjectData m => m.FormatVersion,
        ItemObjectData m => m.FormatVersion,
        AbilityObjectData m => m.FormatVersion,
        DestructableObjectData m => m.FormatVersion,
        DoodadObjectData m => m.FormatVersion,
        BuffObjectData m => m.FormatVersion,
        UpgradeObjectData m => m.FormatVersion,
        _ => ObjectDataFormatVersion.v2,
    };

    // ---- file helpers ------------------------------------------------------

    private static MapFileEntry? FindFile(MapDocument doc, string path)
    {
        foreach (var p in new[] { path, path.Replace('/', '\\'), path.Replace('\\', '/') }.Distinct())
            if (doc.GetFile(p) is { } e) return e;
        return null;
    }

    private static bool PathEq(string a, string b) =>
        string.Equals(a.Replace('/', '\\'), b.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Key form for the preview batch's simulated-copy lookup (slash-normalized;
    /// the dictionary itself compares case-insensitively).</summary>
    private static string NormalizePath(string path) => path.Replace('/', '\\');
}
