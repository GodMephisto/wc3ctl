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
    /// A prefix for every carried function and global, so nothing the definition brings can clash
    /// with a name the target already uses. The prefix is derived from the hero's rawcode, which is
    /// unique per definition and stable across installs.
    /// </summary>
    private static Dictionary<string, string> BuildNamespace(
        string rootRawcode, IEnumerable<string> functions, IEnumerable<string> globalLines)
    {
        var prefix = "h" + new string(rootRawcode.Where(char.IsLetterOrDigit).ToArray()) + "__";
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fn in functions) map[fn] = prefix + fn;

        var decl = new System.Text.RegularExpressions.Regex(
            @"^\s*(?:constant\s+)?[A-Za-z_][A-Za-z0-9_]*\s+(?:array\s+)?([A-Za-z_][A-Za-z0-9_]*)");
        foreach (var line in globalLines)
        {
            var m = decl.Match(line);
            if (m.Success) map[m.Groups[1].Value] = prefix + m.Groups[1].Value;
        }
        // 'main' and 'config' are the engine's, never ours to rename.
        map.Remove("main");
        map.Remove("config");
        return map;
    }

    /// <summary>
    /// Rewrites carried symbol names, including inside string literals, because a map can dispatch
    /// by name (<c>ExecuteFunc("Foo")</c>) and renaming the declaration without the literal would
    /// break that call at runtime with no compile error to warn about it.
    /// </summary>
    private static string ApplyNamespace(string text, Dictionary<string, string> rename)
    {
        if (rename.Count == 0) return text;
        // Longest first, so a name that is a prefix of another cannot corrupt it.
        var pattern = string.Join("|", rename.Keys
            .OrderByDescending(k => k.Length)
            .Select(System.Text.RegularExpressions.Regex.Escape));
        // A match preceded by a backslash is part of an escape sequence, not an identifier.
        // Without this guard a carried global named 'n' turned every "\\n" in the script into
        // "\\hH0DA__n", which pjass rejects as an invalid escape sequence. A word boundary is
        // not enough here because the backslash IS a word boundary.
        return System.Text.RegularExpressions.Regex.Replace(
            text, @"(?<!\\)\b(?:" + pattern + @")\b",
            m => rename.TryGetValue(m.Value, out var to) ? to : m.Value);
    }

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

        // A carried global's INITIALISER can name another global, so closing only over function
        // bodies leaves those dangling: 'integer A= B' carried without B fails with "Undeclared
        // variable B". Iterate until the set stops growing.
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var name in used.ToList())
                foreach (System.Text.RegularExpressions.Match m in word.Matches(declOf[name]))
                    if (m.Value != name && declOf.ContainsKey(m.Value) && used.Add(m.Value))
                        grew = true;
        }

        // Preserve the source's declaration order: a constant may initialise from an earlier one.
        return declOf.Where(kv => used.Contains(kv.Key)).Select(kv => kv.Value)
            .OrderBy(line => Array.IndexOf(lines, line)).ToList();
    }

    /// <summary>
    /// Every function declaration mapped to its line span, via the shared
    /// <see cref="JassFunctionIndex"/> rather than a local regex.
    /// </summary>
    /// <remarks>
    /// A hand-rolled matcher here required <c>function</c> at column zero, so an INDENTED
    /// declaration was invisible to it. Those functions never entered the closure and the emitted
    /// script failed with "Undeclared function MakeSound". <c>JassFunctionIndex</c> exists in this
    /// repo precisely because throwaway scans miss that (it also handles <c>constant function</c>,
    /// trailing line comments, and vJASS <c>function interface</c>), so the fix is to use it rather
    /// than to keep a second, weaker parser alongside it.
    /// </remarks>
    private static Dictionary<string, (int start, int end)> IndexFunctions(string[] lines)
    {
        var spans = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        foreach (var fn in JassFunctionIndex.Parse(string.Join("\n", lines)))
            spans[fn.Name] = (fn.StartLine - 1, fn.EndLine - 1);   // Parse reports 1-based lines
        return spans;
    }

    /// <summary>
    /// Transitive callees of the seed set. Matches a name followed by '(' as well as
    /// 'function X': JASS uses the <c>call</c> keyword ONLY for statement-level calls, so a call
    /// inside an expression like <c>if IsValid(u) then</c> carries no keyword. Keying on
    /// <c>call</c> alone misses most references, which is exactly how an earlier pass lost live
    /// code. Over-matching costs a few extra functions; under-matching breaks the script.
    /// </summary>
    /// <summary>
    /// A class that is a SIBLING of the exported unit rather than something it depends on. The
    /// convention in these maps is one class per character, so a name ending in "Spells" is
    /// another character's kit; reaching it means the walk passed through the shared dispatcher,
    /// which is the thing to declare instead (see the roster-registration requirement).
    /// </summary>
    private static bool IsPeerClass(string wurstClass) =>
        wurstClass.EndsWith("Spells", StringComparison.Ordinal)
        || wurstClass.EndsWith("Debuff", StringComparison.Ordinal);

    /// <summary>Wurst emits s__Class_method / si__Class_field / dispatch_Class_method.</summary>
    private static readonly System.Text.RegularExpressions.Regex WurstSymbol =
        new(@"^(?:s__|si__|sc__|dispatch_|init_)([A-Za-z0-9]+?)_", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Transitive callees of the seed set, STOPPING at the boundary of a Wurst class the hero does
    /// not belong to.
    /// </summary>
    /// <remarks>
    /// Unbounded call-following carried 1,138 functions for a 10-ability hero, of which only 30
    /// were hers. The rest were the source map's item shop and the Wurst standard library
    /// (ShopUI, ShopAppearance, ItemInShop, LinkedList, HashList, Table...), reached because one
    /// utility call leads into the generated class dispatcher and from there to everything the map
    /// ever compiled. That volume is what a target cannot absorb.
    ///
    /// Wurst's output is namespaced, so the seam is in the naming rather than in the call graph:
    /// the hero's own class is discoverable from the seeds, and any OTHER class is a separate
    /// concern to be declared as a requirement instead of swallowed. Hand-written JASS has no such
    /// marker and is still followed normally.
    ///
    /// Matching a name followed by '(' as well as 'function X' is deliberate: JASS uses the
    /// <c>call</c> keyword ONLY for statement-level calls, so a call inside an expression carries
    /// no keyword, and keying on <c>call</c> alone loses most references.
    /// </remarks>
    private static HashSet<string> Closure(
        Dictionary<string, (int start, int end)> spans, IEnumerable<string> seed,
        SortedSet<string> excludedClasses, out bool sawPeer)
    {
        sawPeer = false;
        var seedList = seed.Where(spans.ContainsKey).ToList();

        // Classes the hero's own functions belong to; everything else is somebody else's system.
        var ownClasses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in seedList)
        {
            var m = WurstSymbol.Match(name);
            if (m.Success) ownClasses.Add(m.Groups[1].Value);
        }

        var reference = new System.Text.RegularExpressions.Regex(
            @"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(|\bfunction\s+([A-Za-z_][A-Za-z0-9_]*)");
        var keep = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(seedList);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!keep.Add(name)) continue;
            var (from, to) = spans[name];
            for (int i = from; i <= to; i++)
                foreach (System.Text.RegularExpressions.Match m in reference.Matches(_scriptLines![i]))
                {
                    var callee = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    if (!spans.ContainsKey(callee) || keep.Contains(callee)) continue;

                    var wm = WurstSymbol.Match(callee);
                    if (wm.Success && !ownClasses.Contains(wm.Groups[1].Value))
                    {
                        var cls = wm.Groups[1].Value;
                        // A PEER, not a dependency. 20 of 28 excluded classes were other heroes'
                        // spell classes (AlucardSpells, AsNodtSpells, ...), and Nanaya does not
                        // call Alucard's spells: all hero classes route through one shared Wurst
                        // dispatcher, so reaching it makes every hero look like a neighbour. One
                        // edge, hit 20 times. A peer is neither carried NOR declared - listing it
                        // as a requirement would ask a target to supply unrelated characters.
                        if (IsPeerClass(cls)) sawPeer = true; else excludedClasses.Add(cls);
                        continue;
                    }
                    queue.Enqueue(callee);
                }
        }
        return keep;
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
        var excludedClassNames = new SortedSet<string>(StringComparer.Ordinal);
        bool sharedDispatcherSeen = false;
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
            var wanted = Closure(spans, bundle.Functions.Select(f => f.Name), excludedClassNames,
                out sharedDispatcherSeen);
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
            var rawGlobals = GlobalsUsedBy(lines, wanted, spans);

            // Namespace every carried symbol. Rawcodes are remapped on install and assets are
            // hash-checked, but script symbols had no collision handling, which produced
            // "Symbol s already defined as global variable" the moment globals started being
            // carried: the source declares 's', the target has a local 's'. Prefixing per hero
            // means a carried symbol cannot collide on ANY target, which is the same fix already
            // applied to the other two namespaces.
            var rename = BuildNamespace(bundle.RootRawcode, wanted, rawGlobals);
            var body = ApplyNamespace(sb.ToString(), rename);
            globals.AddRange(rawGlobals.Select(g => ApplyNamespace(g, rename)));
            entryPoints = entryPoints.Select(n => rename.TryGetValue(n, out var r) ? r : n).ToList();

            scriptFile = "script.j";
            File.WriteAllText(Path.Combine(outputDirectory, scriptFile), body, new UTF8Encoding(false));
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
        // Wurst classes the closure deliberately stopped at. These are the source map's own
        // systems and standard library, not the hero's, so the target must already provide them.
        // Swallowing them instead is what produced 1,138 functions for a 30-function hero.
        foreach (var cls in excludedClassNames)
            requires.Add(new DefinitionRequirement("wurst-class",
                $"the target must provide the Wurst class '{cls}', which this hero's code calls into",
                Satisfiable: false));
        if (sharedDispatcherSeen)
            requires.Add(new DefinitionRequirement("shared-spell-dispatcher",
                "this hero's spells route through the source map's shared dispatcher, which every "
                + "character there shares. The target must provide an equivalent entry point, or the "
                + "spells will not fire. Peer characters' classes were deliberately NOT carried.",
                Satisfiable: false));

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
