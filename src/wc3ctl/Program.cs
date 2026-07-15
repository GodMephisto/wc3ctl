// src/wc3ctl/Program.cs
using System.CommandLine;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3Ctl;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var jsonOption = new Option<bool>("--json", "Emit machine-readable JSON.");
        var gameDirOption = new Option<string?>("--game-dir",
            "Warcraft III install directory (overrides auto-detection).");
        var mapArg = new Argument<string>("map", "Path to a .w3x/.w3m map.");

        var root = new RootCommand("wc3ctl — Warcraft III map tool");
        root.AddGlobalOption(jsonOption);
        root.AddGlobalOption(gameDirOption);

        int exitCode = 0;

        void Emit(bool json, object result, Func<string> human) =>
            Console.WriteLine(json ? Render.AsJson(result) : human());

        // Run a command body, turning expected failures into a clean one-line
        // message on stderr + a non-zero exit code (never a raw stack trace).
        void RunSafely(Action body)
        {
            try
            {
                body();
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"error: file not found: {ex.FileName ?? "(unknown)"}");
                exitCode = 1;
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                exitCode = 1;
            }
            catch (InvalidDataException ex)
            {
                Console.Error.WriteLine($"error: not a valid MPQ/.w3x map — {ex.Message}");
                exitCode = 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                exitCode = 1;
            }
        }

        var info = new Command("info", "Show map metadata.") { mapArg };
        info.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = InfoCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.Info(r));
        }), mapArg, jsonOption);

        var ls = new Command("ls", "List internal files.") { mapArg };
        ls.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = ListCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.List(r));
        }), mapArg, jsonOption);

        var rt = new Command("roundtrip", "Verify byte-faithful round-trip.") { mapArg };
        rt.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = RoundtripCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.Roundtrip(r));
        }), mapArg, jsonOption);

        var queryArg = new Argument<string>("query", "Substring to search for.");
        var search = new Command("search", "Search map contents.") { mapArg, queryArg };
        search.SetHandler((string map, string query, bool json) => RunSafely(() =>
        {
            var r = SearchCommand.Execute(MapDocument.Load(map), query);
            Emit(json, r, () => Render.Search(r));
        }), mapArg, queryArg, jsonOption);

        var mapB = new Argument<string>("mapB", "Path to the second map.");
        var diff = new Command("diff", "Diff two maps.") { mapArg, mapB };
        diff.SetHandler((string a, string b, bool json) => RunSafely(() =>
        {
            var r = DiffCommand.Execute(MapDocument.Load(a), MapDocument.Load(b));
            Emit(json, r, () => Render.Diff(r));
        }), mapArg, mapB, jsonOption);

        var objRawcode = new Argument<string>("rawcode", "Four-character object rawcode.");
        var objField = new Option<string?>("--field", "Restrict output to a single field.");
        var obj = new Command("object", "Object data queries.");
        var objGet = new Command("get", "Get an object's merged fields (base game data ⊕ map deltas).")
        { mapArg, objRawcode, objField };
        objGet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string rawcode = p.GetValueForArgument(objRawcode);
            string? field = p.GetValueForOption(objField);
            bool json = p.GetValueForOption(jsonOption);

            var r = ObjectGetCommand.Execute(MapDocument.Load(map), rawcode, p.GetValueForOption(gameDirOption));
            if (field is not null)
            {
                // Leveled ability fields are keyed "code:N" — match the bare code too.
                var match = r.Fields
                    .Where(f => string.Equals(f.Code, field, StringComparison.OrdinalIgnoreCase)
                        || f.Code.StartsWith(field + ":", StringComparison.OrdinalIgnoreCase)).ToList();
                var diags = match.Count == 0 && r.Found
                    ? r.Diagnostics.Append($"no such field {field}").ToList()
                    : r.Diagnostics;
                r = r with { Fields = match, Diagnostics = diags };
            }
            Emit(json, r, () => Render.ObjectGet(r));
        }));
        obj.AddCommand(objGet);

        var objList = new Command("list", "List the map's custom/modified units.") { mapArg };
        objList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ObjectListCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(mapArg)), p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ObjectList(r));
        }));
        obj.AddCommand(objList);

        var setFieldArg = new Argument<string>("field", "Field code or English field name.");
        var setValueArg = new Argument<string>("value", "New value for the field.");
        var objSet = new Command("set", "Set an object field (stub — write-back pending).")
        { mapArg, objRawcode, setFieldArg, setValueArg };
        objSet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ObjectSetCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForArgument(objRawcode),
                p.GetValueForArgument(setFieldArg),
                p.GetValueForArgument(setValueArg));
            Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
            if (!r.Ok) exitCode = 1;
        }));
        obj.AddCommand(objSet);

        var renderOut = new Option<string?>(new[] { "-o", "--out" },
            "Output PNG path. Default: <map name>.png in the current directory.");
        var render = new Command("render", "Render a top-down terrain image to PNG.") { mapArg, renderOut };
        render.SetHandler((string map, string? outPath) => RunSafely(() =>
        {
            var png = RenderCommand.Execute(MapDocument.Load(map));
            var dest = outPath ?? Path.GetFileNameWithoutExtension(map) + ".png";
            File.WriteAllBytes(dest, png);
            Console.WriteLine($"Wrote {png.Length:N0} bytes to {dest}");
        }), mapArg, renderOut);

        var script = new Command("script", "Map script queries.");
        var scriptFunctions = new Command("functions", "List functions declared in the map script.") { mapArg };
        scriptFunctions.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = ScriptCommand.Functions(MapDocument.Load(map));
            Emit(json, r, () => Render.ScriptFunctions(r));
        }), mapArg, jsonOption);
        script.AddCommand(scriptFunctions);

        var internalPathArg = new Argument<string?>("internal-path", () => null, "Exact internal file path to extract.");
        var outOption = new Option<string?>(new[] { "-o", "--out" },
            "Output directory (or output file for a single named extraction). Default: current directory.");
        var patternOption = new Option<string[]>("--pattern",
            "Glob pattern(s) matched against internal names ('*' wildcard); repeatable or comma-separated.")
        { AllowMultipleArgumentsPerToken = true };
        var modelsOption = new Option<bool>("--models", "Extract models (*.mdx, *.mdl).");
        var texturesOption = new Option<bool>("--textures", "Extract textures (*.blp, *.tga, *.dds).");
        var soundsOption = new Option<bool>("--sounds", "Extract sounds (*.mp3, *.wav).");
        var allOption = new Option<bool>("--all", "Extract every internal file (including unnamed entries).");
        var extract = new Command("extract", "Extract internal files to disk.")
        { mapArg, internalPathArg, outOption, patternOption, modelsOption, texturesOption, soundsOption, allOption };
        extract.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string? exact = p.GetValueForArgument(internalPathArg);
            string? outPath = p.GetValueForOption(outOption);
            bool all = p.GetValueForOption(allOption);
            bool json = p.GetValueForOption(jsonOption);
            var patterns = (p.GetValueForOption(patternOption) ?? Array.Empty<string>())
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToList();
            if (p.GetValueForOption(modelsOption)) patterns.AddRange(ExtractSelector.GroupPatterns("models"));
            if (p.GetValueForOption(texturesOption)) patterns.AddRange(ExtractSelector.GroupPatterns("textures"));
            if (p.GetValueForOption(soundsOption)) patterns.AddRange(ExtractSelector.GroupPatterns("sounds"));

            if (exact is null && patterns.Count == 0 && !all)
                throw new ArgumentException(
                    "nothing selected — pass an internal path, --pattern, --models/--textures/--sounds, or --all");
            if (exact is not null && (patterns.Count > 0 || all))
                throw new ArgumentException(
                    "an internal path cannot be combined with --pattern/--models/--textures/--sounds/--all");

            var selector = new ExtractSelector { ExactName = exact, Patterns = patterns, All = all };
            var r = ExtractCommand.Execute(MapDocument.Load(map), selector);
            if (exact is not null && r.Items.Count == 0)
                throw new FileNotFoundException($"'{exact}' not found in map", exact);

            ExtractManifest manifest;
            string dest;
            if (exact is not null && outPath is not null && !Directory.Exists(outPath))
            {
                // Single named extraction with -o pointing at a file path.
                manifest = ExtractWriter.WriteSingle(r.Items[0], outPath);
                dest = manifest.Files[0].Path;
            }
            else
            {
                var outDir = outPath ?? Directory.GetCurrentDirectory();
                manifest = ExtractWriter.WriteAll(r, outDir);
                dest = Path.GetFullPath(outDir);
            }
            Emit(json, manifest, () => Render.Extract(manifest, dest));
        }));

        root.AddCommand(info); root.AddCommand(ls); root.AddCommand(rt);
        root.AddCommand(search); root.AddCommand(diff); root.AddCommand(obj);
        root.AddCommand(extract); root.AddCommand(render); root.AddCommand(script);

        int parseResult = await root.InvokeAsync(args);
        return parseResult != 0 ? parseResult : exitCode;
    }
}
