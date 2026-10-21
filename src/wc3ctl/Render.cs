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
        // ObjectKind as "Unit", not 0, and a placed object's scale as sx, sy, sz rather than {}.
        Converters = { new JsonStringEnumConverter(), new ScaleJsonConverter() },
    };
    public static string AsJson(object o) => JsonSerializer.Serialize(o, Json);

    public static string Validate(ValidateResult r)
    {
        var sb = new StringBuilder();
        foreach (var i in r.Issues)
            sb.AppendLine($"{i.Severity}: [{i.Category}] {i.FileName}, {i.Message}");
        if (r.Issues.Count > 0) sb.AppendLine();
        sb.Append(r.Valid
            ? $"OK, valid ({r.Errors} error(s), {r.Warnings} warning(s))"
            : $"INVALID, {r.Errors} error(s), {r.Warnings} warning(s)");
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

    public static string ScriptVerify(ScriptVerifyResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Message);
        foreach (var g in r.Findings.GroupBy(f => (f.Severity, f.Rule))
                     .OrderBy(g => g.Key.Severity == "high" ? 0 : 1).ThenByDescending(g => g.Count()))
        {
            sb.AppendLine($"[{g.Key.Severity}] {g.Key.Rule} ({g.Count()})");
            foreach (var f in g.Take(20))
                sb.AppendLine($"    {f.Function}: {f.Detail}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string ScriptBench(ScriptBenchResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Message);
        var top = r.Rows.Where(x => x.PerSecond > 0).Take(40).ToList();
        foreach (var row in top)
            sb.AppendLine($"{row.PerSecond,9}  iv={row.Interval,-6} create={row.CreateUnit,-3} sfx={row.SpecialEffect,-3} enum={row.GroupEnum,-3} cond={row.ConditionAndFilter,-3} dmg={row.Damage,-3} {row.Leaks,-18} {row.Function}");
        return sb.ToString().TrimEnd();
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

    private static string Num(double? v) =>
        v is { } x ? x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "n/a";

    public static string LeakList(LeakListResult r)
    {
        var sb = new StringBuilder();
        foreach (var g in r.Findings.GroupBy(l => l.Heat))
        {
            sb.AppendLine($"{g.Key} ({g.Count()})");
            foreach (var l in g)
            {
                string code = l.Code.Length > 110 ? l.Code[..107] + "..." : l.Code;
                sb.AppendLine($"  {l.Line,7}  {l.Rule,-15} {l.Handle,-9} {l.Function}");
                sb.AppendLine($"           {code}");
            }
        }
        sb.AppendLine($"periodic, {r.Periodic.Count} driver(s)");
        foreach (var p in r.Periodic)
        {
            string period = p.Period is { } s
                ? s.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " s"
                : "variable";
            sb.AppendLine($"  {period,-9} line {p.Line,7}  {p.Function}  "
                + $"({p.HotLeaks} hot leak(s), reaches {p.Reached})");
        }
        sb.AppendLine($"targets (hot), {r.Targets.Count}");
        foreach (var t in r.Targets)
            sb.AppendLine($"  {t.Function}  {t.Handle}  x{t.Count}  line {t.Line}");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note  {d}");
        sb.AppendLine(r.Message);
        return sb.ToString();
    }

    public static string LeakHarness(LeakHarnessResult r) =>
        r.Ok ? r.Message + "\n" + r.Source : r.Message;

    public static string LeakJoin(LeakJoinResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Message);
        if (!r.Ok) return sb.ToString();
        sb.AppendLine($"span {r.Span}, {r.SampleCount} sample(s), {r.EventsJoined} event(s)");
        if (r.GcProbe is { } gc)
            sb.AppendLine($"gc_probe  {gc:HH:mm:ss}, slopes measured from it");
        sb.AppendLine($"handle slope  {Num(r.HandleSlopePerMin)} per minute (r {Num(r.HandleCorrelation)})");
        sb.AppendLine($"memory slope  {Num(r.MemorySlopePerMin)} MB per minute (r {Num(r.MemoryCorrelation)})");
        sb.AppendLine("drivers by predicted handle rate");
        foreach (var d in r.Drivers)
        {
            string period = d.Period is { } p
                ? p.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " s"
                : "variable";
            sb.AppendLine($"  {d.Rank,2}. {d.Function,-24} {period,-9} {d.HotLeaks} hot leak(s)  "
                + $"about {d.PredictedPerMin:0.#} a minute");
        }
        sb.AppendLine("rising windows, likely driver and nearby evidence");
        if (r.Windows.Count == 0) sb.AppendLine("  none");
        foreach (var w in r.Windows)
        {
            sb.AppendLine($"  {w.Start} to {w.End}  {w.Samples} sample(s)  {w.HandlesPerMin:0.#} handles a minute"
                + (w.Driver is null ? "" : $"  ({w.Driver})"));
            sb.AppendLine($"      near {w.Near}");
        }
        foreach (var d in r.Diagnostics) sb.AppendLine($"note  {d}");
        return sb.ToString();
    }

    public static string LeakAb(LeakAbResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Message);
        foreach (var s in new[] { r.A, r.B })
        {
            if (s.Error is not null) { sb.AppendLine($"  {s.Session}, {s.Error}"); continue; }
            sb.AppendLine($"  {s.Session}");
            sb.AppendLine($"    {s.Samples} sample(s), {s.Span}");
            sb.AppendLine($"    handles {Num(s.HandlesPerMin)} per minute (r {Num(s.HandleCorrelation)}), "
                + $"memory {Num(s.MemoryPerMin)} MB per minute (r {Num(s.MemoryCorrelation)})");
            if (s.Baseline is { } b)
                sb.AppendLine($"    measured from gc_probe at {b:HH:mm:ss}");
        }
        if (r.LikelyDisabled is not null)
            sb.AppendLine($"likely disabled candidate, {r.LikelyDisabled} (inferred from the static list)");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note  {d}");
        return sb.ToString();
    }

    public static string LeakBenchmark(LeakBenchmarkResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Message);
        sb.AppendLine("  kind        creator             expected  detected  false+  ok");
        foreach (var c in r.Cases)
            sb.AppendLine($"  {c.Kind,-10}  {c.Creator,-18} {c.Expected,8}  {c.Detected,8}  "
                + $"{c.FalsePositives,6}  {(c.TruePositive ? "yes" : "no")}");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note  {d}");
        return sb.ToString();
    }

    public static string LeakModel(LeakModelResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ModelsScanned} model(s), {r.TexturesScanned} texture(s), "
            + $"{r.FindingCount} finding(s)");
        sb.AppendLine($"  leaked imports {r.LeakedImports}, missing textures {r.MissingTextures}, "
            + $"duplicate imports {r.DuplicateImports}, unused textures {r.UnusedTextures}");
        foreach (var g in r.Findings.GroupBy(f => f.Kind))
        {
            sb.AppendLine($"{g.Key} ({g.Count()})");
            foreach (var f in g)
            {
                sb.AppendLine($"  {f.Path}");
                sb.AppendLine($"      {f.Reason}");
            }
        }
        foreach (var d in r.Diagnostics) sb.AppendLine($"note  {d}");
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

    public static string ReplayPlayers(ReplayPlayersReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"slot",4}  {"name",-22}  {"team",4}  {"race",10}  {"result",-12}  "
            + $"{"left",8}  reason");
        foreach (var p in r.Players)
            sb.AppendLine($"{p.Slot,4}  {Short(p.Name,22),-22}  {Num(p.Team),4}  {Short(p.Race,10),-10}  "
                + $"{Short(p.LeaveMeaning,12),-12}  {Clock(p.LeftAt),8}  {Num(p.LeaveReason)}");
        if (r.Problem is not null) sb.AppendLine("note " + r.Problem);
        return sb.ToString();
    }

    public static string ReplayTimeline(ReplayTimelineReport r)
    {
        var sb = new StringBuilder();
        foreach (var e in r.Events)
        {
            string who = e.PlayerId is { } id ? $"{id} {e.Name}" : string.Empty;
            sb.AppendLine($"{e.At:mm\\:ss}  {e.Kind,-10}  {who,-24}  {e.Text}");
        }
        if (r.Problem is not null) sb.AppendLine("note " + r.Problem);
        return sb.ToString();
    }

    public static string ReplayDiff(ReplayDiffReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"A  {Path.GetFileName(r.FileA)}  {Leaf(r.MapA)}");
        sb.AppendLine($"B  {Path.GetFileName(r.FileB)}  {Leaf(r.MapB)}");
        sb.AppendLine($"same map {(r.SameMap ? "yes" : "no")}   length A {r.LengthA:mm\\:ss}  "
            + $"B {r.LengthB:mm\\:ss}  delta {r.LengthDelta:mm\\:ss}");
        sb.AppendLine();
        sb.AppendLine($"{"slot",4}  {"name",-22}  {"A left",8}  {"B left",8}  {"delta",8}");
        foreach (var d in r.LeaveDeltas)
            sb.AppendLine($"{d.PlayerId,4}  {Short(d.Name,22),-22}  {Clock(d.AtA),8}  "
                + $"{Clock(d.AtB),8}  {Clock(d.Delta),8}");
        if (r.ProblemA is not null) sb.AppendLine("note A " + r.ProblemA);
        if (r.ProblemB is not null) sb.AppendLine("note B " + r.ProblemB);
        return sb.ToString();
    }

    public static string ReplayFindMap(ReplayFindMapReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"recorded  {r.RecordedMap ?? "(none)"}");
        sb.AppendLine($"cleaned   {r.CleanMapPath ?? "(none)"}");
        sb.AppendLine($"folder    {r.MapsFolder}");
        sb.AppendLine(r.Found ? $"found     {r.ResolvedPath}" : "found     no");
        foreach (var c in r.Candidates) sb.AppendLine($"  candidate {c}");
        if (r.Problem is not null) sb.AppendLine("note " + r.Problem);
        return sb.ToString();
    }

    public static string ReplayStats(ReplayStatsReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"duration {r.Duration:mm\\:ss}   players {r.PlayerCount}   disconnects {r.Disconnects}");
        if (r.ActionsAvailable)
        {
            sb.AppendLine($"{"slot",4}  {"name",-22}  {"actions",8}  {"apm",8}");
            foreach (var p in r.Players)
                sb.AppendLine($"{p.Slot,4}  {Short(p.Name,22),-22}  {Num(p.Actions),8}  "
                    + $"{(p.ActionsPerMinute is { } a ? a.ToString("0.0") : "-"),8}");
        }
        else
        {
            sb.AppendLine("actions per minute not carried in this record");
            foreach (var p in r.Players)
                sb.AppendLine($"  slot {p.Slot}  {p.Name}  {p.LeaveMeaning ?? "in game"}");
        }
        if (r.Problem is not null) sb.AppendLine("note " + r.Problem);
        return sb.ToString();
    }

    private static string Short(string? s, int max) =>
        s is null ? string.Empty : s.Length <= max ? s : s[..Math.Max(0, max - 1)] + "~";

    private static string Num(int? v) => v is { } n ? n.ToString() : "-";

    private static string Clock(TimeSpan? t) => t is { } v ? v.ToString("mm\\:ss") : "-";

    private static string Leaf(string? map) =>
        map is null ? "(map unknown)" : Path.GetFileName(map.Replace('\\', '/'));

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
            $"{f.Name ?? "(unnamed)",-28} {f.SizeBytes,10}  {(f.Known ? "known" : "unknown")}{(f.Parsed ? "/parsed" : "")}"
            + (f.ContentType is null ? "" : $"  {f.ContentType}")
            + (f.NameFromHarvest ? "  (name from harvest)" : "")));

    public static string Info(MapInfoResult r) =>
        $"Name:    {r.Name}\nAuthor:  {r.Author}\nPlayers: {r.Players}\nSize:    {r.Width}x{r.Height}"
        + (r.Diagnostics.Count == 0 ? "" : "\n\nDiagnostics:\n" + string.Join("\n", r.Diagnostics));

    public static string MapOverview(MapOverviewResult r) =>
        Info(r.Info)
        + $"\n\nFiles {r.Files.Count}"
        + "\nObjects by kind\n"
        + string.Join("\n", r.ObjectCounts.Select(c => $"  {c.Kind,-13} {c.Count}"));

    /// <summary>A null annotation prints as '?' since the server omitted it, a real bool prints as true or false.</summary>
    private static string Hint(bool? value) => value is null ? "?" : value.Value ? "true" : "false";

    public static string McpTools(IReadOnlyList<Wc3.Mcp.McpToolInfo> tools) =>
        string.Join("\n", tools.Select(t =>
            $"{t.Name,-30} ro={Hint(t.ReadOnly)} d={Hint(t.Destructive)} i={Hint(t.Idempotent)} ow={Hint(t.OpenWorld)}"));

    public static string McpAudit(Wc3.Mcp.McpAnnotationAuditResult r) =>
        r.Count == 0
            ? "clean, no annotation problems"
            : string.Join("\n", r.Problems.Select(p => $"{p.Tool,-30} {p.Problem}")) + $"\n{r.Count} problem(s)";

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
        r.Hits.Count == 0 ? "(no hits)" : string.Join("\n", r.Hits.Select(h => $"{h.FileName}  [{h.Context}]"))
            + (r.Truncated ? $"\n(stopped at {SearchCommand.MaxHits} hits, narrow the query)" : "");

    public static string TerrainAudit(TerrainAuditResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Path.GetFileName(r.Path)}  terrain {r.GridWidth}x{r.GridHeight} corners, "
            + $"{r.TileCount} tile(s), {r.TerrainFileBytes:N0} byte(s)");
        sb.AppendLine($"  tilesets    {r.GroundTileTypes} ground / {r.CliffTileTypes} cliff declared, "
            + $"{r.DistinctGroundTextures} ground / {r.DistinctCliffTextures} cliff in use");
        sb.AppendLine($"  cells       cliff {r.CliffCells}  ramp {r.RampCells}  water {r.WaterCells}  "
            + $"blight {r.BlightCells}  boundary {r.BoundaryCells}  edge {r.EdgeCells}");
        sb.AppendLine($"  height      {r.MinHeight:0.###}..{r.MaxHeight:0.###} (mean {r.MeanHeight:0.###})  "
            + $"cliff {r.MinCliff}..{r.MaxCliff}");
        foreach (var d in r.Diagnostics) sb.AppendLine($"  ! {d}");
        if (r.Issues.Count == 0)
        {
            sb.AppendLine("  no measured waste");
            return sb.ToString().TrimEnd();
        }
        sb.AppendLine($"  {r.Issues.Count} finding(s)");
        foreach (var issue in r.Issues)
            sb.AppendLine($"    {issue.Kind,-26} {issue.Detail}");
        return sb.ToString().TrimEnd();
    }

    public static string TerrainOptimize(TerrainOptimizeResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Path.GetFileName(r.Path)}  {r.Message}");
        sb.AppendLine($"  tilesets    ground {r.GroundTypesBefore} -> {r.GroundTypesAfter} "
            + $"(-{r.GroundTypesRemoved})  cliff {r.CliffTypesBefore} -> {r.CliffTypesAfter} "
            + $"(-{r.CliffTypesRemoved})");
        sb.AppendLine($"  war3map.w3e {r.BytesBefore:N0} -> {r.BytesAfter:N0} byte(s) "
            + $"(-{r.BytesSaved:N0})  {r.CellsRemapped} corner(s) remapped");
        foreach (var d in r.Diagnostics) sb.AppendLine($"  ! {d}");
        return sb.ToString().TrimEnd();
    }

    public static string TerrainCorner(TerrainEditCommand.CornerInfo c) => string.Join("\n", new[]
    {
        $"corner           ({c.Col}, {c.Row})",
        $"ground height    {c.GroundHeight:0.###}   cliff level {c.CliffLevel}",
        $"water            {(c.Water ? $"yes, height {c.WaterHeight:0.###}" : "no")}",
        $"ground texture   {c.GroundTexture}   variation {c.TextureVariation}",
        $"cliff texture    {c.CliffTexture}   variation {c.CliffVariation}",
        $"flags            ramp={c.Ramp}  blighted={c.Blighted}  boundary={c.Boundary}  edge={c.EdgeTile}",
    });

    public static string FieldOptions(ObjectFieldOptionsResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"type {(r.Type.Length == 0 ? "(unknown)" : r.Type)}"
            + (r.IsList ? "  (a comma-separated list)" : ""));
        if (r.Diagnostic is { Length: > 0 }) sb.AppendLine("note: " + r.Diagnostic);
        if (r.Options.Count == 0)
        {
            sb.Append("No enumerated options. This field takes free text or a number, and with no "
                + "game data present nothing can be listed at all.");
            return sb.ToString();
        }
        sb.AppendLine($"{r.Options.Count} value(s) the base data already uses:");
        foreach (var o in r.Options) sb.AppendLine("  " + o);
        return sb.ToString().TrimEnd();
    }

    public static string AssetList(AssetListResult r, IReadOnlyList<string> shown, string source)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Family} paths, source {source}. "
            + $"{r.MapPaths.Count} from the map, {r.GamePaths.Count} from the base game.");
        if (r.Diagnostic is { Length: > 0 })
            sb.AppendLine($"note: {r.Diagnostic}");
        if (shown.Count == 0)
        {
            sb.Append("(nothing to list)");
            return sb.ToString();
        }
        // Marked, because which side a path came from decides whether it travels with the map.
        var fromMap = new HashSet<string>(r.MapPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var p in shown)
            sb.AppendLine($"  {(fromMap.Contains(p) ? "map " : "game")}  {p}");
        return sb.ToString().TrimEnd();
    }

    public static string AssetUnused(AssetUnusedResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Unused.Count} of {r.ImportedEntries} imported file(s) are referenced nowhere");
        foreach (var e in r.Unused)
            sb.AppendLine($"  {e.Path}  {e.SizeBytes} bytes");
        return sb.ToString().TrimEnd();
    }

    public static string AssetReport(AssetReportResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Entries} imported entries, {r.TotalBytes / 1e6:0.0} MB uncompressed");
        sb.AppendLine("By type");
        foreach (var t in r.ByType)
            sb.AppendLine($"  {t.Count,6}  {t.Bytes / 1e6,9:0.0} MB  {t.Type}");
        sb.AppendLine($"Findings, {r.Findings.Count} across {r.Summary.Count} kind(s)");
        foreach (var kv in r.Summary)
            sb.AppendLine($"  {kv.Value,5}  {kv.Key}");
        if (r.Findings.Count > 0)
        {
            sb.AppendLine("Detail");
            foreach (var f in r.Findings)
                sb.AppendLine($"  [{f.Kind}] {f.Path} ({f.SizeBytes} bytes) {f.Detail}");
        }
        if (r.EngineFaults.Count > 0)
        {
            sb.AppendLine($"Engine faults, from the game log, {r.EngineFaults.Count} distinct");
            foreach (var f in r.EngineFaults)
                sb.AppendLine($"  [{f.Kind}] x{f.Count} {f.Path}");
        }
        sb.AppendLine("The full entry list is in --json, one row per imported file.");
        return sb.ToString().TrimEnd();
    }

    public static string AssetDuplicates(AssetDuplicatesResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Groups.Count} duplicate group(s) across {r.ImportedEntries} imported file(s), "
            + $"{r.RecoverableBytes} recoverable byte(s)");
        foreach (var g in r.Groups)
        {
            sb.AppendLine($"  {g.SizeBytes} bytes each, {g.Paths.Count} copies ({g.Sha256[..12]})");
            foreach (var p in g.Paths) sb.AppendLine($"    {p}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string AssetAudit(AssetAuditResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Findings.Count} finding(s) across {r.Scanned} imported file(s), "
            + $"oversized threshold {r.MaxBytes} bytes");
        foreach (var f in r.Findings)
            sb.AppendLine($"  {f.Kind,-20} {f.Path}  ({f.Detail})");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note {d}");
        return sb.ToString().TrimEnd();
    }

    public static string AssetSizeReport(AssetSizeReportResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.TotalEntries} imported file(s), {r.TotalBytes} bytes total");
        foreach (var f in r.Folders)
            sb.AppendLine($"  {f.Bytes,12}  {f.Entries,5} file(s)  {f.Folder}");
        return sb.ToString().TrimEnd();
    }

    public static string AssetModelLint(AssetModelLintResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Findings.Count} finding(s) across {r.ModelsScanned} imported model(s)");
        foreach (var f in r.Findings)
            sb.AppendLine($"  {f.Kind,-22} {f.Path}  ({f.Detail})");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note {d}");
        return sb.ToString().TrimEnd();
    }

    public static string AssetThumbnails(AssetThumbnailsResult r, string outDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Rendered} thumbnail(s) written to {outDir}");
        foreach (var t in r.Thumbnails)
            sb.AppendLine($"  {t.Name}  <- {t.Source}  ({t.Width}x{t.Height})");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note {d}");
        return sb.ToString().TrimEnd();
    }

    public static string AssetConvertBatch(AssetConvertBatchResult r, string outDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Converted} texture(s) converted to {r.ToExtension}, written under {outDir}");
        foreach (var t in r.Textures)
            sb.AppendLine($"  {t.RelativePath}");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note {d}");
        return sb.ToString().TrimEnd();
    }

    public static string AssetImportWire(AssetImportWireResult r, string? savedTo)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Message);
        foreach (var p in r.Imported) sb.AppendLine($"  imported {p}");
        foreach (var f in r.FieldsSet) sb.AppendLine($"  set {f}");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note {d}");
        if (savedTo is not null) sb.AppendLine($"saved to {savedTo}");
        return sb.ToString().TrimEnd();
    }

    public static string ModelExportObj(ModelExport export, string objPath, string mtlPath, IReadOnlyList<string> textures)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Wrote {objPath} + {mtlPath}");
        if (textures.Count > 0)
            sb.AppendLine($"{textures.Count} texture(s) converted, {string.Join(", ", textures)}");
        else
            sb.AppendLine("no map-resolvable textures, materials reference nothing or base-game files");
        return sb.ToString().TrimEnd();
    }

    public static string ModelAudit(ModelAuditResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Path.GetFileName(r.Path)}  {r.Format}  {r.Bytes} byte(s)");
        sb.AppendLine($"  geosets {r.Geosets}  vertices {r.Vertices}  faces {r.Faces}  "
            + $"textures {r.Textures}  sequences {r.Sequences}  nodes {r.Nodes}");
        sb.AppendLine($"  effects {r.Effects}  particle emitters {r.ParticleEmitters}  "
            + $"particle emitters 2 {r.ParticleEmitters2}  ribbons {r.Ribbons}  popcorns {r.Corns}");
        foreach (var seq in r.SequenceList)
            sb.AppendLine($"    seq {seq.Name}: {(seq.IntervalEnd - seq.IntervalStart) / 1000.0:0.000}s"
                + $"  [{seq.IntervalStart}..{seq.IntervalEnd} ms]");
        if (r.Diagnostics.Count > 0)
            foreach (var d in r.Diagnostics) sb.AppendLine($"  ! {d}");
        if (r.Issues.Count == 0)
        {
            sb.AppendLine("  no issues");
            return sb.ToString().TrimEnd();
        }
        sb.AppendLine($"  {r.Issues.Count} issue(s)");
        foreach (var issue in r.Issues)
            sb.AppendLine($"    {issue.Kind,-24} {issue.Detail}");
        return sb.ToString().TrimEnd();
    }

    public static string ModelSquish(ModelSquishResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Wrote {Path.GetFileName(r.OutPath)}");
        sb.AppendLine($"  model {r.BytesBefore} -> {r.BytesAfter} byte(s)");
        if (r.TexturesSquished > 0)
            sb.AppendLine($"  {r.TexturesSquished} texture(s), {r.TextureBytesBefore} -> {r.TextureBytesAfter} byte(s)");
        foreach (var st in r.Steps)
            sb.AppendLine($"  {st.Name,-14} {st.BytesBefore} -> {st.BytesAfter}  ssim {st.Ssim:0.0000}  "
                + $"silhouette {st.SilhouetteIou:0.0000}  {(st.Kept ? "kept" : "refused")}"
                + (st.Note is null ? "" : $" ({st.Note})"));
        sb.AppendLine($"  final ssim {r.FinalSsim:0.0000}  silhouette {r.FinalSilhouetteIou:0.0000}");
        foreach (var d in r.Diagnostics) sb.AppendLine($"  ! {d}");
        return sb.ToString().TrimEnd();
    }

    public static string ModelCompare(ModelCompareResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Path.GetFileName(r.A)}  against  {Path.GetFileName(r.B)}  {r.Width}x{r.Height}");
        foreach (var a in r.Angles)
            sb.AppendLine($"  yaw {a.Yaw,3:0}  pitch {a.Pitch,3:0}  ssim {a.Ssim:0.0000}  silhouette {a.SilhouetteIou:0.0000}");
        sb.AppendLine($"  mean ssim {r.MeanSsim:0.0000}  worst silhouette {r.MinSilhouetteIou:0.0000}");
        foreach (var d in r.Diagnostics) sb.AppendLine($"  ! {d}");
        return sb.ToString().TrimEnd();
    }

    public static string ModelLint(ModelLintResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Path.GetFileName(r.Path)}  geosets {r.Geosets}  vertices {r.Vertices}  textures {r.Textures}");
        if (r.Findings.Count == 0)
        {
            sb.AppendLine("  no issues");
            return sb.ToString().TrimEnd();
        }
        sb.AppendLine($"  {r.Findings.Count} issue(s)");
        foreach (var f in r.Findings)
            sb.AppendLine($"    {f.Kind,-24} {f.Detail}");
        return sb.ToString().TrimEnd();
    }

    public static string ModelOptimize(ModelOptimizeResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Wrote {Path.GetFileName(r.Path)}");
        sb.AppendLine($"  {r.Geosets} geoset(s), vertices {r.VerticesBefore} -> {r.VerticesAfter} "
            + $"(-{r.VerticesRemoved}), faces {r.FacesBefore} -> {r.FacesAfter} (-{r.FacesRemoved})");
        if (r.EffectsRemoved > 0) sb.AppendLine($"  {r.EffectsRemoved} effect chunk(s) stripped");
        sb.AppendLine($"  {r.BytesBefore} -> {r.BytesAfter} byte(s)");
        foreach (var d in r.Diagnostics) sb.AppendLine($"  ! {d}");
        return sb.ToString().TrimEnd();
    }

    public static string ModelLod(ModelLodResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Wrote {r.Levels.Count} level(s) of {Path.GetFileName(r.Path)}");
        foreach (var l in r.Levels)
            sb.AppendLine($"  {l.Ratio.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}  "
                + $"{Path.GetFileName(l.Path)}  vertices {l.Vertices}  faces {l.Faces}  {l.Bytes} byte(s)");
        sb.AppendLine($"  total {r.TotalBytes} byte(s)");
        foreach (var d in r.Diagnostics) sb.AppendLine($"  ! {d}");
        return sb.ToString().TrimEnd();
    }

    public static string ModelImport(ModelImportResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Wrote {Path.GetFileName(r.Path)}");
        sb.AppendLine($"  {r.Geosets} geoset(s), vertices {r.Vertices}, faces {r.Faces}, "
            + $"textures {r.Textures}, materials {r.Materials}");
        if (r.Sequences > 0)
            sb.AppendLine($"  {r.Sequences} sequence(s), {string.Join(", ", r.SequenceNames ?? Array.Empty<string>())}");
        foreach (var t in r.TextureFiles) sb.AppendLine($"  wrote {t}");
        sb.AppendLine($"  {r.Bytes} byte(s)");
        foreach (var d in r.Diagnostics) sb.AppendLine($"  ! {d}");
        return sb.ToString().TrimEnd();
    }

    public static string TextureOptimize(TextureOptimizeResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Wrote {Path.GetFileName(r.Path)}  {r.Format}");
        sb.AppendLine($"  {r.WidthBefore}x{r.HeightBefore} -> {r.WidthAfter}x{r.HeightAfter}, "
            + $"{r.BytesBefore} -> {r.BytesAfter} byte(s)");
        return sb.ToString().TrimEnd();
    }

    public static string TextureAtlas(TextureAtlasResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Wrote {Path.GetFileName(r.Output)}  {r.Format}  {r.AtlasWidth}x{r.AtlasHeight}, "
            + $"{r.Bytes} byte(s), {r.Entries.Count} image(s)");
        foreach (var e in r.Entries)
            sb.AppendLine($"  {e.Name}  ({e.X}, {e.Y}) {e.Width}x{e.Height}");
        return sb.ToString().TrimEnd();
    }

    public static string MapOptimize(MapOptimizeResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Wrote {Path.GetFileName(r.Path)}");
        sb.AppendLine($"  {r.ModelsOptimized} model(s), {r.TexturesOptimized} texture(s) changed, "
            + $"{r.BytesSaved:N0} byte(s) saved across {r.Changed} file(s)");
        sb.AppendLine($"  map {r.BytesBefore:N0} -> {r.BytesAfter:N0} byte(s)");
        foreach (var f in r.Files)
            sb.AppendLine($"  {f.Kind,-7} {f.Path}  {f.BytesBefore} -> {f.BytesAfter} byte(s)  ({f.Detail})");
        foreach (var d in r.Diagnostics) sb.AppendLine($"  ! {d}");
        return sb.ToString().TrimEnd();
    }

    public static string SoundOptimize(SoundOptimizeResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Wrote {Path.GetFileName(r.Path)}  {r.Format.ToUpperInvariant()}");
        var bitrate = r.BitrateKbps > 0 ? $" / {r.BitrateKbps} kbps" : "";
        sb.AppendLine($"  {r.SampleRateBefore} Hz / {r.ChannelsBefore} ch / {r.BytesBefore} byte(s) -> "
            + $"{r.SampleRateAfter} Hz / {r.ChannelsAfter} ch{bitrate} / {r.BytesAfter} byte(s)");
        if (!string.IsNullOrWhiteSpace(r.Note))
            sb.AppendLine($"  ! {r.Note}");
        return sb.ToString().TrimEnd();
    }

    public static string Imports(ImportsListResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Entries.Count} import table entry(s)");
        foreach (var e in r.Entries)
            sb.AppendLine($"  {(e.InArchive ? "ok  " : "MISS")} {e.Path}");
        // A file in the archive that the table does not list is the other half of the check.
        var missing = r.Entries.Count(e => e.InManifest && !e.InArchive);
        if (missing > 0)
            sb.AppendLine($"{missing} table entry(s) have no file behind them in the archive.");
        return sb.ToString().TrimEnd();
    }

    public static string Triggers(TriggerModel t)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"script language {t.ScriptLanguage}");
        sb.AppendLine($"{t.Categories.Count} category(s), {t.Triggers.Count} trigger(s), "
            + $"{t.Variables.Count} GUI variable(s)");
        if (t.Triggers.Count == 0)
        {
            sb.Append("This map has no GUI trigger tree. Its logic is in the compiled script.");
            return sb.ToString();
        }
        var byCategory = t.Triggers.GroupBy(x => x.ParentCategoryId);
        foreach (var group in byCategory)
        {
            var name = t.Categories.FirstOrDefault(c => c.Id == group.Key)?.Name ?? "(no category)";
            sb.AppendLine();
            sb.AppendLine($"== {name} ({group.Count()}) ==");
            foreach (var tr in group)
                sb.AppendLine($"  {(tr.Enabled ? " " : "x")} {tr.Name}"
                    + (tr.IsCustomText ? "  [custom text]" : $"  {tr.Functions.Count} function(s)"));
        }
        return sb.ToString().TrimEnd();
    }

    // ---- trigger analysis (to-script, export, lint, dead, diff) ----

    public static string TriggerScript(TriggerScriptResult r) => r.Script.TrimEnd();

    public static string TriggerExport(TriggerExportResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"script language {r.ScriptLanguage}");
        sb.AppendLine($"{r.Categories.Count} category(s), {r.Triggers.Count} trigger(s), "
            + $"{r.Variables.Count} variable(s)");
        foreach (var t in r.Triggers)
        {
            sb.AppendLine();
            sb.AppendLine($"trigger #{t.Id} {t.Name}"
                + (t.Enabled ? "" : "  [disabled]")
                + (t.InitiallyOn ? "" : "  [initially off]")
                + (t.RunOnMapInit ? "  [run on map init]" : "")
                + (t.IsCustomText ? "  [custom text]" : ""));
            if (t.IsCustomText)
            {
                var body = (t.CustomText ?? string.Empty).TrimEnd();
                sb.AppendLine(body.Length == 0 ? "  (empty body)" : Indent(body, 2));
                continue;
            }
            foreach (var node in t.Events) sb.AppendLine("  " + FunctionLine(node));
            foreach (var node in t.Conditions) sb.AppendLine("  " + FunctionLine(node));
            foreach (var node in t.Actions) sb.AppendLine("  " + FunctionLine(node));
            foreach (var node in t.Other) sb.AppendLine("  " + FunctionLine(node));
        }
        foreach (var v in r.Variables)
            sb.AppendLine($"variable {v.Name} {v.Type}{(v.IsArray ? "[]" : "")}"
                + (v.InitialValue is null ? "" : $" = {v.InitialValue}"));
        return sb.ToString().TrimEnd();
    }

    private static string FunctionLine(TriggerFunctionInfo f)
    {
        var sb = new StringBuilder();
        sb.Append(f.Enabled ? "   " : "[x] ");
        sb.Append($"{f.Kind,-9} {f.Name}");
        if (f.Parameters.Count > 0) sb.Append($" ({string.Join(", ", f.Parameters)})");
        foreach (var child in f.Children)
            sb.Append($"\n      {FunctionLine(child)}");
        return sb.ToString();
    }

    public static string TriggerLint(TriggerLintResult r)
    {
        if (r.Findings.Count == 0) return r.Summary;
        var sb = new StringBuilder();
        foreach (var f in r.Findings)
            sb.AppendLine($"{f.Rule,-16} {f.TriggerName}  {f.Detail}");
        sb.AppendLine();
        sb.Append(r.Summary);
        return sb.ToString();
    }

    public static string TriggerDead(TriggerDeadResult r)
    {
        if (r.Dead.Count == 0) return r.Summary;
        var sb = new StringBuilder();
        foreach (var t in r.Dead)
        {
            sb.AppendLine($"#{t.Id} {t.Name}");
            foreach (var reason in t.Reasons)
                sb.AppendLine($"  {reason.Kind,-30} {reason.Detail}");
        }
        sb.AppendLine();
        sb.Append(r.Summary);
        return sb.ToString();
    }

    public static string TriggerDiff(TriggerDiffResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.TriggersInA} trigger(s) in the first map, {r.TriggersInB} in the second.");
        foreach (var name in r.Added) sb.AppendLine($"+ added    {name}");
        foreach (var name in r.Removed) sb.AppendLine($"- removed  {name}");
        foreach (var rename in r.Renamed)
            sb.AppendLine($"~ renamed  #{rename.Id} '{rename.From}' to '{rename.To}'");
        foreach (var change in r.Changed)
        {
            sb.AppendLine($"* changed  #{change.Id} {change.Name}");
            foreach (var d in change.Changes)
                sb.AppendLine($"{(d.Change == "added" ? '+' : '-')} {d.Kind,-9} {d.Description}");
        }
        if (r.Added.Count == 0 && r.Removed.Count == 0 && r.Renamed.Count == 0 && r.Changed.Count == 0)
            sb.AppendLine("The two trigger trees are identical.");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Indents every line of <paramref name="text"/> by <paramref name="spaces"/>.</summary>
    private static string Indent(string text, int spaces)
    {
        string pad = new(' ', spaces);
        return string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(l => pad + l));
    }

    public static string PlacedUnit(UnitInstanceInfo u) => string.Join("\n", new[]
    {
        $"creation number {u.CreationNumber}   type {u.TypeRawcode}"
            + (u.Name is null ? "" : $"   {u.Name}"),
        $"owner            {u.OwnerId}",
        $"position         ({u.X:0.###}, {u.Y:0.###})   facing {u.Rotation:0.###} rad",
        $"scale            {u.Scale.Sx:0.###}, {u.Scale.Sy:0.###}, {u.Scale.Sz:0.###}",
        $"hero             level {u.HeroLevel}   str {u.HeroStrength}   agi {u.HeroAgility}   int {u.HeroIntelligence}",
        $"hp / mana        {u.HpPercent}% / {u.ManaPercent}%",
        $"gold             {u.GoldAmount}",
        $"target acquire   {u.TargetAcquisition:0.###}",
    });

    public static string PlacedDoodad(DoodadInstanceInfo d) => string.Join("\n", new[]
    {
        $"creation number {d.CreationNumber}   type {d.TypeRawcode}",
        $"position         ({d.X:0.###}, {d.Y:0.###}, {d.Z:0.###})",
        $"rotation         {d.Rotation:0.###} rad",
        $"scale            {d.Scale.Sx:0.###}, {d.Scale.Sy:0.###}, {d.Scale.Sz:0.###}",
        $"variation        {d.Variation}",
        $"life             {d.LifePercent}%",
    });

    public static string ObjectForm(ObjectForm f)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Headline(f.Rawcode, f.Name, f.BaseRawcode));
        sb.AppendLine($"{f.FieldCount} field(s) in {f.Groups.Count} group(s)"
            + (f.HiddenFieldCount > 0
                ? $", {f.HiddenFieldCount} hidden as not applying to this object"
                : ""));

        foreach (var g in f.Groups)
        {
            sb.AppendLine();
            sb.AppendLine($"== {g.Title} ({g.Fields.Count}) ==");
            foreach (var x in g.Fields)
            {
                var bounds = x.MinValue is null && x.MaxValue is null
                    ? ""
                    : $"  [{x.MinValue ?? "*"}..{x.MaxValue ?? "*"}]";
                var layer = x.Layer == ObjectLayer.Skin ? "  -> skin" : "";
                sb.AppendLine($"  {x.Name} ({x.Code}) = {x.Display}  [{x.Source}]{bounds}{layer}");
            }
        }
        foreach (var d in f.Diagnostics) sb.AppendLine($"note: {d}");
        return sb.ToString().TrimEnd();
    }

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

    public static string ObjectSearch(ObjectSearchResult r)
    {
        var head = $"{r.Count} match(es) for \"{r.Query}\"";
        var lines = r.Hits.Select(h =>
        {
            var name = h.Name is null ? "" : $" \"{h.Name}\"";
            var baseVal = h.BaseValue is null ? "" : $" base={h.BaseValue}";
            var mapVal = h.MapValue is null ? "" : $" map={h.MapValue}";
            return $"{h.Kind} {h.Rawcode}{name} {h.FieldName} ({h.FieldCode}){baseVal}{mapVal}";
        });
        var notes = r.Diagnostics.Select(d => $"note: {d}");
        return string.Join("\n", new[] { head }.Concat(lines).Concat(notes));
    }

    public static string ObjectNoopFields(ObjectNoopFieldsResult r)
    {
        var head = r.Count == 0
            ? "no fields equal their base game value"
            : $"{r.Count} field(s) equal their base game value";
        var lines = r.Fields.Select(f =>
            $"{f.Kind} {f.Rawcode}{(f.Name is null ? "" : $" \"{f.Name}\"")} "
            + $"{f.FieldName} ({f.FieldCode}) = {f.Value}  [same as base]");
        var notes = r.Diagnostics.Select(d => $"note: {d}");
        return string.Join("\n", new[] { head }.Concat(lines).Concat(notes));
    }

    public static string ObjectDeprecated(ObjectDeprecatedResult r)
    {
        var head = $"{r.Fields.Count} field(s) not defined in {r.TargetBuild} "
            + $"({r.FieldsChecked} map field(s) checked)";
        var lines = r.Fields.Select(f =>
            $"{f.Kind} {f.Rawcode}{(f.Name is null ? "" : $" \"{f.Name}\"")} field {f.FieldCode}");
        var notes = r.Diagnostics.Select(d => $"note: {d}");
        return string.Join("\n", new[] { head }.Concat(lines).Concat(notes));
    }

    public static string ObjectRawcodeSuggest(ObjectRawcodeSuggestResult r)
    {
        var head = r.Count == 0
            ? $"no free {r.Kind} rawcodes proposed"
            : $"{r.Count} free {r.Kind} rawcode(s)";
        var lines = r.Rawcodes.Select(c => $"  {c}");
        var notes = r.Diagnostics.Select(d => $"note: {d}");
        return string.Join("\n", new[] { head }.Concat(lines).Concat(notes));
    }

    public static string ObjectMigrate(ObjectMigrateResult r, string? savedTo = null)
    {
        var head = r.Ok
            ? $"migrated to {r.TargetBuild}: {r.FieldsKept} field(s) kept, {r.FieldsDropped} dropped"
            : r.Message;
        var lines = r.Drops.Select(d => $"  dropped {d.Kind} {d.Rawcode} field {d.FieldCode}");
        var notes = r.UncheckedKinds.Count > 0
            ? new[] { $"  kinds not checked (no object schema): {string.Join(", ", r.UncheckedKinds)}" }
            : Array.Empty<string>();
        var diags = r.Diagnostics.Select(d => $"note: {d}");
        var tail = savedTo is null ? Array.Empty<string>() : new[] { $"saved: {savedTo}" };
        return string.Join("\n", new[] { head }.Concat(lines).Concat(notes).Concat(diags).Concat(tail));
    }

    public static string ObjectBulkSet(ObjectBulkSetResult r, string? savedTo = null)
    {
        var head = $"{r.Message}, {r.Skipped} skipped";
        var lines = r.Entries.Where(e => !e.Changed)
            .Select(e => $"  {e.Rawcode}: {e.Detail}");
        var notes = r.Diagnostics.Select(d => $"note: {d}");
        var tail = savedTo is null ? Array.Empty<string>() : new[] { $"saved: {savedTo}" };
        return string.Join("\n", new[] { head }.Concat(lines).Concat(notes).Concat(tail));
    }

    public static string ObjectExport(ObjectExportResult r, string? writtenTo = null)
    {
        var head = $"{r.Message} ({r.Columns.Count} column(s))";
        var notes = r.Diagnostics.Select(d => $"note: {d}");
        var tail = writtenTo is null ? Array.Empty<string>() : new[] { $"written: {writtenTo}" };
        return string.Join("\n", new[] { head }.Concat(notes).Concat(tail));
    }

    public static string ObjectImport(ObjectImportResult r, string? savedTo = null)
    {
        var head = r.Ok ? $"{r.Message}" : $"refused: {r.Message}";
        var notes = r.Diagnostics.Select(d => $"note: {d}");
        var tail = savedTo is null || !r.Ok ? Array.Empty<string>() : new[] { $"saved: {savedTo}" };
        return string.Join("\n", new[] { head }.Concat(notes).Concat(tail));
    }

    public static string ObjectRename(ObjectRenameResult r, string? savedTo = null)
    {
        var head = r.Ok ? r.Message : $"refused: {r.Message}";
        var tail = savedTo is null || !r.Ok ? Array.Empty<string>() : new[] { $"saved: {savedTo}" };
        return string.Join("\n", new[] { head }.Concat(tail));
    }

    public static string DoodadPalette(DoodadPaletteResult r)
    {
        // A base doodad the map edits in place carries BaseRawcode == its own code; suppress
        // the "(base: self)" noise there, the [source] tag already says "map-modified".
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

        // The shape first. A bundle for one hero on a large map runs to thousands of lines, and
        // the fact that the closure hit its cap used to be a note printed after all of them. An
        // incomplete result that reads as exhaustive is the worst way to be wrong, so the
        // warning goes where it will actually be seen.
        var sum = BundleStructure.Summarize(r);
        sb.Append($"{sum.Objects} object(s), {sum.Files} file(s), {sum.Strings} string(s), ")
          .Append($"{sum.Functions} function(s)\n")
          .Append("  ").Append(BundleStructure.DescribeEdges(sum)).Append('\n');
        if (sum.Truncated)
            sb.Append("  ").Append(BundleStructure.TruncatedWarning)
              .Append(" See the notes at the end.\n");
        sb.Append('\n');

        var byCode = r.Objects.ToDictionary(o => o.Rawcode, StringComparer.Ordinal);
        // Real references only. The script closure seed edges are the deliberate over-carry
        // (other heroes' kits), shown in their own group below rather than as the hero's own.
        var realChildren = BundleStructure.RealChildEdges(r);
        var carried = BundleStructure.CarriedByScriptClosure(r);

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
                if (realChildren.TryGetValue(code, out var kids))
                    foreach (var e in kids) Print(e.To, e.Via, depth + 1);
            }
            Print(r.RootRawcode, via: null, depth: 0);

            if (carried.Count > 0)
            {
                var carriedParents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                foreach (var kv in realChildren)
                    foreach (var e in kv.Value)
                    {
                        if (!carriedParents.TryGetValue(e.To, out var ps))
                            carriedParents[e.To] = ps = new HashSet<string>(StringComparer.Ordinal);
                        ps.Add(e.From);
                    }
                bool HasCarriedParent(string code) =>
                    carriedParents.TryGetValue(code, out var ps) && ps.Any(carried.Contains);
                string ViaInto(string code) =>
                    string.Join(", ", r.Edges.Where(e => e.To == code).Select(e => e.Via).Distinct());

                sb.AppendLine().AppendLine($"Carried by the script closure ({carried.Count}):");
                foreach (var o in r.Objects)
                    if (carried.Contains(o.Rawcode) && !HasCarriedParent(o.Rawcode) && !printed.Contains(o.Rawcode))
                        Print(o.Rawcode, ViaInto(o.Rawcode), depth: 1);
                foreach (var o in r.Objects)
                    if (carried.Contains(o.Rawcode) && !printed.Contains(o.Rawcode))
                        Print(o.Rawcode, ViaInto(o.Rawcode), depth: 1);
            }
        }

        // Files and strings get the same treatment as the objects above. Every foreign icon
        // arrives through a real art field of a foreign object, so the split is by WHICH object
        // asked, never by the field code. The closure's share is counted, not dumped, since it is
        // hundreds of other heroes' assets. --json still carries the complete lists.
        var realFiles = BundleStructure.RealFiles(r);
        var ownFiles = r.Files.Where(f => realFiles.Contains(f.Path)).ToList();
        var carriedFiles = r.Files.Where(f => !realFiles.Contains(f.Path)).ToList();

        sb.AppendLine().AppendLine($"Files ({ownFiles.Count}):");
        foreach (var f in ownFiles)
            sb.AppendLine($"  {(f.PresentInMap ? "[in map] " : "[missing]")} {f.Path}  ({f.Category})");
        if (carriedFiles.Count > 0)
            sb.AppendLine($"  plus {carriedFiles.Count} carried by the script closure"
                + $" ({Tally(carriedFiles.Select(f => f.Category))}), run with --json to list them");

        var realStrings = BundleStructure.RealStrings(r);
        var ownStrings = r.Strings.Where(realStrings.Contains).ToList();
        int carriedStrings = r.Strings.Count - ownStrings.Count;

        sb.AppendLine().AppendLine($"Strings ({ownStrings.Count}):");
        foreach (var s in ownStrings)
            sb.AppendLine($"  \"{s}\"");
        if (carriedStrings > 0)
            sb.AppendLine($"  plus {carriedStrings} carried by the script closure,"
                + " run with --json to list them");

        sb.AppendLine().AppendLine($"Functions ({r.Functions.Count}):");
        foreach (var f in r.Functions)
            sb.AppendLine($"  line {f.StartLine}-{f.EndLine}  {f.Name}  ({f.Reason})");

        foreach (var d in r.Diagnostics)
            sb.AppendLine($"note: {d}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>"187 icon, 157 sound, 36 model", commonest first, for summarizing a list that is
    /// too long to print.</summary>
    private static string Tally(IEnumerable<string> values) =>
        string.Join(", ", values
            .GroupBy(v => v, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Count()} {g.Key}"));

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

    /// <summary>The full dependency report for a rawcode, grouped, with asset sizes.</summary>
    public static string PortDeps(PortDepsResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.RootRawcode}{(r.RootName is null ? "" : $"  \"{r.RootName}\"")}  ({r.RootKind})");
        sb.AppendLine($"  {r.ObjectCount} object(s), {r.Assets.Count} asset(s) "
            + $"({r.AssetsInMap} in map, {r.AssetsMissing} base-game), "
            + $"{Size(r.BytesInMap)} of imported bytes, {r.Strings.Count} string(s), {r.Functions} function(s).");

        sb.AppendLine().AppendLine($"Objects ({r.ObjectCount})");
        foreach (var g in r.ObjectGroups)
        {
            sb.AppendLine($"  {g.Kind} ({g.Objects.Count})");
            foreach (var o in g.Objects)
                sb.AppendLine($"    {o.Rawcode}{(o.Name is null ? "" : $"  \"{o.Name}\"")}"
                    + $"{(o.CustomToMap ? "  custom" : "  base")}"
                    + $"{(o.CarriedByScriptClosure ? "  [carried by the script closure]" : "")}");
        }

        sb.AppendLine().AppendLine($"Assets ({r.Assets.Count})");
        foreach (var a in r.Assets)
            sb.AppendLine($"  {(a.PresentInMap ? "[in map] " : "[missing]")} {a.Path}  ({a.Category}"
                + (a.SizeBytes is { } s ? $", {Size(s)}" : "") + ")");

        if (r.Strings.Count > 0)
        {
            sb.AppendLine().AppendLine($"Strings ({r.Strings.Count})");
            foreach (var s in r.Strings) sb.AppendLine($"  \"{s}\"");
        }

        foreach (var d in r.Diagnostics) sb.AppendLine($"note: {d}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>Human byte size, e.g. "1.2 KB".</summary>
    private static string Size(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B",
    };

    /// <summary>What one bundle has that another does not.</summary>
    public static string BundleDiff(BundleDiffResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.RootA} vs {r.RootB}, {r.Shared} shared member(s).");
        void Section(string title, IReadOnlyList<BundleMember> members)
        {
            sb.AppendLine().AppendLine($"{title} ({members.Count})");
            if (members.Count == 0) sb.AppendLine("  (none)");
            foreach (var m in members)
                sb.AppendLine($"  {m.Kind} {m.Id}{(m.Name is null ? "" : $"  \"{m.Name}\"")}");
        }
        Section("Added (only in the second)", r.Added);
        Section("Removed (only in the first)", r.Removed);
        Section("Changed", r.Changed);
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>The merge outcome, or the refusal with every conflicting rawcode named.</summary>
    public static string BundleMerge(BundleMergeResult r, string? outPath)
    {
        var sb = new StringBuilder();
        if (!r.Ok)
        {
            sb.AppendLine(r.Message);
            foreach (var c in r.Conflicts)
                sb.AppendLine($"  {c.Rawcode}  ({c.Kind})  {c.Detail}");
            sb.AppendLine().AppendLine("nothing written, resolve the conflict and rerun");
            return sb.ToString().TrimEnd('\r', '\n');
        }
        sb.AppendLine(r.Message);
        sb.AppendLine().AppendLine(outPath is null ? "DRY RUN - nothing written" : $"Saved: {outPath}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>The port report plus the downgrade: fields removed and identifiers flagged.</summary>
    public static string PortIntoVersion(PortIntoVersionResult r, string? outPath)
    {
        var sb = new StringBuilder();
        AppendPortBody(sb, r.Port);
        sb.AppendLine().AppendLine($"Downgraded to {r.TargetBuild}");
        sb.AppendLine($"  {r.FieldsRemoved.Count} object field(s) not declared by the build were removed, "
            + $"{r.ScriptIdentifiersFlagged.Count} script identifier(s) are not declared and were flagged.");
        foreach (var f in r.FieldsRemoved.Take(40))
            sb.AppendLine($"  removed  {f.Kind} {f.Rawcode}{(f.Name is null ? "" : $"  \"{f.Name}\"")}.{f.FieldCode}");
        if (r.FieldsRemoved.Count > 40) sb.AppendLine($"  ... and {r.FieldsRemoved.Count - 40} more");
        foreach (var i in r.ScriptIdentifiersFlagged.Take(40))
            sb.AppendLine($"  flagged  {i.Name}  (line {i.FirstLine}, used {i.Count} time(s))");
        if (r.ScriptIdentifiersFlagged.Count > 40)
            sb.AppendLine($"  ... and {r.ScriptIdentifiersFlagged.Count - 40} more");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note: {d}");
        AppendPortFooter(sb, outPath);
        return sb.ToString().TrimEnd('\r', '\n');
    }

    // One shared report body, PortReport in Wc3.Commands. It used to live here and again inline
    // in Studio's port dialog, and the copies drifted, so Studio kept listing the whole closure.
    private static void AppendPortBody(StringBuilder sb, PortResult r) => PortReport.Append(sb, r);

    public static string ScriptFunctions(ScriptFunctionsResult r) =>
        $"{r.Functions.Count} functions in {r.ScriptFile}"
        + string.Concat(r.Functions.Select(f => $"\nline {f.StartLine}-{f.EndLine}  {f.Name}"));

    /// <summary>
    /// Uses of a name, grouped so the reader sees WHERE from, not just how many. The code-value
    /// uses are called out because they are the ones a text search misses.
    /// </summary>
    public static string ScriptReferences(ScriptReferencesResult r)
    {
        if (r.References.Count == 0)
            return $"nothing in {r.ScriptFile} uses '{r.Name}'";

        var sb = new StringBuilder();
        sb.Append($"{r.References.Count} use(s) of '{r.Name}' in {r.ScriptFile}");
        if (r.AsCode > 0)
            sb.Append($", {r.AsCode} passing it as a code value");
        foreach (var u in r.References)
        {
            var kind = u.Kind switch
            {
                JassReferenceKind.Call => "call   ",
                JassReferenceKind.CodeReference => "as code",
                _ => "declares",
            };
            sb.AppendLine();
            sb.Append($"{u.Line,8}  {kind}  {u.InFunction ?? "(file scope)"}");
            sb.AppendLine();
            sb.Append($"              {u.Text}");
        }
        return sb.ToString();
    }

    public static string Extract(ExtractManifest m, string dest) =>
        $"Extracted {m.Count} file(s) ({m.TotalBytes:N0} bytes) to {dest}";

    public static string GameHang(HangReport r)
    {
        if (!r.Found) return r.Message;
        var sb = new StringBuilder();
        sb.AppendLine($"pid {r.ProcessId}, {r.Message}");
        sb.AppendLine($"CPU used: {r.ProcessCpuMilliseconds:N0} ms over {r.SampleSeconds:0.0}s "
                      + $"= {r.CpuCoresBusy:0.000} core(s) busy");
        sb.AppendLine();
        sb.AppendLine(r.Verdict);
        if (r.BusiestThreads.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{"thread",-9}{"cpu ms",9}  {"state",-12}{"wait reason",-22}start module");
            foreach (var t in r.BusiestThreads)
                sb.AppendLine($"{t.Id,-9}{t.CpuMillisecondsUsed,9:0.0}  {t.State,-12}{t.WaitReason,-22}{t.StartModule}");
        }
        if (r.HotThreadNote is not null || r.HotModules.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Where the busiest thread was actually executing:");
            foreach (var h in r.HotModules)
                sb.AppendLine($"  {h.Percent,5:0.0}%  {h.Samples,4} sample(s)  {h.Module}  (e.g. {h.ExampleAddress})");
            if (r.HotAddresses.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Distinct addresses it was caught at (a short list means a tight loop):");
                foreach (var a in r.HotAddresses)
                    sb.AppendLine($"  {a.Percent,5:0.0}%  {a.Samples,4}x  {a.Module}{a.ModuleOffset}");
            }
            if (r.HotThreadNote is not null) sb.AppendLine($"  {r.HotThreadNote}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string ScriptStrip(StripResult r, string dest)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"functions  {r.FunctionsBefore,7:N0} -> {r.FunctionsAfter,7:N0}   "
                      + $"removed {r.FunctionsRemoved:N0} ({r.PercentRemoved}%)");
        sb.AppendLine($"lines      {r.LinesBefore,7:N0} -> {r.LinesAfter,7:N0}");
        sb.AppendLine($"roots      {r.EntryPoints} engine entry point(s) + {r.StringRoots} named in a string literal");
        sb.AppendLine();
        sb.AppendLine("Globals were not touched. Verify with 'lint' and 'script loops' before trusting this.");
        sb.Append($"saved: {dest}");
        return sb.ToString();
    }

    public static string ScriptRoots(ScriptRootsResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ScriptFile}");
        sb.AppendLine($"  functions declared            {r.FunctionsDeclared}");
        sb.AppendLine($"  referenced by identifier      {r.ReferencedByIdentifier}");
        sb.AppendLine($"  named in a string literal     {r.NamedInStringLiteral}");
        sb.AppendLine($"  ROOTS (string dispatch only)  {r.Roots.Count}  ({r.RootPercent}% of all functions)");
        if (r.Roots.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("A reachability pass must pin these or it will delete live code:");
            foreach (var root in r.Roots)
                sb.AppendLine($"  line {root.DeclaredAtLine,-8} {root.Function}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string ScriptLoops(ScriptLoopsResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ScriptFile}: {r.TotalLoops} loop(s), {r.Findings.Count} reported, {r.HighRisk} high risk");
        foreach (var f in r.Findings)
        {
            sb.AppendLine();
            sb.AppendLine($"[{f.Risk}] {f.Function}  lines {f.StartLine}-{f.EndLine}  "
                          + $"exitwhen x{f.ExitWhenCount}{(f.HasReturnInside ? ", has return" : "")}");
            sb.AppendLine($"        {f.Reason}");
            if (f.ConditionSample.Length > 0) sb.AppendLine($"        {f.ConditionSample}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string ScriptUnusedGlobals(ScriptUnusedGlobalsResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ScriptFile}: {r.Declared} global(s), {r.Framework} framework, {r.Unused} unused");
        foreach (var g in r.Globals)
            sb.AppendLine($"  line {g.Line,-7} {g.Name,-28} {g.Type}");
        return sb.ToString().TrimEnd();
    }

    public static string ScriptUnusedFunctions(ScriptUnusedFunctionsResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ScriptFile}: {r.Declared} function(s), {r.Unused} unreachable");
        foreach (var f in r.Functions)
            sb.AppendLine($"  line {f.Line,-7} {f.Name}");
        return sb.ToString().TrimEnd();
    }

    public static string ScriptNullLocals(ScriptNullLocalsResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ScriptFile}: {r.Checked} handle local(s) checked, {r.Findings} destroyed without null");
        foreach (var l in r.Locals)
            sb.AppendLine($"  line {l.Line,-7} {l.Function}  {l.Type} {l.Name}  destroyed by {l.Destroyer} at line {l.DestroyedAtLine}");
        return sb.ToString().TrimEnd();
    }

    public static string ScriptComplexity(ScriptComplexityResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ScriptFile}: {r.FunctionCount} function(s), {r.TotalDecisions} decision point(s) total");
        foreach (var f in r.Functions)
            sb.AppendLine($"  {f.Complexity,5}  line {f.Line,-7} {f.Function}");
        return sb.ToString().TrimEnd();
    }

    public static string ScriptCallGraph(ScriptCallGraphResult r, bool dot)
    {
        var sb = new StringBuilder();
        if (dot)
        {
            sb.AppendLine("digraph callgraph {");
            foreach (var f in r.Calls.Select(c => c.From).Distinct().OrderBy(x => x, StringComparer.Ordinal))
                sb.AppendLine($"  \"{f}\";");
            foreach (var c in r.Calls)
                sb.AppendLine($"  \"{c.From}\" -> \"{c.To}\";");
            if (r.Calls.Count == 0) sb.AppendLine("  // no edges");
            sb.AppendLine("}");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"{r.ScriptFile}: {r.Functions} function(s), {r.Edges} edge(s)");
        foreach (var group in r.Calls.GroupBy(c => c.From))
            sb.AppendLine($"  {group.Key} -> {string.Join(", ", group.Select(c => c.To))}");
        return sb.ToString().TrimEnd();
    }

    public static string ScriptReachable(ScriptReachableResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ScriptFile} has {r.Reachable} reachable and {r.Dead} dead ({r.DeadPercent}%)");
        sb.AppendLine("reachable");
        foreach (var f in r.ReachableFunctions)
            sb.AppendLine($"  line {f.Line,-7} {f.Name}");
        if (r.DeadFunctions.Count > 0)
        {
            sb.AppendLine("dead");
            foreach (var f in r.DeadFunctions)
                sb.AppendLine($"  line {f.Line,-7} {f.Name}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string ScriptApiUsage(ScriptApiUsageResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ScriptFile} against {r.TargetBuild}, "
                      + $"{r.Missing} of {r.Used} native and function use(s) missing");
        foreach (var u in r.Usages)
            sb.AppendLine($"  line {u.Line,-7} {u.Kind,-9} {u.Name}  (used {u.Count}x)");
        return sb.ToString().TrimEnd();
    }

    public static string LuaScriptLeaks(LuaScriptLeaksResult r, bool all)
    {
        if (!r.Ok) return r.Message;
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
        sb.AppendLine($"periodic, {r.Periodic.Count} driver(s)");
        foreach (var p in r.Periodic.Take(25))
            sb.AppendLine($"  {(p.Period is { } s ? s.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " s" : "variable"),-9} line {p.Line,7}  {p.Function}  (reaches {p.Reached} function(s))");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note  {d}");
        sb.AppendLine(r.Message);
        return sb.ToString().TrimEnd();
    }

    public static string LuaGlobals(LuaGlobalsResult r)
    {
        if (!r.Ok) return r.Message;
        var sb = new StringBuilder();
        sb.AppendLine(r.Message);
        foreach (var g in r.Globals)
        {
            string reads = g.Reads == 0 ? "never read" : $"read x{g.Reads} at {string.Join(",", g.ReadLines)}";
            sb.AppendLine($"  line {g.FirstWriteLine,-7} {g.Name}  (written x{g.Writes}, {reads}){(g.Framework ? "  [entry point]" : "")}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string LuaCheck(LuaCheckResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Files} Lua file(s), {r.Errors} with a syntax error");
        foreach (var c in r.Checks)
            sb.AppendLine(c.Ok
                ? $"  ok      {c.FileName}  ({c.Bytes} bytes, {c.Lines} lines)"
                : $"  error   {c.FileName}:{c.Line}:{c.Column}  {c.Message}");
        return sb.ToString().TrimEnd();
    }

    public static string LuaApi(LuaApiResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ScriptFile} against {r.TargetBuild}, {r.Missing} of {r.Used} native and function use(s) missing");
        sb.AppendLine($"mapping  {r.Mapping}");
        foreach (var u in r.Usages)
            sb.AppendLine($"  line {u.Line,-7} {u.Kind,-9} {u.Name}  (used {u.Count}x"
                          + (u.JassSpelling != u.Name ? $", jass {u.JassSpelling}" : "") + ")");
        return sb.ToString().TrimEnd();
    }

    public static string LuaToJass(LuaToJassResult r)
    {
        var sb = new StringBuilder();
        if (!r.Ok)
        {
            sb.AppendLine(string.Join("\n", r.Diagnostics));
            return sb.ToString().TrimEnd();
        }
        sb.AppendLine($"{r.ScriptFile} converted to JASS, {r.FunctionsConverted} function(s), "
                      + $"{r.GlobalsConverted} global(s), {r.FunctionsSkipped} function(s) left");
        foreach (var u in r.Unconverted)
            sb.AppendLine($"  ! line {u.Line,7}  {u.Construct,-10} {u.Detail}");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note  {d}");
        return sb.ToString().TrimEnd();
    }


    public static string ScriptSemanticDiff(ScriptSemanticDiffResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ScriptA} vs {r.ScriptB} ({r.Language}), {r.Changes} real change(s)");
        foreach (var l in r.Lines)
        {
            string where = l.LineA > 0 && l.LineB > 0 ? $"{l.LineA}/{l.LineB}"
                : l.LineA > 0 ? l.LineA.ToString()
                : l.LineB.ToString();
            sb.AppendLine($"  {l.Kind,-16} line {where,-10} {l.Text}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string ScriptDuplicateBodies(ScriptDuplicateBodiesResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ScriptFile}, {r.Functions} function(s), {r.DuplicateGroups} duplicate group(s), "
                      + $"{r.DuplicateFunctions} function(s) duplicated");
        foreach (var g in r.Groups)
        {
            sb.AppendLine($"  {g.Size} copies");
            foreach (var f in g.Functions)
                sb.AppendLine($"    line {f.Line,-7} {f.Name}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string ScriptModernize(ScriptModernizeResult r, string? dest)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ScriptFile} against {r.TargetBuild}, {r.Rewrites} native rewrite(s)");
        foreach (var c in r.Changes)
            sb.AppendLine($"  line {c.Line,-7} {c.From} to {c.To}  ({c.Count}x)");
        if (dest is not null) sb.Append($"saved: {dest}");
        return sb.ToString().TrimEnd();
    }

    public static string MpqHash(HashTableView v)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{v.Path}");
        sb.AppendLine($"  slots        {v.Slots}");
        sb.AppendLine($"  occupied     {v.Occupied}");
        sb.AppendLine($"  deleted      {v.Deleted}   (does NOT stop a probe)");
        sb.AppendLine($"  never-used   {v.NeverUsed}   (the only thing that stops a probe)");
        sb.AppendLine($"  worst-case probe for an absent name: {v.LongestRunWithoutNeverUsed} slot(s)");
        sb.AppendLine();
        sb.Append(v.LookupCanLoopForever
            ? "BROKEN - zero never-used slots. A lookup for a name that is not present has no "
              + "terminator and will probe forever, which hangs the game with no crash and no log."
            : "ok - a failed lookup terminates.");
        if (v.Sample.Count > 0)
        {
            sb.AppendLine().AppendLine();
            sb.AppendLine($"{"slot",6}  {"kind",-11}{"block",-10}nameA");
            foreach (var s in v.Sample)
                sb.AppendLine($"{s.Index,6}  {s.Kind,-11}{s.BlockIndex,-10}0x{s.NameA:X8}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string MpqDiff(StructureDiff d)
    {
        var sb = new StringBuilder();
        void Side(string label, ArchiveStructure s) => sb.AppendLine(
            $"{label}  v{s.FormatVersion + 1}  sector={512 << s.SectorShift}  hash={s.HashTableSize}"
            + $"  blocks={s.BlockTableSize}  occupied={s.OccupiedHashSlots}"
            + $"  longestProbeRun={s.MaxProbeDistance}  bytes={s.FileBytes:N0}");
        Side("A:", d.A);
        Side("B:", d.B);

        void Section(string title, IReadOnlyList<string> items)
        {
            sb.AppendLine().AppendLine($"{title} ({items.Count})");
            foreach (var i in items) sb.AppendLine($"  {i}");
        }
        if (d.HeaderDifferences.Count > 0) Section("Header/table differences", d.HeaderDifferences);
        if (d.OnlyInA.Count > 0) Section("Only in A", d.OnlyInA);
        if (d.OnlyInB.Count > 0) Section("Only in B", d.OnlyInB);
        if (d.EncodingDifferences.Count > 0)
            Section("Files stored differently (same name, different physical encoding)", d.EncodingDifferences);

        sb.AppendLine();
        sb.Append(d.Identical
            ? "STRUCTURALLY IDENTICAL"
            : $"STRUCTURE DIFFERS - {d.HeaderDifferences.Count} header, "
              + $"{d.EncodingDifferences.Count} re-encoded, {d.OnlyInA.Count} lost, {d.OnlyInB.Count} added");
        return sb.ToString();
    }

    public static string MpqVerify(MpqVerifyResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Path);
        sb.AppendLine($"  entries     {r.TotalEntries}");
        sb.AppendLine($"  readable    {r.ReadableEntries}");
        sb.AppendLine($"  unreadable  {r.UnreadableEntries}");
        if (r.Unreadable.Count > 0)
        {
            sb.AppendLine();
            foreach (var u in r.Unreadable)
                sb.AppendLine($"  block {u.BlockIndex,5}  {u.SizeBytes,10:N0} B  "
                    + $"{u.Name ?? "(unnamed)"}  {u.Error}");
        }
        sb.AppendLine();
        sb.Append(r.UnreadableEntries == 0
            ? "ok. Every entry opened and read to its end."
            : $"{r.UnreadableEntries} entry(s) could not be read.");
        return sb.ToString();
    }

    public static string MpqListfile(MpqListfileResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Path);
        sb.AppendLine($"  entries                      {r.TotalEntries}");
        sb.AppendLine($"  named by the listfile        {r.NamedByListfile}");
        sb.AppendLine($"  recovered by standard probe  {r.RecoveredByStandardProbe}");
        sb.AppendLine($"  with no name at all          {r.WithoutName}");
        if (r.WithoutNameSample.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("unnamed sample");
            foreach (var s in r.WithoutNameSample) sb.AppendLine($"  {s}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string MpqNameRecover(MpqNameRecoverResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"dictionary {r.DictionaryPath}");
        sb.AppendLine($"  candidates tried  {r.CandidatesTried}");
        sb.AppendLine($"  unnamed before    {r.UnnamedBefore}");
        sb.AppendLine($"  resolved          {r.Recovered}");
        foreach (var n in r.Names)
            sb.AppendLine($"  block {n.BlockIndex,5}  {n.SizeBytes,10:N0} B  {n.Name}");
        return sb.ToString().TrimEnd();
    }

    public static string MpqStrip(MpqStripResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"saved {r.SavedTo}");
        sb.AppendLine($"  input entries   {r.InputEntries}");
        sb.AppendLine($"  output entries  {r.OutputEntries}");
        sb.AppendLine($"  output bytes    {r.OutputBytes:N0}");
        sb.AppendLine(r.ListfilePresent ? "  (listfile) is still present" : "  (listfile) dropped");
        sb.AppendLine(r.AttributesPresent ? "  (attributes) is still present" : "  (attributes) dropped");
        sb.Append(r.Reopens
            ? "ok. The output opens again."
            : $"FAILED. The output does not open. {r.ReopenError}");
        return sb.ToString();
    }

    public static string MpqBytesDiff(MpqBytesDiffResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"A  {r.PathA}");
        sb.AppendLine($"B  {r.PathB}");
        sb.AppendLine($"  in both    {r.InBoth}");
        sb.AppendLine($"  identical  {r.IdenticalCount}");
        sb.AppendLine($"  differ     {r.Differ}");

        void Section(string title, IReadOnlyList<string> items)
        {
            sb.AppendLine().AppendLine($"{title} ({items.Count})");
            foreach (var i in items.Take(40)) sb.AppendLine($"  {i}");
            if (items.Count > 40) sb.AppendLine($"  ... and {items.Count - 40} more");
        }
        if (r.Differences.Count > 0)
            Section("payloads that differ",
                r.Differences.Select(d => $"{d.Name}  {d.SizeA:N0} -> {d.SizeB:N0} bytes").ToList());
        if (r.OnlyInA.Count > 0) Section("only in A", r.OnlyInA);
        if (r.OnlyInB.Count > 0) Section("only in B", r.OnlyInB);

        sb.AppendLine();
        sb.Append(r.Identical
            ? "IDENTICAL. Every shared name has the same payload bytes"
            : $"DIFFERS. {r.Differ} changed, {r.OnlyInA.Count} only in A, {r.OnlyInB.Count} only in B");
        return sb.ToString();
    }

    public static string MpqRebuild(MpqRebuildResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"saved {r.SavedTo}");
        sb.AppendLine($"  input entries   {r.InputEntries}");
        sb.AppendLine($"  output entries  {r.OutputEntries}");
        sb.AppendLine($"  dropped         {r.DroppedEntries}");
        sb.AppendLine($"  hash slots      {r.InputHashSlots} -> {r.OutputHashSlots}");
        sb.AppendLine($"  output bytes    {r.OutputBytes:N0}");
        foreach (var d in r.Dropped.Take(40))
            sb.AppendLine($"  dropped  {d.Name}  ({d.Reason})");
        if (r.Dropped.Count > 40) sb.AppendLine($"  ... and {r.Dropped.Count - 40} more");
        sb.Append(r.Reopens
            ? "ok. The rebuilt copy opens again."
            : $"FAILED. The rebuilt copy does not open. {r.ReopenError}");
        return sb.ToString();
    }

    public static string MpqMerge(MpqMergeResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"saved {r.SavedTo}");
        sb.AppendLine($"  A  {r.PathA}  ({r.EntriesA} entries)");
        sb.AppendLine($"  B  {r.PathB}  ({r.EntriesB} entries)");
        sb.AppendLine($"  preferred  {r.Preferred}");
        sb.AppendLine($"  merged     {r.MergedEntries}  (from A {r.FromA}, from B {r.FromB})");
        sb.AppendLine($"  conflicts  {r.Conflicts}");
        foreach (var c in r.ConflictList.Take(40))
            sb.AppendLine($"    {c.Name}  kept {c.KeptFrom} ({c.SizeKept:N0} B) over ({c.SizeOther:N0} B)"
                + (c.Identical ? "  identical" : ""));
        if (r.OnlyInA.Count > 0)
            sb.AppendLine($"  only in A  {string.Join(", ", r.OnlyInA.Take(20))}");
        if (r.OnlyInB.Count > 0)
            sb.AppendLine($"  only in B  {string.Join(", ", r.OnlyInB.Take(20))}");
        sb.Append(r.Reopens
            ? "ok. The merged copy opens again."
            : $"FAILED. The merged copy does not open. {r.ReopenError}");
        return sb.ToString();
    }

    public static string MpqAdd(MpqAddResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"saved {r.SavedTo}");
        sb.AppendLine($"  {r.Name}");
        sb.AppendLine(r.Replaced ? "  replaced an existing entry" : "  added a new entry");
        sb.AppendLine($"  payload         {r.SizeBytes:N0} bytes");
        sb.AppendLine($"  input entries   {r.InputEntries}");
        sb.AppendLine($"  output entries  {r.OutputEntries}");
        sb.AppendLine($"  output bytes    {r.OutputBytes:N0}");
        sb.Append(r.Reopens && r.ByteFaithful
            ? "ok. The payload round-trips byte for byte."
            : r.Reopens
                ? "FAILED. The payload did not read back identical."
                : $"FAILED. The copy does not open. {r.ReopenError}");
        return sb.ToString();
    }

    public static string MpqRemove(MpqRemoveResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"saved {r.SavedTo}");
        sb.AppendLine($"  {r.Name}");
        sb.AppendLine(r.Found ? "  the name was present" : "  the name was not present");
        sb.AppendLine($"  input entries   {r.InputEntries}");
        sb.AppendLine($"  output entries  {r.OutputEntries}");
        sb.AppendLine(r.DroppedOnRebuild
            ? "  the rebuild dropped the name"
            : "  the rebuild did NOT drop the name");
        sb.AppendLine($"  output bytes    {r.OutputBytes:N0}");
        sb.Append(r.Reopens
            ? "ok. The copy opens again."
            : $"FAILED. The copy does not open. {r.ReopenError}");
        return sb.ToString();
    }

    public static string MpqExtractAll(MpqExtractAllReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Path);
        sb.AppendLine($"  output dir     {r.OutputDir}");
        sb.AppendLine($"  entries        {r.TotalEntries}  (named {r.NamedEntries}, unnamed {r.UnnamedEntries})");
        sb.AppendLine($"  bytes          {r.TotalBytes:N0}");
        sb.Append(r.TotalEntries == 0
            ? "nothing to extract."
            : $"ok. Wrote {r.Files.Count} file(s).");
        return sb.ToString();
    }

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

    public static string UnitAbilities(UnitAbilitiesResult r)
    {
        var sb = new StringBuilder();
        if (!r.Found)
        {
            sb.AppendLine($"unit {r.Unit} not found");
            foreach (var d in r.Diagnostics) sb.AppendLine($"  {d}");
            return sb.ToString().TrimEnd('\r', '\n');
        }
        string who = r.PlacedCreationNumber is { } cn ? $"placed unit #{cn}, " : "";
        sb.AppendLine($"{who}{r.Unit} {r.Name}  {r.Abilities.Count} abilit(ies)");
        foreach (var g in r.Abilities.GroupBy(a => a.Source).OrderBy(g => g.Key))
        {
            sb.AppendLine();
            sb.AppendLine($"{UnitAbilitiesCommand.SourceLabel(g.Key)} ({g.Count()})");
            foreach (var a in g) sb.AppendLine($"  {UnitAbilitiesCommand.Describe(a)}");
        }
        foreach (var d in r.Diagnostics) sb.AppendLine($"note: {d}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

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
    public static string TestLoad(TestLoadResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Verdict}  ({r.SecondsElapsed:0.0}s)");
        sb.AppendLine($"  {r.Detail}");
        sb.AppendLine($"  test map: {r.TestMapPath}");
        sb.AppendLine($"  marker:   {r.MarkerPath}");
        if (r.HotAddresses.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Where it was looping (a short list means a tight loop):");
            foreach (var a in r.HotAddresses.Take(8))
                sb.AppendLine($"  {a.Percent,5:0.0}%  {a.Samples,4}x  {a.Module}{a.ModuleOffset}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string HeroLint(HeroLintResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Directory}" + (r.Id is null ? "" : $"  [{r.Id}]"));
        foreach (var c in r.Checks)
        {
            var mark = c.Severity switch
            {
                LintSeverity.Error => "FAIL",
                LintSeverity.Warning => "WARN",
                _ => "ok  ",
            };
            sb.AppendLine($"{mark}  {c.Name,-22} {c.Summary}");
            foreach (var d in c.Detail) sb.AppendLine($"          {d}");
        }
        sb.AppendLine();
        sb.Append(r.Ok
            ? $"DEFINITION OK - {r.Checks.Count} check(s), {r.Warnings} warning(s)"
            : $"DEFINITION INVALID - {r.Errors} error(s), {r.Warnings} warning(s)");
        return sb.ToString();
    }

    public static string HeroInstall(InstallResult r, string? saved)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Ok ? r.Message : "REFUSED: " + r.Message);
        if (r.Ok)
            sb.AppendLine($"  {r.ObjectsCreated} object(s), {r.FieldsApplied} field(s), "
                          + $"{r.AssetsWritten} asset(s) written, {r.AssetsSkippedIdentical} already present");
        if (r.Collisions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Asset collisions (the target already has a DIFFERENT file):");
            foreach (var c in r.Collisions.Take(15)) sb.AppendLine($"  {c}");
        }
        // Before the requirements, because which calls reach the target's own code and which are
        // still empty placeholders is the difference between a working hero and one that loads and
        // then does nothing, and that used to be invisible in this report.
        if (r.ScriptBindings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Calls into classes this definition did not carry:");
            foreach (var b in r.ScriptBindings) sb.AppendLine($"  {b}");
        }
        if (r.UnmetRequirements.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("UNMET requirements - the hero may not be usable until these are handled:");
            foreach (var u in r.UnmetRequirements) sb.AppendLine($"  {u}");
        }
        if (r.RegisteredWith is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Registered with the target's own roster:");
            sb.AppendLine($"  {r.RegisteredWith}");
            // Every assumption, not the first six. The cap was set when there were two of them,
            // and it silently swallowed a stat-convention line that HAD been applied to the map,
            // so the report disagreed with the file it had just written.
            foreach (var a in r.Assumptions) sb.AppendLine($"    assumed: {a}");
        }
        else if (r.NextSteps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Still to wire:");
            foreach (var n in r.NextSteps) sb.AppendLine($"  {n}");
        }
        if (saved is not null) { sb.AppendLine(); sb.Append($"saved: {saved}"); }
        return sb.ToString().TrimEnd();
    }

    public static string HeroExport(HeroExportResult r)
    {
        var d = r.Definition;
        var sb = new StringBuilder();
        sb.AppendLine($"{d.Id}  \"{d.Name}\"  (schema v{d.SchemaVersion}, from {d.SourceMap})");
        sb.AppendLine($"  {d.Objects.Count} object(s), {r.AssetsWritten} asset(s) ({r.AssetBytes:N0} bytes), "
                      + $"{d.Strings.Count} string(s), {d.ScriptEntryPoints.Count} script function(s)");
        sb.AppendLine($"  written to {r.Directory}");
        if (d.Requires.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("The TARGET map must provide:");
            foreach (var q in d.Requires) sb.AppendLine($"  [{q.Kind}] {q.Detail}");
        }
        if (d.ReviewNotes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Review before installing:");
            foreach (var n in d.ReviewNotes.Take(10)) sb.AppendLine($"  - {n}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string IntegrationGap(IntegrationGap g)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"reference {g.ReferenceHero} is wired into {g.ReferenceFunctions} function(s)");
        sb.AppendLine($"candidate {g.CandidateHero} is wired into {g.CandidateFunctions}");
        if (g.Missing.Count == 0)
        {
            sb.Append("\nNothing missing: the candidate appears everywhere the reference does.");
            return sb.ToString();
        }
        var shared = g.Missing.Where(m => !m.HeroSpecificName).ToList();
        var priv = g.Missing.Where(m => m.HeroSpecificName).ToList();

        sb.AppendLine();
        sb.AppendLine($"MISSING, shared systems every hero needs ({shared.Count}):");
        foreach (var m in shared) sb.AppendLine($"  {m.References,3}x  {m.Function}");
        if (priv.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"MISSING, but named for the reference hero ({priv.Count}), so this is that");
            sb.AppendLine("character's own code and must NOT be copied, only mirrored by hand:");
            foreach (var m in priv) sb.AppendLine($"  {m.References,3}x  {m.Function}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string Contract(ContractResult r)
    {
        if (!r.Any) return $"{r.ScriptFile}: no hero integration contract detected.";
        var sb = new StringBuilder();
        sb.AppendLine($"{r.ScriptFile}");
        if (r.Registries.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ROSTER REGISTRATION - a hero absent from these does not exist to the map:");
            foreach (var reg in r.Registries)
            {
                sb.AppendLine($"  {reg.Function}({reg.Signature})");
                sb.AppendLine($"      called {reg.CallCount}x for {reg.RegisteredRawcodes.Count} distinct rawcode(s)");
                sb.AppendLine($"      e.g. {reg.ExampleCall}");
            }
        }
        if (r.SpellDispatchers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("SPELL DISPATCH - an ability id that reaches none of these casts nothing:");
            foreach (var d in r.SpellDispatchers) sb.AppendLine($"  {d}");
        }
        if (r.Templates.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ROSTER TEMPLATE - this map registers a hero with a BLOCK, not one call.");
            sb.AppendLine("Copy one entry and substitute; a single-call roster is just a 1-line template.");
            foreach (var t in r.Templates)
            {
                sb.AppendLine();
                sb.AppendLine($"  {t.ArrayName}: {t.Entries} entries, template at lines {t.StartLine}-{t.EndLine}");
                foreach (var l in t.TemplateLines) sb.AppendLine($"      {l.Trim()}");
            }
        }
        if (r.HeroArrays.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("HERO ARRAYS - a hero missing from these is invisible to the map's systems:");
            foreach (var a in r.HeroArrays)
                sb.AppendLine($"  {a.Type,-8} {a.Name,-32} assigned at {a.AssignmentSites} site(s)");
        }
        if (r.HeroDispatchChains.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("HERO KIT BRANCHES - one hand-written branch per hero. A hero with no");
            sb.AppendLine("branch here is fully selectable, spawns, and has no kit. Largest first,");
            sb.AppendLine("not exhaustive:");
            foreach (var c in r.HeroDispatchChains)
                sb.AppendLine($"  {c.Function} ({c.Branches} hero rawcode branches)");
        }
        if (r.StatConvention is { } sc)
        {
            sb.AppendLine();
            sb.AppendLine("HERO STAT CONVENTION - what this map's own heroes look like. A hero who");
            sb.AppendLine("keeps her home map's numbers can be fully registered and still unplayable.");
            sb.AppendLine($"  measured over {sc.SampleDescription}");
            foreach (var f in sc.Fields) sb.AppendLine($"      {f.Evidence}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string TraceLoad(TraceLoadResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Verdict}  ({r.SecondsElapsed:0.0}s)");
        sb.AppendLine($"  {r.Detail}");
        sb.AppendLine($"  {r.FunctionsEntered} of {r.FunctionsInstrumented} instrumented function(s) "
                      + $"entered, {r.StepsReached} of {r.StepsInstrumented} call site(s) ran");
        if (r.LastReached is not null) sb.AppendLine($"  last event   {r.LastReached}");
        // The unbalanced frame IS the answer when a map hangs, so it leads.
        if (r.HangFunction is not null) sb.AppendLine($"  HANG SITE    {r.HangFunction} (entered, never returned)");
        if (r.OpenFrames.Count > 1)
            sb.AppendLine($"  open frames  {string.Join(" inside ", r.OpenFrames)}");
        if (r.FunctionsSkippedByCap > 0 || r.CallSitesSkippedByCap > 0)
            sb.AppendLine($"  CAPPED       {r.FunctionsSkippedByCap} function(s) and "
                          + $"{r.CallSitesSkippedByCap} call site(s) carry no marker "
                          + $"(caps are {r.FunctionCap} and {r.CallSiteCap})");
        var entered = r.Frames.Where(f => f.Entered).OrderBy(f => f.EnteredAt).TakeLast(8).ToList();
        if (entered.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("last functions entered, in the order they ran");
            foreach (var f in entered)
                sb.AppendLine($"  {(f.Open ? "OPEN" : "ok  ")}  {f.Function}");
        }
        var tail = r.Steps.Where(s2 => s2.Reached).TakeLast(6).ToList();
        if (tail.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("last call sites that ran (source order, not run order)");
            foreach (var s2 in tail) sb.AppendLine($"  {s2.Index,5}  {s2.Label}");
        }
        var missed = r.Steps.Where(s2 => !s2.Reached).Take(4).ToList();
        if (missed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("first call sites that did NOT run");
            foreach (var s2 in missed) sb.AppendLine($"  {s2.Index,5}  {s2.Label}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string Lint(LintResult r)
    {
        var sb = new StringBuilder();
        foreach (var c in r.Checks)
        {
            var mark = c.Severity switch
            {
                LintSeverity.Error => "FAIL",
                LintSeverity.Warning => "WARN",
                _ => "ok  ",
            };
            sb.AppendLine($"{mark}  {c.Name,-22} {c.Summary}");
            foreach (var d in c.Detail)
                sb.AppendLine($"          {d}");
        }
        sb.AppendLine();
        sb.Append(r.Ok
            ? $"LINT OK - {r.Checks.Count} check(s), {r.Warnings} warning(s)"
            : $"LINT FAILED - {r.Errors} error(s), {r.Warnings} warning(s)");
        return sb.ToString();
    }

    public static string GameSchemaWrite(SchemaWriteResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"build {r.Build}  from {r.Source}");
        sb.AppendLine($"  {r.Sections.Count} section(s), {string.Join(", ", r.Sections)}");
        sb.AppendLine($"  {r.ObjectFiles} object file(s), {r.Fields} field(s), "
                      + $"{r.TriggerFunctions} trigger function(s), "
                      + $"{r.ScriptFiles} script file(s), {r.Natives} native(s)");
        if (r.Missing.Count > 0)
            sb.AppendLine($"  {string.Join(", ", r.Missing)} missing");
        sb.AppendLine($"  {r.OutDir}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string GameSchemaDiff(SchemaDiffResult r, string? section = null, int? limit = null)
    {
        var sb = new StringBuilder();
        var filtered = section is null ? r.Changes : r.Changes.Where(c => c.Section == section);
        var filteredNotCompared = section is null ? r.NotCompared
            : r.NotCompared.Where(n => n.Split(' ')[0] == section);

        sb.AppendLine($"{r.BuildA} against {r.BuildB}, {filtered.Count()} change(s)");
        if (filtered.Count() > 0)
        {
            var printed = 0;
            foreach (var c in filtered)
            {
                sb.AppendLine($"  [{c.Section}] {c.Group}  {c.Name} {c.Change}"
                    + (c.Detail is not null ? $"  ({c.Detail})" : ""));
                if (limit is not null)
                {
                    printed++;
                    if (printed >= limit)
                    {
                        var hidden = filtered.Count() - printed;
                        if (hidden > 0)
                            sb.AppendLine($"  ... {hidden} more change(s) hidden by --limit");
                        break;
                    }
                }
            }
        }
        if (filteredNotCompared.Count() > 0)
        {
            sb.AppendLine();
            foreach (var nc in filteredNotCompared)
                sb.AppendLine($"  [not compared] {nc}");
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string GameSchemaIndex(IReadOnlyList<SchemaIndexEntry> entries, string indexPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{entries.Count} schema build(s) from {indexPath}");
        if (entries.Count == 0) return sb.ToString().TrimEnd('\r', '\n');
        sb.AppendLine($"  index: {indexPath}");
        foreach (var e in entries)
        {
            sb.Append($"  {e.Build}  from {e.Source}  ");
            sb.Append($"[{string.Join(", ", e.Sections)}]  ");
            var natives = e.Natives.Count == 0 ? "(none)" : string.Join("; ", e.Natives.Select(kv => $"{kv.Key}:{kv.Value}"));
            var functions = e.Functions.Count == 0 ? "(none)" : string.Join("; ", e.Functions.Select(kv => $"{kv.Key}:{kv.Value}"));
            sb.Append($"objects={e.ObjectFiles}  fields={e.Fields}  triggers={e.TriggerFunctions}  natives={natives}  functions={functions}");
            if (e.Missing.Count > 0) sb.Append($"  missing={string.Join(", ", e.Missing)}");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>Render a TargetCompatCommand result, shared by lint --target and the compat command.</summary>
    public static string CompatResult(CompatResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"compat  {r.TargetBuild}  {r.Map}");
        int errors = 0, warnings = 0;
        foreach (var f in r.Findings)
        {
            if (f.Severity == "error") errors++; else warnings++;
            sb.AppendLine($"  {f.Severity} [{f.Kind}] {f.Name}  {f.Where}  {f.Detail}");
        }
        sb.AppendLine();
        sb.Append($"results  {errors} error(s), {warnings} warning(s)");
        sb.Append($"  identifiers checked  {r.IdentifiersChecked}  fields checked  {r.FieldsChecked}");
        if (r.NotChecked.Count > 0) sb.Append($"  not checked  {string.Join(", ", r.NotChecked)}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>Render the strict validate result, the merged issues plus the strict-only count.</summary>
    public static string ValidateStrict(ValidateStrictResult r)
    {
        var sb = new StringBuilder();
        foreach (var i in r.Issues)
            sb.AppendLine($"{i.Severity} [{i.Category}] {i.FileName} {i.Message}");
        if (r.Issues.Count > 0) sb.AppendLine();
        sb.Append(r.Valid
            ? $"OK, valid ({r.Errors} error(s), {r.Warnings} warning(s), {r.StrictIssues.Count} strict-only)"
            : $"INVALID ({r.Errors} error(s), {r.Warnings} warning(s), {r.StrictIssues.Count} strict-only)");
        foreach (var d in r.Diagnostics) sb.AppendLine($"note {d}");
        return sb.ToString();
    }

    /// <summary>Render the outcome of writing a lint baseline.</summary>
    public static string LintBaselineWrite(LintBaselineWriteResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"wrote baseline, {r.Findings} finding(s)");
        sb.AppendLine($"  {r.OutPath}");
        sb.Append($"map  {r.Map}");
        return sb.ToString();
    }

    /// <summary>Render the lint baseline gate, the new findings first.</summary>
    public static string LintGate(LintGateResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Ok
            ? "baseline OK, no new findings"
            : $"baseline FAILED, {r.Added.Count} finding(s) not in the baseline");
        foreach (var f in r.Added)
        {
            sb.AppendLine($"NEW  {f.Check}  {f.Summary}");
            foreach (var d in f.Detail) sb.AppendLine($"     {d}");
        }
        if (r.Resolved.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{r.Resolved.Count} baseline finding(s) are no longer reported");
            foreach (var f in r.Resolved) sb.AppendLine($"GONE {f.Check}");
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>
    /// Render a CI run, one line per gate followed by the overall verdict and exit code. Shared by
    /// every ci command so a passing gate prints its summary and detail, not just silence.
    /// </summary>
    public static string CiGates(string title, bool passed, int exitCode, IReadOnlyList<CiGateLine> gates)
    {
        var sb = new StringBuilder();
        foreach (var g in gates)
        {
            sb.AppendLine($"{(g.Passed ? "PASS" : "FAIL")}  {g.Name,-16} {g.Summary}");
            foreach (var d in g.Detail)
                sb.AppendLine($"                              {d}");
        }
        sb.AppendLine();
        sb.Append($"{title} {(passed ? "PASS" : "FAIL")}  exit {exitCode}");
        return sb.ToString();
    }

    public static string CiLint(CiLintResult r) => CiGates("ci lint", r.Passed, r.ExitCode, r.Gates);

    public static string CiValidate(CiValidateResult r) => CiGates("ci validate", r.Passed, r.ExitCode, r.Gates);

    public static string CiCompat(CiCompatResult r) => CiGates("ci compat", r.Passed, r.ExitCode, r.Gates);

    public static string CiSchema(CiSchemaResult r) => CiGates("ci schema", r.Passed, r.ExitCode, r.Gates);

    public static string CiAll(CiAllResult r) => CiGates("ci all", r.Passed, r.ExitCode, r.Gates);

    /// <summary>Render the compat matrix as a table, one row per map.</summary>
    public static string CompatMatrix(CompatMatrixResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"compat matrix, {r.Maps.Count} map(s) by {r.Targets.Count} target build(s)");
        int mapWidth = Math.Max(3, r.Maps.Count == 0 ? 3 : r.Maps.Max(m => m.Length));
        sb.Append("map".PadRight(mapWidth));
        foreach (var t in r.Targets) sb.Append($"  {t}");
        sb.AppendLine();
        foreach (var m in r.Maps)
        {
            sb.Append(m.PadRight(mapWidth));
            foreach (var t in r.Targets)
            {
                var cell = r.Cells.First(c => c.Map == m && c.Target == t);
                sb.Append($"  {(cell.Pass ? "pass" : "fail")}({cell.Findings})");
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>Render the explanation of one identifier against one target build.</summary>
    public static string CompatExplain(CompatExplainResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"compat explain  {r.Name}");
        sb.AppendLine($"target build  {r.TargetBuild}");
        if (!r.IsIdentifier)
        {
            sb.Append("no schema declares this name in any kind");
            return sb.ToString();
        }
        sb.AppendLine($"kind  {r.Kind}");
        sb.AppendLine(r.DeclaredInTarget
            ? "declared by the target build"
            : "not declared by the target build");
        if (!r.DeclaredInTarget)
        {
            if (r.AddedIn is not null) sb.AppendLine($"added in  {r.AddedIn}");
            if (r.FirstSeen is not null) sb.AppendLine($"first seen in  {r.FirstSeen}");
            if (r.AddedIn is null && r.FirstSeen is null)
                sb.AppendLine("no newer build declares it either");
        }
        sb.AppendLine($"declared by {r.Builds.Count} build(s)");
        foreach (var b in r.Builds) sb.AppendLine($"  {b}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>Render the audit trend between two maps, grouped by check.</summary>
    public static string AuditTrend(AuditTrendResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"audit trend");
        sb.AppendLine($"  from  {r.MapA}");
        sb.AppendLine($"  to    {r.MapB}");
        sb.AppendLine($"{r.Added} added, {r.Removed} removed");
        foreach (var row in r.Rows)
        {
            sb.AppendLine($"  {row.Check}  +{row.Added} -{row.Removed}");
            foreach (var d in row.AddedDetail) sb.AppendLine($"     + {d}");
            foreach (var d in row.RemovedDetail) sb.AppendLine($"     - {d}");
        }
        foreach (var d in r.Diagnostics) sb.AppendLine($"note  {d}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string GamedataHistory(GamedataHistoryResult r)
    {
        var sb = new StringBuilder();
        if (!r.Found)
        {
            sb.AppendLine($"{r.Name}  not found in any of {r.BuildsSearched} build(s)");
            return sb.ToString().TrimEnd('\r', '\n');
        }
        sb.AppendLine($"{r.Name}  found in {r.FirstBuild}..{r.LastBuild}, {r.BuildsSearched} build(s) searched");
        foreach (var kind in r.Kinds)
            sb.AppendLine($"  {kind.Kind,-9} {kind.Builds.Count,-3} {string.Join(", ", kind.Builds)}");
        if (r.ObjectFiles.Count > 0)
            sb.AppendLine($"  object files  {string.Join(", ", r.ObjectFiles)}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string GamedataChangelog(GamedataChangelogResult r)
    {
        var sb = new StringBuilder();
        int total = r.Added + r.Removed + r.Retyped + r.BoundsChanged;
        sb.AppendLine($"{r.BuildA} against {r.BuildB}, {total} change(s)");
        foreach (var group in r.Groups)
        {
            sb.AppendLine($"  {group.Slk}");
            foreach (var c in group.Changes)
                sb.AppendLine($"    {c.Code,-28} {c.Change,-8}"
                    + (c.Detail is not null ? $"  ({c.Detail})" : ""));
        }
        if (r.NotCompared.Count > 0)
        {
            sb.AppendLine();
            foreach (var nc in r.NotCompared)
                sb.AppendLine($"  [not compared] {nc}");
        }
        sb.AppendLine();
        sb.Append($"  added {r.Added}, removed {r.Removed}, retyped {r.Retyped}, bounds {r.BoundsChanged}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string GamedataFieldLookup(GamedataFieldLookupResult r)
    {
        var sb = new StringBuilder();
        var builds = r.Matches.Select(m => m.Build).Distinct(StringComparer.Ordinal).Count();
        sb.AppendLine($"query '{r.Query}', {r.Matches.Count} match(es) in {builds} build(s)"
            + (r.Target is not null ? $", target {r.Target}" : ""));
        foreach (var m in r.Matches)
            sb.AppendLine($"  {m.Build,-24} {m.Code,-24} {m.DisplayName,-28} {m.Type,-10} {m.Slk}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string GamedataBounds(GamedataBoundsResult r)
    {
        var sb = new StringBuilder();
        if (r.Entries.Count == 0)
        {
            sb.AppendLine($"code {r.Code}  not found in any build"
                + (r.Target is not null ? $" (searched {r.Target})" : ""));
            return sb.ToString().TrimEnd('\r', '\n');
        }
        sb.AppendLine($"code {r.Code}, {r.Entries.Count} entry(ies)");
        foreach (var e in r.Entries)
            sb.AppendLine($"  {e.Build,-24} {e.Slk,-32} {e.Type,-10}"
                + $" min {Show(e.MinValue)}  max {Show(e.MaxValue)}  canBeEmpty {e.CanBeEmpty}");
        return sb.ToString().TrimEnd('\r', '\n');
        static string Show(string s) => string.IsNullOrEmpty(s) ? "(none)" : s;
    }

    public static string GamedataEnum(GamedataEnumResult r)
    {
        var sb = new StringBuilder();
        if (r.Entries.Count == 0)
        {
            sb.AppendLine($"type {r.Type}  not found in any build"
                + (r.Target is not null ? $" (searched {r.Target})" : ""));
            return sb.ToString().TrimEnd('\r', '\n');
        }
        sb.AppendLine($"type {r.Type}, {r.Entries.Count} build(s)");
        foreach (var e in r.Entries)
        {
            sb.AppendLine($"  {e.Build}  {e.Values.Count} value(s)");
            foreach (var v in e.Values)
                sb.AppendLine($"    {v.Value,-24} {v.DisplayName ?? v.Value}");
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string GamedataEnumTypes(IReadOnlyList<string> types)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{types.Count} enumerated field type(s)");
        foreach (var t in types) sb.AppendLine($"  {t}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string GamedataCoverage(GamedataCoverageResult r)    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Rows.Count} schema build(s)");
        foreach (var row in r.Rows)
            sb.AppendLine($"  {row.Build,-24} {row.Era,-9} {string.Join(", ", row.Sections),-28}"
                + $" fields {row.Fields}  natives {row.Natives}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    // ---- bench ----------------------------------------------------------

    public static string BenchSchemaCoverage(SchemaCoverageResult r)
    {
        var t = r.Totals;
        var sb = new StringBuilder();
        sb.AppendLine($"{t.Schemas} schema folder(s), {t.Real} real, {t.EraDerived} era-derived, "
            + $"{t.Absent} absent, {t.Fields} object field(s)");
        sb.AppendLine();
        sb.AppendLine($"  {"build",-34} {"era",-10} {"object data",-12} {"fields",6}");
        foreach (var row in r.Rows)
            sb.AppendLine($"  {Short(row.Build, 34),-34} {Short(row.Era, 10),-10} {row.ObjectData,-12} {row.Fields,6}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string BenchCompat(BenchCompatResult r)
    {
        var a = r.Aggregate;
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Maps.Count} map(s) x {r.Targets.Count} target(s), {a.Cells} cell(s), "
            + $"{a.Passed} pass, {a.Failed} fail, {a.Errors} error(s), {a.Warnings} warning(s), {a.Findings} finding(s)");
        sb.AppendLine();
        sb.AppendLine($"  {"map",-28} {"target",-28} {"result",7} {"err",5} {"warn",5} {"find",5}");
        foreach (var c in r.Cells)
            sb.AppendLine($"  {Short(c.Map, 28),-28} {Short(c.Target, 28),-28} {(c.Pass ? "pass" : "FAIL"),7} "
                + $"{c.Errors,5} {c.Warnings,5} {c.Findings,5}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string BenchLeaks(BenchLeaksResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Maps.Count} map(s), {r.TotalLeaks} leak(s), {r.Hot} in hot code, {r.Repeat} in repeated code");
        sb.AppendLine();
        sb.AppendLine($"  {"map",-36} {"leaks",6} {"hot",5} {"repeat",7}");
        foreach (var m in r.Maps)
            sb.AppendLine($"  {Short(m.Map, 36),-36} {m.Leaks,6} {m.Hot,5} {m.Repeat,7}"
                + (m.Ok ? "" : "  unreadable"));
        if (r.Kinds.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  {"leak kind",-36} {"count",6}");
            foreach (var k in r.Kinds)
                sb.AppendLine($"  {k.Rule,-36} {k.Count,6}");
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string BenchOpen(BenchOpenResult r)
    {
        var sb = new StringBuilder();
        string slowest = r.Slowest is null ? "none" : $"{r.Slowest.Map} at {r.Slowest.Milliseconds} ms";
        sb.AppendLine($"{r.Maps.Count} map(s), slowest {slowest}, median {r.MedianMs:0.##} ms, "
            + $"{r.TotalBytes} byte(s) across entries");
        sb.AppendLine();
        sb.AppendLine($"  {"map",-36} {"ms",8} {"entries",8} {"bytes",14}");
        foreach (var m in r.Maps)
            sb.AppendLine($"  {Short(m.Map, 36),-36} {m.Milliseconds,8} {m.Entries,8} {m.Bytes,14}");
        foreach (var f in r.Failures)
            sb.AppendLine($"  unreadable {f}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string LibraryScan(LibraryScanResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Maps.Count} map(s), {r.Loaded} loaded, {r.Failed} failed, {r.TotalBytes} byte(s)");
        sb.AppendLine();
        sb.AppendLine($"  {"map",-36} {"bytes",12} {"build",-16} {"load",4} {"entries",8} {"diag",5}");
        foreach (var m in r.Maps)
            sb.AppendLine($"  {Short(m.Map, 36),-36} {m.Bytes,12} {Short(m.TargetBuild ?? "unknown", 16),-16} "
                + $"{(m.Loads ? "yes" : "no"),4} {m.Entries,8} {m.Diagnostics,5}"
                + (m.Error is null ? "" : "  " + m.Error));
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string LibraryAudit(LibraryAuditResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Maps.Count} map(s), {r.TotalFindings} finding(s), {r.MapsWithFindings} map(s) with findings");
        sb.AppendLine();
        sb.AppendLine($"  {"map",-36} {"errors",6} {"warnings",8} {"findings",8}");
        foreach (var m in r.Maps)
            sb.AppendLine($"  {Short(m.Map, 36),-36} {m.Errors,6} {m.Warnings,8} {m.Issues.Count,8}"
                + (m.Error is null ? "" : "  " + m.Error));
        if (r.Checks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  {"check",-28} {"count",6}");
            foreach (var c in r.Checks)
                sb.AppendLine($"  {c.Check,-28} {c.Count,6}");
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string LibraryValidate(LibraryValidateResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Maps.Count} map(s), {r.Valid} valid, {r.Invalid} invalid");
        foreach (var m in r.Maps.Where(m => !m.Valid))
        {
            sb.AppendLine();
            sb.AppendLine($"  {Short(m.Map, 36),-36} {m.Errors} error(s)"
                + (m.Error is null ? "" : "  " + m.Error));
            foreach (var i in m.Issues.Where(i => i.Severity == DiagnosticSeverity.Error))
                sb.AppendLine($"     ({i.Category}) {i.Message}");
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string LibraryCompare(LibraryCompareResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"only in A {r.OnlyInA.Count}, only in B {r.OnlyInB.Count}, "
            + $"changed {r.Changed.Count}, same {r.Same}, ambiguous {r.AmbiguousNames.Count}");
        AppendNames(sb, "only in A", r.OnlyInA);
        AppendNames(sb, "only in B", r.OnlyInB);
        AppendNames(sb, "changed", r.Changed);
        AppendNames(sb, "ambiguous name", r.AmbiguousNames);
        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static void AppendNames(StringBuilder sb, string label, IReadOnlyList<string> names)
    {
        if (names.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine("  " + label);
        foreach (var n in names) sb.AppendLine("    " + n);
    }

    public static string LibraryReport(LibraryReportResult r) =>
        $"wrote {r.Path}\n{r.Maps} map(s), {r.Loaded} loaded, {r.Failed} failed, "
        + $"{r.Findings} finding(s), {r.Invalid} invalid";

    public static string BenchPerfGate(PerfGateResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"perf gate {(r.Pass ? "PASS" : "FAIL")} at {r.MaxRegressionPct:0.##} percent, "
            + $"{r.MetricsCompared} metric(s) compared, {r.Regressed} regressed");
        if (r.MetricsCompared > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  {"metric",-28} {"baseline",-14} {"run",-14} {"change",14}");
            foreach (var m in r.Metrics)
            {
                string change = m.ChangePct is null ? "n/a" : $"{m.ChangePct.Value:+0.00;-0.00} percent";
                sb.AppendLine($"  {Short(m.Metric, 28),-28} {m.Baseline,-14:0.##} {m.Run,-14:0.##} {change,14}"
                    + (m.Regressed ? "  REGRESSED" : ""));
            }
        }
        foreach (var name in r.MissingInRun)
            sb.AppendLine($"  missing in run {name}");
        foreach (var name in r.AddedInRun)
            sb.AppendLine($"  new in run {name}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string GamedataSources(GamedataSourcesResult r, string outPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Sources.Count} schema build(s)");
        foreach (var s in r.Sources)
            sb.AppendLine($"  {s.Build,-24} {s.Era,-9} {string.Join(", ", s.Sections),-28}"
                + $" sectionFile(s) {s.SectionHashes.Count}  input(s) {s.Inputs.Count}");
        sb.AppendLine();
        sb.Append($"wrote {outPath}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string GamedataExportSchemas(GamedataExportResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Builds.Count} schema build(s), {r.Files} file(s), {r.Bytes} byte(s)");
        sb.AppendLine($"  {(r.Zipped ? "zip" : "dir")} {r.Target}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string ReleaseVersion(ReleaseVersion r) =>
        $"wc3ctl {r.ProductVersion}\n"
        + $"git {r.GitHash}, built {r.BuildTimeUtc}\n"
        + $"mcp server {r.McpServerName} {r.McpVersion}";

    public static string Changelog(ChangelogResult r, string outPath) =>
        r.Ok
            ? $"wrote {outPath}, {r.Groups.Count} group(s), {r.Groups.Sum(g => g.Entries.Count)} commit(s)"
            : r.Message ?? "changelog failed";

    public static string McpExportVerify(McpExportVerifyResult r)
    {
        if (r.Message is { Length: > 0 }) return r.Message;
        if (r.InStep) return $"in step, {r.CheckedFiles} file(s) checked";
        var sb = new StringBuilder();
        sb.AppendLine($"out of step, {r.Differences.Count} difference(s), {r.CheckedFiles} file(s) checked");
        foreach (var d in r.Differences)
            sb.AppendLine($"  {d.Status,-8}{d.Path}");
        return sb.ToString().TrimEnd('\r', '\n');
    }
}
