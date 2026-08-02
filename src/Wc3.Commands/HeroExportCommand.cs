// src/Wc3.Commands/HeroExportCommand.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wc3.Model;

namespace Wc3.Commands;

public sealed record HeroExportResult(
    string Directory,
    HeroDefinition Definition,
    int AssetsWritten,
    long AssetBytes,
    int ObjectsExcluded,
    int FilesExcluded);

/// <summary>
/// Exports a hero out of a map as a reviewable <see cref="HeroDefinition"/> plus its assets,
/// which is the <c>import</c> verb of the format.
///
/// The critical decision here is WHAT NOT TO CARRY. The porter's failure was pulling in whatever
/// a script closure could reach - on one hero that was 61 extra objects and thousands of foreign
/// functions belonging to other heroes - and then trimming the calls it could not resolve, which
/// is what produced uninitialized locals and non-terminating loops. So this exports the hero's
/// REAL dependencies (edges through actual object fields) and REPORTS the closure carry as review
/// notes instead of silently including it. A human decides.
///
/// Being incomplete is acceptable and expected. A definition is reviewed before it is installed,
/// so "found the kit, flagged 61 maybes" is a good result. That was never true of a porter whose
/// output went straight into a map someone then hosted.
/// </summary>
public static class HeroExportCommand
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };



    /// <summary>
    /// Declarations from the source's <c>globals</c> block that the carried functions actually
    /// reference, returned verbatim so a type or initialiser is never re-derived. Only what is used
    /// is carried: copying the whole block would drag in thousands of unrelated declarations, which
    /// is the same over-carry mistake in a different place.
    /// </summary>
    private static List<string> GlobalsUsedBy(string[] lines, HashSet<string> functions,
        Dictionary<string, (int start, int end)> spans)
    {
        int blockStart = -1, blockEnd = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (blockStart < 0 && t == "globals") { blockStart = i; continue; }
            if (blockStart >= 0 && t == "endglobals") { blockEnd = i; break; }
        }
        if (blockStart < 0 || blockEnd < 0) return new List<string>();

        // name -> its declaration line
        var declOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var decl = new System.Text.RegularExpressions.Regex(
            @"^\s*(?:constant\s+)?[A-Za-z_][A-Za-z0-9_]*\s+(?:array\s+)?([A-Za-z_][A-Za-z0-9_]*)");
        for (int i = blockStart + 1; i < blockEnd; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var m = decl.Match(line);
            if (m.Success && !declOf.ContainsKey(m.Groups[1].Value))
                declOf[m.Groups[1].Value] = line;
        }

        var word = new System.Text.RegularExpressions.Regex(@"[A-Za-z_][A-Za-z0-9_]*");
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fn in functions)
        {
            if (!spans.TryGetValue(fn, out var span)) continue;
            for (int i = span.start; i <= span.end; i++)
                foreach (System.Text.RegularExpressions.Match m in word.Matches(lines[i]))
                    if (declOf.ContainsKey(m.Value)) used.Add(m.Value);
        }

        // Preserve the source's declaration order: a constant may initialise from an earlier one.
        return declOf.Where(kv => used.Contains(kv.Key)).Select(kv => kv.Value)
            .OrderBy(line => Array.IndexOf(lines, line)).ToList();
    }

    /// <summary>Every function declaration in the script, mapped to its line span.</summary>
    private static Dictionary<string, (int start, int end)> IndexFunctions(string[] lines)
    {
        var spans = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        var decl = new System.Text.RegularExpressions.Regex(@"^function\s+([A-Za-z_][A-Za-z0-9_]*)");
        string? open = null; int openAt = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (open is null)
            {
                var m = decl.Match(lines[i]);
                if (m.Success) { open = m.Groups[1].Value; openAt = i; }
            }
            else if (lines[i].StartsWith("endfunction", StringComparison.Ordinal))
            {
                spans[open] = (openAt, i);
                open = null;
            }
        }
        return spans;
    }

    /// <summary>
    /// Transitive callees of the seed set. Matches a name followed by '(' as well as
    /// 'function X': JASS uses the <c>call</c> keyword ONLY for statement-level calls, so a call
    /// inside an expression like <c>if IsValid(u) then</c> carries no keyword. Keying on
    /// <c>call</c> alone misses most references, which is exactly how an earlier pass lost live
    /// code. Over-matching costs a few extra functions; under-matching breaks the script.
    /// </summary>
    private static HashSet<string> Closure(
        Dictionary<string, (int start, int end)> spans, IEnumerable<string> seed)
    {
        var reference = new System.Text.RegularExpressions.Regex(
            @"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(|\bfunction\s+([A-Za-z_][A-Za-z0-9_]*)");
        var keep = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(seed.Where(spans.ContainsKey));
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!keep.Add(name)) continue;
            var (start, end) = spans[name];
            for (int i = start; i <= end; i++)
                foreach (System.Text.RegularExpressions.Match m in reference.Matches(lineOf(i)))
                {
                    var callee = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    if (spans.ContainsKey(callee) && !keep.Contains(callee)) queue.Enqueue(callee);
                }
        }
        return keep;

        string lineOf(int i) => _scriptLines is null || i >= _scriptLines.Length ? "" : _scriptLines[i];
    }

    [ThreadStatic] private static string[]? _scriptLines;

    public static HeroExportResult Run(MapDocument doc, string rawcode, string sourceMapPath,
        string outputDirectory, string? gameDir = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        Directory.CreateDirectory(outputDirectory);
        var assetDir = Path.Combine(outputDirectory, "assets");

        var bundle = BundleCommand.ResolveUnit(doc, rawcode, gameDir);

        // The hero's own dependency set, not everything a script could reach.
        var realObjects = BundleStructure.RealObjects(bundle);
        var realFiles = BundleStructure.RealFiles(bundle);

        // Carry each object's MAP-LEVEL fields, so the definition stands alone. Base-game values
        // are deliberately excluded: they belong to the engine, not the hero, and baking them in
        // would make a definition wrong the moment a patch changes a default.
        var objects = new List<DefinitionObject>();
        foreach (var o in bundle.Objects.Where(o => realObjects.Contains(o.Rawcode)))
        {
            var merged = ObjectGetCommand.Execute(doc, o.Kind, o.Rawcode, gameDir);
            var fields = merged.Found
                ? merged.Fields.Where(f => f.Source == "map")
                    .Select(f => new DefinitionField(f.Code, f.Value)).ToList()
                : new List<DefinitionField>();
            objects.Add(new DefinitionObject(o.Rawcode, o.Kind.ToString().ToLowerInvariant(),
                o.Name, merged.BaseRawcode, o.CustomToMap,
                o.Rawcode == bundle.RootRawcode ? "root" : "dependency", fields));
        }

        var assets = new List<DefinitionAsset>();
        long bytes = 0;
        foreach (var f in bundle.Files.Where(f => realFiles.Contains(f.Path)))
        {
            var entry = doc.GetFile(f.Path);
            if (entry is null) continue;
            var payload = entry.OverrideBytes ?? entry.RawBytes;
            var dest = Path.Combine(assetDir, f.Path.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, payload);
            assets.Add(new DefinitionAsset(f.Path, f.Category, payload.Length,
                Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()));
            bytes += payload.Length;
        }

        string? scriptFile = null;
        var entryPoints = new List<string>();
        var globals = new List<string>();
        if (bundle.Functions.Count > 0 && doc.GetFile("war3map.j") is { } js)
        {
            var text = Encoding.UTF8.GetString(js.OverrideBytes ?? js.RawBytes);
            var lines = text.Split('\n');

            // Carry the hero's functions AND everything they call, transitively. Carrying only the
            // seed set produced 50 "undeclared function" errors: the opposite failure to the
            // porter's over-carry, but just as fatal, since a script that does not compile means a
            // hosted map has no player slots. Over-including here is cheap; under-including is not.
            _scriptLines = lines;
            var spans = IndexFunctions(lines);
            var wanted = Closure(spans, bundle.Functions.Select(f => f.Name));
            var reason = bundle.Functions.ToDictionary(f => f.Name, f => f.Reason, StringComparer.Ordinal);

            var sb = new StringBuilder();
            foreach (var name in wanted.OrderBy(n => spans[n].start))
            {
                var (start, end) = spans[name];
                sb.AppendLine($"// {name}  ({(reason.TryGetValue(name, out var r) ? r : "called by a carried function")})");
                for (int i = start; i <= end; i++) sb.AppendLine(lines[i].TrimEnd('\r'));
                sb.AppendLine();
                entryPoints.Add(name);
            }
            // Globals the carried code reads. A function without its globals is as broken as a
            // function without its callees, and fails the same way: the script will not compile.
            globals.AddRange(GlobalsUsedBy(lines, wanted, spans));

            scriptFile = "script.j";
            File.WriteAllText(Path.Combine(outputDirectory, scriptFile), sb.ToString(), new UTF8Encoding(false));
        }

        // What a target must already provide. Stated so an install can refuse rather than produce
        // a map that silently lacks the hero.
        var requires = new List<DefinitionRequirement>
        {
            new("roster-registration",
                "the target must register this rawcode with its own hero roster; find the call with "
                + "'wc3ctl contract <target>' (GGGA's is RPB_AddHero(rawcode, role, portrait))",
                Satisfiable: false),
            new("spell-dispatch",
                "the target must route this hero's ability ids to its cast handlers, or the abilities "
                + "will exist but do nothing",
                Satisfiable: false),
        };

        int excludedObjects = bundle.Objects.Count - objects.Count;
        int excludedFiles = bundle.Files.Count - assets.Count;
        var notes = new List<string>();
        if (excludedObjects > 0)
            notes.Add($"{excludedObjects} object(s) reachable only through the script closure were "
                      + "EXCLUDED; they usually belong to other heroes. Review and add any that are "
                      + "genuinely this hero's.");
        if (excludedFiles > 0)
            notes.Add($"{excludedFiles} file(s) reachable only through the script closure were EXCLUDED.");
        foreach (var d in bundle.Diagnostics) notes.Add(d);

        var def = new HeroDefinition(HeroDefinition.CurrentSchemaVersion, bundle.RootRawcode,
            bundle.RootName, Path.GetFileName(sourceMapPath), "wc3ctl hero export",
            objects, assets, bundle.Strings, scriptFile, entryPoints, globals, requires, notes);

        File.WriteAllText(Path.Combine(outputDirectory, "hero.json"),
            JsonSerializer.Serialize(def, Json), new UTF8Encoding(false));

        return new HeroExportResult(outputDirectory, def, assets.Count, bytes,
            excludedObjects, excludedFiles);
    }
}
