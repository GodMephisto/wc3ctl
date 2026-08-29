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

    public static string List(FileListResult r) =>
        string.Join("\n", r.Files.Select(f =>
            $"{f.Name ?? "(unnamed)",-28} {f.SizeBytes,10}  {(f.Known ? "known" : "unknown")}{(f.Parsed ? "/parsed" : "")}"
            + (f.ContentType is null ? "" : $"  {f.ContentType}")
            + (f.NameFromHarvest ? "  (name from harvest)" : "")));

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
}
