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
        var mapArg = new Argument<string>("map", "Path to a .w3x/.w3m map.");

        var root = new RootCommand("wc3ctl — Warcraft III map tool");
        root.AddGlobalOption(jsonOption);

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
        var objGet = new Command("object", "Object data queries.");
        var objGetSub = new Command("get", "Get an object's fields.") { mapArg, objRawcode, objField };
        objGetSub.SetHandler((string map, string rawcode, string? field, bool json) => RunSafely(() =>
        {
            // --field filtering and --game-dir arrive with the merged-output rendering (Task 9).
            var r = ObjectGetCommand.Execute(MapDocument.Load(map), rawcode, gameDirOverride: null);
            Emit(json, r, () => Render.ObjectGet(r));
        }), mapArg, objRawcode, objField, jsonOption);
        objGet.AddCommand(objGetSub);

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
        root.AddCommand(search); root.AddCommand(diff); root.AddCommand(objGet);
        root.AddCommand(extract);

        int parseResult = await root.InvokeAsync(args);
        return parseResult != 0 ? parseResult : exitCode;
    }
}
