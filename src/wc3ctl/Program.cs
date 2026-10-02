// src/wc3ctl/Program.cs
using System.CommandLine;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3Ctl;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var root = BuildRoot(out var exitCode);
        int parseResult = await root.InvokeAsync(args);
        return parseResult != 0 ? parseResult : exitCode[0];
    }

    /// <summary>Builds the full wc3ctl command tree. Exposed (parameterless) so
    /// CLI&lt;-&gt;MCP parity tests can introspect the real CLI surface without
    /// spawning a process.</summary>
    public static RootCommand BuildRoot() => BuildRoot(out _);

    private static RootCommand BuildRoot(out int[] exitCodeOut)
    {
        // 1-element box so command handlers (closures) and Main share the exit code.
        var exitCode = new int[1];
        exitCodeOut = exitCode;

        var jsonOption = new Option<bool>("--json", "Emit machine-readable JSON.");
        var gameDirOption = new Option<string?>("--game-dir",
            "Warcraft III install directory (overrides auto-detection).");
        var mapArg = new Argument<string>("map", "Path to a .w3x/.w3m map.");

        var root = new RootCommand("wc3ctl - Warcraft III map tool");
        root.AddGlobalOption(jsonOption);
        root.AddGlobalOption(gameDirOption);

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
                exitCode[0] = 1;
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                exitCode[0] = 1;
            }
            catch (InvalidDataException ex)
            {
                Console.Error.WriteLine($"error: not a valid MPQ/.w3x map - {ex.Message}");
                exitCode[0] = 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                exitCode[0] = 1;
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
                exitCode[0] = 1;
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
                exitCode[0] = 1;
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

        // ---- placement: add doodads/regions to a map and save the edited copy ----
        var placeXArg = new Argument<float>("x", "World X coordinate.");
        var placeYArg = new Argument<float>("y", "World Y coordinate.");
        var placeZOpt = new Option<float>("--z", () => 0f, "World Z (height offset). Default: 0.");
        var placeRotOpt = new Option<float>("--rotation", () => 0f, "Facing angle in radians. Default: 0.");
        var placeScaleOpt = new Option<float>("--scale", () => 1f, "Uniform scale. Default: 1.");
        var placeVarOpt = new Option<int>("--variation", () => 0, "Doodad variation index. Default: 0.");
        var place = new Command("place", "Place objects on a map and save the edited copy.");

        var placeDooRawcode = new Argument<string>("rawcode", "Four-character doodad type rawcode.");
        var placeDoodad = new Command("doodad",
            "Place a doodad at (x, y) and save the edited map. Writes war3map.doo.")
        { mapArg, placeDooRawcode, placeXArg, placeYArg, placeZOpt, placeRotOpt, placeScaleOpt, placeVarOpt, setOut };
        placeDoodad.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = PlacementCommand.PlaceDoodad(doc,
                p.GetValueForArgument(placeDooRawcode),
                p.GetValueForArgument(placeXArg),
                p.GetValueForArgument(placeYArg),
                p.GetValueForOption(placeZOpt),
                p.GetValueForOption(placeRotOpt),
                p.GetValueForOption(placeScaleOpt),
                p.GetValueForOption(placeVarOpt));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = p.GetValueForOption(setOut) ?? Path.Combine(
                Path.GetDirectoryName(map) ?? "",
                Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.CreationNumber, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        place.AddCommand(placeDoodad);

        var placeRegName = new Argument<string>("name", "Region name.");
        var placeRegLeft = new Argument<float>("left", "West edge (min X).");
        var placeRegBottom = new Argument<float>("bottom", "South edge (min Y).");
        var placeRegRight = new Argument<float>("right", "East edge (max X).");
        var placeRegTop = new Argument<float>("top", "North edge (max Y).");
        var placeRegion = new Command("region",
            "Add a rectangular region and save the edited map. Writes war3map.w3r.")
        { mapArg, placeRegName, placeRegLeft, placeRegBottom, placeRegRight, placeRegTop, setOut };
        placeRegion.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = PlacementCommand.PlaceRegion(doc,
                p.GetValueForArgument(placeRegName),
                p.GetValueForArgument(placeRegLeft),
                p.GetValueForArgument(placeRegBottom),
                p.GetValueForArgument(placeRegRight),
                p.GetValueForArgument(placeRegTop));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = p.GetValueForOption(setOut) ?? Path.Combine(
                Path.GetDirectoryName(map) ?? "",
                Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.CreationNumber, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        place.AddCommand(placeRegion);

        var placeUnitRawcode = new Argument<string>("rawcode", "Four-character unit type rawcode.");
        var placeOwnerArg = new Argument<int>("owner", "Owning player id (0-based; 0 = red).");
        var placeUnit = new Command("unit",
            "Place a unit at (x, y) and save the edited map. Writes war3mapUnits.doo.")
        { mapArg, placeUnitRawcode, placeOwnerArg, placeXArg, placeYArg, placeZOpt, placeRotOpt, placeScaleOpt, setOut };
        placeUnit.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = PlacementCommand.PlaceUnit(doc,
                p.GetValueForArgument(placeUnitRawcode),
                p.GetValueForArgument(placeOwnerArg),
                p.GetValueForArgument(placeXArg),
                p.GetValueForArgument(placeYArg),
                p.GetValueForOption(placeZOpt),
                p.GetValueForOption(placeRotOpt),
                p.GetValueForOption(placeScaleOpt));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = p.GetValueForOption(setOut) ?? Path.Combine(
                Path.GetDirectoryName(map) ?? "",
                Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.CreationNumber, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        place.AddCommand(placeUnit);

        var placePlayerArg = new Argument<int>("player", "Player whose start location this is (0-based; 0 = red).");
        var placeStartLoc = new Command("start-location",
            "Place or move a player's start location at (x, y) and save the edited map. "
            + "One per player — an existing one for this player is moved. Writes war3mapUnits.doo.")
        { mapArg, placePlayerArg, placeXArg, placeYArg, setOut };
        placeStartLoc.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = PlacementCommand.PlaceStartLocation(doc,
                p.GetValueForArgument(placePlayerArg),
                p.GetValueForArgument(placeXArg),
                p.GetValueForArgument(placeYArg));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = p.GetValueForOption(setOut) ?? Path.Combine(
                Path.GetDirectoryName(map) ?? "",
                Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.CreationNumber, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        place.AddCommand(placeStartLoc);

        var placeItemRawcode = new Argument<string>("rawcode", "Four-character item type rawcode.");
        var placeItem = new Command("item",
            "Place a preplaced item at (x, y) and save the edited map. Writes war3mapUnits.doo (item slot).")
        { mapArg, placeItemRawcode, placeXArg, placeYArg, placeZOpt, placeRotOpt, placeScaleOpt, setOut };
        placeItem.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = PlacementCommand.PlaceItem(doc,
                p.GetValueForArgument(placeItemRawcode),
                p.GetValueForArgument(placeXArg),
                p.GetValueForArgument(placeYArg),
                p.GetValueForOption(placeZOpt),
                p.GetValueForOption(placeRotOpt),
                p.GetValueForOption(placeScaleOpt));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = p.GetValueForOption(setOut) ?? Path.Combine(
                Path.GetDirectoryName(map) ?? "",
                Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.CreationNumber, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        place.AddCommand(placeItem);

        // ---- palette: the doodad types placeable on a map (base catalog ⊕ map object-data) ----
        var palette = new Command("palette",
            "List the doodad types placeable on a map (base-game catalog + the map's own object-data).")
        { mapArg };
        palette.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = PaletteCommand.DoodadPalette(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.DoodadPalette(r));
        }));

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

        var bundleObjKind = new Option<string>("--kind", () => "unit",
            "Root object type: unit|item|ability|destructable|doodad|buff|upgrade.");
        var bundleObject = new Command("object",
            "Resolve everything an object of any kind depends on ('bundle unit', generalized).")
        { mapArg, objRawcode, bundleObjKind };
        bundleObject.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = BundleCommand.ResolveObject(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                ObjectKinds.Parse(p.GetValueForOption(bundleObjKind)!),
                p.GetValueForArgument(objRawcode),
                p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.BundleUnit(r));
        }));
        bundle.AddCommand(bundleObject);

        var script = new Command("script", "Map script queries.");
        var scriptFunctions = new Command("functions", "List functions declared in the map script.") { mapArg };
        scriptFunctions.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = ScriptCommand.Functions(MapDocument.Load(map));
            Emit(json, r, () => Render.ScriptFunctions(r));
        }), mapArg, jsonOption);
        script.AddCommand(scriptFunctions);

        var leaksAll = new Option<bool>("--all", "List every finding, including code that runs only once at init.");
        var scriptLeaks = new Command("leaks",
            "Find handle leaks in war3map.j (locations, groups, forces, effects, timers, text tags, "
            + "lightning, triggers created and never destroyed), ranked by how often the code runs, and "
            + "list the periodic timers. Read-only.") { mapArg, leaksAll };
        scriptLeaks.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ScriptLeaksCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ScriptLeaks(r, p.GetValueForOption(leaksAll)));
            if (!r.Ok) exitCode[0] = 1;
        }));
        script.AddCommand(scriptLeaks);

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

        var validate = new Command("validate",
            "Check a map for problems (missing/empty files, loader errors). Exits 2 if invalid.")
            { mapArg };
        validate.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = ValidateCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.Validate(r));
            if (!r.Valid) exitCode[0] = 2;
        }), mapArg, jsonOption);

        // Behavioural audit. validate answers whether a map LOADS, audit answers whether its
        // abilities do what they claim, by checking the object data against the tooltips the
        // author wrote. Exits 2 when a check finds something that cannot work at all.
        var auditCheck = new Option<string[]>("--check",
            "Limit to named checks. Repeatable. Default runs all: "
            + string.Join(", ", AuditCommand.AllChecks))
        { AllowMultipleArgumentsPerToken = true };
        var audit = new Command("audit",
            "Check abilities against the map's own tooltips, plus inherited requirements, "
            + "portrait-risk models, dangling object references and model paths that load no file. "
            + "Read-only. Exits 2 if any error-level issue is found.")
            { mapArg, auditCheck };
        audit.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = AuditCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForOption(gameDirOption),
                p.GetValueForOption(auditCheck));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.Audit(r));
            if (!r.Ok) exitCode[0] = 2;
        }));

        // ---- deprotect: recover the file names a protected map stripped ----
        // A protected map ships no (listfile), so most entries have no name and GetFile can
        // never find them. On one real map that was 793 of 851 entries, and it made an audit
        // of imported models report 3 affected units when the real number was 178.
        var deprotDict = new Option<string[]>(new[] { "--listfile" },
            "Extra name dictionary, one name per line. Repeatable. Community listfiles and the "
            + "listfiles of sibling maps both work.")
        { AllowMultipleArgumentsPerToken = false };
        var deprotApply = new Option<bool>("--apply",
            "Write the recovered names into the map as a real (listfile), so every later load "
            + "names those entries. Changes no other byte.");
        var deprotect = new Command("deprotect",
            "Recover the file names a protected map stripped, from its own data, its script, its "
            + "models' texture declarations, and any listfiles supplied. Read-only without --apply.")
        { mapArg, deprotDict, deprotApply, setOut };
        deprotect.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = NameRecoveryCommand.Execute(
                doc, p.GetValueForOption(deprotDict) ?? Array.Empty<string>());
            bool json = p.GetValueForOption(jsonOption);
            if (!p.GetValueForOption(deprotApply))
            {
                Emit(json, r, () => Render.NameRecovery(r));
                return;
            }
            int stamped = NameRecoveryCommand.ApplyNames(doc, r);
            FinishEdit(json, p.GetValueForOption(setOut), map, doc, true,
                $"recovered {r.NamedAfter - r.NamedBefore} name(s), naming {stamped} entries "
                + "(those are recompressed, every other entry is byte-faithful)");
        }));

        // ---- repair portraits: strip the camera from pre-900 models ----
        var portraitApply = new Option<bool>("--apply",
            "Rewrite the affected models. Without it, nothing is changed.");
        var portraitStamp = new Option<int?>("--stamp-version",
            "After removing the camera, rewrite the model's VERS to this value, for example 1800. "
            + "Removing the camera alone is not enough on its own.");
        var portraitWiden = new Option<bool>("--upgrade-geometry",
            "With --stamp-version, also insert the fields a version 900 or above model carries "
            + "that an 800 era one does not, a u32 lod plus char[80] lod name per geoset and a "
            + "char[80] shader name per material, so the declared version and the actual layout "
            + "agree. Without it only the version number changes.");
        var repairPortraits = new Command("portraits",
            "Remove the camera chunk from models below version 900, which is what makes Reforged "
            + "3.0.0 render a black portrait pane. Models with no camera already render correctly.")
        { mapArg, portraitApply, portraitStamp, portraitWiden, setOut };
        repairPortraits.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            bool apply = p.GetValueForOption(portraitApply);
            var r = PortraitRepairCommand.Execute(doc, apply,
                p.GetValueForOption(portraitStamp), p.GetValueForOption(portraitWiden));
            bool json = p.GetValueForOption(jsonOption);
            if (!apply)
            {
                Emit(json, r, () => Render.PortraitRepair(r));
                return;
            }
            FinishEdit(json, p.GetValueForOption(setOut), map, doc, true,
                $"{r.ModelsRepaired} of {r.ModelsAtRisk} at-risk model(s) rewritten. "
                + "TEST THIS BUILD before distributing it, a clean parse is not proof the game "
                + "renders it.");
        }));

        // ---- repair data-pointers: levels that name the wrong Data column ----
        var ptrApply = new Option<bool>("--apply",
            "Correct the pointers. Without it, nothing is changed.");
        var repairPointers = new Command("data-pointers",
            "Correct a levelled field whose data pointer is 0 on some levels and a real DataA to "
            + "DataF selector on others, which makes those levels name a different column than "
            + "the author meant. Caused by a level ADDED by an earlier version of this tool.")
        { mapArg, ptrApply, setOut };
        repairPointers.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            bool apply = p.GetValueForOption(ptrApply);
            var r = DataPointerRepairCommand.Execute(doc, apply);
            bool json = p.GetValueForOption(jsonOption);
            if (!apply)
            {
                Emit(json, r, () => Render.DataPointerRepair(r));
                return;
            }
            FinishEdit(json, p.GetValueForOption(setOut), map, doc, true,
                $"{r.LevelsFixed} level(s) across {r.FieldsFixed} field(s) corrected.");
        }));

        // ---- repair model-paths: models the engine fails to load, and retries forever ----
        var mpApply = new Option<bool>("--apply",
            "Rewrite the paths that have exactly one intended file. Without it, nothing is changed.");
        var mpNoEmpty = new Option<bool>("--keep-placeholders",
            "Leave placeholder models such as '.mdl' untouched. By default they are pointed at a "
            + "bundled model with no geometry, which still draws nothing but loads.");
        var repairModels = new Command("model-paths",
            "Find model paths in the script and object data that resolve to no file, which the "
            + "engine logs as 'model creation failed' and retries on every creation. Fixes the "
            + "ones with a single obvious target, reports the rest.")
        { mapArg, mpApply, mpNoEmpty, setOut };
        repairModels.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            bool json = p.GetValueForOption(jsonOption);
            string? gameDir = p.GetValueForOption(gameDirOption);
            if (!p.GetValueForOption(mpApply))
            {
                var scan = ModelPathCommand.Scan(doc, gameDir);
                Emit(json, scan, () => Render.ModelPaths(scan));
                return;
            }
            var r = ModelPathCommand.Repair(doc, gameDir, !p.GetValueForOption(mpNoEmpty));
            if (!json) Console.WriteLine(Render.ModelPathRepair(r));
            FinishEdit(json, p.GetValueForOption(setOut), map, doc, r.Changes.Count > 0,
                $"{r.Changes.Count} change(s), {r.LeftAlone.Count} path(s) left for the author. "
                + "TEST THIS BUILD before distributing it.");
        }));

        // ---- repair audit-errors, the audit's dangling-reference and requirement errors ----
        var auditApply = new Option<bool>("--apply",
            "Make the edits. Without it, every edit is listed and nothing is changed.");
        var repairAudit = new Command("audit-errors",
            "Fix what audit reports as dangling-reference and requirement errors, with the same "
            + "detection. Dead ids leave unit and item ability lists, a missing buff gets its base "
            + "ability's value back, and an inherited requirement the map never defines is cleared.")
        { mapArg, auditApply, setOut };
        repairAudit.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            bool json = p.GetValueForOption(jsonOption);
            bool apply = p.GetValueForOption(auditApply);
            var r = AuditRepairCommand.Execute(doc, apply, p.GetValueForOption(gameDirOption));
            if (!apply || !r.Ok || r.Edits.Count == 0)
            {
                Emit(json, r, () => Render.AuditRepair(r));
                if (!r.Ok) exitCode[0] = 1;
                return;
            }
            if (!json) Console.WriteLine(Render.AuditRepair(r));
            FinishEdit(json, p.GetValueForOption(setOut), map, doc, true,
                $"{r.Edits.Count} field(s) changed. Run audit on the result to confirm both checks are clean.");
        }));

        // ---- repair preload, the first-seconds asset load moved under the loading screen ----
        var preApply = new Option<bool>("--apply",
            "Write the preload into the script. Without it, only what would be preloaded is shown.");
        var preWithin = new Option<double>("--within", () => 5,
            "A trigger on a single timer at or below this many seconds counts as early.");
        var preFunctions = new Option<string[]>("--function",
            "Also walk from these script functions, for a map that starts its heavy work another way.")
        { AllowMultipleArgumentsPerToken = true };
        var repairPreload = new Command("preload",
            "Find the unit types and abilities a map touches in its first seconds of play, from "
            + "triggers on an early single timer, and load them while the loading screen is still up. "
            + "Fixes the freeze right after loading. The map's own code is not changed.")
        { mapArg, preApply, preWithin, preFunctions, setOut };
        var preAllUnits = new Option<bool>("--all-units",
            "Also preload every unit type the map defines that the script names anywhere, for a map "
            + "that spawns new types all game, such as a tower defense's waves.");
        repairPreload.AddOption(preAllUnits);
        repairPreload.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            bool json = p.GetValueForOption(jsonOption);
            bool apply = p.GetValueForOption(preApply);
            var fns = (p.GetValueForOption(preFunctions) ?? Array.Empty<string>())
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
            var r = PreloadRepairCommand.Execute(doc, apply, p.GetValueForOption(preWithin), fns,
                p.GetValueForOption(preAllUnits));
            bool changed = r.UnitTypes.Count + r.Abilities.Count > 0;
            if (!apply || !r.Ok || !changed)
            {
                Emit(json, r, () => Render.Preload(r));
                if (!r.Ok) exitCode[0] = 1;
                return;
            }
            if (!json) Console.WriteLine(Render.Preload(r));
            FinishEdit(json, p.GetValueForOption(setOut), map, doc, true,
                $"{r.UnitTypes.Count} unit type(s) and {r.Abilities.Count} abilit(ies) now load under the "
                + "loading screen. TEST THIS BUILD before distributing it.");
        }));

        // ---- repair uabi-runtime: unit ability lists moved into the script ----
        var uabiApply = new Option<bool>("--apply",
            "Move the lists and write the adder into the script. Without it, only the counts are shown.");
        var repairUabi = new Command("uabi-runtime",
            "Move each unit type's normal ability list (uabi) out of the object data and add the same "
            + "abilities by script the moment each unit is created. Hero abilities and morph targets "
            + "stay. Reported fix for disconnects on maps with thousands of distinct uabi ids.");
        var uabiIds = new Option<string[]>("--ids",
            "Move only these ability ids, comma or space separated, and leave every other uabi entry in place.")
        { AllowMultipleArgumentsPerToken = true };
        repairUabi.AddOption(uabiIds);
        repairUabi.AddArgument(mapArg); repairUabi.AddOption(uabiApply); repairUabi.AddOption(setOut);
        repairUabi.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            bool json = p.GetValueForOption(jsonOption);
            bool apply = p.GetValueForOption(uabiApply);
            var ids = (p.GetValueForOption(uabiIds) ?? Array.Empty<string>())
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
            var r = UabiRuntimeRepairCommand.Execute(doc, apply, ids);
            if (!apply || !r.Ok || r.UnitTypesMoved == 0)
            {
                Emit(json, r, () => Render.UabiRuntime(r));
                if (!r.Ok) exitCode[0] = 1;
                return;
            }
            if (!json) Console.WriteLine(Render.UabiRuntime(r));
            FinishEdit(json, p.GetValueForOption(setOut), map, doc, true,
                $"{r.ReferencesMoved} ability reference(s) on {r.UnitTypesMoved} unit type(s) now added by script. "
                + "TEST THIS BUILD before distributing it.");
        }));

        // ---- replay: score recorded games, for disconnect testing ----
        var replayPath = new Argument<string?>("path",
            () => null, "A .w3g file or a folder searched recursively. Default: every Battle.net "
            + "account's replays under Documents\\Warcraft III\\BattleNet.");
        var replayMap = new Option<string?>("--map", "Only games whose map path contains this text.");
        var replay = new Command("replay",
            "Read recorded games (.w3g) and list each one's map, length, and how every player left, "
            + "flagging games where someone disconnected. Read-only.")
        { replayPath, replayMap };
        replay.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ReplayCommand.Execute(p.GetValueForArgument(replayPath), p.GetValueForOption(replayMap));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.Replays(r));
        }));
        root.AddCommand(replay);

        // ---- uabi-profile: compare unit ability lists across many maps at once ----
        var profilePaths = new Argument<string[]>("paths", "Maps, or folders searched recursively.")
        { Arity = ArgumentArity.OneOrMore };
        var uabiProfile = new Command("uabi-profile",
            "Measure every map's unit ability lists (uabi) side by side, without starting the game. "
            + "Counts, distinct ids, hero and item abilities in normal lists, dangling ids, "
            + "duplicates, and a per-race breakdown. Read-only.")
        { profilePaths };
        uabiProfile.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = UabiProfileCommand.Execute(p.GetValueForArgument(profilePaths), p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.UabiProfiles(r));
        }));
        root.AddCommand(uabiProfile);

        // ---- terrain editing ----
        void FinishTerrain(bool json, string? outOpt, string map, MapDocument doc,
            bool ok, string message, int tiles)
        {
            if (!ok)
            {
                Emit(json, new { Ok = false, Message = message }, () => message);
                exitCode[0] = 1;
                return;
            }
            var dest = outOpt ?? Path.Combine(
                Path.GetDirectoryName(map) ?? "",
                Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
            doc.Save(dest);
            Emit(json, new { Ok = true, Message = message, TilesChanged = tiles, SavedTo = dest },
                () => $"{message}\nsaved: {dest}");
        }

        void FinishSound(bool json, string? outOpt, string map, MapDocument doc, SoundOpResult r)
        {
            if (!r.Ok)
            {
                Emit(json, new { r.Ok, r.Message }, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = outOpt ?? Path.Combine(
                Path.GetDirectoryName(map) ?? "",
                Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
            doc.Save(dest);
            Emit(json, new { r.Ok, r.Message, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }

        // Generic save-and-report for any (Ok, Message) mutation result. On failure emits the
        // message and sets exit 1; on success saves to --out (or a sibling .edited) and reports.
        void FinishEdit(bool json, string? outOpt, string map, MapDocument doc, bool ok, string message)
        {
            if (!ok)
            {
                Emit(json, new { Ok = false, Message = message }, () => message);
                exitCode[0] = 1;
                return;
            }
            var dest = outOpt ?? Path.Combine(
                Path.GetDirectoryName(map) ?? "",
                Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
            doc.Save(dest);
            Emit(json, new { Ok = true, Message = message, SavedTo = dest },
                () => $"{message}\nsaved: {dest}");
        }

        var tCX = new Argument<int>("cx", "Brush centre tile X (column).");
        var tCY = new Argument<int>("cy", "Brush centre tile Y (row).");
        var tRadius = new Argument<int>("radius", "Brush radius in tiles.");
        var tShapeOpt = new Option<TerrainCommand.BrushShape>(
            "--shape", () => TerrainCommand.BrushShape.Circle, "Brush footprint: Circle or Square.");

        var terrainStats = new Command("stats",
            "Summarize terrain: tile count, height range and cliff range.") { mapArg };
        terrainStats.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = TerrainCommand.Stats(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r,
                () => r.Ok
                    ? $"tiles={r.TileCount}  height=[{r.MinHeight}..{r.MaxHeight}] mean={r.MeanHeight}  cliff=[{r.MinCliff}..{r.MaxCliff}]"
                    : r.Message);
        }));

        var tOpDeform = new Argument<TerrainCommand.HeightOp>("op", "raise|lower|set|flatten|smooth");
        var tAmountOpt = new Option<float>("--amount", () => 1f,
            "Step for raise/lower; target for set. Default: 1.");
        var terrainDeform = new Command("deform",
            "Raise/lower/set/flatten/smooth ground height over a brush.")
        { mapArg, tCX, tCY, tRadius, tOpDeform, tAmountOpt, tShapeOpt, setOut };
        terrainDeform.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TerrainCommand.Deform(doc,
                p.GetValueForArgument(tCX), p.GetValueForArgument(tCY), p.GetValueForArgument(tRadius),
                p.GetValueForArgument(tOpDeform), p.GetValueForOption(tAmountOpt), p.GetValueForOption(tShapeOpt));
            FinishTerrain(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message, r.TilesChanged);
        }));

        var tOpCliff = new Argument<TerrainCommand.CliffOp>("op", "raise|lower|set");
        var tLevelOpt = new Option<int>("--level", () => 1,
            "Step count for raise/lower; absolute level for set. Default: 1.");
        var terrainCliff = new Command("cliff",
            "Raise/lower/set the cliff (stepped-terrain) level over a brush.")
        { mapArg, tCX, tCY, tRadius, tOpCliff, tLevelOpt, tShapeOpt, setOut };
        terrainCliff.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TerrainCommand.Cliff(doc,
                p.GetValueForArgument(tCX), p.GetValueForArgument(tCY), p.GetValueForArgument(tRadius),
                p.GetValueForArgument(tOpCliff), p.GetValueForOption(tLevelOpt), p.GetValueForOption(tShapeOpt));
            FinishTerrain(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message, r.TilesChanged);
        }));

        var tRampOff = new Option<bool>("--off", () => false, "Clear the ramp flag instead of setting it.");
        var terrainRamp = new Command("ramp",
            "Toggle the ramp (sloped cliff transition) flag over a brush.")
        { mapArg, tCX, tCY, tRadius, tRampOff, tShapeOpt, setOut };
        terrainRamp.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TerrainCommand.Ramp(doc,
                p.GetValueForArgument(tCX), p.GetValueForArgument(tCY), p.GetValueForArgument(tRadius),
                !p.GetValueForOption(tRampOff), p.GetValueForOption(tShapeOpt));
            FinishTerrain(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message, r.TilesChanged);
        }));

        var tTexArg = new Argument<int>("texture", "Ground texture slot index in the map's tileset table.");
        var tVarOpt = new Option<int?>("--variation", () => null, "Tile variation; omit for default.");
        var terrainPaint = new Command("paint", "Paint a ground texture over a brush.")
        { mapArg, tCX, tCY, tRadius, tTexArg, tVarOpt, tShapeOpt, setOut };
        terrainPaint.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TerrainCommand.Paint(doc,
                p.GetValueForArgument(tCX), p.GetValueForArgument(tCY), p.GetValueForArgument(tRadius),
                p.GetValueForArgument(tTexArg), p.GetValueForOption(tVarOpt), p.GetValueForOption(tShapeOpt));
            FinishTerrain(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message, r.TilesChanged);
        }));

        var tOpWater = new Argument<TerrainCommand.WaterOp>("op", "set|raise|lower|remove");
        var tWaterAmount = new Option<float>("--amount", () => 0f,
            "Absolute height for set; delta for raise/lower. Default: 0.");
        var terrainWater = new Command("water", "Set/raise/lower/remove water over a brush.")
        { mapArg, tCX, tCY, tRadius, tOpWater, tWaterAmount, tShapeOpt, setOut };
        terrainWater.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TerrainCommand.Water(doc,
                p.GetValueForArgument(tCX), p.GetValueForArgument(tCY), p.GetValueForArgument(tRadius),
                p.GetValueForArgument(tOpWater), p.GetValueForOption(tWaterAmount), p.GetValueForOption(tShapeOpt));
            FinishTerrain(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message, r.TilesChanged);
        }));

        var tBlightClear = new Option<bool>("--clear", () => false, "Clear blight instead of setting it.");
        var terrainBlight = new Command("blight",
            "Set or clear the blight (corrupted ground) flag over a brush.")
        { mapArg, tCX, tCY, tRadius, tBlightClear, tShapeOpt, setOut };
        terrainBlight.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TerrainCommand.Blight(doc,
                p.GetValueForArgument(tCX), p.GetValueForArgument(tCY), p.GetValueForArgument(tRadius),
                !p.GetValueForOption(tBlightClear), p.GetValueForOption(tShapeOpt));
            FinishTerrain(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message, r.TilesChanged);
        }));

        var terrain = new Command("terrain",
            "Terrain editing: height, cliffs, ramps, textures, water, blight.");
        terrain.AddCommand(terrainStats);
        terrain.AddCommand(terrainDeform);
        terrain.AddCommand(terrainCliff);
        terrain.AddCommand(terrainRamp);
        terrain.AddCommand(terrainPaint);
        terrain.AddCommand(terrainWater);
        terrain.AddCommand(terrainBlight);

        // ---- sound: edit the map's sound catalog (war3map.w3s) ----
        var soundList = new Command("list", "List the map's sound definitions.") { mapArg };
        soundList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var sounds = SoundCommand.List(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), sounds,
                () => sounds.Count == 0
                    ? "(no sounds)"
                    : string.Join("\n", sounds.Select(s =>
                        $"{s.Name}  file={s.FilePath}  ch={s.Channel}  vol={s.Volume}  flags={s.Flags}")));
        }));

        var soundNameArg = new Argument<string>("name", "Sound label (the key triggers/UI reference).");
        var soundFileOpt = new Option<string?>("--file",
            "Audio file path inside the map (e.g. war3mapImported\\snd.wav).");
        var soundAdd = new Command("add", "Add a new sound definition and save the edited map.")
        { mapArg, soundNameArg, soundFileOpt, setOut };
        soundAdd.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = SoundCommand.Add(doc, p.GetValueForArgument(soundNameArg),
                p.GetValueForOption(soundFileOpt));
            FinishSound(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r);
        }));

        var soundFieldArg = new Argument<string>("field",
            "Field: " + string.Join("|", SoundCommand.EditableFields) + ".");
        var soundValueArg = new Argument<string>("value", "New value for the field.");
        var soundSet = new Command("set", "Set a field on a sound and save the edited map.")
        { mapArg, soundNameArg, soundFieldArg, soundValueArg, setOut };
        soundSet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = SoundCommand.Set(doc, p.GetValueForArgument(soundNameArg),
                p.GetValueForArgument(soundFieldArg), p.GetValueForArgument(soundValueArg));
            FinishSound(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r);
        }));

        var soundRemove = new Command("remove", "Remove a sound definition and save the edited map.")
        { mapArg, soundNameArg, setOut };
        soundRemove.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = SoundCommand.Remove(doc, p.GetValueForArgument(soundNameArg));
            FinishSound(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r);
        }));

        var sound = new Command("sound",
            "Sound catalog editing (war3map.w3s): list, add, set, remove.");
        sound.AddCommand(soundList);
        sound.AddCommand(soundAdd);
        sound.AddCommand(soundSet);
        sound.AddCommand(soundRemove);

        // ---- camera: edit the map's camera catalog (war3map.w3c) ----
        var camList = new Command("list", "List the map's cameras.") { mapArg };
        camList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var cams = CameraCommand.List(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), cams,
                () => cams.Count == 0
                    ? "(no cameras)"
                    : string.Join("\n", cams.Select(c =>
                        $"{c.Name}  target=({c.TargetX:0},{c.TargetY:0})  rot={c.Rotation:0}  aoa={c.AngleOfAttack:0}  dist={c.TargetDistance:0}  fov={c.FieldOfView:0}")));
        }));

        var camNameArg = new Argument<string>("name", "Camera name.");
        var camXArg = new Argument<float>("x", "Camera target X (map coordinates).");
        var camYArg = new Argument<float>("y", "Camera target Y (map coordinates).");
        var camAdd = new Command("add", "Add a camera at a target position and save the edited map.")
        { mapArg, camNameArg, camXArg, camYArg, setOut };
        camAdd.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = CameraCommand.Add(doc, p.GetValueForArgument(camNameArg),
                p.GetValueForArgument(camXArg), p.GetValueForArgument(camYArg));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var camFieldArg = new Argument<string>("field",
            "Field: " + string.Join("|", CameraCommand.EditableFields) + ".");
        var camValueArg = new Argument<string>("value", "New value for the field.");
        var camSet = new Command("set", "Set a field on a camera and save the edited map.")
        { mapArg, camNameArg, camFieldArg, camValueArg, setOut };
        camSet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = CameraCommand.Set(doc, p.GetValueForArgument(camNameArg),
                p.GetValueForArgument(camFieldArg), p.GetValueForArgument(camValueArg));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var camRemove = new Command("remove", "Remove a camera and save the edited map.")
        { mapArg, camNameArg, setOut };
        camRemove.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = CameraCommand.Remove(doc, p.GetValueForArgument(camNameArg));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var camera = new Command("camera",
            "Camera catalog editing (war3map.w3c): list, add, set, remove.");
        camera.AddCommand(camList);
        camera.AddCommand(camAdd);
        camera.AddCommand(camSet);
        camera.AddCommand(camRemove);

        // ---- pathing: HiveWE-style brush over the pathing map (war3map.wpm) ----
        var pathCX = new Argument<int>("cx", "Brush centre pathing-cell X.");
        var pathCY = new Argument<int>("cy", "Brush centre pathing-cell Y.");
        var pathRadius = new Argument<int>("radius", "Brush radius in pathing cells.");
        var pathFlagsArg = new Argument<string>("flags",
            "Pathing bits to affect (comma-separated): Walk, Fly, Build, Blight, Water. "
            + "A set bit RESTRICTS that capability (Walk set = units cannot walk there).");
        var pathOpOpt = new Option<PathingCommand.BrushOp>(
            "--op", () => PathingCommand.BrushOp.Set, "How the bits combine: Set, Clear or Toggle.");
        var pathShapeOpt = new Option<PathingCommand.BrushShape>(
            "--shape", () => PathingCommand.BrushShape.Circle, "Brush footprint: Circle or Square.");
        var pathPaint = new Command("paint",
            "Paint pathing bits over a brush and save the edited map. Writes war3map.wpm.")
        { mapArg, pathCX, pathCY, pathRadius, pathFlagsArg, pathOpOpt, pathShapeOpt, setOut };
        pathPaint.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var flagText = p.GetValueForArgument(pathFlagsArg).Replace(" ", "");
            if (!Enum.TryParse<War3Net.Build.Environment.PathingType>(flagText, ignoreCase: true, out var flags))
            {
                var msg = $"unknown pathing flag(s) '{flagText}'. Valid: "
                    + string.Join(", ", Enum.GetNames(typeof(War3Net.Build.Environment.PathingType)));
                Emit(p.GetValueForOption(jsonOption), new { Ok = false, Message = msg }, () => msg);
                exitCode[0] = 1;
                return;
            }
            var doc = MapDocument.Load(map);
            var r = PathingCommand.Paint(doc,
                p.GetValueForArgument(pathCX), p.GetValueForArgument(pathCY),
                p.GetValueForArgument(pathRadius), flags,
                p.GetValueForOption(pathOpOpt), p.GetValueForOption(pathShapeOpt));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc,
                r.Ok, r.Ok ? $"{r.Message} ({r.CellsChanged} cells)" : r.Message);
        }));
        var pathing = new Command("pathing",
            "Pathing-map editing (war3map.wpm): paint walk/build/fly/water bits over a brush.");
        pathing.AddCommand(pathPaint);

        // ---- map-info: read/edit the editable scenario fields (war3map.w3i) ----
        var miGet = new Command("get", "Show the map's editable scenario fields.") { mapArg };
        miGet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var f = MapInfoCommand.Read(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), f, () => string.Join("\n", new[]
            {
                $"Name        : {f.MapName}",
                $"Author      : {f.Author}",
                $"Description : {f.Description}",
                $"Players rec : {f.RecommendedPlayers}",
                $"Tileset     : {f.Tileset}",
                $"Playable    : {f.PlayableWidth} x {f.PlayableHeight}",
            }));
        }));
        var miFieldArg = new Argument<string>("field",
            "Field: " + string.Join("|", MapInfoCommand.EditableFields) + ".");
        var miValueArg = new Argument<string>("value", "New value for the field.");
        var miSet = new Command("set", "Set a scenario field and save the edited map.")
        { mapArg, miFieldArg, miValueArg, setOut };
        miSet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var f = MapInfoCommand.Set(doc, p.GetValueForArgument(miFieldArg), p.GetValueForArgument(miValueArg));
            var dest = p.GetValueForOption(setOut) ?? Path.Combine(
                Path.GetDirectoryName(map) ?? "",
                Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption), new { Ok = true, Fields = f, SavedTo = dest },
                () => $"set {p.GetValueForArgument(miFieldArg)} = {p.GetValueForArgument(miValueArg)}\nsaved: {dest}");
        }));
        var mapInfo = new Command("map-info",
            "Scenario fields (war3map.w3i): get, set (name, author, description, tileset, ...).");
        mapInfo.AddCommand(miGet);
        mapInfo.AddCommand(miSet);

        // ---- player / force: the Scenario Players and Forces dialogs (war3map.w3i) ----
        var playerList = new Command("list", "List the map's player slots.") { mapArg };
        playerList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var players = PlayerForceCommand.GetPlayers(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), players,
                () => players.Count == 0 ? "(no players)" : string.Join("\n", players.Select(pl =>
                    $"[{pl.Id}] {pl.Name}  {pl.Race}/{pl.Controller}  rgb=({pl.Color.R},{pl.Color.G},{pl.Color.B})  fixedStart={pl.FixedStartPosition}")));
        }));
        var pfPlayerId = new Argument<int>("player", "Player id (0-based).");
        var pfForceIndex = new Argument<int>("force", "Force index (0-based).");
        var playerSetForce = new Command("set-force",
            "Move a player into a force and save the edited map.")
        { mapArg, pfPlayerId, pfForceIndex, setOut };
        playerSetForce.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = PlayerForceCommand.SetPlayerForce(doc,
                p.GetValueForArgument(pfPlayerId), p.GetValueForArgument(pfForceIndex));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));
        var player = new Command("player", "Player slots (war3map.w3i): list, set-force.");
        player.AddCommand(playerList);
        player.AddCommand(playerSetForce);

        var forceList = new Command("list", "List the map's forces (teams).") { mapArg };
        forceList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var forces = PlayerForceCommand.GetForces(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), forces,
                () => forces.Count == 0 ? "(no forces)" : string.Join("\n", forces.Select(f =>
                    $"[{f.Index}] {f.Name}  players=[{string.Join(",", f.PlayerIds)}]  allied={f.Allied} alliedVictory={f.AlliedVictory} vision={f.SharedVision} control={f.SharedUnitControl}")));
        }));
        var ffAllied = new Argument<bool>("allied", "Allied.");
        var ffAlliedVictory = new Argument<bool>("alliedVictory", "Allied victory.");
        var ffVision = new Argument<bool>("sharedVision", "Shared vision.");
        var ffControl = new Argument<bool>("sharedControl", "Shared unit control.");
        var ffAdvControl = new Argument<bool>("sharedAdvControl", "Shared advanced unit control.");
        var forceSetFlags = new Command("set-flags",
            "Set a force's alliance/sharing flags and save the edited map.")
        { mapArg, pfForceIndex, ffAllied, ffAlliedVictory, ffVision, ffControl, ffAdvControl, setOut };
        forceSetFlags.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = PlayerForceCommand.SetForceFlags(doc, p.GetValueForArgument(pfForceIndex),
                p.GetValueForArgument(ffAllied), p.GetValueForArgument(ffAlliedVictory),
                p.GetValueForArgument(ffVision), p.GetValueForArgument(ffControl),
                p.GetValueForArgument(ffAdvControl));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));
        var force = new Command("force", "Forces / teams (war3map.w3i): list, set-flags.");
        force.AddCommand(forceList);
        force.AddCommand(forceSetFlags);

        // ---- file: raw/text editing of an internal archive entry ----
        // Wc3.Commands.FileEditCommand already carried this and no front-end exposed it, so
        // editing an override file like war3mapSkin.txt meant leaving the toolkit. Writes go
        // through the raw byte path rather than a text path, because a map's override files
        // vary in encoding (some carry a UTF-8 BOM, some do not) and round-tripping the exact
        // bytes is the only way to stay byte-faithful. Content comes from a file on disk, not
        // from an argument, because these payloads are multi-line and full of pipes and
        // colour codes that no shell quotes cleanly.
        var feNameArg = new Argument<string>("internal-path",
            "Internal archive path, for example war3mapSkin.txt.");
        var feFrom = new Option<string>(new[] { "--from" },
            "Disk file whose exact bytes become the new payload.") { IsRequired = true };

        var fileList = new Command("list",
            "List archive entries with their CURRENT size, reflecting pending replacements.")
        { mapArg };
        fileList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var files = FileEditCommand.ListFiles(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), files,
                () => string.Join("\n", files.Select(f =>
                    $"{f.Name ?? "(unnamed)",-58} {f.SizeBytes,10}  dirty={f.IsDirty}")));
        }));

        var fileGetText = new Command("get-text",
            "Print an internal file decoded as UTF-8, with any leading BOM stripped.")
        { mapArg, feNameArg };
        fileGetText.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string text = FileEditCommand.ReadText(
                MapDocument.Load(p.GetValueForArgument(mapArg)), p.GetValueForArgument(feNameArg));
            Emit(p.GetValueForOption(jsonOption),
                new { Name = p.GetValueForArgument(feNameArg), Text = text }, () => text);
        }));

        var fileSet = new Command("set",
            "Replace an internal file's bytes from a disk file and save the edited map.")
        { mapArg, feNameArg, feFrom, setOut };
        fileSet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string name = p.GetValueForArgument(feNameArg);
            string from = p.GetValueForOption(feFrom)!;
            var doc = MapDocument.Load(map);
            var r = FileEditCommand.AddOrReplace(doc, name, File.ReadAllBytes(from));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc,
                true, $"{(r.Replaced ? "replaced" : "added")} {r.Name}, {r.SizeBytes} bytes");
        }));

        var file = new Command("file",
            "Internal archive files, list, get-text, set (raw byte replace from disk).");
        file.AddCommand(fileList);
        file.AddCommand(fileGetText);
        file.AddCommand(fileSet);

        // ---- repair: reusable fixups for generated/byproduct maps ----
        var repair = new Command("repair", "Repair generated/byproduct map issues and save the edited copy.");
        var repairGeneratedHeroes = new Command("generated-heroes",
            "Repair the generated preplaced-hero helper block so finalized heroes use distinct owner slots and owner-aware spell wiring.")
        { mapArg, setOut };
        repairGeneratedHeroes.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = GeneratedMapRepairCommand.RepairGeneratedHeroes(doc);
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => Render.GeneratedHeroRepair(r));
                exitCode[0] = 1;
                return;
            }

            var dest = p.GetValueForOption(setOut) ?? Path.Combine(
                Path.GetDirectoryName(map) ?? "",
                Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new
                {
                    r.Ok,
                    r.Message,
                    r.PlayerSlotsBefore,
                    r.PlayerSlotsAfter,
                    r.ScriptUpdated,
                    r.MapInfoUpdated,
                    r.HeaderUpdated,
                    r.Heroes,
                    SavedTo = dest,
                },
                () => Render.GeneratedHeroRepair(r) + $"\nsaved: {dest}");
        }));
        repair.AddCommand(repairGeneratedHeroes);

        var repairApply = new Option<bool>("--apply",
            "Apply the detected Warcraft III 3.0.0 compatibility repairs and save a new map.");
        var repairReforged3 = new Command("reforged-3",
            "Detect or repair legacy SLK-map incompatibilities introduced by Reforged 3.0.0 (build 24268).")
        { mapArg, repairApply, setOut };
        repairReforged3.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            bool apply = p.GetValueForOption(repairApply);
            string? requestedOut = p.GetValueForOption(setOut);
            if (!apply && requestedOut is not null)
            {
                const string message = "--out requires --apply; without --apply this command is a read-only check";
                Emit(p.GetValueForOption(jsonOption), new { Ok = false, Message = message }, () => message);
                exitCode[0] = 1;
                return;
            }

            var doc = MapDocument.Load(map);
            var r = Reforged3RepairCommand.Execute(doc, apply);
            string? savedTo = null;
            if (apply)
            {
                savedTo = requestedOut ?? Path.Combine(
                    Path.GetDirectoryName(map) ?? "",
                    Path.GetFileNameWithoutExtension(map) + ".reforged-3-fixed" + Path.GetExtension(map));
                doc.Save(savedTo);
            }

            Emit(p.GetValueForOption(jsonOption),
                new
                {
                    r.Ok,
                    r.Message,
                    r.Applied,
                    r.IssueCount,
                    r.FileColumnsRemoved,
                    r.ModelsMoved,
                    r.NumericCellsNormalized,
                    r.ButtonPositionsCompleted,
                    r.StrayCommentTerminatorsRemoved,
                    r.AbilityLevelColumnsAdded,
                    r.ChangedFiles,
                    SavedTo = savedTo,
                },
                () => Render.Reforged3Repair(r, savedTo));
        }));
        repair.AddCommand(repairReforged3);
        repair.AddCommand(repairPortraits);
        repair.AddCommand(repairPointers);
        repair.AddCommand(repairModels);
        repair.AddCommand(repairUabi);
        repair.AddCommand(repairAudit);
        repair.AddCommand(repairPreload);

        // ---- new: create a blank, World-Editor-openable map ----
        var newOut = new Argument<string>("out", "Path to write the new .w3x/.w3m map.");
        var newNameOpt = new Option<string?>("--name", "Map name (default: Blank Map).");
        var newTilesOpt = new Option<int?>("--tiles", "Playable size in tiles per edge (default: 32).");
        var newMap = new Command("new", "Create a blank, World-Editor-openable map at the given path.")
        { newOut, newNameOpt, newTilesOpt };
        newMap.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var opts = new BlankMapOptions();
            if (p.GetValueForOption(newNameOpt) is { } n && n.Length > 0) opts = opts with { MapName = n };
            if (p.GetValueForOption(newTilesOpt) is { } t) opts = opts with { TileEdge = t };
            byte[] bytes = BlankMap.CreateArchiveBytes(opts);
            string dest = p.GetValueForArgument(newOut);
            File.WriteAllBytes(dest, bytes);
            Emit(p.GetValueForOption(jsonOption),
                new { Ok = true, MapName = opts.MapName, opts.TileEdge, Bytes = bytes.Length, SavedTo = dest },
                () => $"created \"{opts.MapName}\" ({opts.TileEdge}x{opts.TileEdge} tiles, {bytes.Length} bytes)\nsaved: {dest}");
        }));

        // --- trigger catalog (read-only GUI-trigger reference from UI\TriggerData.txt) ---
        var trigFileOpt = new Option<string?>("--file",
            "Read the catalog from an explicit TriggerData.txt instead of the installed game.");
        var trigKindOpt = new Option<string?>("--kind",
            "Filter by kind: event|condition|action|call.");
        var trigSearchOpt = new Option<string?>("--search",
            "Case-insensitive substring filter over function name and display name.");

        var trigList = new Command("list",
            "List GUI-trigger functions from the World-Editor catalog.")
        { trigFileOpt, trigKindOpt, trigSearchOpt };
        trigList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var cat = TriggerCatalogCommand.Load(
                p.GetValueForOption(trigFileOpt), p.GetValueForOption(gameDirOption), out var source);
            var res = TriggerCatalogCommand.List(cat, source,
                p.GetValueForOption(trigKindOpt), p.GetValueForOption(trigSearchOpt));
            Emit(p.GetValueForOption(jsonOption), res,
                () => res.Total == 0
                    ? "(no matching functions)"
                    : string.Join("\n", res.Functions.Select(f =>
                        $"{f.Kind,-9} {f.Name}" +
                        (f.ReturnType is null ? "" : $" : {f.ReturnType}") +
                        $"  ({string.Join(", ", f.ArgumentTypes)})" +
                        (f.DisplayName is null ? "" : $"  — {f.DisplayName}")))
                      + $"\n\n{res.Total} function(s) from {res.Source}");
        }));

        var trigNameArg = new Argument<string>("name", "Function name (e.g. DoNothing).");
        var trigDescribe = new Command("describe",
            "Show full detail for one GUI-trigger function.")
        { trigNameArg, trigFileOpt };
        trigDescribe.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var cat = TriggerCatalogCommand.Load(
                p.GetValueForOption(trigFileOpt), p.GetValueForOption(gameDirOption), out _);
            string name = p.GetValueForArgument(trigNameArg);
            var d = TriggerCatalogCommand.Describe(cat, name);
            if (d is null)
            {
                Emit(p.GetValueForOption(jsonOption), new { found = false, name },
                    () => $"No such function: {name}");
                return;
            }
            Emit(p.GetValueForOption(jsonOption), d, () => string.Join("\n", new[]
            {
                $"{d.Name}  [{d.Kind}]",
                d.DisplayName is null ? null : $"  display   : {d.DisplayName}",
                $"  version   : {d.GameVersion}",
                d.Kind == "Call" ? $"  in events : {d.UsableInEvents}" : null,
                d.ReturnType is null ? null : $"  returns   : {d.ReturnType}",
                $"  args      : {(d.ArgumentTypes.Count == 0 ? "(none)" : string.Join(", ", d.ArgumentTypes))}",
                d.ParametersLayout is null ? null : $"  layout    : {d.ParametersLayout}",
                d.Defaults is null ? null : $"  defaults  : {d.Defaults}",
                d.Category is null ? null : $"  category  : {d.Category}",
            }.Where(x => x is not null)!));
        }));

        var trigCatalog = new Command("catalog",
            "GUI-trigger catalog from UI\\TriggerData.txt: list, describe.");
        trigCatalog.AddCommand(trigList);
        trigCatalog.AddCommand(trigDescribe);

        var trigger = new Command("trigger", "GUI trigger tooling (World-Editor catalog).");
        trigger.AddCommand(trigCatalog);

        // Blizzard publishes no diff of what a patch changes inside the game data, so the only
        // way to see it is to have kept the previous copy. Snapshot, commit, re-run after every
        // update, and git shows every added native, changed default and retuned constant.
        var snapOut = new Argument<string>("out-dir", "Directory to write the snapshot into.");
        var snapLocales = new Option<bool>("--locales",
            "Include the per-locale string tables (much larger, rarely what a diff needs).");
        var snapNoStrings = new Option<bool>("--no-binary-strings",
            "Skip the client/editor string dump. That dump is the only place editor-only "
            + "changes show up, since the World Editor's behaviour is not in the game data.");
        var gamedataSnapshot = new Command("snapshot",
            "Dump the base game's text/data files (plus client and editor strings) for diffing across patches.")
            { snapOut, snapLocales, snapNoStrings };
        gamedataSnapshot.SetHandler(
            (string outDir, bool locales, bool noStrings, string? gameDir, bool json) => RunSafely(() =>
            {
                var r = GameDataSnapshotCommand.Run(gameDir, outDir,
                    includeLocales: locales, includeBinaryStrings: !noStrings);
                Emit(json, r, () => Render.GameDataSnapshot(r));
            }),
            snapOut, snapLocales, snapNoStrings, gameDirOption, jsonOption);

        var gamedata = new Command("gamedata", "Base game data, for tracking what a patch changes.");
        gamedata.AddCommand(gamedataSnapshot);

        root.AddCommand(info); root.AddCommand(ls); root.AddCommand(rt);
        root.AddCommand(search); root.AddCommand(diff); root.AddCommand(obj);
        root.AddCommand(extract); root.AddCommand(render); root.AddCommand(renderModel);
        root.AddCommand(script); root.AddCommand(bundle); root.AddCommand(port);
        root.AddCommand(convert); root.AddCommand(validate); root.AddCommand(audit);
        root.AddCommand(deprotect);
        root.AddCommand(place); root.AddCommand(palette); root.AddCommand(terrain);
        root.AddCommand(sound); root.AddCommand(camera); root.AddCommand(pathing);
        root.AddCommand(mapInfo); root.AddCommand(player); root.AddCommand(force);
        root.AddCommand(file); root.AddCommand(repair);
        root.AddCommand(newMap); root.AddCommand(trigger); root.AddCommand(gamedata);

        return root;
    }
}
