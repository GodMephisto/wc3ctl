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

        var root = new RootCommand("wc3ctl - Warcraft III map tool");
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
                Console.Error.WriteLine($"error: not a valid MPQ/.w3x map - {ex.Message}");
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
        var objGetKind = new Option<string?>("--kind",
            "Object type: unit|item|ability|destructable|doodad|buff|upgrade. Default: auto-detect.");
        var objListKind = new Option<string>("--kind", () => "unit",
            "Object type: unit|item|ability|destructable|doodad|buff|upgrade.");
        var obj = new Command("object", "Object data queries.");
        var objGet = new Command("get", "Get an object's merged fields (base game data ⊕ map deltas).")
        { mapArg, objRawcode, objField, objGetKind };
        objGet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string rawcode = p.GetValueForArgument(objRawcode);
            string? field = p.GetValueForOption(objField);
            string? kindToken = p.GetValueForOption(objGetKind);
            bool json = p.GetValueForOption(jsonOption);

            // No --kind → probe every kind (map deltas first, then base stores).
            var r = kindToken is null
                ? ObjectGetCommand.Execute(MapDocument.Load(map), rawcode, p.GetValueForOption(gameDirOption))
                : ObjectGetCommand.Execute(MapDocument.Load(map), ObjectKinds.Parse(kindToken), rawcode,
                    p.GetValueForOption(gameDirOption));
            if (field is not null)
            {
                // Leveled ability fields are keyed "code:N" - match the bare code too.
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

        var objList = new Command("list", "List the map's custom/modified objects of one kind.")
        { mapArg, objListKind };
        objList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ObjectListCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                ObjectKinds.Parse(p.GetValueForOption(objListKind)!),
                p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ObjectList(r));
        }));
        obj.AddCommand(objList);

        var setFieldArg = new Argument<string>("field",
            "Four-character field code (e.g. uhpm), or code:N for an ability/upgrade level or doodad variation.");
        var setValueArg = new Argument<string>("value", "New value for the field.");
        var setOut = new Option<string?>(new[] { "-o", "--out" },
            "Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.");
        var objSetKind = new Option<string>("--kind", () => "unit",
            "Object type: unit|item|ability|destructable|doodad|buff|upgrade.");
        var objSet = new Command("set", "Set an object field (any kind) and save the edited map.")
        { mapArg, objRawcode, setFieldArg, setValueArg, objSetKind, setOut };
        objSet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = ObjectSetCommand.Execute(doc,
                ObjectKinds.Parse(p.GetValueForOption(objSetKind)!),
                p.GetValueForArgument(objRawcode),
                p.GetValueForArgument(setFieldArg),
                p.GetValueForArgument(setValueArg));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode = 1;
                return;
            }
            var dest = p.GetValueForOption(setOut) ?? Path.Combine(
                Path.GetDirectoryName(map) ?? "",
                Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.Warning, SavedTo = dest },
                () => (r.Warning is null ? "" : $"warning: {r.Warning}\n") + $"{r.Message}\nsaved: {dest}");
        }));
        obj.AddCommand(objSet);

        var newKindArg = new Argument<string>("kind",
            "Object type: unit|item|ability|destructable|doodad|buff|upgrade.");
        var newBaseArg = new Argument<string>("base",
            "Four-character rawcode of the base object the new custom object derives from.");
        var objNew = new Command("new",
            "Create a custom object derived from a base rawcode and save the edited map. Prints the fresh rawcode.")
        { mapArg, newKindArg, newBaseArg, setOut };
        objNew.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = ObjectNewCommand.Execute(doc,
                ObjectKinds.Parse(p.GetValueForArgument(newKindArg)),
                p.GetValueForArgument(newBaseArg));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode = 1;
                return;
            }
            var dest = p.GetValueForOption(setOut) ?? Path.Combine(
                Path.GetDirectoryName(map) ?? "",
                Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.NewRawcode, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        obj.AddCommand(objNew);

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

        var rmTarget = new Argument<string>("rawcode",
            "Four-character object rawcode, or an internal model path (contains '\\', '/' or '.').");
        var rmKind = new Option<string?>("--kind",
            "Object type: unit|item|destructable|doodad. Default: auto-detect.");
        var rmOut = new Option<string?>(new[] { "-o", "--out" },
            "Output PNG path. Default: <rawcode>.png in the current directory.");
        var renderModel = new Command("render-model",
            "Render an object's model (.mdx/.mdl) to PNG - map-imported models first, base-game (CASC) models when a WC3 install is available.")
        { mapArg, rmTarget, rmKind, rmOut };
        renderModel.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string target = p.GetValueForArgument(rmTarget);
            string? kindToken = p.GetValueForOption(rmKind);
            var doc = MapDocument.Load(p.GetValueForArgument(mapArg));

            // Anything that can't be a rawcode is treated as an internal model path.
            bool isPath = target.Length != 4 || target.IndexOfAny(new[] { '\\', '/', '.' }) >= 0;
            var png = isPath
                ? RenderModelCommand.ExecutePath(doc, target, p.GetValueForOption(gameDirOption))
                : kindToken is null
                    ? RenderModelCommand.Execute(doc, target, p.GetValueForOption(gameDirOption))
                    : RenderModelCommand.Execute(doc, ObjectKinds.Parse(kindToken), target,
                        p.GetValueForOption(gameDirOption));

            var dest = p.GetValueForOption(rmOut)
                ?? (isPath ? Path.GetFileNameWithoutExtension(target) : target) + ".png";
            File.WriteAllBytes(dest, png);
            Console.WriteLine($"Wrote {png.Length:N0} bytes to {dest}");
        }));

        var bundle = new Command("bundle", "Dependency bundles for porting between maps.");
        var bundleUnit = new Command("unit",
            "Resolve everything a unit depends on: objects, asset files and trigger strings.")
        { mapArg, objRawcode };
        bundleUnit.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = BundleCommand.ResolveUnit(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForArgument(objRawcode),
                p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.BundleUnit(r));
        }));
        bundle.AddCommand(bundleUnit);

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
                    "nothing selected - pass an internal path, --pattern, --models/--textures/--sounds, or --all");
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

        var convertInput = new Argument<string>("input",
            "Source file: an image (.blp/.png/.jpg/.jpeg/.bmp/.tga/.gif) or a model (.mdx/.mdl).");
        var convertOutput = new Argument<string>("output",
            "Destination file; its extension picks the format. Images: .png/.jpg/.jpeg/.bmp/.tga/.gif/.blp. Models: .obj (a .mtl is written beside it).");
        var convert = new Command("convert",
            "Convert asset files between WC3 and standard formats (disk-to-disk).")
        { convertInput, convertOutput };
        convert.SetHandler((string input, string output) => RunSafely(() =>
        {
            var fromExt = Path.GetExtension(input);
            var toExt = Path.GetExtension(output);
            bool modelInput = fromExt.ToLowerInvariant() is ".mdx" or ".mdl";
            bool objOutput = string.Equals(toExt, ".obj", StringComparison.OrdinalIgnoreCase);
            if (modelInput != objOutput)
                throw new ArgumentException(modelInput
                    ? "models convert to .obj only — pass an output ending in .obj"
                    : $"only .mdx/.mdl convert to {toExt} — image sources convert to image formats");

            if (modelInput)
            {
                // Name the parse input after the OUTPUT so the obj's mtllib line
                // matches the .mtl actually written beside it.
                var export = ConvertCommand.ExportModelToObj(
                    File.ReadAllBytes(input), Path.GetFileNameWithoutExtension(output) + fromExt);
                var mtlPath = Path.ChangeExtension(output, ".mtl");
                File.WriteAllText(output, export.Obj);
                File.WriteAllText(mtlPath, export.Mtl);
                Console.WriteLine($"Wrote {output} + {mtlPath} (textures referenced by basename; convert them separately)");
            }
            else
            {
                var bytes = ConvertCommand.ConvertImage(File.ReadAllBytes(input), fromExt, toExt);
                File.WriteAllBytes(output, bytes);
                Console.WriteLine($"Wrote {bytes.Length:N0} bytes to {output}");
            }
        }), convertInput, convertOutput);

        var portSource = new Argument<string>("source-map", "Map to port FROM.");
        var portTarget = new Argument<string>("target-map", "Map to port INTO.");
        var portRawcodes = new Argument<string>("rawcode",
            "Four-character unit rawcode, or a comma-separated list (H000,H001,…) to port several units into the same target in one operation.");
        var noScriptOption = new Option<bool>("--no-script",
            "Port object data + assets + strings only; skip the best-effort JASS script closure append.");
        var dryRunOption = new Option<bool>("--dry-run",
            "Preview the port: print the full report (remaps, objects, files, strings, script) without writing anything.");
        var port = new Command("port", "Port content between maps.");
        var portUnit = new Command("unit",
            "Port a unit (its custom objects + assets + strings, and best-effort its trigger script) from one map into another, auto-remapping rawcode collisions.")
        { portSource, portRawcodes, portTarget, outOption, noScriptOption, dryRunOption };
        portUnit.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string sourcePath = p.GetValueForArgument(portSource);
            string targetPath = p.GetValueForArgument(portTarget);
            var rawcodes = p.GetValueForArgument(portRawcodes)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (rawcodes.Length == 0)
                throw new ArgumentException("no rawcode given");
            bool json = p.GetValueForOption(jsonOption);
            bool dryRun = p.GetValueForOption(dryRunOption);
            bool includeScript = !p.GetValueForOption(noScriptOption);
            string? gameDir = p.GetValueForOption(gameDirOption);

            var source = MapDocument.Load(sourcePath);
            var target = MapDocument.Load(targetPath);

            // Never clobber the target - write a sibling <target>.ported.<ext> by default.
            // A dry run writes nothing at all.
            string? outPath = dryRun ? null : p.GetValueForOption(outOption)
                ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? ".",
                    Path.GetFileNameWithoutExtension(targetPath) + ".ported" + Path.GetExtension(targetPath));

            if (rawcodes.Length == 1)
            {
                var bundle = BundleCommand.ResolveUnit(source, rawcodes[0], gameDir);
                var result = dryRun
                    ? PortCommand.PreviewPort(source, bundle, target, includeScript)
                    : PortCommand.PortUnit(source, bundle, target, includeScript);
                if (outPath is not null) target.Save(outPath);
                Emit(json, result, () => Render.Port(result, outPath));
            }
            else
            {
                var bundles = rawcodes.Select(rc => BundleCommand.ResolveUnit(source, rc, gameDir)).ToList();
                var result = dryRun
                    ? PortCommand.PreviewPorts(source, bundles, target, includeScript)
                    : PortCommand.PortUnits(source, bundles, target, includeScript);
                if (outPath is not null) target.Save(outPath);
                Emit(json, result, () => Render.PortBatch(result, outPath));
            }
        }));
        port.AddCommand(portUnit);

        root.AddCommand(info); root.AddCommand(ls); root.AddCommand(rt);
        root.AddCommand(search); root.AddCommand(diff); root.AddCommand(obj);
        root.AddCommand(extract); root.AddCommand(render); root.AddCommand(renderModel);
        root.AddCommand(script); root.AddCommand(bundle); root.AddCommand(port);
        root.AddCommand(convert);

        int parseResult = await root.InvokeAsync(args);
        return parseResult != 0 ? parseResult : exitCode;
    }
}
