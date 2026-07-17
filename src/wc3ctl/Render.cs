// src/wc3ctl/Render.cs
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wc3.Commands;

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

    public static string Extract(ExtractManifest m, string dest) =>
        $"Extracted {m.Count} file(s) ({m.TotalBytes:N0} bytes) to {dest}";
}
