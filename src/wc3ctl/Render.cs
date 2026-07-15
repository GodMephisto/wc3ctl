// src/wc3ctl/Render.cs
using System.Text.Json;
using Wc3.Commands;

namespace Wc3Ctl;

public static class Render
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string AsJson(object o) => JsonSerializer.Serialize(o, Json);

    public static string List(FileListResult r) =>
        string.Join("\n", r.Files.Select(f =>
            $"{f.Name ?? "(unnamed)",-28} {f.SizeBytes,10}  {(f.Known ? "known" : "unknown")}{(f.Parsed ? "/parsed" : "")}"));

    public static string Info(MapInfoResult r) =>
        $"Name:    {r.Name}\nAuthor:  {r.Author}\nPlayers: {r.Players}\nSize:    {r.Width}x{r.Height}"
        + (r.Diagnostics.Count == 0 ? "" : "\n\nDiagnostics:\n" + string.Join("\n", r.Diagnostics));

    public static string Roundtrip(RoundtripResult r)
    {
        var verdict = r.Faithful ? "OK — round-trip is byte-faithful for all files."
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

    public static string Extract(ExtractManifest m, string dest) =>
        $"Extracted {m.Count} file(s) ({m.TotalBytes:N0} bytes) to {dest}";
}
