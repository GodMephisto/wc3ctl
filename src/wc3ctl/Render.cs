// src/wc3ctl/Render.cs
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3Ctl;

public static class Render
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }, // ObjectKind as "Unit", not 0
    };
    public static string AsJson(object o) => JsonSerializer.Serialize(o, Json);

    public static string Validate(ValidateResult r)
    {
        var sb = new StringBuilder();
        foreach (var i in r.Issues)
            sb.AppendLine($"{i.Severity}: [{i.Category}] {i.FileName} — {i.Message}");
        if (r.Issues.Count > 0) sb.AppendLine();
        sb.Append(r.Valid
            ? $"OK — valid ({r.Errors} error(s), {r.Warnings} warning(s))"
            : $"INVALID — {r.Errors} error(s), {r.Warnings} warning(s)");
        return sb.ToString();
    }

    public static string DataPointerRepair(DataPointerRepairResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ObjectsScanned} levelled object(s), {r.FieldsScanned} field(s), "
            + $"{r.FieldsFixed} field(s) carrying {r.LevelsFixed} mismatched level(s)");
        if (r.Fixes.Count > 0)
        {
            sb.AppendLine();
            foreach (var f in r.Fixes.Take(40))
                sb.AppendLine($"   {f.Rawcode}  {f.Field,-6} level(s) "
                    + $"[{string.Join(",", f.Levels)}] carry pointer 0, the field uses {f.Pointer}");
            if (r.Fixes.Count > 40) sb.AppendLine($"   ... and {r.Fixes.Count - 40} more");
        }
        foreach (var d in r.Diagnostics) sb.AppendLine($"note: {d}");
        return sb.ToString();
    }

    public static string ModelPaths(ModelPathScan s)
    {
        var sb = new StringBuilder();
        int fixable = s.Missing.Count(m => m.Placeholder || m.Suggestion is not null);
        sb.AppendLine($"{s.Missing.Count} model path(s) resolve to no file, {fixable} fixable "
            + $"({s.Missing.Count(m => m.Placeholder)} placeholder(s), "
            + $"{s.Missing.Count(m => m.Suggestion is not null)} with one obvious target)");
        if (s.Missing.Count > 0) sb.AppendLine();
        foreach (var m in s.Missing.Take(80))
        {
            string where = m.Source == "script" ? "script" : $"{m.Source} {m.Rawcode}.{m.Field}";
            string fix = m.Placeholder ? "placeholder, would point at the empty model"
                : m.Suggestion is { } t ? $"would become '{t}'" : "no single match, left for the author";
            sb.AppendLine($"  {m.Uses,4}  '{m.Path}'  [{where}]  {fix}");
        }
        if (s.Missing.Count > 80) sb.AppendLine($"  ... and {s.Missing.Count - 80} more");
        foreach (var d in s.Diagnostics) sb.AppendLine($"note: {d}");
        return sb.ToString();
    }

    public static string ModelPathRepair(ModelPathRepairResult r)
    {
        var sb = new StringBuilder();
        foreach (var c in r.Changes) sb.AppendLine($"  fixed  {c}");
        if (r.LeftAlone.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{r.LeftAlone.Count} path(s) have no single intended file and were left alone.");
            foreach (var m in r.LeftAlone.Take(40))
                sb.AppendLine($"  {m.Uses,4}  '{m.Path}'  [{(m.Source == "script" ? "script" : $"{m.Source} {m.Rawcode}.{m.Field}")}]");
        }
        return sb.ToString();
    }

    public static string AuditRepair(AuditRepairResult r)
    {
        var sb = new StringBuilder();
        foreach (var e in r.Edits.Take(60))
        {
            string before = e.Before.Length > 40 ? e.Before[..37] + "..." : e.Before;
            string after = e.After.Length > 40 ? e.After[..37] + "..." : e.After;
            sb.AppendLine($"  {e.Kind} {e.Rawcode} {e.Field}  '{before}' to '{after}'  ({e.Why})");
        }
        if (r.Edits.Count > 60) sb.AppendLine($"  and {r.Edits.Count - 60} more, --json lists every one");
        foreach (var d in r.Diagnostics) sb.AppendLine($"  note  {d}");
        sb.AppendLine(r.Message);
        return sb.ToString();
    }

    public static string Preload(PreloadResult r)
    {
        var sb = new StringBuilder();
        foreach (var e in r.Entries)
            sb.AppendLine($"  entry  {e.Trigger} at {e.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} s runs {e.Function}");
        if (r.UnitTypes.Count > 0) sb.AppendLine($"  units      {string.Join(" ", r.UnitTypes)}");
        if (r.Abilities.Count > 0)
            sb.AppendLine($"  abilities  {string.Join(" ", r.Abilities.Take(60))}"
                + (r.Abilities.Count > 60 ? $" and {r.Abilities.Count - 60} more" : ""));
        foreach (var d in r.Diagnostics) sb.AppendLine($"  note  {d}");
        sb.AppendLine(r.Message);
        return sb.ToString();
    }

    public static string ScriptLeaks(ScriptLeaksResult r, bool all)
    {
        var sb = new StringBuilder();
        var shown = all ? r.Leaks : r.Leaks.Where(l => l.Heat != "once").ToList();
        foreach (var g in shown.GroupBy(l => l.Heat))
        {
            sb.AppendLine($"{g.Key} ({g.Count()})");
            foreach (var l in g.Take(all ? int.MaxValue : 80))
            {
                string code = l.Code.Length > 110 ? l.Code[..107] + "..." : l.Code;
                sb.AppendLine($"  {l.Line,7}  {l.Rule,-15} {l.Handle,-9} {l.Function}");
                sb.AppendLine($"           {code}");
            }
            if (!all && g.Count() > 80) sb.AppendLine($"  and {g.Count() - 80} more, --all or --json lists every one");
        }
        var fast = r.Periodic.Where(p => p.Period is < 0.1).ToList();
        sb.AppendLine($"periodic, {r.Periodic.Count} total, {fast.Count} faster than 0.1 s");
        foreach (var p in r.Periodic.Take(25))
            sb.AppendLine($"  {(p.Period is { } s ? s.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " s" : "variable"),-9} line {p.Line,7}  {p.Function}  (reaches {p.Reached} function(s))");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note  {d}");
        sb.AppendLine(r.Message);
        return sb.ToString();
    }

    public static string Replays(ReplayReport r)
    {
        var sb = new StringBuilder();
        foreach (var g in r.Games)
        {
            string map = g.Map is null ? "(map unknown)" : Path.GetFileName(g.Map.Replace('\\', '/'));
            string verdict = g.Problem is not null && g.Leaves.Count == 0 ? "UNREADABLE"
                : g.Disconnected ? "DISCONNECT" : "clean";
            sb.AppendLine($"{g.Saved:yyyy-MM-dd HH:mm}  {verdict,-10}  {g.Length:mm\\:ss}  {map}");
            foreach (var l in g.Leaves)
                sb.AppendLine($"      {l.At:mm\\:ss}  player {l.PlayerId} {l.Name}  {l.Meaning}"
                    + $"  (reason 0x{l.Reason:X2}, result 0x{l.Result:X2})");
            if (g.Problem is not null) sb.AppendLine($"      note: {g.Problem}");
        }
        if (r.Games.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("per map                                                   games  disconnects");
            foreach (var m in r.Games.GroupBy(g => g.Map is null ? "(map unknown)"
                         : Path.GetFileName(g.Map.Replace('\\', '/'))).OrderBy(m => m.Key))
                sb.AppendLine($"  {m.Key,-56} {m.Count(),5}  {m.Count(g => g.Disconnected),11}");
        }
        foreach (var d in r.Diagnostics) sb.AppendLine($"note: {d}");
        return sb.ToString();
    }

    public static string UabiProfiles(UabiProfileReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"map",-58} {"units",6} {"w/uabi",6} {"refs",6} {"dist",5} {"abil",5} "
            + $"{"heroR",5} {"heroD",5} {"heroNH",6} {"item",5} {"dangl",5} {"dupe",4} {"uhab",5}  races (units/refs/distinct)");
        foreach (var m in r.Maps)
        {
            if (m.Problem is not null) { sb.AppendLine($"{m.Map,-58} {m.Problem}"); continue; }
            string races = string.Join("  ", m.Races.Take(5).Select(x => $"{x.Race} {x.UnitTypes}/{x.References}/{x.Distinct}"));
            sb.AppendLine($"{Trim(m.Map, 58),-58} {m.UnitTypes,6} {m.WithUabi,6} {m.References,6} {m.Distinct,5} "
                + $"{m.AbilityObjects,5} {m.HeroAbilityRefs,5} {m.HeroAbilityDistinct,5} {m.HeroAbilityOnNonHeroUnits,6} "
                + $"{m.ItemAbilityRefs,5} {m.DanglingRefs,5} {m.DuplicateWithinList,4} {m.AlsoInSomeUhab,5}  {races}");
        }
        sb.AppendLine();
        sb.AppendLine("heroR/heroD  uabi references and distinct ids that are HERO abilities (aher 1)");
        sb.AppendLine("heroNH       of those, references on units that are not heroes");
        sb.AppendLine("item         uabi references that are ITEM abilities (aite 1)");
        sb.AppendLine("dangl        uabi ids neither the map nor the game defines");
        sb.AppendLine("dupe         units listing the same id twice   uhab  uabi refs also learnable as a hero ability");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note: {d}");
        return sb.ToString();

        static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 2)] + "..";
    }

    public static string UabiRuntime(UabiRuntimeResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Message);
        foreach (var k in r.Kept.Take(30)) sb.AppendLine($"  kept  {k.Rawcode}  {k.Reason}");
        if (r.Kept.Count > 30) sb.AppendLine($"  ... and {r.Kept.Count - 30} more kept");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note: {d}");
        return sb.ToString();
    }

    public static string PortraitRepair(PortraitRepairResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.UnitsScanned} units, {r.ModelsResolved} distinct models in the archive, "
            + $"{r.ModelsAtRisk} at risk, {r.ModelsRepaired} repairable");
        sb.AppendLine();
        foreach (var g in r.Models.GroupBy(m => m.Action).OrderByDescending(g => g.Count()))
            sb.AppendLine($"  {g.Count(),5}  {g.Key}");
        var risky = r.Models.Where(m => m.Action.StartsWith("would") || m.Action.StartsWith("camera"))
            .ToList();
        if (risky.Count > 0)
        {
            sb.AppendLine();
            foreach (var m in risky.Take(30))
                sb.AppendLine($"   {m.Name,-44} VERS {m.Version}  "
                    + $"{m.BytesBefore:N0} -> {m.BytesAfter:N0} B");
            if (risky.Count > 30) sb.AppendLine($"   ... and {risky.Count - 30} more");
        }
        foreach (var d in r.Diagnostics) sb.AppendLine($"note: {d}");
        return sb.ToString();
    }

    public static string NameRecovery(NameRecoveryResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.TotalBlocks} archive entries, {r.NamedBefore} named before, "
            + $"{r.NamedAfter} after ({100.0 * r.NamedAfter / Math.Max(1, r.TotalBlocks):F1}%)");
        sb.AppendLine();
        foreach (var g in r.Recovered.GroupBy(x => x.Source).OrderByDescending(g => g.Count()))
            sb.AppendLine($"  {g.Count(),5}  {g.Key}");
        if (r.StillUnnamed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"still unnamed: {r.StillUnnamed.Count} entries, "
                + $"{r.StillUnnamed.Sum(u => (long)u.SizeBytes):N0} bytes. Their CONTENT reads "
                + "fine, only the name is missing, and nothing in the map names them.");
            foreach (var u in r.StillUnnamed.Take(20))
                sb.AppendLine($"   block {u.BlockIndex,4}  {u.SizeBytes,9:N0} B  {u.Kind}"
                    + (u.SelfName.Length > 0 ? $"  calls itself '{u.SelfName}'" : ""));
        }
        foreach (var d in r.Diagnostics) sb.AppendLine($"note: {d}");
        return sb.ToString();
    }

    public static string Audit(AuditResult r)
    {
        var sb = new StringBuilder();
        foreach (var g in r.Issues.GroupBy(i => i.Check).OrderBy(g => g.Key))
        {
            sb.AppendLine($"[{g.Key}]  {g.Count()}");
            foreach (var i in g.OrderBy(i => i.Rawcode))
                sb.AppendLine($"   {i.Severity,-7} {i.Rawcode,-6} {i.Owner ?? "",-34} {i.Message}");
            sb.AppendLine();
        }
        foreach (var d in r.Diagnostics)
            sb.AppendLine($"note: {d}");
        int errors = r.Issues.Count(i => i.Severity == DiagnosticSeverity.Error);
        sb.Append(r.Ok
            ? $"OK, {r.ObjectsChecked} abilities checked, {errors} error(s), "
              + $"{r.Issues.Count - errors} warning(s)"
            : $"FAILED, {r.ObjectsChecked} abilities checked, {errors} error(s), "
              + $"{r.Issues.Count - errors} warning(s)");
        return sb.ToString();
    }

    public static string List(FileListResult r) =>
        string.Join("\n", r.Files.Select(f =>
            $"{f.Name ?? "(unnamed)",-28} {f.SizeBytes,10}  {(f.Known ? "known" : "unknown")}{(f.Parsed ? "/parsed" : "")}"));

    public static string Info(MapInfoResult r) =>
        $"Name:    {r.Name}\nAuthor:  {r.Author}\nPlayers: {r.Players}\nSize:    {r.Width}x{r.Height}"
        + (r.Diagnostics.Count == 0 ? "" : "\n\nDiagnostics:\n" + string.Join("\n", r.Diagnostics));

    public static string Roundtrip(RoundtripResult r)
    {
        var verdict = r.Faithful ? "OK - round-trip is byte-faithful for all files."
                                 : "MISMATCH:\n" + string.Join("\n", r.Mismatches);
        return r.ExcludedNotes.Count == 0
            ? verdict
            : verdict + "\n" + string.Join("\n", r.ExcludedNotes.Select(n => $"Note: {n}."));
    }

    public static string Diff(DiffResult r) =>
        r.Entries.Count == 0 ? "(identical)" : string.Join("\n", r.Entries.Select(e => $"{e.Change,-9} {e.Name}"));

    public static string Search(SearchResult r) =>
        r.Hits.Count == 0 ? "(no hits)" : string.Join("\n", r.Hits.Select(h => $"{h.FileName}  [{h.Context}]"));

    public static string ObjectGet(MergedObjectResult r)
    {
        if (!r.Found) return $"{r.Rawcode}: not found";
        var head = Headline(r.Rawcode, r.Name, r.BaseRawcode);
        var lines = r.Fields.Select(f => $"{f.Name} ({f.Code}) = {f.Value}  [{f.Source}]");
        var notes = r.Diagnostics.Select(d => $"note: {d}");
        return string.Join("\n", new[] { head }.Concat(lines).Concat(notes));
    }

    public static string ObjectList(ObjectListResult r) =>
        r.Items.Count == 0 ? "(no custom objects)"
        : string.Join("\n", r.Items.Select(i => Headline(i.Rawcode, i.Name, i.BaseRawcode)));

    public static string DoodadPalette(DoodadPaletteResult r)
    {
        // A base doodad the map edits in place carries BaseRawcode == its own code; suppress
        // the "(base: self)" noise there — the [source] tag already says "map-modified".
        var lines = r.Entries.Select(e =>
            Headline(e.Rawcode, e.Name, e.BaseRawcode == e.Rawcode ? null : e.BaseRawcode) + $"  [{e.Source}]");
        var notes = r.Diagnostics.Select(d => $"note: {d}");
        return string.Join("\n", new[] { r.Message }.Concat(lines).Concat(notes));
    }

    // e.g. H000  "Paladin"  (base: Hpal)
    private static string Headline(string rawcode, string? name, string? baseRawcode) =>
        string.Join("  ", new[]
        {
            rawcode,
            name is null ? null : $"\"{name}\"",
            baseRawcode is null ? null : $"(base: {baseRawcode})",
        }.Where(part => part is not null));

    /// <summary>
    /// Indented dependency tree (root → deps, each line "code \"name\" [custom kind]
    /// (via field)"; repeats collapse), then the file and string lists.
    /// </summary>
    public static string BundleUnit(UnitBundle r)
    {
        var sb = new StringBuilder();
        var byCode = r.Objects.ToDictionary(o => o.Rawcode, StringComparer.Ordinal);
        var children = r.Edges
            .Where(e => byCode.ContainsKey(e.To))
            .GroupBy(e => e.From, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        if (!byCode.ContainsKey(r.RootRawcode))
        {
            sb.AppendLine($"{r.RootRawcode}: not found");
        }
        else
        {
            var printed = new HashSet<string>(StringComparer.Ordinal);
            void Print(string code, string? via, int depth)
            {
                var n = byCode[code];
                sb.Append(new string(' ', depth * 2))
                  .Append(n.Rawcode)
                  .Append(n.Name is null ? "" : $"  \"{n.Name}\"")
                  .Append($"  [{(n.CustomToMap ? "custom" : "base")} {n.Kind.ToString().ToLowerInvariant()}]")
                  .Append(via is null ? "" : $"  (via {via})");
                if (!printed.Add(code)) { sb.AppendLine("  (see above)"); return; }
                sb.AppendLine();
                if (children.TryGetValue(code, out var kids))
                    foreach (var e in kids) Print(e.To, e.Via, depth + 1);
            }
            Print(r.RootRawcode, via: null, depth: 0);
        }

        sb.AppendLine().AppendLine($"Files ({r.Files.Count}):");
        foreach (var f in r.Files)
            sb.AppendLine($"  {(f.PresentInMap ? "[in map] " : "[missing]")} {f.Path}  ({f.Category})");

        sb.AppendLine().AppendLine($"Strings ({r.Strings.Count}):");
        foreach (var s in r.Strings)
            sb.AppendLine($"  \"{s}\"");

        sb.AppendLine().AppendLine($"Functions ({r.Functions.Count}):");
        foreach (var f in r.Functions)
            sb.AppendLine($"  line {f.StartLine}-{f.EndLine}  {f.Name}  ({f.Reason})");

        foreach (var d in r.Diagnostics)
            sb.AppendLine($"note: {d}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>One port report; a null <paramref name="outPath"/> marks a dry run.</summary>
    public static string Port(PortResult r, string? outPath)
    {
        var sb = new StringBuilder();
        AppendPortBody(sb, r);
        AppendPortFooter(sb, outPath);
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>Combined batch report; a null <paramref name="outPath"/> marks a dry run.</summary>
    public static string PortBatch(BatchPortResult r, string? outPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ported {r.Units.Count} unit(s) into one target: "
            + string.Join(", ", r.Units.Select(u =>
                u.RootPortedTo == u.RootRawcode ? u.RootRawcode : $"{u.RootRawcode} → {u.RootPortedTo}")));

        foreach (var u in r.Units)
        {
            sb.AppendLine().AppendLine("----------------------------------------");
            AppendPortBody(sb, u);
        }

        if (r.Script is { } s)
        {
            sb.AppendLine().AppendLine(
                $"Script (best-effort, merged across all units): {s.Functions} function(s), "
                + $"{s.Globals} global(s) carried, {s.Renamed} renamed, init {(s.InitHooked ? "wired" : "NOT wired")}.");
            foreach (var n in s.Notes) sb.AppendLine($"  - {n}");
        }

        if (r.Warnings.Count > 0)
        {
            sb.AppendLine().AppendLine("Warnings:");
            foreach (var w in r.Warnings) sb.AppendLine($"  ! {w}");
        }

        AppendPortFooter(sb, outPath);
        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static void AppendPortFooter(StringBuilder sb, string? outPath) =>
        sb.AppendLine().AppendLine(outPath is null ? "DRY RUN - nothing written" : $"Saved: {outPath}");

    private static void AppendPortBody(StringBuilder sb, PortResult r)
    {
        string root = r.RootPortedTo == r.RootRawcode ? r.RootRawcode : $"{r.RootRawcode} → {r.RootPortedTo}";
        sb.AppendLine($"Ported {root}{(r.RootName is null ? "" : $"  \"{r.RootName}\"")}");
        sb.AppendLine($"  {r.Objects.Count} object(s), {r.CopiedFiles.Count} file(s) copied, "
                      + $"{r.InlinedStrings} string(s) inlined, {r.Remaps.Count} rawcode(s) remapped.");

        if (r.Remaps.Count > 0)
        {
            sb.AppendLine().AppendLine("Rawcode remaps (collisions with the target):");
            foreach (var m in r.Remaps)
                sb.AppendLine($"  {m.Kind.ToString().ToLowerInvariant()} {m.From} → {m.To}");
        }

        sb.AppendLine().AppendLine($"Objects ({r.Objects.Count}):");
        foreach (var o in r.Objects)
            sb.AppendLine($"  {o.Kind.ToString().ToLowerInvariant()} {o.Rawcode}"
                          + $"{(o.Name is null ? "" : $"  \"{o.Name}\"")}"
                          + $"{(o.ModifiesStandard ? "  (modifies standard object)" : "")}");

        if (r.CopiedFiles.Count > 0)
        {
            sb.AppendLine().AppendLine($"Copied files ({r.CopiedFiles.Count}):");
            foreach (var f in r.CopiedFiles) sb.AppendLine($"  {f}");
        }

        if (r.Script is { } s)
        {
            sb.AppendLine().AppendLine(
                $"Script (best-effort): {s.Functions} function(s), {s.Globals} global(s) carried, "
                + $"{s.Renamed} renamed, init {(s.InitHooked ? "wired" : "NOT wired")}.");
            foreach (var n in s.Notes) sb.AppendLine($"  - {n}");
        }

        if (r.Warnings.Count > 0)
        {
            sb.AppendLine().AppendLine("Warnings:");
            foreach (var w in r.Warnings) sb.AppendLine($"  ! {w}");
        }

        foreach (var d in r.Diagnostics) sb.AppendLine($"note: {d}");
    }

    public static string ScriptFunctions(ScriptFunctionsResult r) =>
        $"{r.Functions.Count} functions in {r.ScriptFile}"
        + string.Concat(r.Functions.Select(f => $"\nline {f.StartLine}-{f.EndLine}  {f.Name}"));

    public static string GeneratedHeroRepair(GeneratedHeroRepairResult r)
    {
        if (!r.Ok)
            return r.Message;

        var sb = new StringBuilder();
        sb.AppendLine(r.Message);
        sb.AppendLine($"player slots: {r.PlayerSlotsBefore} -> {r.PlayerSlotsAfter}");
        foreach (var hero in r.Heroes)
            sb.AppendLine($"  {hero.Rawcode}: owner {hero.OriginalOwnerId} -> {hero.OwnerId}  start=({hero.X}, {hero.Y})");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string Reforged3Repair(Reforged3RepairResult r, string? savedTo)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Message);
        sb.AppendLine($"  obsolete file columns : {r.FileColumnsRemoved}");
        sb.AppendLine($"  model paths migrated  : {r.ModelsMoved}");
        sb.AppendLine($"  numeric cells cleaned : {r.NumericCellsNormalized}");
        sb.AppendLine($"  button positions fixed: {r.ButtonPositionsCompleted}");
        sb.AppendLine($"  FDF terminators removed: {r.StrayCommentTerminatorsRemoved}");
        sb.AppendLine($"  ability columns added : {r.AbilityLevelColumnsAdded}");
        if (r.ChangedFiles.Count > 0)
        {
            sb.AppendLine("files:");
            foreach (string file in r.ChangedFiles)
                sb.AppendLine($"  {file}");
        }
        sb.AppendLine(r.Applied ? $"saved: {savedTo}" : "DRY RUN - use --apply to write a repaired copy");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string Extract(ExtractManifest m, string dest) =>
        $"Extracted {m.Count} file(s) ({m.TotalBytes:N0} bytes) to {dest}";

    public static string GameDataSnapshot(SnapshotResult r)
    {
        var sb = new StringBuilder();
        if (r.FilesWritten == 0)
        {
            sb.AppendLine("nothing written");
            foreach (var d in r.Diagnostics) sb.AppendLine($"  {d}");
            return sb.ToString().TrimEnd('\r', '\n');
        }

        sb.AppendLine($"build {r.Build ?? "unknown"}");
        sb.AppendLine($"  {r.FilesWritten:N0} file(s), {r.BytesWritten:N0} bytes -> {r.OutDir}");
        if (r.FilesSkipped > 0) sb.AppendLine($"  {r.FilesSkipped} skipped");
        foreach (var d in r.Diagnostics) sb.AppendLine($"  {d}");
        sb.AppendLine();
        sb.AppendLine("commit this directory now, then re-run after the next patch and diff it.");
        return sb.ToString().TrimEnd('\r', '\n');
    }
}
