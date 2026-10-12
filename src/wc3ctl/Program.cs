// src/wc3ctl/Program.cs
using System.CommandLine;
using System.CommandLine.Parsing;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3Ctl;

public static class Program
{
    /// <summary>Convert TargetSource enum to plain English for display.</summary>
    private static string TargetSourceLabel(TargetSource src) => src switch
    {
        TargetSource.Option => "option",
        TargetSource.Environment => "environment variable",
        TargetSource.Settings => "settings",
        TargetSource.Default => "default",
        _ => "unknown",
    };

    /// <summary>Game install build version, or "not found".</summary>
    private static string GameInstallLine(string? schemasDir)
    {
        try
        {
            string? installDir = Wc3.GameData.GameInstall.Locate();
            if (installDir is not null)
            {
                string? ib = Wc3.GameData.GameInstall.ReadBuildVersion(installDir);
                if (ib is not null) return ib;
            }
        }
        catch { /* no game install */ }
        return "not found";
    }

    public static async Task<int> Main(string[] args)
    {
        if (McpCommand.TryRun(args) is { } mcp) return await mcp;

        // Handle --version before any command processing.
        // System.CommandLine auto-adds a --version handler, but we override it here.
        if (Array.IndexOf(args, "--version") >= 0)
        {
            var asm = typeof(Program).Assembly;
            var v = asm.GetName().Version;
            Console.WriteLine(v?.ToString(3) ?? "0.0.0");
            try
            {
                var settings = TargetBuildSettings.Get();
                Console.WriteLine("target  " + settings.Build + "  (" + TargetSourceLabel(settings.Source) + "), game install " + GameInstallLine(null));
            }
            catch { /* schemas unavailable */ }
            return 0;
        }

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

        // Column-safe shortening for table output. Only the human rendering uses it, the JSON
        // carries the full value, so a truncated column can never become a truncated result.
        static string Trunc(string s, int max) =>
            string.IsNullOrEmpty(s) ? string.Empty
            : s.Length <= max ? s
            : s[..Math.Max(0, max - 1)] + "…";

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

        // Best-effort World-Editor catalog for the to-script renderer. A machine without the game
        // installed has none, and that is not an error: the renderer keeps raw preset names and
        // says in its output that it could not resolve them.
        static Wc3.GameData.TriggerDataCatalog? TryCatalog(string? gameDir) =>
            TriggerCatalogCommand.TryLoad(null, gameDir, out _);

        var info = new Command("info", "Show map metadata.") { mapArg };
        info.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = InfoCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.Info(r));
        }), mapArg, jsonOption);

        var lsHarvestOption = new Option<bool>("--harvest",
            "Recover unnamed entries' names from the map's own script and object data before listing "
            + "(a second pass worth paying for on a protected map, wasted on a healthy one).");
        var ls = new Command("ls", "List internal files.") { mapArg, lsHarvestOption };
        ls.SetHandler((string map, bool harvest, bool json) => RunSafely(() =>
        {
            var doc = MapDocument.Load(map);
            if (harvest) doc.HarvestAssetNames();
            var r = ListCommand.Execute(doc, typeUnnamed: true);
            Emit(json, r, () => Render.List(r));
        }), mapArg, lsHarvestOption, jsonOption);

        var overview = new Command("overview",
            "One call per map, metadata, every archive file, the custom object counts by kind and the diagnostics.")
        { mapArg };
        overview.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = MapOverviewCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.MapOverview(r));
        }), mapArg, jsonOption);

        var rt = new Command("roundtrip", "Verify byte-faithful round-trip.") { mapArg };
        rt.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = RoundtripCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.Roundtrip(r));
        }), mapArg, jsonOption);

        var queryArg = new Argument<string>("query", "Substring to search for.");
        var search = new Command("search",
            "Search a map for text, ignoring case: file names, script lines, string-table entries and "
            + "object data (custom rawcodes and field values).") { mapArg, queryArg };
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
        var objForm = new Command("form",
            "Show an object the way an editor should: fields grouped and ordered like the World "
            + "Editor, fields that do not apply to this object hidden, legal ranges shown, and "
            + "which object-data layer each field is written to.")
        { mapArg, objRawcode, objGetKind, jsonOption };
        objForm.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var kindToken = p.GetValueForOption(objGetKind);
            var form = ObjectFormCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                kindToken is null ? ObjectKind.Unit : ObjectKinds.Parse(kindToken),
                p.GetValueForArgument(objRawcode),
                p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), form, () => Render.ObjectForm(form));
        }));
        obj.AddCommand(objForm);
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

        // The one rule every mutating object command shares: the default output sits beside the
        // input, and writing in place is refused so an original is never clobbered. Emits the
        // refusal and returns null when the caller must stop.
        string? ObjectEditedPath(ParseResult p, string map)
        {
            var dest = p.GetValueForOption(setOut) ?? Path.Combine(
                Path.GetDirectoryName(map) ?? "",
                Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
            if (Path.GetFullPath(dest).Equals(Path.GetFullPath(map), StringComparison.OrdinalIgnoreCase))
            {
                const string inPlace =
                    "refusing to overwrite the input map, pass -o <path> for a distinct output";
                Emit(p.GetValueForOption(jsonOption),
                    new { Ok = false, Message = inPlace, SavedTo = (string?)null },
                    () => inPlace);
                exitCode[0] = 1;
                return null;
            }
            return dest;
        }

        // Split a "code=value" pair on the first '='; the value may itself contain '='.
        static bool TrySplitPair(string token, out string code, out string value)
        {
            int eq = token.IndexOf('=');
            if (eq <= 0) { code = ""; value = ""; return false; }
            code = token[..eq];
            value = token[(eq + 1)..];
            return true;
        }

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
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
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
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.NewRawcode, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        obj.AddCommand(objNew);

        var objSearchQuery = new Argument<string>("query",
            "Text to find in object field values (case-insensitive substring).");
        var objSearchField = new Option<string?>("--field", "Restrict the search to one field code.");
        var objSearch = new Command("search",
            "Search every custom/modified object's field values (base and map) for text. Read-only.")
        { mapArg, objSearchQuery, objSearchField, objGetKind };
        objSearch.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var kindToken = p.GetValueForOption(objGetKind);
            var r = ObjectSearchCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForArgument(objSearchQuery),
                kindToken is null ? null : ObjectKinds.Parse(kindToken),
                p.GetValueForOption(objSearchField),
                p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ObjectSearch(r));
        }));
        obj.AddCommand(objSearch);

        var objNoop = new Command("noop-fields",
            "List fields whose map value equals the base game value, so the edit changes nothing. "
            + "Read-only. Needs --game-dir for the base, or it reports map-only.")
        { mapArg, objGetKind };
        objNoop.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var kindToken = p.GetValueForOption(objGetKind);
            var r = ObjectNoopFieldsCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                kindToken is null ? null : ObjectKinds.Parse(kindToken),
                p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ObjectNoopFields(r));
        }));
        obj.AddCommand(objNoop);

        var objDeprecatedTarget = new Option<string>("--target",
            "Target build name or path to a schema folder.") { IsRequired = true };
        var objDeprecatedSchemas = new Option<string?>("--schemas",
            "Directory of schema folders. Default: search <base>/schemas then walk up to data/schemas.");
        var objDeprecated = new Command("deprecated",
            "List object fields the target build's schema does not define but the map still sets. Read-only.")
        { mapArg, objDeprecatedTarget, objDeprecatedSchemas };
        objDeprecated.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ObjectDeprecatedCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForOption(objDeprecatedTarget)!,
                p.GetValueForOption(objDeprecatedSchemas));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ObjectDeprecated(r));
        }));
        obj.AddCommand(objDeprecated);

        var objSuggestKind = new Argument<string>("kind",
            "Object type: unit|item|ability|destructable|doodad|buff|upgrade.");
        var objSuggestCount = new Option<int>("--count", () => 10, "How many rawcodes to propose.");
        var objSuggest = new Command("rawcode-suggest",
            "Propose free four-character rawcodes that collide with neither the map nor the "
            + "base game data for the kind. Read-only.")
        { mapArg, objSuggestKind, objSuggestCount };
        objSuggest.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ObjectRawcodeSuggestCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                ObjectKinds.Parse(p.GetValueForArgument(objSuggestKind)),
                p.GetValueForOption(objSuggestCount),
                p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ObjectRawcodeSuggest(r));
        }));
        obj.AddCommand(objSuggest);

        var objCloneKind = new Argument<string>("kind",
            "Object type: unit|item|ability|destructable|doodad|buff|upgrade.");
        var objCloneSource = new Argument<string>("rawcode", "Four-character source rawcode to clone.");
        var objCloneNew = new Argument<string>("new-rawcode", "Four-character rawcode for the clone.");
        var objClone = new Command("clone",
            "Deep-clone a map object (its field deltas, and level data for an ability) under a new "
            + "rawcode and save the edited map. Refuses a rawcode already taken.")
        { mapArg, objCloneKind, objCloneSource, objCloneNew, setOut };
        objClone.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = ObjectCloneCommand.Execute(doc,
                ObjectKinds.Parse(p.GetValueForArgument(objCloneKind)),
                p.GetValueForArgument(objCloneSource),
                p.GetValueForArgument(objCloneNew));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = p.GetValueForOption(setOut) ?? Path.Combine(
                Path.GetDirectoryName(map) ?? "",
                Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
            if (Path.GetFullPath(dest).Equals(Path.GetFullPath(map), StringComparison.OrdinalIgnoreCase))
            {
                const string inPlace = "refusing to overwrite the input map, pass -o <path> for a distinct output";
                Emit(p.GetValueForOption(jsonOption),
                    new { Ok = false, Message = inPlace, SavedTo = (string?)null },
                    () => inPlace);
                exitCode[0] = 1;
                return;
            }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.NewRawcode, r.FieldsCopied, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        obj.AddCommand(objClone);

        // ---- migrate: rewrite object data to a target build's field set ----
        var migrateTo = new Option<string>("--to",
            "Target build name or path to a schema folder (read like compat).") { IsRequired = true };
        var migrateSchemas = new Option<string?>("--schemas",
            "Directory of schema folders. Default: search <base>/schemas then walk up to data/schemas.");
        var objMigrate = new Command("migrate",
            "Rewrite the map's object data to the target build's field set, dropping fields the "
            + "target does not define and reporting each drop. Saves an edited copy.")
        { mapArg, migrateTo, migrateSchemas, setOut };
        objMigrate.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = ObjectMigrateCommand.Execute(doc, p.GetValueForOption(migrateTo)!, p.GetValueForOption(migrateSchemas));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.TargetBuild, r.FieldsKept, r.FieldsDropped, r.Drops, r.UncheckedKinds, SavedTo = dest },
                () => Render.ObjectMigrate(r, dest));
        }));
        obj.AddCommand(objMigrate);

        // ---- bulk-set: set one field on every object matching every where clause ----
        var bulkKind = new Option<string>("--kind", () => "unit",
            "Object type: unit|item|ability|destructable|doodad|buff|upgrade.");
        var bulkSet = new Option<string>("--set",
            "The field and value to write, as code=value (e.g. uhpm=1000).") { IsRequired = true };
        var bulkWhere = new Option<string[]>("--where",
            "A match clause code=value; repeat for several. An object must match every clause.")
        { AllowMultipleArgumentsPerToken = false };
        var objBulkSet = new Command("bulk-set",
            "Set one field on every object of a kind matching every --where clause, and save the "
            + "edited copy. Reports how many changed.")
        { mapArg, bulkKind, bulkSet, bulkWhere, setOut };
        objBulkSet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            if (!TrySplitPair(p.GetValueForOption(bulkSet)!, out var setField, out var setValue))
            {
                Emit(p.GetValueForOption(jsonOption), new { Ok = false, Message = "expected --set code=value" },
                    () => "expected --set code=value");
                exitCode[0] = 1;
                return;
            }
            var clauses = new List<BulkSetWhere>();
            foreach (var raw in p.GetValueForOption(bulkWhere) ?? Array.Empty<string>())
            {
                if (!TrySplitPair(raw, out var wc, out var wv))
                {
                    Emit(p.GetValueForOption(jsonOption), new { Ok = false, Message = $"expected --where code=value, got '{raw}'" },
                        () => $"expected --where code=value, got '{raw}'");
                    exitCode[0] = 1;
                    return;
                }
                clauses.Add(new BulkSetWhere(wc, wv));
            }
            var doc = MapDocument.Load(map);
            var r = ObjectBulkSetCommand.Execute(doc, ObjectKinds.Parse(p.GetValueForOption(bulkKind)!),
                setField, setValue, clauses, p.GetValueForOption(gameDirOption));
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.Kind, r.Field, r.Value, r.Matched, r.Changed, r.Skipped, r.Entries, SavedTo = dest },
                () => Render.ObjectBulkSet(r, dest));
        }));
        obj.AddCommand(objBulkSet);

        // ---- export: write one kind's objects to CSV ----
        var exportKind = new Option<string>("--kind", () => "unit",
            "Object type: unit|item|ability|destructable|doodad|buff|upgrade.");
        var exportOut = new Option<string>(new[] { "-o", "--out" },
            "CSV output path.") { IsRequired = true };
        var objExport = new Command("export",
            "Export the objects of a kind (base game data overlaid by the map) to CSV, one row per "
            + "object, one column per field code. Read-only.")
        { mapArg, exportKind, exportOut };
        objExport.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string outPath = p.GetValueForOption(exportOut)!;
            var r = ObjectExportCommand.Execute(MapDocument.Load(map),
                ObjectKinds.Parse(p.GetValueForOption(exportKind)!), p.GetValueForOption(gameDirOption));
            if (Path.GetDirectoryName(Path.GetFullPath(outPath)) is { Length: > 0 } dir)
                Directory.CreateDirectory(dir);
            File.WriteAllText(outPath, r.Csv);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.Kind, r.ObjectCount, r.Columns, r.Diagnostics, WrittenTo = Path.GetFullPath(outPath) },
                () => Render.ObjectExport(r, outPath));
        }));
        obj.AddCommand(objExport);

        // ---- import: read a kind's CSV back into the map ----
        var importKind = new Option<string>("--kind", () => "unit",
            "Object type: unit|item|ability|destructable|doodad|buff|upgrade.");
        var importCsv = new Option<string>("--csv", "Path to the CSV file to import.") { IsRequired = true };
        var objImport = new Command("import",
            "Import a CSV of one kind back into the map, adding or updating its objects. Refuses an "
            + "unknown column. Saves an edited copy.")
        { mapArg, importKind, importCsv, setOut };
        objImport.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string csvPath = p.GetValueForOption(importCsv)!;
            if (!File.Exists(csvPath))
            {
                Emit(p.GetValueForOption(jsonOption), new { Ok = false, Message = $"no CSV file at {csvPath}" },
                    () => $"no CSV file at {csvPath}");
                exitCode[0] = 1;
                return;
            }
            var doc = MapDocument.Load(map);
            var r = ObjectImportCommand.Execute(doc, ObjectKinds.Parse(p.GetValueForOption(importKind)!),
                File.ReadAllText(csvPath), p.GetValueForOption(gameDirOption));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.Kind, r.Added, r.Updated, r.FieldsSet, r.Diagnostics, SavedTo = dest },
                () => Render.ObjectImport(r, dest));
        }));
        obj.AddCommand(objImport);

        // ---- rename: rename a rawcode and repoint its references ----
        var renameKind = new Option<string>("--kind", () => "unit",
            "Object type: unit|item|ability|destructable|doodad|buff|upgrade.");
        var renameOld = new Argument<string>("old", "The rawcode to rename.");
        var renameNew = new Argument<string>("new", "The new rawcode.");
        var renameRefs = new Option<bool>("--rewrite-refs",
            "Also rewrite every object-data field value and script string literal that names the old rawcode.");
        var objRename = new Command("rename",
            "Rename an object rawcode and optionally rewrite every field and script literal that "
            + "references it. Saves an edited copy.")
        { mapArg, renameKind, renameOld, renameNew, renameRefs, setOut };
        objRename.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = ObjectRenameCommand.Execute(doc, ObjectKinds.Parse(p.GetValueForOption(renameKind)!),
                p.GetValueForArgument(renameOld), p.GetValueForArgument(renameNew), p.GetValueForOption(renameRefs));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.Kind, r.OldRawcode, r.NewRawcode, r.FieldsRewritten, r.ScriptLiteralsRewritten, SavedTo = dest },
                () => Render.ObjectRename(r, dest));
        }));
        obj.AddCommand(objRename);

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
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
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
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.CreationNumber, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        place.AddCommand(placeRegion);

        var placeUnitRawcode = new Argument<string>("rawcode", "Four-character unit type rawcode.");
        var placeOwnerArg = new Argument<int>("owner", "Owning player id (0-based; 0 = red).");
        var placeUnit = new Command("unit",
            "Place a unit at (x, y) and save the edited map. Writes war3mapUnits.doo, and the map's "
            + "script when a spawn call has to be wired (a preplaced unit no script creates "
            + "never appears in the game).")
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
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.CreationNumber, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        place.AddCommand(placeUnit);

        var placePlayerArg = new Argument<int>("player", "Player whose start location this is (0-based; 0 = red).");
        var placeStartLoc = new Command("start-location",
            "Place or move a player's start location at (x, y) and save the edited map. "
            + "One per player. An existing one for this player is moved. Writes war3mapUnits.doo, "
            + "and the map's script when a spawn call has to be wired.")
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
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.CreationNumber, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        place.AddCommand(placeStartLoc);

        var placeItemRawcode = new Argument<string>("rawcode", "Four-character item type rawcode.");
        var placeItem = new Command("item",
            "Place a preplaced item at (x, y) and save the edited map. Writes war3mapUnits.doo "
            + "(item slot), and the map's script when a spawn call has to be wired.")
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
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.CreationNumber, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        place.AddCommand(placeItem);

        var placeGridRawcode = new Argument<string>("rawcode", "Four-character unit or doodad type rawcode.");
        var placeGridRows = new Option<int>("--rows", "Number of grid rows.") { IsRequired = true };
        var placeGridCols = new Option<int>("--cols", "Number of grid columns.") { IsRequired = true };
        var placeGridSpacing = new Option<float>("--spacing", () => PlaceGridCommand.DefaultSpacing,
            "Spacing between adjacent cells in world units. Defaults to 128, one tile.");
        var placeGridOwner = new Option<int>("--owner", () => 0,
            "Owning player id for a unit grid (0-based, 0 = red). Defaults to 0.");
        var placeGridKind = new Option<string>("--kind", () => "unit",
            "Placement kind, unit or doodad. Defaults to unit.");
        var placeGrid = new Command("grid",
            "Place a regular rows x cols grid of copies of one type and save the edited map. "
            + "The grid is centred on the map origin. Writes war3mapUnits.doo for units or "
            + "war3map.doo for doodads.")
        { mapArg, placeGridRawcode, placeGridRows, placeGridCols, placeGridSpacing, placeGridOwner, placeGridKind, setOut };
        placeGrid.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = PlaceGridCommand.Execute(doc,
                p.GetValueForArgument(placeGridRawcode),
                p.GetValueForOption(placeGridRows),
                p.GetValueForOption(placeGridCols),
                p.GetValueForOption(placeGridSpacing),
                p.GetValueForOption(placeGridOwner),
                p.GetValueForOption(placeGridKind));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.Placed, r.CreationNumbers, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        place.AddCommand(placeGrid);

        var placeScatterRawcode = new Argument<string>("rawcode", "Four-character unit type rawcode.");
        var placeScatterCount = new Option<int>("--count", "Number of copies to place.") { IsRequired = true };
        var placeScatterRegion = new Option<string?>("--region",
            "Region name to scatter inside. Omit to scatter across the whole playable map.");
        var placeScatterSeed = new Option<int>("--seed", () => PlaceScatterCommand.DefaultSeed,
            "Deterministic random seed. Defaults to 0.");
        var placeScatter = new Command("scatter",
            "Place N copies of a unit type at deterministic random positions and save the edited map. "
            + "Positions are uniform inside a named region or the whole playable area, and the same "
            + "seed reproduces the same layout. Writes war3mapUnits.doo.")
        { mapArg, placeScatterRawcode, placeScatterCount, placeScatterRegion, placeScatterSeed, setOut };
        placeScatter.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = PlaceScatterCommand.Execute(doc,
                p.GetValueForArgument(placeScatterRawcode),
                p.GetValueForOption(placeScatterCount),
                p.GetValueForOption(placeScatterRegion),
                p.GetValueForOption(placeScatterSeed));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.Placed, r.CreationNumbers, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        place.AddCommand(placeScatter);

        var placeSync = new Command("sync",
            "Regenerate the runtime creation script (CreateAllUnits/CreateAllItems in war3map.j) "
            + "from war3mapUnits.doo so preplaced widgets actually spawn in game. Repairs maps whose "
            + "units were placed without the spawning script. Placing through wc3ctl does this "
            + "automatically; this repairs a map that predates it.")
        { mapArg, setOut };
        placeSync.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = PreplacedUnitsScript.Sync(doc);
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.Units, r.Items, SavedTo = dest },
                () => $"{r.Message}\nsaved: {dest}");
        }));
        place.AddCommand(placeSync);

        // ---- placed, census and reference hygiene over what is already on the map ----
        var placed = new Command("placed",
            "Placed-object census and reference checks, stats, missing-refs and fix-refs.");

        var placedStats = new Command("stats",
            "Count placed units and doodads by rawcode and kind, resolving each type's name from the "
            + "map's object data and, when --game-dir is given, the base game data. Read-only.")
        { mapArg };
        placedStats.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = PlacedStatsCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r,
                () => r.Types.Count == 0
                    ? r.Message
                    : r.Message + "\n" + string.Join("\n", r.Types.Select(t =>
                        $"{t.Count,6}  {t.Kind,-7}  {t.Rawcode}  {t.Name ?? "(no name)"}")));
        }));
        placed.AddCommand(placedStats);

        var placedMissing = new Command("missing-refs",
            "List placements whose rawcode is defined neither in the map's object data nor, when "
            + "--game-dir is given, in the base game data. Read-only.")
        { mapArg };
        placedMissing.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = PlacedMissingRefsCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r,
                () => r.Missing.Count == 0
                    ? r.Message
                    : r.Message + "\n" + string.Join("\n", r.Missing.Select(m =>
                        $"{m.File} #{m.CreationNumber}  {m.Kind,-7}  {m.Rawcode}  at ({m.X:0.#}, {m.Y:0.#})")));
        }));
        placed.AddCommand(placedMissing);

        var placedFixTo = new Option<string?>("--to",
            "Rewrite each missing placement's rawcode to this four-character code instead of dropping it.");
        var placedFix = new Command("fix-refs",
            "Drop (or, with --to, rewrite) placements that reference a missing rawcode and save the "
            + "edited map, reporting each change. Refuses when nothing is wrong. Writes "
            + "war3mapUnits.doo and war3map.doo.")
        { mapArg, placedFixTo, setOut };
        placedFix.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = PlacedFixRefsCommand.Execute(doc,
                p.GetValueForOption(gameDirOption), p.GetValueForOption(placedFixTo));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.Changed, r.Changes, SavedTo = dest },
                () => r.Changes.Count == 0
                    ? $"{r.Message}\nsaved: {dest}"
                    : r.Message + "\n" + string.Join("\n", r.Changes.Select(c =>
                        $"{c.File} #{c.CreationNumber}  {c.Action}  {c.Rawcode}"
                        + (c.ToRawcode is null ? "" : $" -> {c.ToRawcode}"))) + $"\nsaved: {dest}");
        }));
        placed.AddCommand(placedFix);

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
        var bundleOut = new Option<string?>(new[] { "-o", "--out" },
            "Write the resolved bundle to this JSON file, so 'bundle diff' and 'bundle merge' can read it.");
        var bundleUnit = new Command("unit",
            "Resolve everything a unit depends on: objects, asset files and trigger strings.")
        { mapArg, objRawcode, bundleOut };
        bundleUnit.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = BundleCommand.ResolveUnit(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForArgument(objRawcode),
                p.GetValueForOption(gameDirOption));
            if (p.GetValueForOption(bundleOut) is { } outPath)
            {
                BundleJson.Save(r, outPath);
                Console.WriteLine($"Wrote bundle to {outPath}");
            }
            else Emit(p.GetValueForOption(jsonOption), r, () => Render.BundleUnit(r));
        }));
        bundle.AddCommand(bundleUnit);

        var bundleObjKind = new Option<string>("--kind", () => "unit",
            "Root object type: unit|item|ability|destructable|doodad|buff|upgrade.");
        var bundleObject = new Command("object",
            "Resolve everything an object of any kind depends on ('bundle unit', generalized).")
        { mapArg, objRawcode, bundleObjKind, bundleOut };
        bundleObject.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = BundleCommand.ResolveObject(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                ObjectKinds.Parse(p.GetValueForOption(bundleObjKind)!),
                p.GetValueForArgument(objRawcode),
                p.GetValueForOption(gameDirOption));
            if (p.GetValueForOption(bundleOut) is { } outPath)
            {
                BundleJson.Save(r, outPath);
                Console.WriteLine($"Wrote bundle to {outPath}");
            }
            else Emit(p.GetValueForOption(jsonOption), r, () => Render.BundleUnit(r));
        }));
        bundle.AddCommand(bundleObject);

        var bundleA = new Argument<string>("bundle-a", "Path to a bundle JSON file (from 'bundle unit -o').");
        var bundleB = new Argument<string>("bundle-b", "Path to a second bundle JSON file.");
        var bundleDiff = new Command("diff",
            "Compare two dependency bundles by their object and asset members, listing what each adds.")
        { bundleA, bundleB };
        bundleDiff.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = BundleDiffCommand.Execute(
                BundleJson.Load(p.GetValueForArgument(bundleA)),
                BundleJson.Load(p.GetValueForArgument(bundleB)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.BundleDiff(r));
        }));
        bundle.AddCommand(bundleDiff);

        var bundleMergeOut = new Option<string?>(new[] { "-o", "--out" },
            "Path to write the merged bundle JSON.");
        var bundleMerge = new Command("merge",
            "Merge two dependency bundles into one, refusing when the same rawcode is defined differently.")
        { bundleA, bundleB, bundleMergeOut };
        bundleMerge.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = BundleMergeCommand.Execute(
                BundleJson.Load(p.GetValueForArgument(bundleA)),
                BundleJson.Load(p.GetValueForArgument(bundleB)));
            string? outPath = null;
            if (r.Ok)
            {
                outPath = p.GetValueForOption(bundleMergeOut) ?? throw new ArgumentException(
                    "-o <out> is required to write the merged bundle");
                BundleJson.Save(r.Merged!, outPath);
            }
            Emit(p.GetValueForOption(jsonOption), r, () => Render.BundleMerge(r, outPath));
            if (!r.Ok) exitCode[0] = 1;
        }));
        bundle.AddCommand(bundleMerge);


        // Loops that may never terminate. A JASS loop that cannot exit presents as a tight native
        // loop inside the game executable (the interpreter lives there), with no crash and no log,
        // which is indistinguishable from an engine bug until you look at the script.
        var loopsAll = new Option<bool>("--all", () => false,
            "List every loop, not just the risky ones.");
        var scriptLoops = new Command("loops",
            "Find JASS loops that may never terminate: no exitwhen at all, or an exit condition "
            + "naming only values the loop body never assigns.")
        { mapArg, loopsAll, jsonOption };
        scriptLoops.SetHandler(ctx => RunSafely(() =>
        {
            var p3 = ctx.ParseResult;
            var doc3 = MapDocument.Load(p3.GetValueForArgument(mapArg));
            var r3 = ScriptLoopsCommand.Run(doc3, onlyRisky: !p3.GetValueForOption(loopsAll));
            Emit(p3.GetValueForOption(jsonOption), r3, () => Render.ScriptLoops(r3));
        }));
        // Roots for any reachability-based cleanup: functions invoked only by name at runtime,
        // which a caller-graph pass cannot see and would delete. Prerequisite for replacing the
        // porter's line-by-line trimming (which is what actually breaks ported maps) with a
        // carry-complete-then-strip pass.
        var scriptRoots = new Command("roots",
            "List functions reachable only via a string literal, e.g. ExecuteFunc(\"Name\"). A "
            + "dead-code pass must pin these or it will delete live code.")
        { mapArg, jsonOption };
        scriptRoots.SetHandler(ctx => RunSafely(() =>
        {
            var p4 = ctx.ParseResult;
            var doc4 = MapDocument.Load(p4.GetValueForArgument(mapArg));
            var r4 = ScriptRootsCommand.Run(doc4);
            Emit(p4.GetValueForOption(jsonOption), r4, () => Render.ScriptRoots(r4));
        }));
        // Remove functions nothing can reach, as the alternative to the porter's trimming (which
        // is what breaks ported maps: an unresolvable call commented out leaves a local declared
        // and never assigned, or strips an iterator's advance so its loop can never exit).
        // Deleting a whole unreachable function cannot cause either, since nothing left refers to it.
        var stripOut = new Option<string?>(new[] { "-o", "--out" },
            "Output map path. Default: '<map>.stripped.<ext>' next to the input.");
        var scriptStrip = new Command("strip",
            "Remove unreachable functions from war3map.j by call-graph closure from the engine's "
            + "entry points plus every function named in a string literal. Globals are untouched.")
        { mapArg, stripOut, jsonOption };
        scriptStrip.SetHandler(ctx => RunSafely(() =>
        {
            var p5 = ctx.ParseResult;
            string map5 = p5.GetValueForArgument(mapArg);
            var doc5 = MapDocument.Load(map5);
            var r5 = ScriptStripCommand.Run(doc5);
            FileEditCommand.WriteText(doc5, "war3map.j", r5.NewScript);
            var dest5 = p5.GetValueForOption(stripOut) ?? Path.Combine(
                Path.GetDirectoryName(map5) ?? "",
                Path.GetFileNameWithoutExtension(map5) + ".stripped" + Path.GetExtension(map5));
            doc5.Save(dest5);
            Emit(p5.GetValueForOption(jsonOption), new { r5.FunctionsBefore, r5.FunctionsAfter,
                r5.FunctionsRemoved, r5.PercentRemoved, r5.LinesBefore, r5.LinesAfter,
                r5.EntryPoints, r5.StringRoots, SavedTo = dest5 }, () => Render.ScriptStrip(r5, dest5));
        }));
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

        var scriptUnusedGlobals = new Command("unused-globals",
            "Find globals declared in war3map.j and never read after their declaration, including "
            + "ones that are only ever assigned. The World Editor's generated gg_ and bj_ globals are "
            + "excluded. Read-only.") { mapArg, jsonOption };
        scriptUnusedGlobals.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ScriptUnusedGlobalsCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ScriptUnusedGlobals(r));
        }));
        script.AddCommand(scriptUnusedGlobals);

        var scriptUnusedFunctions = new Command("unused-functions",
            "Find functions no other function calls and nothing else reaches, excluding the engine's "
            + "entry points and anything an InitTrig_* function or ExecuteFunc(\"...\") names. Read-only.")
        { mapArg, jsonOption };
        scriptUnusedFunctions.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ScriptUnusedFunctionsCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ScriptUnusedFunctions(r));
        }));
        script.AddCommand(scriptUnusedFunctions);

        var scriptNullLocals = new Command("null-locals",
            "Find handle locals (location, group, force, effect, timer, trigger, text tag, lightning) "
            + "destroyed with Remove/Destroy but never set to null, the classic JASS handle leak. Read-only.")
        { mapArg, jsonOption };
        scriptNullLocals.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ScriptNullLocalsCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ScriptNullLocals(r));
        }));
        script.AddCommand(scriptNullLocals);

        var scriptComplexity = new Command("complexity",
            "Per-function cyclomatic complexity (if, loop, and, or), ranked worst first, with the line. Read-only.")
        { mapArg, jsonOption };
        scriptComplexity.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ScriptComplexityCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ScriptComplexity(r));
        }));
        script.AddCommand(scriptComplexity);

        var callgraphDot = new Option<bool>("--dot",
            "Emit Graphviz DOT instead of a text adjacency list.");
        var scriptCallGraph = new Command("callgraph",
            "The call edges between functions the script declares, as a text adjacency list or, "
            + "with --dot, Graphviz DOT. Read-only.") { mapArg, callgraphDot, jsonOption };
        scriptCallGraph.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ScriptCallGraphCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            bool dot = p.GetValueForOption(callgraphDot);
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ScriptCallGraph(r, dot));
        }));
        script.AddCommand(scriptCallGraph);

        var refsName = new Argument<string>("name", "Function or global name to find uses of.");
        var scriptRefs = new Command("refs",
            "Find every use of a name in the map script: direct calls, and the places it is passed "
            + "as a code value (Condition, Filter, TriggerAddAction), which a text search for "
            + "'name(' cannot see.") { mapArg, refsName };
        scriptRefs.SetHandler((string map, string name, bool json) => RunSafely(() =>
        {
            var r = ScriptCommand.References(MapDocument.Load(map), name);
            Emit(json, r, () => Render.ScriptReferences(r));
        }), mapArg, refsName, jsonOption);
        script.AddCommand(scriptRefs);

        var scriptReachable = new Command("reachable",
            "List the functions reachable from main and the InitTrig entry points, and the dead "
            + "functions nothing reaches, each with its line. Read-only.") { mapArg, jsonOption };
        scriptReachable.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ScriptReachableCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ScriptReachable(r));
        }));
        script.AddCommand(scriptReachable);

        var scriptToolTarget = new Option<string>("--target",
            "Target build name or schema folder path.") { IsRequired = true };
        var scriptToolSchemas = new Option<string?>("--schemas",
            "Directory of schema folders. Default: search <base>/schemas then walk up to data/schemas.");
        var scriptApiUsage = new Command("api-usage",
            "List the natives and functions the script uses that the target build does not declare. "
            + "Read-only.") { mapArg, scriptToolTarget, scriptToolSchemas, jsonOption };
        scriptApiUsage.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var doc = MapDocument.Load(p.GetValueForArgument(mapArg));
            var r = ScriptApiUsageCommand.Execute(doc,
                p.GetValueForOption(scriptToolTarget)!, p.GetValueForOption(scriptToolSchemas));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ScriptApiUsage(r));
        }));
        script.AddCommand(scriptApiUsage);

        var scriptSemanticDiff = new Command("semantic-diff",
            "Compare two scripts line by line after normalizing whitespace and ignoring pure renames "
            + "of local variables, reporting the real changes. Read-only.") { mapArg, mapB, jsonOption };
        scriptSemanticDiff.SetHandler((string a, string b, bool json) => RunSafely(() =>
        {
            var r = ScriptSemanticDiffCommand.Execute(MapDocument.Load(a), MapDocument.Load(b));
            Emit(json, r, () => Render.ScriptSemanticDiff(r));
        }), mapArg, mapB, jsonOption);
        script.AddCommand(scriptSemanticDiff);

        var scriptDuplicateBodies = new Command("duplicate-bodies",
            "Find functions whose normalized bodies are identical, up to comments, whitespace and "
            + "local variable names. Read-only.") { mapArg, jsonOption };
        scriptDuplicateBodies.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ScriptDuplicateBodiesCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ScriptDuplicateBodies(r));
        }));
        script.AddCommand(scriptDuplicateBodies);

        var modernizeOut = new Option<string?>(new[] { "-o", "--out" },
            "Output map path. Default: '<map>.modernized.<ext>' next to the input - the original is never overwritten.");
        var scriptModernize = new Command("modernize",
            "Rewrite deprecated or renamed natives in the script to the target build's spelling "
            + "where a mapping is known, leaving everything else untouched.")
        { mapArg, scriptToolTarget, scriptToolSchemas, modernizeOut, jsonOption };
        scriptModernize.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = ScriptModernizeCommand.Execute(doc,
                p.GetValueForOption(scriptToolTarget)!, p.GetValueForOption(scriptToolSchemas));
            string? dest = null;
            if (r.Rewrites > 0)
            {
                dest = p.GetValueForOption(modernizeOut) ?? Path.Combine(
                    Path.GetDirectoryName(map) ?? "",
                    Path.GetFileNameWithoutExtension(map) + ".modernized" + Path.GetExtension(map));
                doc.Save(dest);
            }
            Emit(p.GetValueForOption(jsonOption),
                new { r.ScriptFile, r.TargetBuild, r.Rewrites, r.Changes, SavedTo = dest },
                () => Render.ScriptModernize(r, dest));
        }));
        script.AddCommand(scriptModernize);

        script.AddCommand(scriptLoops);
        script.AddCommand(scriptRoots);
        script.AddCommand(scriptStrip);
        var repairOut = new Option<string?>(new[] { "-o", "--out" },
            "Output map path. Default: '<map>.repaired.<ext>' next to the input - the original is never overwritten.");
        var scriptRepair = new Command("repair",
            "Repair a map whose script cannot compile (restores declarations whose initializer was dropped by a port). "
            + "An un-compilable war3map.j is why a hosted map shows no player slots.")
        { mapArg, repairOut };
        scriptRepair.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            bool json = p.GetValueForOption(jsonOption);
            var doc = MapDocument.Load(map);
            var r = ScriptRepairCommand.Execute(doc);

            // Nothing changed means nothing to write, so the original is left exactly as it is.
            string? dest = null;
            if (r.Repairs > 0)
            {
                dest = p.GetValueForOption(repairOut) ?? Path.Combine(
                    Path.GetDirectoryName(map) ?? "",
                    Path.GetFileNameWithoutExtension(map) + ".repaired" + Path.GetExtension(map));
                doc.Save(dest);
            }
            if (!r.Ok) exitCode[0] = 2;
            Emit(json, new { r.Ok, r.Message, r.Repairs, r.Remaining, SavedTo = dest }, () =>
                r.Message
                + (dest is null ? "" : $"\nsaved: {dest}")
                + (r.Remaining.Count == 0 ? "" : "\n" + string.Join("\n", r.Remaining.Select(x => "  ! " + x))));
        }));
        script.AddCommand(scriptRepair);

        var scriptCheck = new Command("check",
            "Check the map script for errors that stop it loading. A Lua script is checked against "
            + "Lua 5.3 exactly as luac would, a JASS script by the built-in JASS checks. Exits 1 on an error.")
        { mapArg };
        scriptCheck.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = ScriptCommand.Check(MapDocument.Load(map));
            if (!r.Ok) exitCode[0] = 1;
            Emit(json, r, () =>
                $"{r.ScriptFile} ({r.Language}): {(r.Ok ? "OK" : "errors")}"
                + string.Concat(r.Issues.Select(i =>
                    $"\n  {i.Severity.ToString().ToLowerInvariant()}{(i.Line > 0 ? $" line {i.Line}" : "")}. {i.Message}")));
        }), mapArg, jsonOption);
        script.AddCommand(scriptCheck);

        var formatStyle = new Option<string>("--style", () => "pretty", "pretty (indented, comments kept) or canonical (one fixed form, for diffing).");
        var formatOut = new Option<string?>(new[] { "-o", "--out" },
            "Output map path. Default '<map>.formatted.<ext>' next to the input, the original is never overwritten.");
        var scriptFormat = new Command("format",
            "Reprint a Lua map script, pretty or canonical. The result compiles to the same Lua "
            + "instructions as the original. JASS maps are refused.")
        { mapArg, formatStyle, formatOut };
        scriptFormat.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = ScriptCommand.Format(doc, p.GetValueForOption(formatStyle)!);
            string? dest = null;
            if (r.Changed)
            {
                dest = p.GetValueForOption(formatOut) ?? Path.Combine(
                    Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(map)) ?? "",
                    Path.GetFileNameWithoutExtension(Path.TrimEndingDirectorySeparator(map)) + ".formatted" + Path.GetExtension(Path.TrimEndingDirectorySeparator(map)));
                doc.Save(dest);
            }
            Emit(p.GetValueForOption(jsonOption), new { r.ScriptFile, r.Style, r.Changed, r.BytesBefore, r.BytesAfter, SavedTo = dest }, () =>
                r.Changed ? $"{r.ScriptFile} reprinted {r.Style}, {r.BytesBefore} to {r.BytesAfter} bytes\nsaved: {dest}"
                          : $"{r.ScriptFile} is already {r.Style}, nothing written");
        }));
        script.AddCommand(scriptFormat);

        var toLuaOut = new Option<string?>(new[] { "-o", "--out" },
            "Output map path. Default '<map>.lua.<ext>' next to the input. An existing file is never overwritten.");
        var scriptToLua = new Command("to-lua",
            "Convert a JASS map to a Lua map. The script becomes war3map.lua with the JASS integer, division "
            + "and null string rules kept, and the w3i script language is set to Lua.")
        { mapArg, toLuaOut };
        scriptToLua.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string trimmed = Path.TrimEndingDirectorySeparator(map);
            string dest = p.GetValueForOption(toLuaOut) ?? Path.Combine(
                Path.GetDirectoryName(trimmed) ?? "",
                Path.GetFileNameWithoutExtension(trimmed) + ".lua" + Path.GetExtension(trimmed));
            if (File.Exists(dest) || Directory.Exists(dest))
                throw new IOException($"{dest} already exists, choose another path with -o.");
            var doc = MapDocument.Load(map);
            var r = JassToLuaCommand.Convert(doc);
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption), new { r.ScriptFile, r.Functions, r.ExsLiterals, r.LuaChunks, r.BytesBefore, r.BytesAfter, r.UsedGameHeaders, r.Warnings, SavedTo = dest }, () =>
                $"{r.ScriptFile} converted to war3map.lua, {r.Functions} function(s), {r.BytesBefore} to {r.BytesAfter} bytes\nsaved: {dest}"
                + (r.Warnings.Count == 0 ? "" : $"\n{r.Warnings.Count} warning(s)" + string.Concat(r.Warnings.Take(20).Select(w => "\n  ! " + w))
                   + (r.Warnings.Count > 20 ? $"\n  ... {r.Warnings.Count - 20} more, see --json" : "")));
        }));
        script.AddCommand(scriptToLua);

        var decompileOut = new Option<string?>(new[] { "-o", "--out" },
            "Output map path. Default '<map>.decompiled.<ext>' next to the input. An existing file is never overwritten.");
        var scriptDecompile = new Command("decompile",
            "Turn Lua entries shipped as Lua 5.3 bytecode back into Lua source that runs the same. "
            + "Nothing is written when the map holds no bytecode.")
        { mapArg, decompileOut };
        scriptDecompile.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string trimmed = Path.TrimEndingDirectorySeparator(map);
            string dest = p.GetValueForOption(decompileOut) ?? Path.Combine(
                Path.GetDirectoryName(trimmed) ?? "",
                Path.GetFileNameWithoutExtension(trimmed) + ".decompiled" + Path.GetExtension(trimmed));
            if (File.Exists(dest) || Directory.Exists(dest))
                throw new IOException($"{dest} already exists, choose another path with -o.");
            var doc = MapDocument.Load(map);
            var r = LuaDecompileCommand.Decompile(doc);
            string? saved = null;
            if (r.Files.Count > 0)
            {
                doc.Save(dest);
                saved = dest;
            }
            if (r.Failed.Count > 0) exitCode[0] = 1;
            Emit(p.GetValueForOption(jsonOption), new { r.Files, r.Failed, SavedTo = saved }, () =>
                (r.Files.Count == 0 && r.Failed.Count == 0 ? "no Lua bytecode in this map, nothing written" : "")
                + string.Concat(r.Files.Select(f => $"{f.FileName} ({f.OpcodeTable}), {f.Functions} function(s), {f.BytesBefore} to {f.BytesAfter} bytes\n"))
                + string.Concat(r.Failed.Select(f => $"  ! {f}\n"))
                + (saved is null ? "" : $"saved: {saved}"));
        }));
        script.AddCommand(scriptDecompile);

        var luaLeaksAll = new Option<bool>("--all", "List every finding, including code that runs only once.");
        var scriptLuaLeaks = new Command("lua-leaks",
            "Find handle leaks in a Lua map script: a creator dropped as a statement, a creator on a "
            + "period that is never released, and a handle kept in a table the script never clears. "
            + "Read-only.") { mapArg, luaLeaksAll, jsonOption };
        scriptLuaLeaks.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = LuaScriptLeaksCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.Functions, r.HotFunctions, r.RepeatFunctions, r.Leaks, r.Periodic, r.Diagnostics },
                () => Render.LuaScriptLeaks(r, p.GetValueForOption(luaLeaksAll)));
            if (!r.Ok) exitCode[0] = 1;
        }));
        script.AddCommand(scriptLuaLeaks);

        var scriptLuaGlobals = new Command("lua-globals",
            "List the globals a Lua map defines, where each is read and written, and any global that "
            + "is written but never read. Read-only.") { mapArg, jsonOption };
        scriptLuaGlobals.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = LuaGlobalsCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.LuaGlobals(r));
        }));
        script.AddCommand(scriptLuaGlobals);

        var scriptLuaCheck = new Command("lua-check",
            "Parse every Lua file in a map and report the syntax errors with their line and column, "
            + "the way luac 5.3 refuses them. Exits 1 on an error.") { mapArg, jsonOption };
        scriptLuaCheck.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = LuaCheckCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.LuaCheck(r));
            if (!r.Ok) exitCode[0] = 1;
        }));
        script.AddCommand(scriptLuaCheck);

        var scriptLuaApi = new Command("lua-api",
            "List the natives and functions a Lua script calls that the target build does not declare. "
            + "Read-only.") { mapArg, scriptToolTarget, scriptToolSchemas, jsonOption };
        scriptLuaApi.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var doc = MapDocument.Load(p.GetValueForArgument(mapArg));
            var r = LuaApiCommand.Execute(doc,
                p.GetValueForOption(scriptToolTarget)!, p.GetValueForOption(scriptToolSchemas));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.LuaApi(r));
        }));
        script.AddCommand(scriptLuaApi);

        var luaToJassOut = new Option<string?>(new[] { "-o", "--out" },
            "Output map path. Default '<map>.jass.<ext>' next to the input. An existing file is never overwritten.");
        var scriptLuaToJass = new Command("lua-to-jass",
            "Best effort conversion of a Lua map back to JASS. Only the constructs that map back "
            + "without guessing are converted, and every construct left is reported.")
        { mapArg, luaToJassOut };
        scriptLuaToJass.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string trimmed = Path.TrimEndingDirectorySeparator(map);
            string dest = p.GetValueForOption(luaToJassOut) ?? Path.Combine(
                Path.GetDirectoryName(trimmed) ?? "",
                Path.GetFileNameWithoutExtension(trimmed) + ".jass" + Path.GetExtension(trimmed));
            if (File.Exists(dest) || Directory.Exists(dest))
                throw new IOException($"{dest} already exists, choose another path with -o.");
            var doc = MapDocument.Load(map);
            var r = LuaToJassCommand.Execute(doc);
            if (r.Ok) doc.Save(dest);
            if (!r.Ok) exitCode[0] = 1;
            Emit(p.GetValueForOption(jsonOption),
                new { r.ScriptFile, r.Ok, r.FunctionsConverted, r.FunctionsSkipped, r.GlobalsConverted, r.Unconverted, r.Diagnostics, SavedTo = r.Ok ? dest : null },
                () => Render.LuaToJass(r));
        }));
        script.AddCommand(scriptLuaToJass);

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
                    ? "models convert to .obj only, pass an output ending in .obj"
                    : $"only .mdx/.mdl convert to {toExt}, image sources convert to image formats");

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
        var synthDispatchOption = new Option<bool>("--synth-dispatch",
            "Single-unit port only. Instead of carrying the source's shared cast dispatcher (every gate it was "
            + "written to satisfy on the source map, a placed-hero registration array, a map rect that reads "
            + "null off the source map, a cooldown hashtable), read the unit's OWN branch of it and synthesize "
            + "a fresh, minimal, self-contained dispatcher for exactly its abilities. Does not make the unit "
            + "dependency-free (spell handlers still need their own carried state), only replaces the SHARED "
            + "entry point into them. Default off: a plain port is unaffected.");
        var bootstrapStateOption = new Option<bool>("--bootstrap-state",
            "Construct every carried global the ported code reads but nothing ever assigns (a region "
            + "CreateRegions never carried, a timer only a hand-written Init reached through "
            + "ExecuteFunc(\"Init\") ever built), so it holds a real handle instead of silently sitting "
            + "at its type default. Only timer, group, hashtable, trigger, rect and force are ever "
            + "constructed this way (a rect is built empty, never guessed at). Anything else this cannot "
            + "safely build, unit, item, destructable, effect, code, framehandle, an array, is left alone "
            + "and reported instead. Default off, a plain port is unaffected.");
        var port = new Command("port", "Port content between maps.");
        var portUnit = new Command("unit",
            "Port a unit (its custom objects + assets + strings, and best-effort its trigger script) from one map into another, auto-remapping rawcode collisions.")
        { portSource, portRawcodes, portTarget, outOption, noScriptOption, dryRunOption, synthDispatchOption, bootstrapStateOption };
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
            bool synthDispatch = p.GetValueForOption(synthDispatchOption);
            bool bootstrapState = p.GetValueForOption(bootstrapStateOption);
            string? gameDir = p.GetValueForOption(gameDirOption);
            if (synthDispatch && rawcodes.Length != 1)
                throw new ArgumentException("--synth-dispatch only applies when porting a single unit");

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
                    ? PortCommand.PreviewPort(source, bundle, target, includeScript, synthDispatch, bootstrapState)
                    : PortCommand.PortUnit(source, bundle, target, includeScript, synthDispatch, bootstrapState);
                if (outPath is not null) target.Save(outPath);
                Emit(json, result, () => Render.Port(result, outPath));
            }
            else
            {
                var bundles = rawcodes.Select(rc => BundleCommand.ResolveUnit(source, rc, gameDir)).ToList();
                var result = dryRun
                    ? PortCommand.PreviewPorts(source, bundles, target, includeScript, bootstrapState)
                    : PortCommand.PortUnits(source, bundles, target, includeScript, bootstrapState);
                if (outPath is not null) target.Save(outPath);
                Emit(json, result, () => Render.PortBatch(result, outPath));
            }
        }));
        port.AddCommand(portUnit);

        var portDepsKind = new Option<string>("--kind", () => "unit",
            "Root object type: unit|item|ability|destructable|doodad|buff|upgrade.");
        var portDeps = new Command("deps",
            "Report the full dependency closure of a rawcode, its objects grouped by kind, its "
            + "assets with sizes, its display strings and its script functions.")
        { mapArg, objRawcode, portDepsKind };
        portDeps.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = PortDepsCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForArgument(objRawcode),
                ObjectKinds.Parse(p.GetValueForOption(portDepsKind)!),
                p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.PortDeps(r));
        }));
        port.AddCommand(portDeps);

        var previewRawcode = new Argument<string>("rawcode", "Four-character unit rawcode to preview.");
        var portPreview = new Command("preview",
            "Dry run, listing exactly what a port of the rawcode into the target would add or change, "
            + "writing nothing. Same code path as 'port unit', so the two cannot drift.")
        { portSource, previewRawcode, portTarget, noScriptOption };
        portPreview.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = PortPreviewCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(portSource)),
                p.GetValueForArgument(previewRawcode),
                MapDocument.Load(p.GetValueForArgument(portTarget)),
                p.GetValueForOption(gameDirOption),
                includeScript: !p.GetValueForOption(noScriptOption));
            // No path, so the shared footer prints DRY RUN rather than a Saved line.
            Emit(p.GetValueForOption(jsonOption), r, () => Render.Port(r, null));
        }));
        port.AddCommand(portPreview);

        var intoTarget = new Option<string>("--target",
            "Target build name or schema folder path.") { IsRequired = true };
        var intoSchemas = new Option<string?>("--schemas",
            "Schemas directory, when --target is a build name rather than a folder.");
        var portIntoVersion = new Command("into-version",
            "Port a unit, then downgrade the result to a target build's schema. Object fields the "
            + "build does not declare are removed, and script identifiers it lacks are flagged. "
            + "Writes a copy, never in place.")
        { portSource, previewRawcode, portTarget, intoTarget, intoSchemas, outOption, noScriptOption };
        portIntoVersion.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string sourcePath = p.GetValueForArgument(portSource);
            string targetPath = p.GetValueForArgument(portTarget);
            string outPath = p.GetValueForOption(outOption)
                ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? ".",
                    Path.GetFileNameWithoutExtension(targetPath) + ".ported" + Path.GetExtension(targetPath));
            if (string.Equals(Path.GetFullPath(outPath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFullPath(outPath), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("out path must not be the source or target map itself");

            var target = MapDocument.Load(targetPath);
            var r = PortIntoVersionCommand.Execute(
                MapDocument.Load(sourcePath), p.GetValueForArgument(previewRawcode), target,
                p.GetValueForOption(intoTarget)!, p.GetValueForOption(intoSchemas),
                p.GetValueForOption(gameDirOption), includeScript: !p.GetValueForOption(noScriptOption));
            target.Save(outPath);
            Emit(p.GetValueForOption(jsonOption), r, () => Render.PortIntoVersion(r, outPath));
        }));
        port.AddCommand(portIntoVersion);

        var auditHeroArg = new Argument<string?>("hero", () => null,
            "Hero rawcode to audit. Omit to audit every hero placed on the map.");
        var audit = new Command("audit", "Check that a map really wires up what it should.");
        var auditHero = new Command("hero",
            "Verify every ability of a placed hero is wired end to end (object present, dispatch carried, "
            + "trigger attached, event covers the player, handler not empty, identity array registered, "
            + "init called exactly once). Reports the exact missing link per ability. Exits 2 if any fail.")
        { mapArg, auditHeroArg };
        auditHero.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var doc = MapDocument.Load(p.GetValueForArgument(mapArg));
            string? hero = p.GetValueForArgument(auditHeroArg);

            var results = hero is null
                ? HeroWiringAudit.AuditPlacedHeroes(doc)
                : new[] { HeroWiringAudit.Audit(doc, hero, ownerId: 0) };

            Emit(p.GetValueForOption(jsonOption), results, () =>
            {
                var sb = new System.Text.StringBuilder();
                foreach (var r in results)
                {
                    sb.AppendLine($"{r.Hero}  \"{r.Name}\"  (player {r.OwnerId})  "
                        + $"{r.Wired}/{r.Abilities.Count - r.NotCastable} castable abilities wired"
                        + (r.NotCastable > 0 ? $", {r.NotCastable} passive/aura" : "")
                        + (r.Problems.Count > 0 ? $", {r.Problems.Count} problem(s)" : ""));
                    foreach (var a in r.Abilities)
                    {
                        // Three states, not two. A passive is neither wired nor broken, and printing
                        // FAIL beside one was the exact false alarm the reclassification removed.
                        string mark = a.Status switch
                        {
                            WiringStatus.Ok => "ok  ",
                            WiringStatus.NotCastDispatched => "info",
                            _ => "FAIL",
                        };
                        sb.AppendLine($"  {mark} {a.Ability}  \"{a.Name}\"  "
                            + (a.Status == WiringStatus.Ok ? "" : a.Status + ": ") + a.Detail);
                    }
                }
                if (results.Count == 0) sb.AppendLine("no placed heroes found");
                return sb.ToString().TrimEnd();
            });
            if (results.Any(r => r.Problems.Count > 0)) exitCode[0] = 2;
        }));
        audit.AddCommand(auditHero);

        var auditAbilityArg = new Argument<string?>("hero", () => null,
            "Hero rawcode to audit. Omit to audit every hero placed on the map.");
        var auditAbility = new Command("ability",
            "Walk each ability's own runtime chain, not just whether a cast reaches a live trigger: is "
            + "the handler carried and not gutted, does its follow-up loop actually start, does anything "
            + "in its closure deal damage, is every pause matched by an unpause, does a timer callback "
            + "still point at a declared function, is every global it reads actually assigned, does a "
            + "special-effect asset still exist. 'audit hero' only proves dispatch, a hero can read '8 of "
            + "8 wired' there and still do nothing in game. Exits 2 if any ability fails a check.")
        { mapArg, auditAbilityArg };
        auditAbility.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var doc = MapDocument.Load(p.GetValueForArgument(mapArg));
            string? hero = p.GetValueForArgument(auditAbilityArg);

            var results = hero is null
                ? AbilityAuditCommand.AuditPlacedHeroes(doc)
                : new[] { AbilityAuditCommand.Audit(doc, hero, ownerId: 0) };

            Emit(p.GetValueForOption(jsonOption), results, () =>
            {
                var columns = new (AbilityCheck Check, string Label)[]
                {
                    (AbilityCheck.Dispatch, "DISP"), (AbilityCheck.Handler, "HNDLR"),
                    (AbilityCheck.Loop, "LOOP"), (AbilityCheck.Damage, "DMG"),
                    (AbilityCheck.PauseBalance, "PAUSE"), (AbilityCheck.TimerCallbacks, "CB"),
                    (AbilityCheck.State, "STATE"), (AbilityCheck.Effects, "FX"),
                };
                string Mark(AbilityAuditRow a, AbilityCheck c)
                {
                    var chk = a.Checks.FirstOrDefault(x => x.Check == c);
                    return chk is null ? "?" : chk.Verdict switch
                    {
                        CheckVerdict.Pass => "ok",
                        CheckVerdict.NotApplicable => "-",
                        _ => "FAIL",
                    };
                }

                var sb = new System.Text.StringBuilder();
                foreach (var r in results)
                {
                    sb.AppendLine($"{r.Hero}  \"{r.Name}\"  (player {r.OwnerId})  "
                        + $"{r.Passed}/{r.Total} abilities fully verified");
                    sb.Append("  ").Append("ABILITY".PadRight(9)).Append("NAME".PadRight(28));
                    foreach (var col in columns) sb.Append(col.Label.PadRight(7));
                    sb.AppendLine("VERDICT");
                    foreach (var a in r.Abilities)
                    {
                        string name = a.Name ?? "";
                        if (name.Length > 26) name = name[..26];
                        sb.Append("  ").Append(a.Ability.PadRight(9)).Append(name.PadRight(28));
                        foreach (var col in columns) sb.Append(Mark(a, col.Check).PadRight(7));
                        sb.AppendLine(a.Pass ? "PASS" : "FAIL");
                    }
                    foreach (var a in r.Abilities.Where(a => !a.Pass))
                        foreach (var f in a.Failures)
                            sb.AppendLine($"    {a.Ability} {f.Check}: {f.Detail}");
                }
                if (results.Count == 0) sb.AppendLine("no placed heroes found");
                return sb.ToString().TrimEnd();
            });
            if (results.Any(r => r.Abilities.Any(a => !a.Pass))) exitCode[0] = 2;
        }));
        audit.AddCommand(auditAbility);

        var fidSource = new Argument<string>("source-map", "Path to the SOURCE map the object was ported FROM.");
        var fidTarget = new Argument<string>("target-map", "Path to the TARGET map the object was ported INTO.");
        var fidRawcode = new Argument<string>("rawcode", "Root object rawcode to compare (the same rawcode in both maps).");
        var auditFidelity = new Command("fidelity",
            "Compare an object and its whole custom closure between a source and target map, reporting fields, "
            + "per-level values, levels, and objects the port failed to carry. Rawcode remaps and inlined trigger "
            + "strings are accounted for and not counted as faults. Exits 2 if any real data loss is found.")
        { fidSource, fidTarget, fidRawcode };
        auditFidelity.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var source = MapDocument.Load(p.GetValueForArgument(fidSource));
            var target = MapDocument.Load(p.GetValueForArgument(fidTarget));
            var rawcode = p.GetValueForArgument(fidRawcode);
            var r = ObjectFidelityCommand.Compare(source, target, rawcode, p.GetValueForOption(gameDirOption));

            Emit(p.GetValueForOption(jsonOption), r, () =>
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{r.Root}  \"{r.Name}\"  ({r.RootKind})  source vs target");
                foreach (var g in r.Findings.GroupBy(f => f.SourceRawcode)
                                            .OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    var head = g.First();
                    sb.AppendLine($"  {g.Key}  \"{head.Name}\"  ({head.Kind})");
                    foreach (var f in g.OrderBy(f => f.Severity).ThenBy(f => f.Field, StringComparer.Ordinal))
                    {
                        string tag = f.Severity == FidelitySeverity.Error ? "LOSS" : "info";
                        string field = f.Field.Length > 0 ? " " + f.Field : "";
                        string vals = f.SourceValue is not null || f.TargetValue is not null
                            ? $"  (source={f.SourceValue ?? "<none>"}  target={f.TargetValue ?? "<none>"})" : "";
                        sb.AppendLine($"    {tag} {f.Issue}{field}  {f.Detail}{vals}");
                    }
                }
                sb.AppendLine();
                sb.AppendLine(r.Faithful
                    ? $"faithful, {r.ObjectsCompared} object(s) compared, no real loss found ({r.Infos} informational)"
                    : $"{r.Errors} real loss(es) across {r.ObjectsWithLosses} of {r.ObjectsCompared} object(s), {r.Infos} informational");
                foreach (var d in r.Diagnostics) sb.AppendLine("  note, " + d);
                return sb.ToString().TrimEnd();
            });
            if (!r.Faithful) exitCode[0] = 2;
        }));
        audit.AddCommand(auditFidelity);

        var auditReadinessArg = new Argument<string?>("hero", () => null,
            "Hero rawcode to check. Omit to check every hero placed on the map.");
        var auditReadiness = new Command("readiness",
            "Check whether a placed hero's script would actually run, not just fire. Verifies InitGlobals "
            + "and RunInitializationTriggers are both carried and called, and that every udg_ global the "
            + "hero's own carried code reads is assigned somewhere in the script. A hero can audit clean on "
            + "'audit hero' and still do nothing in game if its damage, range or duration values were never "
            + "initialized. Exits 2 if any problem is found.")
        { mapArg, auditReadinessArg };
        auditReadiness.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var doc = MapDocument.Load(p.GetValueForArgument(mapArg));
            string? hero = p.GetValueForArgument(auditReadinessArg);

            var results = hero is null
                ? RuntimeReadinessCommand.CheckPlacedHeroes(doc)
                : new[] { RuntimeReadinessCommand.Check(doc, hero, ownerId: 0) };

            Emit(p.GetValueForOption(jsonOption), results, () =>
            {
                var sb = new System.Text.StringBuilder();
                foreach (var r in results)
                {
                    sb.AppendLine($"{r.Hero}  \"{r.Name}\"  (player {r.OwnerId})  "
                        + (r.Ready ? "ready" : $"{r.Errors} problem(s)"));
                    foreach (var f in r.Findings)
                        sb.AppendLine($"  {(f.Global is null ? "" : f.Global + "  ")}{f.Issue}  {f.Detail}");
                    foreach (var d in r.Diagnostics) sb.AppendLine("  note, " + d);
                }
                if (results.Count == 0) sb.AppendLine("no placed heroes found");
                return sb.ToString().TrimEnd();
            });
            if (results.Any(r => !r.Ready)) exitCode[0] = 2;
        }));
        audit.AddCommand(auditReadiness);

        // ---- debug: instrument a map so the running game reports what it is doing ----
        var debugWiringHeroArg = new Argument<string?>("hero", () => null,
            "Hero rawcode to add a per-hero branch checkpoint for. Omit to instrument only the "
            + "checkpoints shared by every cast, with no per-hero branch print.");
        var debugWiringOut = new Option<string?>(new[] { "-o", "--out" },
            "Output map path. Default is '<map>.debug.<ext>' next to the input, the original is "
            + "never overwritten.");
        var debug = new Command("debug",
            "Instrument a map so the running game reports what it is doing, for problems static analysis cannot see.");
        var debugWiring = new Command("wiring",
            "Instruments an already-ported map's cast-dispatch chain with BJDebugMsg calls, so the running "
            + "game reports exactly where a hero's cast attempt stops. Use this when a hero audits clean on "
            + "'audit hero' and 'audit readiness' and still cannot cast in game, since neither static check "
            + "can see whether the dispatcher's own gate passes, whether its own per-hero branch is ever "
            + "entered, or whether a deferred trigger registration ran before the cast. Opt in, meant for a "
            + "disposable copy of a map, never run as part of an ordinary port.")
        { mapArg, debugWiringHeroArg, debugWiringOut };
        debugWiring.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            string? hero = p.GetValueForArgument(debugWiringHeroArg);
            var r = DebugWiringCommand.Instrument(doc, hero);

            string? dest = null;
            if (r.Ok)
            {
                dest = p.GetValueForOption(debugWiringOut) ?? Path.Combine(
                    Path.GetDirectoryName(map) ?? "",
                    Path.GetFileNameWithoutExtension(map) + ".debug" + Path.GetExtension(map));
                doc.Save(dest);
            }
            else
            {
                exitCode[0] = 1;
            }

            Emit(p.GetValueForOption(jsonOption), new { r.Ok, r.Message, r.Targets, r.Diagnostics, SavedTo = dest }, () =>
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(r.Message);
                foreach (var t in r.Targets)
                {
                    sb.AppendLine($"  {t.Function}  (trigger {t.Trigger})");
                    foreach (var c in t.Checkpoints) sb.AppendLine($"    + {c}");
                }
                foreach (var d in r.Diagnostics) sb.AppendLine("  note, " + d);
                if (dest is not null) sb.AppendLine("saved to " + dest);
                return sb.ToString().TrimEnd();
            });
        }));
        debug.AddCommand(debugWiring);

        var deepOption = new Option<bool>("--deep",
            "Also run pjass, the game's own JASS parser, over the map script (needs a Warcraft III install).");
        var strictOption = new Option<bool>("--strict",
            "Add the stricter checks on top of the default, a map with no (listfile), a script that "
            + "declares no entry point function, and a placement whose type neither the map nor the "
            + "installed game defines. The default validate is unchanged without it.");
        var validate = new Command("validate",
            "Check a map for problems (missing/empty files, loader errors, a script that cannot compile). Exits 2 if invalid.")
            { mapArg, deepOption, strictOption };
        validate.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            bool json = p.GetValueForOption(jsonOption);
            var doc = MapDocument.Load(map);

            PjassResult? deep = null;
            if (p.GetValueForOption(deepOption))
            {
                var entry = doc.GetFile("war3map.j") ?? doc.GetFile("scripts\\war3map.j");
                byte[]? bytes = entry?.CurrentBytes;
                if (bytes is { Length: > 0 })
                    deep = PjassGate.Check(System.Text.Encoding.Latin1.GetString(bytes),
                        p.GetValueForOption(gameDirOption));
            }

            // The strict mode runs the default validate itself and layers its extra checks on
            // top, so the plain path below stays exactly as it was.
            if (p.GetValueForOption(strictOption))
            {
                var strict = ValidateStrictCommand.Execute(doc, p.GetValueForOption(gameDirOption));
                Emit(json, new { Strict = strict, Pjass = deep }, () =>
                    Render.ValidateStrict(strict)
                    + (deep is null ? "" : $"\npjass: {deep.Note}"));
                if (!strict.Valid || deep is { Ran: true, Passed: false }) exitCode[0] = 2;
                return;
            }

            var r = ValidateCommand.Execute(doc);

            // pjass is the game's own parser, so its findings become real issues and change the
            // verdict. Printing "OK, valid" above a list of undefined functions is worse than
            // printing nothing, and that is exactly what this used to do.
            r = ValidateCommand.WithPjass(r, deep);

            Emit(json, new { r.Valid, r.Errors, r.Warnings, r.Issues, Pjass = deep }, () =>
                Render.Validate(r)
                + (deep is null ? "" : $"\npjass: {deep.Note}"));

            if (!r.Valid || deep is { Ran: true, Passed: false }) exitCode[0] = 2;
        }));

        // Behavioural audit. validate answers whether a map LOADS, audit answers whether its
        // abilities do what they claim, by checking the object data against the tooltips the
        // author wrote. Exits 2 when a check finds something that cannot work at all.
        var auditCheck = new Option<string[]>("--check",
            "Limit to named checks. Repeatable. Default runs all: "
            + string.Join(", ", AuditCommand.AllChecks))
        { AllowMultipleArgumentsPerToken = true };
        audit.Description =
            "Check abilities against the map's own tooltips, plus inherited requirements, "
            + "portrait-risk models, dangling object references and model paths that load no file. "
            + "Read-only. Exits 2 if any error-level issue is found. The subcommands check a hero's "
            + "wiring, one ability, object data fidelity and runtime readiness.";
        audit.AddArgument(mapArg);
        audit.AddOption(auditCheck);
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

        // Audit trend: the regression view over two maps. audit answers what is wrong now, trend
        // answers what an edit or a port changed, grouped by check.
        var auditTrendOther = new Argument<string>("other",
            "The second map, the one treated as the newer state.");
        var auditTrend = new Command("trend",
            "Run the behavioural audit against two maps and report the findings the second added "
            + "and removed, grouped by check. Read-only.")
            { mapArg, auditTrendOther, auditCheck };
        auditTrend.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = AuditTrendCommand.Execute(
                p.GetValueForArgument(mapArg),
                p.GetValueForArgument(auditTrendOther),
                p.GetValueForOption(gameDirOption),
                p.GetValueForOption(auditCheck));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.AuditTrend(r));
        }));
        audit.AddCommand(auditTrend);

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
            + "abilities by script the moment each unit is created. Hero abilities, morph targets and "
            + "dummy units (Locust) stay whole, and Locust, Invulnerable and inventory abilities stay in "
            + "every list. Reported fix for disconnects on maps with thousands of distinct uabi ids.");
        var uabiIds = new Option<string[]>("--ids",
            "Move only these ability ids, comma or space separated, and leave every other uabi entry in place.")
        { AllowMultipleArgumentsPerToken = true };
        repairUabi.AddOption(uabiIds);
        var uabiKeepShared = new Option<int>("--keep-shared",
            "Also keep in the data every ability id that at least this many unit types list. Shared ids barely "
            + "raise the distinct count, and moving them costs a runtime add on every unit. 0 keeps only the "
            + "fixed list (Locust, Invulnerable, inventories).");
        repairUabi.AddOption(uabiKeepShared);
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
            var r = UabiRuntimeRepairCommand.Execute(doc, apply, ids, p.GetValueForOption(uabiKeepShared));
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

        var replayArg = new Argument<string>("replay", "Path to a .w3g recorded game.");
        var replayDiffA = new Argument<string>("a", "Path to the first .w3g recorded game.");
        var replayDiffB = new Argument<string>("b", "Path to the second .w3g recorded game.");

        var replayDiff = new Command("diff",
            "Compare two recorded games. Reports same map or not, length delta, and each player's "
            + "leave delta.")
        { replayDiffA, replayDiffB };
        replayDiff.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ReplayDiffCommand.Execute(
                p.GetValueForArgument(replayDiffA), p.GetValueForArgument(replayDiffB));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ReplayDiff(r));
        }));

        var replayPlayers = new Command("players",
            "Per-player table for one recorded game. Columns are name, slot, team, race, result, "
            + "leave time and leave reason.") { replayArg };
        replayPlayers.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ReplayPlayersCommand.Execute(p.GetValueForArgument(replayArg));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ReplayPlayers(r));
        }));

        var replayTimeline = new Command("timeline",
            "Ordered event list for one recorded game. Reports game start, each chat line and "
            + "player leave with its reason, and the game end.") { replayArg };
        replayTimeline.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ReplayTimelineCommand.Execute(p.GetValueForArgument(replayArg));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ReplayTimeline(r));
        }));

        var replayMaps = new Option<string?>("--maps",
            "Folder to search for the recorded map. Default Documents\\Warcraft III\\Maps.");
        var replayFindMap = new Command("find-map",
            "The map path a replay records, and whether that map file exists in the Maps folder, "
            + "resolving the .w3x or .w3m.") { replayArg, replayMaps };
        replayFindMap.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ReplayFindMapCommand.Execute(
                p.GetValueForArgument(replayArg), p.GetValueForOption(replayMaps));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ReplayFindMap(r));
        }));

        var replayStats = new Command("stats",
            "Aggregate stats for one recorded game. Reports duration, player count, actions per "
            + "minute per player if the record carries them, and the disconnect count.") { replayArg };
        replayStats.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ReplayStatsCommand.Execute(p.GetValueForArgument(replayArg));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.ReplayStats(r));
        }));

        replay.AddCommand(replayDiff);
        replay.AddCommand(replayPlayers);
        replay.AddCommand(replayTimeline);
        replay.AddCommand(replayFindMap);
        replay.AddCommand(replayStats);
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

        // ---- doctor: Devo's Map Doctor, run as an external program ----
        var doctor = new Command("doctor",
            "Run Devo's Map Doctor (MIT, github.com/devoltzz/devos-map-doctor) on a map. Found through "
            + "WC3_DOCTOR or %LOCALAPPDATA%\\wc3ctl\\doctor\\<version>\\, and only a pinned build runs.");
        void EmitDoctor(System.CommandLine.Parsing.ParseResult p, MapDoctorResult r)
        {
            Emit(p.GetValueForOption(jsonOption), r, () =>
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"doctor {r.Doctor}, {r.Command}, exit {r.ExitCode}");
                if (r.Output is not null) sb.AppendLine($"wrote {r.Output}");
                if (r.Result is { } j)
                    sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(j,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                if (r.Stderr is not null) sb.AppendLine(r.Stderr);
                return sb.ToString().TrimEnd();
            });
            if (r.ExitCode != 0) Environment.ExitCode = r.ExitCode;
        }

        var docCheckMap = new Argument<string>("map", "Path to a .w3x/.w3m map.");
        var docCheckGame = new Option<string?>("--game", "Check an older game instead, such as 1.27.");
        var docCheck = new Command("check",
            "Will the map run on Reforged 3.0, and what stops it. Read-only, never starts the game.")
        { docCheckMap, docCheckGame };
        docCheck.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            EmitDoctor(p, MapDoctorCommand.Check(p.GetValueForArgument(docCheckMap), p.GetValueForOption(docCheckGame)));
        }));
        doctor.AddCommand(docCheck);

        var docInspectMap = new Argument<string>("map", "Path to a .w3x/.w3m map.");
        var docInspectReport = new Option<string>("--report", () => "diag",
            "One of diag, files, card, rawcodes, recipes.");
        var docInspect = new Command("inspect",
            "A read-only doctor report: protection and editor needs (diag), files, card, rawcodes or recipes.")
        { docInspectMap, docInspectReport };
        docInspect.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            EmitDoctor(p, MapDoctorCommand.ReadOnly(p.GetValueForOption(docInspectReport)!, p.GetValueForArgument(docInspectMap)));
        }));
        doctor.AddCommand(docInspect);

        var docCompareA = new Argument<string>("map", "The first map.");
        var docCompareB = new Argument<string>("other", "The map to compare it with.");
        var docCompare = new Command("compare",
            "What differs between two versions of a map, with names recovered on protected archives.")
        { docCompareA, docCompareB };
        docCompare.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            EmitDoctor(p, MapDoctorCommand.Compare(p.GetValueForArgument(docCompareA), p.GetValueForArgument(docCompareB)));
        }));
        doctor.AddCommand(docCompare);

        var docFixMap = new Argument<string>("map", "Path to a .w3x/.w3m map.");
        var docFixSteps = new Option<string[]>("--step",
            "A fix step or extra, such as listfile, data, portraits, uabi. Repeatable. Prefix no- to drop "
            + "a default step. None applies the steps the doctor picks for this map.")
        { AllowMultipleArgumentsPerToken = true };
        var docFixOut = new Option<string?>(new[] { "-o", "--out" },
            "Where to write the repaired copy. Default <map>_doctor.<ext>. Never overwritten.");
        var docFix = new Command("fix",
            "Repair a COPY of the map for Reforged 3.0 with the doctor's Fix map. The original is never written.")
        { docFixMap, docFixSteps, docFixOut };
        docFix.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            EmitDoctor(p, MapDoctorCommand.Fix(p.GetValueForArgument(docFixMap),
                p.GetValueForOption(docFixSteps), p.GetValueForOption(docFixOut)));
        }));
        doctor.AddCommand(docFix);
        root.AddCommand(doctor);

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

        // Refuse an explicit -o that names the input map itself: a mutating command must
        // never overwrite the map it read. Returns true (and reports) when it refused.
        bool RefusedInPlace(bool json, string? outOpt, string map)
        {
            if (outOpt is null) return false;
            if (!string.Equals(Path.GetFullPath(outOpt), Path.GetFullPath(map), StringComparison.OrdinalIgnoreCase))
                return false;
            const string msg = "refusing to overwrite the input map, pass a different -o path";
            Emit(json, new { Ok = false, Message = msg }, () => msg);
            exitCode[0] = 1;
            return true;
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
        static bool TryParseEcaKind(string text, out War3Net.Build.Script.TriggerFunctionType kind)
{
    switch (text.Trim().ToLowerInvariant())
    {
        case "event": kind = War3Net.Build.Script.TriggerFunctionType.Event; return true;
        case "condition": kind = War3Net.Build.Script.TriggerFunctionType.Condition; return true;
        case "action": kind = War3Net.Build.Script.TriggerFunctionType.Action; return true;
        default: kind = default; return false;
    }
}

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

        // Fill: the rectangle counterpart to the brush commands above. Every bulk terrain
        // operation lived in Wc3.Commands with no caller at all, so filling a region meant
        // clicking a brush repeatedly. They go behind ONE verb rather than a rectangle variant
        // of each, because selecting a region and acting on it is one concept.
        var fX0 = new Argument<int>("x0", "First corner column, 0-based.");
        var fY0 = new Argument<int>("y0", "First corner row, 0-based.");
        var fX1 = new Argument<int>("x1", "Second corner column, 0-based (inclusive, any order).");
        var fY1 = new Argument<int>("y1", "Second corner row, 0-based (inclusive, any order).");
        var fTool = new Argument<TerrainFillCommand.FillTool>("tool",
            "What to apply over the rectangle.");
        var fValue = new Option<float>("--value",
            () => 1f,
            "Height, cliff step count, or ground tile index, depending on the tool. Ignored by "
            + "Flatten, Ramp, RampOff, WaterRemove, Blight and BlightOff.");

        var terrainFill = new Command("fill",
            "Apply a tool over an inclusive rectangle of corners, instead of one brush dab at a "
            + "time. Corners may be given in any order and an off-grid rectangle is clipped.")
        { mapArg, fX0, fY0, fX1, fY1, fTool, fValue, setOut };
        terrainFill.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TerrainFillCommand.Fill(doc,
                p.GetValueForArgument(fX0), p.GetValueForArgument(fY0),
                p.GetValueForArgument(fX1), p.GetValueForArgument(fY1),
                p.GetValueForArgument(fTool), p.GetValueForOption(fValue));
            FinishTerrain(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message, r.TilesChanged);
        }));

        // ---- terrain heightmap PNG round-trip and map-wide smoothing ----
        var heightmapPngOut = new Option<string>(new[] { "-o", "--out" },
            "Output PNG path.") { IsRequired = true };
        var terrainExportHeightmap = new Command("export-heightmap",
            "Write the terrain heights as a 16-bit grayscale PNG, one pixel per corner. Read-only.")
        { mapArg, heightmapPngOut };
        terrainExportHeightmap.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = TerrainExportHeightmapCommand.Export(MapDocument.Load(p.GetValueForArgument(mapArg)));
            if (!r.Ok || r.Png is null)
            {
                Emit(p.GetValueForOption(jsonOption), new { r.Ok, r.Message }, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            string dest = p.GetValueForOption(heightmapPngOut);
            File.WriteAllBytes(dest, r.Png);
            Emit(p.GetValueForOption(jsonOption),
                new { Ok = true, Message = r.Message, Width = r.Width, Height = r.Height,
                      SizeBytes = r.Png.Length, SavedTo = dest },
                () => $"{r.Message}\nwrote {r.Png.Length:N0} bytes to {dest}");
        }));

        var heightmapPngArg = new Argument<string>("png", "Path to the grayscale heightmap PNG.");
        var terrainImportHeightmap = new Command("import-heightmap",
            "Set the tile heights from a grayscale PNG. The PNG must match the map's corner "
            + "grid exactly, and a mismatch is refused.")
        { mapArg, heightmapPngArg, setOut };
        terrainImportHeightmap.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string? outOpt = p.GetValueForOption(setOut);
            if (RefusedInPlace(p.GetValueForOption(jsonOption), outOpt, map)) return;
            var doc = MapDocument.Load(map);
            byte[] png = File.ReadAllBytes(p.GetValueForArgument(heightmapPngArg));
            var r = TerrainImportHeightmapCommand.Import(doc, png);
            FinishTerrain(p.GetValueForOption(jsonOption), outOpt, map, doc, r.Ok, r.Message, r.TilesChanged);
        }));

        var smoothPasses = new Option<int>("--passes", () => 1,
            "Number of smoothing passes, default 1.");
        var terrainSmooth = new Command("smooth",
            "Average every corner height with its neighbours, N times, across the whole map.")
        { mapArg, smoothPasses, setOut };
        terrainSmooth.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string? outOpt = p.GetValueForOption(setOut);
            if (RefusedInPlace(p.GetValueForOption(jsonOption), outOpt, map)) return;
            var doc = MapDocument.Load(map);
            var r = TerrainSmoothCommand.Smooth(doc, p.GetValueForOption(smoothPasses));
            FinishTerrain(p.GetValueForOption(jsonOption), outOpt, map, doc, r.Ok, r.Message, r.TilesChanged);
        }));

        var terrain = new Command("terrain",
            "Terrain editing: height, cliffs, ramps, textures, water, blight. Brush commands "
            + "work a radius at a time, 'fill' works a rectangle at a time.");
        terrain.AddCommand(terrainFill);
        terrain.AddCommand(terrainStats);
        terrain.AddCommand(terrainDeform);
        terrain.AddCommand(terrainCliff);
        terrain.AddCommand(terrainRamp);
        terrain.AddCommand(terrainPaint);
        terrain.AddCommand(terrainWater);
        terrain.AddCommand(terrainExportHeightmap);
        terrain.AddCommand(terrainImportHeightmap);
        terrain.AddCommand(terrainSmooth);
        var colArg = new Argument<int>("col", "Corner column, 0-based.");
        var rowArg = new Argument<int>("row", "Corner row, 0-based.");

        var terrainInfo = new Command("info",
            "Grid extents in corners, plus the map's ground and cliff tile lists. Texture fields "
            + "are indexes into those lists.")
        { mapArg, jsonOption };
        terrainInfo.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var i = TerrainEditCommand.GetInfo(MapDocument.Load(p.GetValueForArgument(mapArg)));
            if (i is null)
            {
                Emit(p.GetValueForOption(jsonOption), new { Ok = false, Message = "no terrain file" },
                    () => "this map has no war3map.w3e");
                exitCode[0] = 1;
                return;
            }
            Emit(p.GetValueForOption(jsonOption), i, () => string.Join("\n", new[]
            {
                $"grid          {i.Width} x {i.Height} corners",
                $"ground tiles  {string.Join(" ", i.GroundTiles)}",
                $"cliff tiles   {string.Join(" ", i.CliffTiles)}",
            }));
        }));

        var cornerGet = new Command("get", "Show one terrain corner's full state.")
        { mapArg, colArg, rowArg, jsonOption };
        cornerGet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            int col = p.GetValueForArgument(colArg), row = p.GetValueForArgument(rowArg);
            var c = TerrainEditCommand.GetCorner(MapDocument.Load(p.GetValueForArgument(mapArg)), col, row);
            if (c is null)
            {
                Emit(p.GetValueForOption(jsonOption),
                    new { Ok = false, Message = $"no corner at ({col}, {row})" },
                    () => $"no terrain corner at ({col}, {row}). Try 'terrain info' for the extents.");
                exitCode[0] = 1;
                return;
            }
            Emit(p.GetValueForOption(jsonOption), c, () => Render.TerrainCorner(c));
        }));

        var cornerFieldArg = new Argument<string>("field",
            "Field: " + string.Join("|", TerrainCornerFields.Fields) + ".");
        var cornerValueArg = new Argument<string>("value", "New value.");
        var cornerSet = new Command("set",
            "Set a field on ONE terrain corner and save the edited map. For area work use "
            + "'terrain deform', 'terrain paint' and the other brush commands.")
        { mapArg, colArg, rowArg, cornerFieldArg, cornerValueArg, setOut, jsonOption };
        cornerSet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TerrainCornerFields.SetField(doc, p.GetValueForArgument(colArg),
                p.GetValueForArgument(rowArg), p.GetValueForArgument(cornerFieldArg),
                p.GetValueForArgument(cornerValueArg));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc,
                r.Ok, r.Message);
        }));

        var corner = new Command("corner",
            "One terrain corner at a time: get, set. The brush commands cover areas.");
        corner.AddCommand(cornerGet);
        corner.AddCommand(cornerSet);
        terrain.AddCommand(terrainInfo);
        terrain.AddCommand(corner);

        terrain.AddCommand(terrainBlight);

        static string Trim(string t) =>
            t.Length <= 88 ? t.Replace("\n", " ") : t[..88].Replace("\n", " ") + "...";

        // ---- unit-type abilities and field options: what a unit can do, and what a field accepts ----
        var abilRawcodeArg = new Argument<string>("rawcode", "Unit type's four-character rawcode.");
        var placedOpt = new Option<int?>("--placed",
            "Show abilities for a placed unit instance by creation number instead.");
        var unitAbilities = new Command("abilities",
            "List all abilities for a unit, grouped by source (normal, hero, "
            + "spellbook, placed unit, morph form, script).")
        { mapArg, abilRawcodeArg, placedOpt, jsonOption, gameDirOption };
        unitAbilities.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var abilities = UnitAbilitiesCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForArgument(abilRawcodeArg), p.GetValueForOption(gameDirOption),
                p.GetValueForOption(placedOpt));
            Emit(p.GetValueForOption(jsonOption), abilities, () => Render.UnitAbilities(abilities));
        }));

        var optKindOpt = new Option<string>("--kind", () => "unit",
            "Object type: unit|item|ability|destructable|doodad|buff|upgrade.");
        var optFieldArg = new Argument<string>("field", "The field's four-character code.");
        var objectOptions = new Command("options",
            "The legal values for an object field, taken from what the base game data uses for it. "
            + "Ask before 'object set' on an enumerated field.")
        { optFieldArg, optKindOpt, jsonOption, gameDirOption };
        objectOptions.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ObjectFieldOptionsCommand.Execute(
                ObjectKinds.Parse(p.GetValueForOption(optKindOpt)!),
                p.GetValueForArgument(optFieldArg), p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.FieldOptions(r));
        }));
        obj.AddCommand(objectOptions);

        // ---- asset list: the paths an icon or model field can actually be set to ----
        var assetFamilyOpt = new Option<string>("--family", () => "icon",
            "Asset family: icon|model. Taken from the field's metadata type, so an icon field is "
            + "'icon' and a model field is 'model'.");
        var assetSourceOpt = new Option<string>("--source", () => "all",
            "Where to look: map|game|all.");
        var assetMapArg = new Argument<string?>("map", () => null,
            "Optional .w3x/.w3m map. Without one, only the base game's paths are listed.");
        var assetList = new Command("list",
            "List the asset paths an icon or model field can be set to, from the map's own imports "
            + "and from the base game. Use this instead of guessing a path, because a field "
            + "naming a file that does not resolve renders as nothing with no error.")
        { assetMapArg, assetFamilyOpt, assetSourceOpt, jsonOption, gameDirOption };
        assetList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var mapPath = p.GetValueForArgument(assetMapArg);
            var doc = mapPath is null ? null : MapDocument.Load(mapPath);
            var r = AssetListCommand.Execute(doc,
                AssetListCommand.ParseFamily(p.GetValueForOption(assetFamilyOpt)!),
                p.GetValueForOption(gameDirOption));

            var source = (p.GetValueForOption(assetSourceOpt) ?? "all").ToLowerInvariant();
            var shown = source switch
            {
                "map" => r.MapPaths,
                "game" => r.GamePaths,
                _ => r.All,
            };
            Emit(p.GetValueForOption(jsonOption), r, () => Render.AssetList(r, shown, source));
        }));
        var assetUnused = new Command("unused",
            "List imported files no object-data field value and no script string literal references, "
            + "with the size each holds. Read-only.") { mapArg, jsonOption };
        assetUnused.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = AssetUnusedCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.AssetUnused(r));
        }), mapArg, jsonOption);

        var assetDuplicates = new Command("duplicates",
            "Group imported files whose bytes are identical (by sha256) and report the space each "
            + "duplicate group wastes. Read-only.") { mapArg, jsonOption };
        assetDuplicates.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = AssetDuplicatesCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.AssetDuplicates(r));
        }), mapArg, jsonOption);

        var auditMaxBytesOpt = new Option<long>("--max-bytes", () => AssetAuditCommand.DefaultMaxBytes,
            "Flag imported entries larger than this many bytes.");
        var assetAudit = new Command("audit",
            "Flag suspicious imported assets. It reports entries over a size threshold, BLP textures "
            + "whose dimensions are not powers of two, a .mdx with no sibling .mdl, and zero-byte "
            + "entries. Read-only.") { mapArg, auditMaxBytesOpt, jsonOption };
        assetAudit.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = AssetAuditCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForOption(auditMaxBytesOpt));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.AssetAudit(r));
        }));

        var assetSizeReport = new Command("size-report",
            "Per-folder total size of the archive's imported entries, largest first. Read-only.")
        { mapArg, jsonOption };
        assetSizeReport.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = AssetSizeReportCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.AssetSizeReport(r));
        }), mapArg, jsonOption);

        var replaceOldArg = new Argument<string>("old", "The asset path currently referenced.");
        var replaceNewArg = new Argument<string>("new", "The asset path to reference instead.");
        var assetReplace = new Command("replace",
            "Replace an asset path everywhere it is referenced (object-data field values, script "
            + "string literals and the import table) and save a copy. Refuses if the old path is "
            + "referenced nowhere. The archive entry is not renamed.")
        { mapArg, replaceOldArg, replaceNewArg, setOut };
        assetReplace.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = AssetReplaceCommand.Execute(doc,
                p.GetValueForArgument(replaceOldArg), p.GetValueForArgument(replaceNewArg));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(map), StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("error: refusing to overwrite the input map, give -o a different path");
                exitCode[0] = 1;
                return;
            }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.ObjectFieldsChanged, r.ScriptLiteralsChanged,
                      r.ImportTableEntriesChanged, SavedTo = dest },
                () => $"{r.Message}\nsaved to {dest}");
        }));

        var assetModelLint = new Command("model-lint",
            "Scan every imported model (.mdx/.mdl) and report the structural problems a map cannot "
            + "render or animate, a geoset with no material, a material naming a texture the map "
            + "does not hold, a sequence with a zero duration, and an out of range bounding box. "
            + "Read-only.")
        { mapArg, jsonOption };
        assetModelLint.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = AssetModelLintCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.AssetModelLint(r));
        }), mapArg, jsonOption);

        var thumbFolderArg = new Argument<string>("folder",
            "Folder holding model (.mdx/.mdl) files, or map (.w3x/.w3m) files whose imported models are rendered.");
        var thumbOutOpt = new Option<string?>(new[] { "-o", "--out" },
            "Output directory for the PNG thumbnails. Defaults to ./thumbnails");
        var thumbSizeOpt = new Option<int>("--size", () => AssetThumbnailsCommand.DefaultSize,
            "Thumbnail edge length in pixels.");
        var assetThumbnails = new Command("thumbnails",
            "Render a thumbnail PNG for every model in a folder, or for every imported model inside "
            + "every map in the folder. Read-only over the source.")
        { thumbFolderArg, thumbOutOpt, thumbSizeOpt, jsonOption };
        assetThumbnails.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            int size = p.GetValueForOption(thumbSizeOpt);
            var r = AssetThumbnailsCommand.Execute(p.GetValueForArgument(thumbFolderArg), size, size);
            var dir = p.GetValueForOption(thumbOutOpt) ?? "thumbnails";
            Directory.CreateDirectory(dir);
            foreach (var t in r.Thumbnails) File.WriteAllBytes(Path.Combine(dir, t.Name), t.Png);
            var files = r.Thumbnails.Select(t => t.Name).ToList();
            Emit(p.GetValueForOption(jsonOption),
                new { r.Rendered, r.Diagnostics, SavedTo = dir, Files = files },
                () => Render.AssetThumbnails(r, dir));
        }));

        var convertBatchFolderArg = new Argument<string>("folder", "Folder holding texture files.");
        var convertBatchToOpt = new Option<string>("--to", "Target format extension, e.g. .png or .blp.")
        { IsRequired = true };
        var convertBatchOutOpt = new Option<string?>(new[] { "-o", "--out" },
            "Output directory for the converted textures. Defaults to ./converted");
        var assetConvertBatch = new Command("convert-batch",
            "Convert every texture in a folder to one target format, reusing the shared converter. "
            + "Read-only over the source, the caller writes the copies.")
        { convertBatchFolderArg, convertBatchToOpt, convertBatchOutOpt, jsonOption };
        assetConvertBatch.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = AssetConvertBatchCommand.Execute(
                p.GetValueForArgument(convertBatchFolderArg), p.GetValueForOption(convertBatchToOpt)!);
            var dir = p.GetValueForOption(convertBatchOutOpt) ?? "converted";
            Directory.CreateDirectory(dir);
            foreach (var t in r.Textures)
            {
                var dest = Path.Combine(dir, t.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllBytes(dest, t.Bytes);
            }
            var files = r.Textures.Select(t => t.RelativePath).ToList();
            Emit(p.GetValueForOption(jsonOption),
                new { r.Converted, r.ToExtension, r.Diagnostics, SavedTo = dir, Files = files },
                () => Render.AssetConvertBatch(r, dir));
        }));

        var wireFilesArg = new Argument<string[]>("files", "One or more disk files to add as imports.")
        { Arity = ArgumentArity.OneOrMore };
        var wireObjectOpt = new Option<string?>("--object",
            "Rawcode of the object whose model or icon field is pointed at each imported file.");
        var wireKindOpt = new Option<string?>("--kind",
            "Object type, one of unit|item|destructable|doodad. Defaults to auto-detect from the object.");
        var assetImportWire = new Command("import-wire",
            "Add disk files to a map as imports and, when a file is a model or an icon and --object "
            + "names an object, point its matching field at the new import. Saves a copy, refuses to "
            + "overwrite the input map.")
        { mapArg, wireFilesArg, wireObjectOpt, wireKindOpt, setOut, jsonOption };
        assetImportWire.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var kind = p.GetValueForOption(wireKindOpt) is { Length: > 0 } k ? ObjectKinds.Parse(k) : (ObjectKind?)null;
            var r = AssetImportWireCommand.Execute(doc, p.GetValueForArgument(wireFilesArg),
                p.GetValueForOption(wireObjectOpt), kind);
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => Render.AssetImportWire(r, null));
                exitCode[0] = 1;
                return;
            }
            if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(map), StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("error: refusing to overwrite the input map, give -o a different path");
                exitCode[0] = 1;
                return;
            }
            doc.Save(dest);
            Emit(p.GetValueForOption(jsonOption),
                new { r.Ok, r.Message, r.Imported, r.FieldsSet, r.Diagnostics, SavedTo = dest },
                () => Render.AssetImportWire(r, dest));
        }));

        var asset = new Command("asset", "Asset paths available to an object field.");
        asset.AddCommand(assetList);
        asset.AddCommand(assetUnused);
        asset.AddCommand(assetDuplicates);
        asset.AddCommand(assetAudit);
        asset.AddCommand(assetSizeReport);
        asset.AddCommand(assetReplace);
        asset.AddCommand(assetModelLint);
        asset.AddCommand(assetThumbnails);
        asset.AddCommand(assetConvertBatch);
        asset.AddCommand(assetImportWire);
        root.AddCommand(asset);

        var modelPathArg = new Argument<string>("model",
            "Internal model path (.mdx/.mdl) inside the map.");
        var modelObjOutOpt = new Option<string?>(new[] { "-o", "--out" },
            "Output .obj path. A .mtl and any map-resolvable textures are written beside it.");
        var modelExportObj = new Command("export-obj",
            "Export a map-imported model to Wavefront OBJ, with an MTL and any map-resolvable "
            + "textures converted to PNG beside it.")
        { mapArg, modelPathArg, modelObjOutOpt, jsonOption };
        modelExportObj.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var doc = MapDocument.Load(p.GetValueForArgument(mapArg));
            var export = ModelExportObjCommand.Execute(doc, p.GetValueForArgument(modelPathArg));
            var dest = p.GetValueForOption(modelObjOutOpt) ?? "model.obj";
            File.WriteAllText(dest, export.Obj);
            var mtlPath = Path.ChangeExtension(dest, ".mtl");
            File.WriteAllText(mtlPath, export.Mtl);
            var dir = Path.GetDirectoryName(Path.GetFullPath(dest)) ?? ".";
            var textures = new List<string>();
            foreach (var (name, bytes) in export.Textures)
            {
                File.WriteAllBytes(Path.Combine(dir, name), bytes);
                textures.Add(name);
            }
            Emit(p.GetValueForOption(jsonOption),
                new { SavedTo = dest, Mtl = mtlPath, Textures = textures },
                () => Render.ModelExportObj(export, dest, mtlPath, textures));
        }));
        var model = new Command("model", "Model asset operations.");
        model.AddCommand(modelExportObj);
        root.AddCommand(model);

        // ---- strings / imports / trigger read: reachable from the GUI, previously not here ----
        var stringsList = new Command("list",
            "List the map's string table (war3map.wts). Every TRIGSTR_ reference resolves here.")
        { mapArg, jsonOption };
        stringsList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = StringsCommand.List(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r,
                () => r.Entries.Count == 0
                    ? "(no string table)"
                    : string.Join("\n", r.Entries.Select(e =>
                        $"TRIGSTR_{e.Id,-6} {Trim(e.Text)}")));
        }));

        var stringsMissing = new Command("missing",
            "List the TRIGSTR_ keys the script and the trigger tree reference that have no "
            + "war3map.wts entry. Read-only.")
        { mapArg, jsonOption };
        stringsMissing.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = StringsMissingCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r,
                () => r.MissingIds.Count == 0
                    ? $"(no missing keys, {r.ReferencedIds} referenced, {r.DefinedEntries} defined)"
                    : string.Join("\n", r.MissingIds.Select(id => $"TRIGSTR_{id}")));
        }));

        var stringsUnused = new Command("unused",
            "List war3map.wts entries no object field, map-info field, trigger or script line "
            + "references. Read-only.")
        { mapArg, jsonOption };
        stringsUnused.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = StringsUnusedCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r,
                () => r.Unused.Count == 0
                    ? $"(no unused entries, {r.DefinedEntries} defined)"
                    : string.Join("\n", r.Unused.Select(u => $"TRIGSTR_{u.Id,-6} {Trim(u.Text)}")));
        }));

        var stringsMapA = new Argument<string>("mapA", "Path to the first .w3x/.w3m map.");
        var stringsMapB = new Argument<string>("mapB", "Path to the second .w3x/.w3m map.");
        var stringsDiff = new Command("diff",
            "Compare two maps' string tables and report entries added, removed and changed. Read-only.")
        { stringsMapA, stringsMapB, jsonOption };
        stringsDiff.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = StringsDiffCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(stringsMapA)),
                MapDocument.Load(p.GetValueForArgument(stringsMapB)));
            Emit(p.GetValueForOption(jsonOption), r, () =>
            {
                var lines = new List<string>();
                lines.AddRange(r.Added.Select(e => $"+ TRIGSTR_{e.Id,-6} {Trim(e.Text)}"));
                lines.AddRange(r.Removed.Select(e => $"- TRIGSTR_{e.Id,-6} {Trim(e.Text)}"));
                lines.AddRange(r.Changed.Select(c => $"~ TRIGSTR_{c.Id,-6} {Trim(c.NewText ?? "")}"));
                return lines.Count == 0 ? "(identical string tables)" : string.Join("\n", lines);
            });
        }));

        var stringsTextArg = new Argument<string>("text",
            "Text to find in entry text, a case-insensitive substring.");
        var stringsFind = new Command("find",
            "Find war3map.wts entries whose text contains the given text (case-insensitive), with "
            + "the id. Read-only.")
        { mapArg, stringsTextArg, jsonOption };
        stringsFind.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = StringsFindCommand.Execute(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForArgument(stringsTextArg));
            Emit(p.GetValueForOption(jsonOption), r,
                () => r.Matches.Count == 0
                    ? $"(no match for '{r.Query}')"
                    : string.Join("\n", r.Matches.Select(e => $"TRIGSTR_{e.Id,-6} {Trim(e.Text)}")));
        }));

        var stringsIdArg = new Argument<int>("id", "war3map.wts entry id to set or add.");
        var stringsSetTextArg = new Argument<string>("text", "New text for the entry.");
        var stringsSet = new Command("set",
            "Set one war3map.wts entry's text, or add it when the id is absent, and save a copy. "
            + "The input map is never overwritten.")
        { mapArg, stringsIdArg, stringsSetTextArg, setOut };
        stringsSet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = StringsSetCommand.Execute(
                p.GetValueForArgument(mapArg),
                p.GetValueForArgument(stringsIdArg),
                p.GetValueForArgument(stringsSetTextArg),
                p.GetValueForOption(setOut));
            Emit(p.GetValueForOption(jsonOption), r,
                () => $"{(r.Added ? "added" : "set")} TRIGSTR_{r.Id}\nsaved to {r.SavedTo}");
        }));

        var strings = new Command("strings", "The map's string table (war3map.wts).");
        strings.AddCommand(stringsList);
        strings.AddCommand(stringsMissing);
        strings.AddCommand(stringsUnused);
        strings.AddCommand(stringsDiff);
        strings.AddCommand(stringsFind);
        strings.AddCommand(stringsSet);
        root.AddCommand(strings);

        var importsList = new Command("list",
            "List the import table (war3map.imp) against what the archive holds, so an entry with "
            + "no file and a file absent from the table both show up.")
        { mapArg, jsonOption };
        importsList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = ImportsCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.Imports(r));
        }));
        var imports = new Command("imports", "The map's import table (war3map.imp).");
        imports.AddCommand(importsList);
        root.AddCommand(imports);

        var triggerRead = new Command("read",
            "Read the GUI trigger tree (war3map.wtg) plus the custom-text bodies (war3map.wct). "
            + "Use the sibling verbs to add, remove, rename and re-flag triggers.")
        { mapArg, jsonOption };
        triggerRead.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var t = TriggerReadCommand.GetTriggers(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), t, () => Render.Triggers(t));
        }));

        // ---- repairing a map wc3ctl generated ----
        // This existed complete and tested and was reachable from nothing, which is the same
        // defect the four trigger edits had. It is deliberately narrow: it looks for the
        // wc3ctl_WirePlacedHeroSpells helper, so it repairs maps this tool generated rather than
        // maps in general, and it says so rather than appearing to be a general repair.
        var repairHeroes = new Command("repair-generated",
            "Repair a map that wc3ctl generated, whose preplaced-hero helper block is too narrow "
            + "for the heroes installed into it. Widens the player slots, rewrites the hero "
            + "owners in the script, and brings war3map.w3i and the map header into line. Only "
            + "applies to maps carrying wc3ctl's own generated helper, and refuses others.")
        { mapArg, setOut };
        repairHeroes.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = GeneratedMapRepairCommand.RepairGeneratedHeroes(doc);

            // The detail goes in the message rather than in a second print, so the --json shape
            // and the human shape carry the same facts through the one shared helper.
            string detail = r.Ok
                ? $"{r.Message} Player slots {r.PlayerSlotsBefore} to {r.PlayerSlotsAfter}. "
                  + $"Script {(r.ScriptUpdated ? "updated" : "unchanged")}, "
                  + $"war3map.w3i {(r.MapInfoUpdated ? "updated" : "unchanged")}, "
                  + $"header {(r.HeaderUpdated ? "updated" : "unchanged")}. "
                  + $"{r.Heroes.Count} hero(es) reassigned."
                : r.Message;

            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut),
                map, doc, r.Ok, detail);
        }));

        // ---- trigger edits ----
        // These four already existed in Wc3.Commands, worked, and were reachable from NO
        // front-end at all. Verified to survive a save and to touch only war3map.wtg before being
        // exposed, because a trigger edit that silently did not persist is worse than none.
        var trigIdArg = new Argument<int>("id", "Trigger item id, as shown by 'trigger read'.");
        var trigOnArg = new Argument<bool>("on", "true or false.");
        var trigNewName = new Argument<string>("name", "New name.");
        var trigParentOpt = new Option<int>(new[] { "-p", "--parent" },
            () => -1, "Parent item id, or -1 for the top level.");
        var trigParentReq = new Argument<int>("parent",
            "Id of the category to add the trigger to, or -1 for the top level.");
        var trigCommentOpt = new Option<bool>("--comment",
            "Create a comment rather than a GUI trigger.");
        var ecaKindArg = new Argument<string>("kind", "event, condition or action.");
        var ecaNameArg = new Argument<string>("name",
            "Function name from the World-Editor table, for example DisplayTextToForce. "
            + "Use 'trigger catalog list' to find one.");
        // An OPTION rather than a trailing variadic argument, because System.CommandLine binds a
        // ZeroOrMore positional greedily: it swallowed the id, the kind and the name, and the
        // command then reported "MapInitializationEvent takes 0 parameter(s), but 3 were given".
        // Repeating -p is unambiguous and reads better for values that contain spaces.
        var ecaParamsOpt = new Option<string[]>(new[] { "-p", "--param" },
            "A parameter value, repeated once per parameter, in the order the function declares "
            + "them. Any you leave off are filled from the World-Editor table's own defaults.")
        { AllowMultipleArgumentsPerToken = false };
        var ecaIndexArg = new Argument<int>("index",
            "Zero-based position of the function within the trigger, as 'trigger read' lists it.");

        var trigRecursiveOpt = new Option<bool>(new[] { "-r", "--recursive" },
            "Remove the item's descendants too, rather than refusing to orphan them.");

        // ---- events, conditions and actions ----
        // war3map.wtg stores a function's parameters but not how many there are, so the count is
        // taken from the World-Editor table on read. Writing the wrong number makes the file
        // unreadable rather than merely wrong, which is why the arity is not negotiable here.
        var ecaAdd = new Command("add-eca",
            "Add an event, condition or action to a GUI trigger and save the edited map. "
            + "Parameters you leave off are filled from the World-Editor table's own defaults, "
            + "because a function written with the wrong number of parameters produces a map "
            + "nothing can read. This edits the World Editor's trigger source (war3map.wtg), not "
            + "the compiled script the game runs (war3map.j), so the trigger takes effect only "
            + "after the map is opened and saved in the World Editor.")
        { mapArg, trigIdArg, ecaKindArg, ecaNameArg, ecaParamsOpt, setOut };
        ecaAdd.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            if (!TryParseEcaKind(p.GetValueForArgument(ecaKindArg), out var kind))
            {
                Console.Error.WriteLine("kind must be event, condition or action.");
                Environment.ExitCode = 1;
                return;
            }
            var doc = MapDocument.Load(map);
            var r = TriggerCommand.AddFunction(doc, p.GetValueForArgument(trigIdArg), kind,
                p.GetValueForArgument(ecaNameArg), p.GetValueForOption(ecaParamsOpt));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var ecaRemove = new Command("remove-eca",
            "Remove one event, condition or action from a GUI trigger by its position.")
        { mapArg, trigIdArg, ecaIndexArg, setOut };
        ecaRemove.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TriggerCommand.RemoveFunction(doc, p.GetValueForArgument(trigIdArg),
                p.GetValueForArgument(ecaIndexArg));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var ecaEnabled = new Command("set-eca-enabled",
            "Enable or disable ONE event, condition or action within a trigger, which is the "
            + "World Editor's per-line toggle rather than the whole-trigger one.")
        { mapArg, trigIdArg, ecaIndexArg, trigOnArg, setOut };
        ecaEnabled.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TriggerCommand.SetFunctionEnabled(doc, p.GetValueForArgument(trigIdArg),
                p.GetValueForArgument(ecaIndexArg), p.GetValueForArgument(trigOnArg));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var trigAddCat = new Command("add-category",
            "Add a category to the trigger tree and save the edited map.")
        { mapArg, trigNewName, trigParentOpt, setOut };
        trigAddCat.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TriggerCommand.AddCategory(doc, p.GetValueForArgument(trigNewName),
                p.GetValueForOption(trigParentOpt));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var trigAdd = new Command("add",
            "Add a trigger under a category and save the edited map. The new trigger is enabled "
            + "and initially on, matching the World Editor. Custom-text triggers cannot be added, "
            + "because their body lives in war3map.wct, which cannot be written back. This edits "
            + "the World Editor's trigger source, not the compiled war3map.j the game runs, so "
            + "the trigger takes effect only after a World Editor save.")
        { mapArg, trigNewName, trigParentReq, trigCommentOpt, setOut };
        trigAdd.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TriggerCommand.AddTrigger(doc, p.GetValueForArgument(trigNewName),
                p.GetValueForArgument(trigParentReq),
                p.GetValueForOption(trigCommentOpt)
                    ? TriggerCommand.NewTriggerKind.Comment
                    : TriggerCommand.NewTriggerKind.Gui);
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var trigRemove = new Command("remove",
            "Remove a trigger item and save the edited map. Refuses to orphan a category's "
            + "children (pass --recursive to take the subtree) and refuses when it would shift a "
            + "war3map.wct code body onto the wrong trigger.")
        { mapArg, trigIdArg, trigRecursiveOpt, setOut };
        trigRemove.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TriggerCommand.Remove(doc, p.GetValueForArgument(trigIdArg),
                p.GetValueForOption(trigRecursiveOpt));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var trigRename = new Command("rename",
            "Rename a trigger item (a category, a trigger or a deleted stub) and save the edited map.")
        { mapArg, trigIdArg, trigNewName, setOut };
        trigRename.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TriggerCommand.Rename(doc, p.GetValueForArgument(trigIdArg),
                p.GetValueForArgument(trigNewName));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var trigEnabled = new Command("set-enabled",
            "Enable or disable a trigger. A disabled trigger is not compiled into the map script.")
        { mapArg, trigIdArg, trigOnArg, setOut };
        trigEnabled.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TriggerCommand.SetEnabled(doc, p.GetValueForArgument(trigIdArg),
                p.GetValueForArgument(trigOnArg));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var trigInitiallyOn = new Command("set-initially-on",
            "Set whether a trigger starts switched on. One that is off at map start never fires "
            + "until something turns it on.")
        { mapArg, trigIdArg, trigOnArg, setOut };
        trigInitiallyOn.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TriggerCommand.SetInitiallyOn(doc, p.GetValueForArgument(trigIdArg),
                p.GetValueForArgument(trigOnArg));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var trigRunOnInit = new Command("set-run-on-map-init",
            "Set whether a trigger runs on map initialization.")
        { mapArg, trigIdArg, trigOnArg, setOut };
        trigRunOnInit.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TriggerCommand.SetRunOnMapInit(doc, p.GetValueForArgument(trigIdArg),
                p.GetValueForArgument(trigOnArg));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        // ---- unit / doodad: the units and doodads ALREADY PLACED on the map ----
        // 'place' adds one. These read, edit and remove what is there, which is most of what an
        // editor is for and was previously reachable only from the GUI.
        var cnArg = new Argument<int>("creation-number",
            "The instance's creation number, as shown by 'list'. Stable for the life of the map.");

        var unitInstList = new Command("list", "List every placed unit, with its creation number.")
        { mapArg, jsonOption };
        unitInstList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var units = UnitInstanceCommand.List(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), units,
                () => units.Count == 0
                    ? "(no placed units)"
                    : string.Join("\n", units.Select(u =>
                        $"{u.CreationNumber,6}  {u.TypeRawcode}  {u.Name ?? "",-24} "
                        + $"owner={u.OwnerId,-3} at ({u.X:0.#}, {u.Y:0.#})"
                        + (u.HeroLevel > 0 ? $"  lvl={u.HeroLevel}" : ""))));
        }));

        var unitInstGet = new Command("get", "Show one placed unit's full state.")
        { mapArg, cnArg, jsonOption };
        unitInstGet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            int cn = p.GetValueForArgument(cnArg);
            var u = UnitInstanceCommand.Get(MapDocument.Load(p.GetValueForArgument(mapArg)), cn);
            if (u is null)
            {
                Emit(p.GetValueForOption(jsonOption), new { Ok = false, Message = $"no placed unit {cn}" },
                    () => $"no placed unit with creation number {cn}");
                exitCode[0] = 1;
                return;
            }
            Emit(p.GetValueForOption(jsonOption), u, () => Render.PlacedUnit(u));
        }));

        var instFieldArg = new Argument<string>("field", "Field to set.");
        var instValueArg = new Argument<string>("value",
            "New value. A position or scale takes a comma-separated tuple, and a single number "
            + "scales uniformly.");

        var unitInstSet = new Command("set",
            "Set a field on a placed unit and save the edited map. Fields: "
            + string.Join("|", PlacedInstanceFields.UnitFields) + ".")
        { mapArg, cnArg, instFieldArg, instValueArg, setOut, jsonOption };
        unitInstSet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = PlacedInstanceFields.SetUnitField(doc, p.GetValueForArgument(cnArg),
                p.GetValueForArgument(instFieldArg), p.GetValueForArgument(instValueArg));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc,
                r.Ok, r.Message);
        }));

        var unitInstRemove = new Command("remove", "Remove a placed unit and save the edited map.")
        { mapArg, cnArg, setOut, jsonOption };
        unitInstRemove.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = UnitInstanceCommand.Delete(doc, p.GetValueForArgument(cnArg));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc,
                r.Ok, r.Message);
        }));

        var unitInst = new Command("unit",
            "Placed units (war3mapUnits.doo): list, get, set, remove. Use 'place unit' to add one.");
        unitInst.AddCommand(unitInstList);
        unitInst.AddCommand(unitInstGet);
        unitInst.AddCommand(unitInstSet);
        unitInst.AddCommand(unitInstRemove);
        unitInst.AddCommand(unitAbilities);
        root.AddCommand(unitInst);

        var doodadInstList = new Command("list", "List every placed doodad, with its creation number.")
        { mapArg, jsonOption };
        doodadInstList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var ds = DoodadInstanceCommand.List(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), ds,
                () => ds.Count == 0
                    ? "(no placed doodads)"
                    : string.Join("\n", ds.Select(d =>
                        $"{d.CreationNumber,6}  {d.TypeRawcode}  at ({d.X:0.#}, {d.Y:0.#}, {d.Z:0.#})"
                        + $"  var={d.Variation}")));
        }));

        var doodadInstGet = new Command("get", "Show one placed doodad's full state.")
        { mapArg, cnArg, jsonOption };
        doodadInstGet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            int cn = p.GetValueForArgument(cnArg);
            var d = DoodadInstanceCommand.Get(MapDocument.Load(p.GetValueForArgument(mapArg)), cn);
            if (d is null)
            {
                Emit(p.GetValueForOption(jsonOption), new { Ok = false, Message = $"no placed doodad {cn}" },
                    () => $"no placed doodad with creation number {cn}");
                exitCode[0] = 1;
                return;
            }
            Emit(p.GetValueForOption(jsonOption), d, () => Render.PlacedDoodad(d));
        }));

        var doodadInstSet = new Command("set",
            "Set a field on a placed doodad and save the edited map. Fields: "
            + string.Join("|", PlacedInstanceFields.DoodadFields) + ".")
        { mapArg, cnArg, instFieldArg, instValueArg, setOut, jsonOption };
        doodadInstSet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = PlacedInstanceFields.SetDoodadField(doc, p.GetValueForArgument(cnArg),
                p.GetValueForArgument(instFieldArg), p.GetValueForArgument(instValueArg));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc,
                r.Ok, r.Message);
        }));

        var doodadInstRemove = new Command("remove", "Remove a placed doodad and save the edited map.")
        { mapArg, cnArg, setOut, jsonOption };
        doodadInstRemove.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = DoodadInstanceCommand.Delete(doc, p.GetValueForArgument(cnArg));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc,
                r.Ok, r.Message);
        }));

        var doodadInst = new Command("doodad",
            "Placed doodads (war3map.doo): list, get, set, remove. Use 'place doodad' to add one.");
        doodadInst.AddCommand(doodadInstList);
        doodadInst.AddCommand(doodadInstGet);
        doodadInst.AddCommand(doodadInstSet);
        doodadInst.AddCommand(doodadInstRemove);
        root.AddCommand(doodadInst);

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
            "Camera catalog editing (war3map.w3c). List, add, set, remove, tour.");
        camera.AddCommand(camList);
        camera.AddCommand(camAdd);
        camera.AddCommand(camSet);
        camera.AddCommand(camRemove);

        var camTourFrom = new Option<string>("--from",
            "Name of the camera the tour starts at.") { IsRequired = true };
        var camTourTo = new Option<string>("--to",
            "Name of the camera the tour ends at.") { IsRequired = true };
        var camTourCount = new Option<int>("--count", () => 3,
            "How many cameras to interpolate between the two.");
        var camTour = new Command("tour",
            "Interpolate N cameras between two existing cameras as a smooth path and save the "
            + "edited map. Every field is blended linearly and the generated cameras sit strictly "
            + "between the endpoints, named <from>-tour-i.")
        { mapArg, camTourFrom, camTourTo, camTourCount, setOut };
        camTour.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = CameraCommand.Tour(doc, p.GetValueForOption(camTourFrom)!,
                p.GetValueForOption(camTourTo)!, p.GetValueForOption(camTourCount));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));
        camera.AddCommand(camTour);

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

        var pathingPngOut = new Option<string>(new[] { "-o", "--out" },
            "Output PNG path.") { IsRequired = true };
        var pathingExportPng = new Command("export-png",
            "Render the pathing map to a colour-coded PNG with a legend of the pathing flags. Read-only.")
        { mapArg, pathingPngOut };
        pathingExportPng.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = PathingExportPngCommand.Export(MapDocument.Load(p.GetValueForArgument(mapArg)));
            if (!r.Ok || r.Png is null)
            {
                Emit(p.GetValueForOption(jsonOption), new { r.Ok, r.Message }, () => r.Message);
                exitCode[0] = 1;
                return;
            }
            string dest = p.GetValueForOption(pathingPngOut);
            File.WriteAllBytes(dest, r.Png);
            Emit(p.GetValueForOption(jsonOption),
                new { Ok = true, Message = r.Message, Width = r.Width, Height = r.Height,
                      SizeBytes = r.Png.Length, SavedTo = dest,
                      Legend = r.Legend!.Select(l => new { l.Name, l.Colour }) },
                () => string.Join("\n", new[]
                {
                    $"{r.Message}",
                    $"wrote {r.Png.Length:N0} bytes to {dest}",
                    "legend",
                    string.Join("\n", r.Legend!.Select(l => $"  {l.Name,-8} {l.Colour}")),
                }));
        }));

        var pathingPngArg = new Argument<string>("png", "Path to the colour-coded pathing PNG.");
        var pathingImportPng = new Command("import-png",
            "Set the pathing flags from a colour-coded PNG. An unknown colour is refused.")
        { mapArg, pathingPngArg, setOut };
        pathingImportPng.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string? outOpt = p.GetValueForOption(setOut);
            if (RefusedInPlace(p.GetValueForOption(jsonOption), outOpt, map)) return;
            var doc = MapDocument.Load(map);
            byte[] png = File.ReadAllBytes(p.GetValueForArgument(pathingPngArg));
            var r = PathingImportPngCommand.Import(doc, png);
            FinishEdit(p.GetValueForOption(jsonOption), outOpt, map, doc,
                r.Ok, r.Ok ? $"{r.Message}" : r.Message);
        }));
        pathing.AddCommand(pathingExportPng);
        pathing.AddCommand(pathingImportPng);

        var pathingValidate = new Command("validate",
            "Flag enclosed unpathable pockets, start locations on unpathable tiles, and "
            + "unreachable map corners. Read-only.")
        { mapArg };
        pathingValidate.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = PathingCommand.Validate(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r,
                () => r.Message + (r.Issues.Count == 0
                    ? ""
                    : "\n" + string.Join("\n", r.Issues.Select(i => $"  [{i.Kind}] {i.Detail}"))));
            if (r.Issues.Count > 0) exitCode[0] = 2;
            else if (!r.Ok) exitCode[0] = 1;
        }));
        pathing.AddCommand(pathingValidate);

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
            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
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
        var pfPlayerOpt = new Option<int>("--player", "Player id, 0-based.") { IsRequired = true };
        var pfNameOpt = new Option<string>("--name", "New name text.") { IsRequired = true };
        var pfColorOpt = new Option<string>("--color",
            "Team color name, for example red, lightblue or darkgreen.") { IsRequired = true };
        var pfRaceOpt = new Option<string>("--race",
            "human, orc, undead, nightelf or random.") { IsRequired = true };

        var playerSetName = new Command("set-name",
            "Set a player's display name in the map info and save the edited map.")
        { mapArg, pfPlayerOpt, pfNameOpt, setOut };
        playerSetName.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string? outOpt = p.GetValueForOption(setOut);
            if (RefusedInPlace(p.GetValueForOption(jsonOption), outOpt, map)) return;
            var doc = MapDocument.Load(map);
            var r = PlayerSetNameCommand.Execute(doc,
                p.GetValueForOption(pfPlayerOpt), p.GetValueForOption(pfNameOpt)!);
            FinishEdit(p.GetValueForOption(jsonOption), outOpt, map, doc, r.Ok, r.Message);
        }));

        var playerSetColor = new Command("set-color",
            "Set a player's team color in the map script and save the edited map.")
        { mapArg, pfPlayerOpt, pfColorOpt, setOut };
        playerSetColor.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string? outOpt = p.GetValueForOption(setOut);
            if (RefusedInPlace(p.GetValueForOption(jsonOption), outOpt, map)) return;
            var doc = MapDocument.Load(map);
            var r = PlayerSetColorCommand.Execute(doc,
                p.GetValueForOption(pfPlayerOpt), p.GetValueForOption(pfColorOpt)!);
            FinishEdit(p.GetValueForOption(jsonOption), outOpt, map, doc, r.Ok, r.Message);
        }));

        var playerSetRace = new Command("set-race",
            "Set a player's race in the map info and save the edited map.")
        { mapArg, pfPlayerOpt, pfRaceOpt, setOut };
        playerSetRace.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string? outOpt = p.GetValueForOption(setOut);
            if (RefusedInPlace(p.GetValueForOption(jsonOption), outOpt, map)) return;
            var doc = MapDocument.Load(map);
            var r = PlayerSetRaceCommand.Execute(doc,
                p.GetValueForOption(pfPlayerOpt), p.GetValueForOption(pfRaceOpt)!);
            FinishEdit(p.GetValueForOption(jsonOption), outOpt, map, doc, r.Ok, r.Message);
        }));

        var player = new Command("player", "Player slots (war3map.w3i): list, set-force.");
        player.AddCommand(playerList);
        player.AddCommand(playerSetForce);
        player.AddCommand(playerSetName);
        player.AddCommand(playerSetColor);
        player.AddCommand(playerSetRace);

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
        var pfForceOpt = new Option<int>("--force", "Force index, 0-based.") { IsRequired = true };
        var pfAddOpt = new Option<int?>("--add", "Add this player id to the force.");
        var pfRemoveOpt = new Option<int?>("--remove", "Remove this player id from the force.");

        var forceRename = new Command("rename",
            "Rename a force and save the edited map.")
        { mapArg, pfForceOpt, pfNameOpt, setOut };
        forceRename.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string? outOpt = p.GetValueForOption(setOut);
            if (RefusedInPlace(p.GetValueForOption(jsonOption), outOpt, map)) return;
            var doc = MapDocument.Load(map);
            var r = ForceRenameCommand.Execute(doc,
                p.GetValueForOption(pfForceOpt), p.GetValueForOption(pfNameOpt)!);
            FinishEdit(p.GetValueForOption(jsonOption), outOpt, map, doc, r.Ok, r.Message);
        }));

        var forceSetPlayers = new Command("set-players",
            "Add or remove a player from a force and save the edited map.")
        { mapArg, pfForceOpt, pfAddOpt, pfRemoveOpt, setOut };
        forceSetPlayers.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string? outOpt = p.GetValueForOption(setOut);
            if (RefusedInPlace(p.GetValueForOption(jsonOption), outOpt, map)) return;
            int? add = p.GetValueForOption(pfAddOpt);
            int? remove = p.GetValueForOption(pfRemoveOpt);
            var doc = MapDocument.Load(map);
            if ((add is null) == (remove is null))
            {
                FinishEdit(p.GetValueForOption(jsonOption), outOpt, map, doc, false,
                    "pass exactly one of --add or --remove");
                return;
            }
            var r = add is not null
                ? ForceSetPlayersCommand.Add(doc, p.GetValueForOption(pfForceOpt), add.Value)
                : ForceSetPlayersCommand.Remove(doc, p.GetValueForOption(pfForceOpt), remove!.Value);
            FinishEdit(p.GetValueForOption(jsonOption), outOpt, map, doc, r.Ok, r.Message);
        }));

        var force = new Command("force", "Forces / teams (war3map.w3i): list, set-flags.");
        force.AddCommand(forceList);
        force.AddCommand(forceSetFlags);
        force.AddCommand(forceRename);
        force.AddCommand(forceSetPlayers);

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
        var feFrom = new Option<string?>(new[] { "--from" },
            "Disk file whose exact bytes become the new payload (or give it as the third argument).");

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

        // The source may come positionally or as --from. Both shapes existed in the two lines of
        // this tool before they merged, so both keep working.
        var feSourceArg = new Argument<string?>("source", () => null,
            "Disk file whose bytes are written into the archive verbatim (or use --from).");
        var fileSet = new Command("set",
            "Write a disk file into the map, replacing that entry or adding it, and save the edited map.")
        { mapArg, feNameArg, feSourceArg, feFrom, setOut };
        fileSet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string name = p.GetValueForArgument(feNameArg);
            string from = p.GetValueForArgument(feSourceArg) ?? p.GetValueForOption(feFrom)
                ?? throw new ArgumentException("name the disk file to write, as a third argument or with --from");
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

            var dest = ObjectEditedPath(p, map);
            if (dest is null) { exitCode[0] = 1; return; }
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
            // Real start-location markers so the map opens a proper host lobby (a marker-less map shows
            // an empty "0/N" slot list on Battle.net).
            var opts = new BlankMapOptions { IncludeStartLocations = true };
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
                        (f.DisplayName is null ? "" : $"  ({f.DisplayName})")))
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

        // Recovery goes one way only. Regenerating the script from a tree is destructive, so
        // this rebuilds the TREE from the script the game already runs, and refuses any map
        // that still has a tree of its own. See TriggerRecoverCommand for the measurements.
        var trigRecover = new Command("recover-from-script",
            "Rebuild a browsable GUI trigger tree for a map that has NONE, by decompiling the "
            + "compiled script. The script is not modified. Refuses when the map already has a "
            + "tree, because a decompiled tree cannot be trusted to replace a real one.")
        { mapArg, setOut };
        trigRecover.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = TriggerRecoverCommand.Recover(doc);
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var trigger = new Command("trigger", "GUI triggers, read the tree, edit a trigger's name "
            + "and flags, browse the World-Editor catalog, recover a tree from the script, and "
            + "analyse the tree read-only (to-script, diff, lint, export, dead).");
        trigger.AddCommand(trigCatalog);
        trigger.AddCommand(trigRecover);

        // --- trigger read-only analysis (to-script, diff, lint, export, dead) ---
        // Every one of these reads the tree and never edits the map. to-script resolves preset
        // parameter values through the World-Editor catalog when the installed game provides one,
        // and falls back to the raw names otherwise rather than refusing.
        var trigToScript = new Command("to-script",
            "Render the GUI trigger tree to JASS-style text, the way the World Editor's 'convert "
            + "to custom text' presents one trigger. Read-only. It is a rendering for reading, not "
            + "a replacement for the compiled script, and it says so in its own header.")
        { mapArg };
        trigToScript.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = TriggerToScriptCommand.Render(
                MapDocument.Load(p.GetValueForArgument(mapArg)),
                TryCatalog(p.GetValueForOption(gameDirOption)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.TriggerScript(r));
        }));

        var trigMapAArg = new Argument<string>("mapA", "Path to the first .w3x/.w3m map.");
        var trigMapBArg = new Argument<string>("mapB", "Path to the second .w3x/.w3m map.");
        var trigDiff = new Command("diff",
            "Compare two maps' GUI trigger trees, reporting triggers added, removed and renamed, "
            + "and event, condition or action changes within a trigger shared by both maps.")
        { trigMapAArg, trigMapBArg };
        trigDiff.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = TriggerDiffCommand.Compare(
                MapDocument.Load(p.GetValueForArgument(trigMapAArg)),
                MapDocument.Load(p.GetValueForArgument(trigMapBArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.TriggerDiff(r));
        }));

        var trigLint = new Command("lint",
            "Check a map's GUI trigger tree for triggers that can never fire (no event and not run "
            + "on map initialization), triggers with a duplicate event, and triggers with no action.")
        { mapArg };
        trigLint.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = TriggerLintCommand.Lint(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.TriggerLint(r));
        }));

        var trigExport = new Command("export",
            "Export the full GUI trigger tree as structured output. Each trigger carries its name "
            + "and enabled flags, and its event, condition and action lists with the catalog "
            + "function name and arguments for every node.")
        { mapArg };
        trigExport.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = TriggerExportCommand.Export(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.TriggerExport(r));
        }));

        var trigDead = new Command("dead",
            "Find triggers that cannot do anything. Those with no action, and those whose events "
            + "never fire, for example a unit event holding a unit variable nothing assigns or a "
            + "variable event watching a variable nothing sets.")
        { mapArg };
        trigDead.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = TriggerDeadCommand.Dead(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.TriggerDead(r));
        }));

        trigger.AddCommand(trigToScript);
        trigger.AddCommand(trigDiff);
        trigger.AddCommand(trigLint);
        trigger.AddCommand(trigExport);
        trigger.AddCommand(trigDead);

        // ---- editor, the World Editor's catalogs (UI\WorldEditData.txt, read-only) ----
        var editorCatalogList = new Command("list",
            "List the catalog names the installed game defines, with entry counts.");
        editorCatalogList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var res = EditorCatalogCommand.Names(p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), res,
                () => string.Join("\n", res.Catalogs.Select(c => $"{c.Name,-24} {c.Entries,4}"))
                      + $"\n\n{res.Total} catalog(s)");
        }));

        var editorCatalogNameArg = new Argument<string>("name",
            "Catalog name, e.g. TileSets (see 'editor catalog list').");
        var editorCatalogGet = new Command("get",
            "Show one catalog's entries, stored key, display name and payload fields.")
        { editorCatalogNameArg };
        editorCatalogGet.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var res = EditorCatalogCommand.List(
                p.GetValueForArgument(editorCatalogNameArg), p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), res,
                () => string.Join("\n", res.Entries.Select(e =>
                          $"{e.Key,-18} {e.Label,-30} {string.Join(", ", e.Values)}"))
                      + $"\n\n{res.Total} entry(ies) in {res.Catalog}");
        }));

        var editorCatalog = new Command("catalog",
            "The World Editor's catalogs from UI\\WorldEditData.txt (list, get).");
        editorCatalog.AddCommand(editorCatalogList);
        editorCatalog.AddCommand(editorCatalogGet);

        var editor = new Command("editor",
            "World Editor base data (TileSets, SkyModels, LoadingScreens, brushes and more).");
        editor.AddCommand(editorCatalog);

        // ---- region: list and remove regions (war3map.w3r). Add via 'place region' ----
        var regionList = new Command("list", "List the map's regions.") { mapArg };
        regionList.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var regions = PlacementCommand.ListRegions(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), regions,
                () => regions.Count == 0 ? "(no regions)" : string.Join("\n", regions.Select(rg =>
                    $"[{rg.CreationNumber}] {rg.Name}  [{rg.Left:0},{rg.Bottom:0}]..[{rg.Right:0},{rg.Top:0}]")));
        }));

        var regionNameArg = new Argument<string>("name", "Region name to remove.");
        var regionRemove = new Command("remove", "Remove a region by name and save the edited map.")
        { mapArg, regionNameArg, setOut };
        regionRemove.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = PlacementCommand.RemoveRegion(doc, p.GetValueForArgument(regionNameArg));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var regionCreateName = new Option<string>("--name", "Region name.") { IsRequired = true };
        var regionCreateX = new Option<float>("--x", "South-west corner X (world units).") { IsRequired = true };
        var regionCreateY = new Option<float>("--y", "South-west corner Y (world units).") { IsRequired = true };
        var regionCreateW = new Option<float>("--w", "Region width (world units).") { IsRequired = true };
        var regionCreateH = new Option<float>("--h", "Region height (world units).") { IsRequired = true };
        var regionCreateWeather = new Option<bool>("--weather",
            "Attach the default weather effect to the region.");
        var regionCreate = new Command("create",
            "Create a rectangular region (south-west corner plus width and height) and save the "
            + "edited map. Writes war3map.w3r.")
        { mapArg, regionCreateName, regionCreateX, regionCreateY, regionCreateW, regionCreateH,
          regionCreateWeather, setOut };
        regionCreate.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            var doc = MapDocument.Load(map);
            var r = RegionCommand.Create(doc, p.GetValueForOption(regionCreateName)!,
                p.GetValueForOption(regionCreateX), p.GetValueForOption(regionCreateY),
                p.GetValueForOption(regionCreateW), p.GetValueForOption(regionCreateH),
                p.GetValueForOption(regionCreateWeather));
            FinishEdit(p.GetValueForOption(jsonOption), p.GetValueForOption(setOut), map, doc, r.Ok, r.Message);
        }));

        var regionToRectName = new Argument<string>("region", "Region name to convert.");
        var regionToRect = new Command("to-rect",
            "Print one region's four corner values for a trigger Rect call. Read-only.")
        { mapArg, regionToRectName };
        regionToRect.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = RegionCommand.ToRect(MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForArgument(regionToRectName));
            Emit(p.GetValueForOption(jsonOption), r,
                () => r.Ok
                    ? $"{r.Name}  left={r.Left}  bottom={r.Bottom}  right={r.Right}  top={r.Top}\n{r.Jass}"
                    : r.Message);
            if (!r.Ok) exitCode[0] = 1;
        }));

        var regionValidate = new Command("validate",
            "Flag overlapping regions, zero-area regions and regions outside the playable "
            + "bounds. Read-only.")
        { mapArg };
        regionValidate.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = RegionCommand.Validate(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r,
                () => r.Message + (r.Issues.Count == 0
                    ? ""
                    : "\n" + string.Join("\n", r.Issues.Select(i => $"  [{i.Kind}] {i.Detail}"))));
            if (r.Issues.Count > 0) exitCode[0] = 2;
            else if (!r.Ok) exitCode[0] = 1;
        }));

        var region = new Command("region",
            "Region tooling for war3map.w3r. Create, list, remove, to-rect, validate.");
        region.AddCommand(regionCreate);
        region.AddCommand(regionList);
        region.AddCommand(regionRemove);
        region.AddCommand(regionToRect);
        region.AddCommand(regionValidate);

        // Pre-flight checks that exist because their absence cost real debugging time. Every
        // rule here corresponds to a failure that a green build, passing tests and a clean
        // 'validate' all let through while a user could not host or load their map.
        var lintAgainst = new Option<string?>("--against",
            "Original map to compare against, enabling the checks that need a before/after: no file "
            + "lost, and none of the target's own assets overwritten by a port.");
        var lintTarget = new Option<string?>("--target",
            "Target build name or schema folder path to check compatibility against.");
        var lintSchemas = new Option<string?>("--schemas",
            "Path to schemas directory containing schema folders with manifest.json.");
        var lintBaseline = new Option<string?>("--baseline",
            "CI gate. Compare against a baseline file written by 'lint baseline' and fail only on a "
            + "finding the baseline does not already hold.");
        var lint = new Command("lint",
            "Run pre-flight checks on a map: does the script compile, is war3map.imp consistent, do "
            + "referenced assets resolve, are all file types loadable, and (with --against) did a "
            + "rebuild lose or clobber anything.")
        { mapArg, lintAgainst, lintTarget, lintSchemas, lintBaseline, jsonOption };
        lint.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var doc = MapDocument.Load(p.GetValueForArgument(mapArg));
            var against = p.GetValueForOption(lintAgainst) is { } a ? MapDocument.Load(a) : null;
            var r = LintCommand.Run(doc, against);
            if (!r.Ok) exitCode[0] = 1;
            string target = p.GetValueForOption(lintTarget);
            string? schemas = p.GetValueForOption(lintSchemas);
            Emit(p.GetValueForOption(jsonOption), r, () => Render.Lint(r));

            // The baseline gate is a self-contained CI mode. When asked for, it is the verdict,
            // and the compat pass below does not run.
            if (p.GetValueForOption(lintBaseline) is { } baselinePath)
            {
                var gate = LintBaselineCommand.Gate(
                    p.GetValueForArgument(mapArg), baselinePath,
                    p.GetValueForOption(lintAgainst));
                Emit(p.GetValueForOption(jsonOption), gate, () => Render.LintGate(gate));
                if (!gate.Ok) exitCode[0] = 1;
                return;
            }
            // Run compat check if --target was given or resolved from settings.
            string? actualTarget = p.GetValueForOption(lintTarget);

            if (actualTarget is not null)
            {
                try
                {
                    var compat = TargetCompatCommand.Execute(p.GetValueForArgument(mapArg), actualTarget, schemas);
                    Emit(p.GetValueForOption(jsonOption), compat, () => Render.CompatResult(compat));
                    int hasErrors = compat.Findings.Any(f => f.Severity == "error") ? 1 : 0;
                    if (hasErrors != 0) exitCode[0] = hasErrors;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("error: " + ex.Message);
                    exitCode[0] = 1;
                }
            }
            else
            {
                // Resolve default from settings.
                var settings = TargetBuildSettings.Get(option: null, schemasDir: schemas);
                Console.WriteLine("target  " + settings.Build + "  (" + TargetSourceLabel(settings.Source) + "), game install " + GameInstallLine(schemas));

                try
                {
                    var compat = TargetCompatCommand.Execute(
                        p.GetValueForArgument(mapArg), settings.Build, schemas);
                    Emit(p.GetValueForOption(jsonOption), compat, () => Render.CompatResult(compat));
                    int hasErrors = compat.Findings.Any(f => f.Severity == "error") ? 1 : 0;
                    if (hasErrors != 0) exitCode[0] = hasErrors;
                }
                catch (DirectoryNotFoundException) { /* no schemas, skip compat */ }
                catch (FileNotFoundException) { /* bad build name, skip compat */ }
            }
        }));
        // lint baseline: record the current findings so a later gate fails only on a new one.
        var lintBaselineOut = new Option<string>(new[] { "-o", "--out" },
            "File to write the baseline to.") { IsRequired = true };
        var lintBaselineAgainst = new Option<string?>("--against",
            "Original map to compare against, so the baseline records the before/after checks too.");
        var lintBaselineCmd = new Command("baseline",
            "Write the map's current lint findings to a baseline file. A later 'lint <map> "
            + "--baseline <file>' then fails only on a finding this file does not already hold.")
            { mapArg, lintBaselineOut, lintBaselineAgainst, jsonOption };
        lintBaselineCmd.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = LintBaselineCommand.Write(
                p.GetValueForArgument(mapArg),
                p.GetValueForOption(lintBaselineOut)!,
                p.GetValueForOption(lintBaselineAgainst));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.LintBaselineWrite(r));
        }));
        lint.AddCommand(lintBaselineCmd);
        // Unattended load test. Closes the feedback loop that previously required a human to
        // launch a 250 MB map and describe what they saw, which is where this toolchain's
        // diagnoses went wrong most often.
        var tlTimeout = new Option<double>("--timeout", () => 120, "Seconds to wait for the marker.");
        var tlDocs = new Option<string?>("--documents",
            @"Warcraft III documents folder. Default: DocumentsWarcraft III (the game writes markers there).");
        var testLoad = new Command("test-load",
            "Load a map in Warcraft III unattended and report whether it reached the end of main(). "
            + "Injects a PreloadGenEnd marker into a COPY, launches the game windowed, waits for the "
            + "marker, and on timeout measures whether the game is spinning or blocked.")
        { mapArg, tlTimeout, tlDocs, jsonOption };
        testLoad.SetHandler(ctx => RunSafely(() =>
        {
            var p6 = ctx.ParseResult;
            var docs = p6.GetValueForOption(tlDocs) ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Warcraft III");
            var gd = Wc3.GameData.GameInstall.Locate(p6.GetValueForOption(gameDirOption))
                ?? throw new InvalidOperationException("Could not locate the Warcraft III install; pass --game-dir.");
            var r6 = TestLoadCommand.Run(p6.GetValueForArgument(mapArg), gd, docs,
                p6.GetValueForOption(tlTimeout));
            if (r6.Verdict != "LOADED") exitCode[0] = 1;
            Emit(p6.GetValueForOption(jsonOption), r6, () => Render.TestLoad(r6));
        }));
        root.AddCommand(testLoad);

        // A map's HERO INTEGRATION CONTRACT: what a unit must be registered with before the map's
        // own systems treat it as playable. The piece nothing else models, and where ported heroes
        // actually die - correct objects, correct assets, compiling script, and still no hero,
        // because the roster is built by the map's own script.
        var contractRef = new Option<string?>("--reference",
            "Rawcode of a hero the map ALREADY supports. Lists every function that mentions it, "
            + "which is the map's real integration contract stated by example rather than guessed "
            + "from shape.");
        var contractAgainst = new Option<string?>("--against",
            "Rawcode of a candidate hero. With --reference, reports only what the reference is "
            + "wired into and the candidate is not, which is the checklist to complete.");
        var contract = new Command("contract",
            "Discover what a map requires before it will treat a unit as a playable hero: roster "
            + "registration calls, spell dispatchers, and per-player hero arrays.")
        { mapArg, contractRef, contractAgainst, jsonOption };
        contract.SetHandler(ctx => RunSafely(() =>
        {
            var p7 = ctx.ParseResult;
            var doc7 = MapDocument.Load(p7.GetValueForArgument(mapArg));
            var refCode = p7.GetValueForOption(contractRef);
            if (refCode is not null)
            {
                var cand = p7.GetValueForOption(contractAgainst) ?? "____";
                var gap = ContractCommand.CompareIntegration(doc7, refCode, cand);
                Emit(p7.GetValueForOption(jsonOption), gap, () => Render.IntegrationGap(gap));
                return;
            }
            var r7 = ContractCommand.Run(doc7);
            Emit(p7.GetValueForOption(jsonOption), r7, () => Render.Contract(r7));
        }));
        root.AddCommand(contract);

        // The format's 'import' verb: a hero as a reviewable artifact instead of something inferred
        // out of a host map every time. Deliberately exports only the hero's REAL dependencies and
        // REPORTS the script-closure carry as review notes, because silently including it is what
        // produced thousands of foreign functions and the trimming damage that followed.
        var heroRawcode = new Argument<string>("rawcode", "Four-character rawcode of the hero to export.");
        var heroOut = new Option<string?>(new[] { "-o", "--out" },
            "Output directory. Default: './<rawcode>-hero'.");
        var heroExport = new Command("export",
            "Export a hero from a map as a reviewable definition (hero.json), its assets, and its "
            + "script. Reports what it deliberately left out.")
        { mapArg, heroRawcode, heroOut, jsonOption };
        heroExport.SetHandler(ctx => RunSafely(() =>
        {
            var p8 = ctx.ParseResult;
            string map8 = p8.GetValueForArgument(mapArg);
            string code8 = p8.GetValueForArgument(heroRawcode);
            var dir8 = p8.GetValueForOption(heroOut) ?? Path.Combine(".", code8 + "-hero");
            var r8 = HeroExportCommand.Run(MapDocument.Load(map8), code8, map8, dir8,
                p8.GetValueForOption(gameDirOption));
            Emit(p8.GetValueForOption(jsonOption), r8, () => Render.HeroExport(r8));
        }));

        var installDefArg = new Argument<string>("definition", "Folder containing hero.json.");
        var installForce = new Option<bool>("--force", () => false,
            "Overwrite the target's own assets on a hash collision. Off by default, because "
            + "silently replacing a target's textures changes how ITS content renders.");
        var installRole = new Option<string?>("--role",
            "Role to register the hero under, when the target's roster takes one (GGGA: Stalker, "
            + "Bruiser, Striker, Tanker, Tech, Supporter). Without this the role is copied from a "
            + "neighbouring registration, which is a guess and usually the wrong tab.");
        var installKeepStats = new Option<bool>("--keep-source-stats", () => false,
            "Keep the attributes and hit/mana pools the hero was exported with. By default the "
            + "target's own heroes are measured and their stat convention is applied, because a "
            + "hero carries the stat MODEL of the map she came from (GGGA pins every hero's "
            + "attributes at 0 and states a flat pool instead, so source attributes make her "
            + "unplayable there). Use this when both maps model heroes the same way.");
        var heroInstall = new Command("install",
            "Install a hero definition into any map. Refuses asset collisions, remaps rawcodes, "
            + "and reports what the target must still be wired with.")
        { installDefArg, mapArg, heroOut, installForce, installRole, installKeepStats, jsonOption };
        heroInstall.SetHandler(ctx => RunSafely(() =>
        {
            var p9 = ctx.ParseResult;
            string tgt = p9.GetValueForArgument(mapArg);
            var doc9 = MapDocument.Load(tgt);
            var r9 = HeroInstallCommand.Run(p9.GetValueForArgument(installDefArg), doc9,
                p9.GetValueForOption(installForce), p9.GetValueForOption(gameDirOption),
                p9.GetValueForOption(installRole), p9.GetValueForOption(installKeepStats));
            string? saved = null;
            if (r9.Ok)
            {
                saved = p9.GetValueForOption(heroOut) ?? Path.Combine(
                    Path.GetDirectoryName(tgt) ?? "",
                    Path.GetFileNameWithoutExtension(tgt) + ".installed" + Path.GetExtension(tgt));
                doc9.Save(saved);
            }
            else exitCode[0] = 1;
            Emit(p9.GetValueForOption(jsonOption), new { r9.Ok, r9.Message, r9.ObjectsCreated,
                r9.FieldsApplied, r9.AssetsWritten, r9.AssetsSkippedIdentical, r9.Collisions,
                r9.UnmetRequirements, r9.NextSteps, SavedTo = saved },
                () => Render.HeroInstall(r9, saved));
        }));

        var heroLintDir = new Argument<string>("definition", "Folder containing hero.json.");
        var heroLint = new Command("lint",
            "Validate a hero definition before installing it: assets present and hash-matched, "
            + "objects installable, declared script functions present, no dangling references.")
        { heroLintDir, jsonOption };
        heroLint.SetHandler(ctx => RunSafely(() =>
        {
            var pa = ctx.ParseResult;
            var ra = HeroLintCommand.Run(pa.GetValueForArgument(heroLintDir));
            if (!ra.Ok) exitCode[0] = 1;
            Emit(pa.GetValueForOption(jsonOption), ra, () => Render.HeroLint(ra));
        }));

        // Counting triggers is not counting heroes, and on a bundled map the two differ by a
        // factor of four. This reports the roster the map actually registers, with the trigger
        // that holds each hero's code beside it.
        var heroRoster = new Command("roster",
            "List the playable heroes the map registers, and which trigger holds each one's "
            + "code. A hero is not a trigger: a map can register 170 heroes whose code is "
            + "bundled into a handful of triggers, so counting triggers under-counts heroes.")
        { mapArg, gameDirOption };
        heroRoster.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var doc = MapDocument.Load(p.GetValueForArgument(mapArg));
            var r = HeroRosterCommand.Run(doc, p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r, () =>
            {
                if (!r.Ok) return r.Message;
                // Only the tags that read as labels. A registration call usually carries a role
                // and an asset path, and the path is both the longer and the less useful of the
                // two, so it would push the role out of the column. The JSON keeps every tag.
                var lines = r.Heroes.Select(h =>
                    $"{h.Rawcode}  {Trunc(h.Name, 28),-28} "
                    + $"{Trunc(string.Join("/", h.Tags.Where(t => !t.Contains('.') && !t.Contains('\\') && !t.Contains('/'))), 16),-16} "
                    + (h.Trigger is null
                        ? "(not in any trigger)"
                        : h.TriggerMatches > 1
                            ? $"{h.Trigger} (+{h.TriggerMatches - 1} more)"
                            : h.Trigger));
                return string.Join("\n", lines) + "\n\n" + r.Message;
            });
        }));

        var hero = new Command("hero", "Hero definitions: the portable, reviewable form of a hero.");
        hero.AddCommand(heroExport);
        hero.AddCommand(heroInstall);
        hero.AddCommand(heroLint);
        hero.AddCommand(heroRoster);
        root.AddCommand(hero);

        // Records HOW FAR initialisation gets, not just whether it finished. A hang on the loading
        // screen otherwise gives nothing at all: no error, no log, no crash dump.
        var trTimeout = new Option<double>("--timeout", () => 180, "Seconds to wait.");
        var trDocs = new Option<string?>("--documents",
            @"Warcraft III documents folder. Default: Documents\Warcraft III.");
        var traceLoad = new Command("trace-load",
            "Instrument a map's initialisation with numbered markers, load it, and report the last "
            + "step that ran and the first that did not. Turns 'stuck on loading' into a line number.")
        { mapArg, trTimeout, trDocs, jsonOption };
        traceLoad.SetHandler(ctx => RunSafely(() =>
        {
            var pt = ctx.ParseResult;
            var docs = pt.GetValueForOption(trDocs) ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Warcraft III");
            var gd = Wc3.GameData.GameInstall.Locate(pt.GetValueForOption(gameDirOption))
                ?? throw new InvalidOperationException("Could not locate the Warcraft III install; pass --game-dir.");
            var rt = TraceLoadCommand.Run(pt.GetValueForArgument(mapArg), gd, docs,
                pt.GetValueForOption(trTimeout));
            if (rt.Verdict != "COMPLETED") exitCode[0] = 1;
            Emit(pt.GetValueForOption(jsonOption), rt, () => Render.TraceLoad(rt));
        }));
        debug.AddCommand(traceLoad);

        root.AddCommand(lint);

        // Container-level comparison. 'diff' compares file CONTENTS, which is why a rebuilt
        // archive with byte-identical files could fail to load and no tool we owned could say
        // why. This reports how files are STORED, plus hash-table crowding.
        var mapArgB = new Argument<string>("mapB", "Second map to compare against.");
        var mpqDiff = new Command("mpq-diff",
            "Compare two archives structurally: header, sector size, hash table capacity and "
            + "crowding, and each file's storage flags, compressed size and offset. Use when two "
            + "maps have identical contents but behave differently.")
        { mapArg, mapArgB, jsonOption };
        mpqDiff.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var d = MpqStructureCommand.Diff(p.GetValueForArgument(mapArg), p.GetValueForArgument(mapArgB));
            Emit(p.GetValueForOption(jsonOption), d, () => Render.MpqDiff(d));
        }));
        root.AddCommand(mpqDiff);

        // Hash table viewer. MPQ resolves a name by probing forward from its home slot and stops
        // only at a NEVER-USED slot; a DELETED slot does not stop it. An archive with zero
        // never-used slots therefore gives a failed lookup no terminator, and the probe runs
        // forever. That is invisible unless the two are counted separately, which is exactly what
        // this does.
        var hashSample = new Option<int>("--slots", () => 0,
            "Also dump this many slots from the start of the table.");
        var mpqHash = new Command("mpq-hash",
            "Inspect an archive's hash table: occupied, deleted and never-used slot counts, the "
            + "worst-case probe length for a name that is absent, and whether a failed lookup can "
            + "loop forever.")
        { mapArg, hashSample, jsonOption };
        mpqHash.SetHandler(ctx => RunSafely(() =>
        {
            var p2 = ctx.ParseResult;
            var v = MpqHashTableCommand.Read(p2.GetValueForArgument(mapArg), p2.GetValueForOption(hashSample));
            if (v.LookupCanLoopForever) exitCode[0] = 1;
            Emit(p2.GetValueForOption(jsonOption), v, () => Render.MpqHash(v));
        }));
        root.AddCommand(mpqHash);

        // The mpq command group. Each verb reads or writes the archive at the container level,
        // beside the existing mpq-diff and mpq-hash. A mutating verb copies, it never edits the
        // input map in place.
        var mpq = new Command("mpq",
            "MPQ container operations. Verify readability, report listfile coverage, recover names "
            + "from a dictionary, strip bookkeeping, and compare payload bytes.");

        var mpqVerify = new Command("verify",
            "Open every archive entry and read it to its end, reporting the ones that fail with "
            + "their error and a summary count. Read-only.") { mapArg };
        mpqVerify.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = MpqVerifyCommand.Execute(map);
            if (r.UnreadableEntries > 0) exitCode[0] = 1;
            Emit(json, r, () => Render.MpqVerify(r));
        }), mapArg, jsonOption);

        var mpqListfile = new Command("listfile",
            "Report listfile coverage, how many entries the listfile names, how many the standard "
            + "name probe recovered, and how many carry no name at all. Read-only.") { mapArg };
        mpqListfile.SetHandler((string map, bool json) => RunSafely(() =>
        {
            var r = MpqListfileCommand.Execute(map);
            Emit(json, r, () => Render.MpqListfile(r));
        }), mapArg, jsonOption);

        var mpqDict = new Option<string?>("--dictionary",
            "Path to a dictionary file, one candidate name per line. Needed by name-recover.");
        var mpqNameRecover = new Command("name-recover",
            "Resolve a dictionary of candidate names against the archive's hash table and report "
            + "the names that hit unnamed blocks. Read-only.") { mapArg, mpqDict };
        mpqNameRecover.SetHandler((string map, string? dict, bool json) => RunSafely(() =>
        {
            if (string.IsNullOrWhiteSpace(dict))
            {
                Console.Error.WriteLine("error. --dictionary is required");
                exitCode[0] = 1;
                return;
            }
            var r = MpqNameRecoverCommand.Execute(MapDocument.Load(map), dict);
            Emit(json, r, () => Render.MpqNameRecover(r));
        }), mapArg, mpqDict, jsonOption);

        var mpqStripOut = new Option<string?>(new[] { "-o", "--out" },
            "Output map path. Default is '<map>.edited.<ext>' next to the input, the original is never overwritten.");
        var mpqStrip = new Command("strip-bookkeeping",
            "Save a copy with (listfile) and (attributes) dropped, using the same switch the "
            + "toolkit already reads, and confirm the copy opens again. The input is never overwritten.")
        { mapArg, mpqStripOut };
        mpqStrip.SetHandler((string map, string? outPath, bool json) => RunSafely(() =>
        {
            var r = MpqStripBookkeepingCommand.Execute(map, outPath);
            if (!r.Reopens) exitCode[0] = 1;
            Emit(json, r, () => Render.MpqStrip(r));
        }), mapArg, mpqStripOut, jsonOption);

        var mpqBytesDiff = new Command("bytes-diff",
            "Compare two archives entry by entry by name. For each name present in both, report "
            + "whether the payload SHA256 matches, and list names only one side carries. Read-only.")
        { mapArg, mapArgB };
        mpqBytesDiff.SetHandler((string map, string mapB, bool json) => RunSafely(() =>
        {
            var r = MpqBytesDiffCommand.Execute(map, mapB);
            Emit(json, r, () => Render.MpqBytesDiff(r));
        }), mapArg, mapArgB, jsonOption);

        // Editing verbs. Each writes a copy and refuses to touch the input map in place, the same
        // stance strip-bookkeeping takes. rebuild and remove build a fresh archive from payloads
        // rather than from the source archive, which is what lets a protected map be rewritten at
        // all, since an MpqArchiveBuilder built FROM the archive dies on the block rows protection
        // corrupted, while one built from payloads never reads them.
        var mpqOut = new Option<string?>(new[] { "-o", "--out" },
            "Output map path. The input map is never overwritten.");
        var mpqGrowHash = new Option<bool>("--grow-hash", () => false,
            "Force a larger hash table on the rebuilt archive. Doubles the source capacity, then "
            + "raises it until 3/4 occupancy fits. Off by default, most maps need no growth.");
        var mpqRebuild = new Command("rebuild",
            "Rebuild the archive from scratch into a healthy one, re-placing every readable entry "
            + "and dropping the blocks protection made unreadable or left nameless. Reports the "
            + "entry counts before and after. Writes a copy.") { mapArg, mpqOut, mpqGrowHash };
        mpqRebuild.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = MpqRebuildCommand.Execute(p.GetValueForArgument(mapArg),
                p.GetValueForOption(mpqOut), p.GetValueForOption(mpqGrowHash));
            if (!r.Reopens) exitCode[0] = 1;
            Emit(p.GetValueForOption(jsonOption), r, () => Render.MpqRebuild(r));
        }));

        var mpqPrefer = new Option<string>("--prefer", () => "a",
            "Which side wins a name both maps carry, 'a' or 'b'. Default 'a'.");
        var mpqMerge = new Command("merge",
            "Merge two archives into one, taking every readable named entry from both. A name in "
            + "both is resolved by the preferred side and reported as a conflict. Writes a copy.")
        { mapArg, mapArgB, mpqOut, mpqPrefer };
        mpqMerge.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = MpqMergeCommand.Execute(p.GetValueForArgument(mapArg), p.GetValueForArgument(mapArgB),
                p.GetValueForOption(mpqOut), p.GetValueForOption(mpqPrefer)!);
            if (!r.Reopens) exitCode[0] = 1;
            Emit(p.GetValueForOption(jsonOption), r, () => Render.MpqMerge(r));
        }));

        var mpqNameArg = new Argument<string>("name", "Internal archive path, for example war3map.j.");
        var mpqFileOpt = new Option<string>(new[] { "--file" },
            "Path to the file whose bytes become the internal file.") { IsRequired = true };
        var mpqAdd = new Command("add",
            "Add or replace one internal file from a file on disk, byte-faithfully. Reports whether "
            + "it was added or replaced. Writes a copy.") { mapArg, mpqNameArg, mpqFileOpt, mpqOut };
        mpqAdd.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = MpqAddCommand.Execute(p.GetValueForArgument(mapArg), p.GetValueForArgument(mpqNameArg),
                p.GetValueForOption(mpqFileOpt)!, p.GetValueForOption(mpqOut));
            if (!r.Reopens) exitCode[0] = 1;
            Emit(p.GetValueForOption(jsonOption), r, () => Render.MpqAdd(r));
        }));

        var mpqRemove = new Command("remove",
            "Remove one internal file by rebuilding the container without it, and report whether the "
            + "rebuild actually dropped the name. Writes a copy.")
        { mapArg, mpqNameArg, mpqOut };
        mpqRemove.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = MpqRemoveCommand.Execute(p.GetValueForArgument(mapArg),
                p.GetValueForArgument(mpqNameArg), p.GetValueForOption(mpqOut));
            if (!r.Reopens) exitCode[0] = 1;
            Emit(p.GetValueForOption(jsonOption), r, () => Render.MpqRemove(r));
        }));

        var mpqExtractDir = new Option<string?>(new[] { "-o", "--out" },
            "Output directory. Default '<map>.extracted' beside the input. Created if absent.");
        var mpqExtractAll = new Command("extract-all",
            "Extract every entry of the archive to a directory, including unnamed protected blocks, "
            + "through the same path-safety rule 'extract' uses. Read-only over the map.")
        { mapArg, mpqExtractDir };
        mpqExtractAll.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(mapArg);
            string outDir = p.GetValueForOption(mpqExtractDir) is { Length: > 0 } d
                ? d
                : Path.GetFullPath(map) + ".extracted";
            var r = MpqExtractAllCommand.Execute(MapDocument.Load(map));
            var manifest = ExtractWriter.WriteAll(new ExtractResult(r.Items), outDir);
            var report = new MpqExtractAllReport(map, Path.GetFullPath(outDir), r.TotalEntries,
                r.NamedEntries, r.UnnamedEntries, r.TotalBytes, manifest.Files);
            Emit(p.GetValueForOption(jsonOption), report, () => Render.MpqExtractAll(report));
        }));

        mpq.AddCommand(mpqVerify);
        mpq.AddCommand(mpqListfile);
        mpq.AddCommand(mpqNameRecover);
        mpq.AddCommand(mpqStrip);
        mpq.AddCommand(mpqBytesDiff);
        mpq.AddCommand(mpqRebuild);
        mpq.AddCommand(mpqMerge);
        mpq.AddCommand(mpqAdd);
        mpq.AddCommand(mpqRemove);
        mpq.AddCommand(mpqExtractAll);
        root.AddCommand(mpq);

        // Observe the running game. Static comparison could not explain a map that sits forever on
        // the loading screen while being measurably equivalent to one that loads, so the behaviour
        // has to be sampled instead of inferred. Read-only: reads OS counters, attaches nothing.
        var hangSeconds = new Option<double>("--seconds", () => 5.0,
            "How long to sample. Longer is steadier; 5s is enough to tell spinning from waiting.");
        var hangThreads = new Option<int>("--threads", () => 6, "How many busiest threads to list.");
        var hangIp = new Option<int>("--locate", () => 0,
            "Take this many live instruction-pointer samples of the busiest thread and report which "
            + "module it is executing in. Needs to briefly suspend that thread, so it is opt-in. "
            + "Use 40 or so; 0 disables it.");
        var hangName = new Option<string?>("--process",
            "Process name override, if the game runs under a name this does not know.");
        var gameHang = new Command("game-hang",
            "Sample a running Warcraft III process to tell whether it is SPINNING (looping in its "
            + "own code) or WAITING (blocked on I/O or a lock). Run it while a map is stuck on the "
            + "loading screen.")
        { hangSeconds, hangThreads, hangIp, hangName, jsonOption };
        gameHang.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = GameHangCommand.Sample(p.GetValueForOption(hangSeconds),
                p.GetValueForOption(hangThreads), p.GetValueForOption(hangName),
                p.GetValueForOption(hangIp));
            if (!r.Found) exitCode[0] = 1;
            Emit(p.GetValueForOption(jsonOption), r, () => Render.GameHang(r));
        }));
        debug.AddCommand(gameHang);

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

        var schemaFrom = new Option<string?>("--from",
            "Build a schema from a folder of loose files instead of a game install.");
        var schemaBuild = new Option<string?>("--build",
            "Override the build version string. Default: read from the install.");
        var gamedataSchema = new Command("schema",
            "Build and save a per-build schema (object fields, trigger functions, script APIs) "
            + "from a game install or a folder of loose files. The schema describes what the "
            + "editor for a specific build shows and what the compiler accepts.")
            { snapOut, schemaFrom, schemaBuild, gameDirOption };
        gamedataSchema.SetHandler(
            (string outDir, string? from, string? build, string? gameDir, bool json) => RunSafely(() =>
            {
                var r = GameSchemaCommand.Execute(outDir, from, build, gameDir);
                Emit(json, r, () => Render.GameSchemaWrite(r));
            }),
            snapOut, schemaFrom, schemaBuild, gameDirOption, jsonOption);

        var schemaDiffSection = new Option<string?>("--section",
            "Show changes for one section only: objects, triggers, or script. Omit for all.");
        var schemaDiffLimit = new Option<int>("--limit", () => 0,
            "Maximum lines of human output to print. Zero means no limit.");
        var schemaDiffA = new Argument<string>("a", "Path to the first schema folder.");
        var schemaDiffB = new Argument<string>("b", "Path to the second schema folder.");
        var gamedataSchemaDiff = new Command("schema-diff",
            "Compare two schema folders and report added, removed and changed fields, trigger "
            + "functions, script types and natives. Groups output by section then by file or "
            + "category. Exits 0 when no changes, 1 when there are changes.")
            { schemaDiffA, schemaDiffB, schemaDiffSection, schemaDiffLimit, jsonOption };
        gamedataSchemaDiff.SetHandler(
            (string a, string b, string? section, int limit, bool json) => RunSafely(() =>
            {
                var r = GameSchemaCommand.Diff(a, b);
                var filtered = GameSchemaCommand.Filter(r, section);
                int? sectionLimit = limit > 0 ? limit : null;
                if (json)
                {
                    Emit(json, filtered, () => Render.GameSchemaDiff(filtered, section, sectionLimit));
                }
                else
                {
                    Console.WriteLine(Render.GameSchemaDiff(filtered, section, sectionLimit));
                }
                exitCode[0] = filtered.Changes.Count > 0 ? 1 : 0;
            }),
            schemaDiffA, schemaDiffB, schemaDiffSection, schemaDiffLimit, jsonOption);

        var schemasDirArg = new Argument<string>("schemas-dir", "Path to the schemas directory.");
        var gamedataSchemaIndex = new Command("schema-index",
            "Read all schema folders in a directory, build an index of entries, and write "
            + "index.json. Prints the entry count and the path.")
            { schemasDirArg };
        gamedataSchemaIndex.SetHandler(
            (string schemasDir) => RunSafely(() =>
            {
                var indexPath = GameSchemaCommand.WriteIndex(schemasDir);
                var entries = GameSchemaCommand.BuildIndex(schemasDir);
                var jsonResult = new { Count = entries.Count, Path = indexPath, Entries = entries };
                Emit(true, jsonResult, () => Render.GameSchemaIndex(entries, indexPath));
            }),
            schemasDirArg);

        var gdSchemas = new Option<string?>("--schemas",
            "Path to schemas directory containing schema folders with manifest.json.");
        var gdTarget = new Option<string?>("--target",
            "Build name or schema folder path. Omit to search every build.");

        var gdHistoryName = new Argument<string>("name", "Name to look for.");
        var gamedataHistory = new Command("history",
            "Report whether a name is an object field code, a native, a type, a global or a function "
            + "in any recorded build, list every build it appears in, and name the first and last build.")
            { gdHistoryName, gdSchemas, jsonOption };
        gamedataHistory.SetHandler(
            (string name, string? schemas, bool json) => RunSafely(() =>
            {
                var r = GamedataHistoryCommand.Execute(name, schemas);
                Emit(json, r, () => Render.GamedataHistory(r));
            }),
            gdHistoryName, gdSchemas, jsonOption);

        var gdChangelogA = new Argument<string>("buildA", "The older build name or schema folder.");
        var gdChangelogB = new Argument<string>("buildB", "The newer build name or schema folder.");
        var gamedataChangelog = new Command("changelog",
            "Diff two builds' object fields and report fields added, removed, retyped, or whose "
            + "minimum or maximum bound changed, grouped by metadata file.")
            { gdChangelogA, gdChangelogB, gdSchemas, jsonOption };
        gamedataChangelog.SetHandler(
            (string buildA, string buildB, string? schemas, bool json) => RunSafely(() =>
            {
                var r = GamedataChangelogCommand.Execute(buildA, buildB, schemas);
                Emit(json, r, () => Render.GamedataChangelog(r));
            }),
            gdChangelogA, gdChangelogB, gdSchemas, jsonOption);

        var gdLookupQuery = new Argument<string>("query", "Text to find inside a field display name.");
        var gamedataFieldLookup = new Command("field-lookup",
            "Find object field codes whose display name contains the query, in one build or all builds.")
            { gdLookupQuery, gdTarget, gdSchemas, jsonOption };
        gamedataFieldLookup.SetHandler(
            (string query, string? target, string? schemas, bool json) => RunSafely(() =>
            {
                var r = GamedataFieldLookupCommand.Execute(query, target, schemas);
                Emit(json, r, () => Render.GamedataFieldLookup(r));
            }),
            gdLookupQuery, gdTarget, gdSchemas, jsonOption);

        var gdBoundsCode = new Argument<string>("code", "The object field code to inspect.");
        var gamedataBounds = new Command("bounds",
            "Report a field code's type, minimum and maximum bounds, whether it can be empty, and the "
            + "metadata file that defines it, in one build or all builds where it exists.")
            { gdBoundsCode, gdTarget, gdSchemas, jsonOption };
        gamedataBounds.SetHandler(
            (string code, string? target, string? schemas, bool json) => RunSafely(() =>
            {
                var r = GamedataBoundsCommand.Execute(code, target, schemas);
                Emit(json, r, () => Render.GamedataBounds(r));
            }),
            gdBoundsCode, gdTarget, gdSchemas, jsonOption);

        var gdEnumType = new Argument<string?>("type", () => null,
            "The enumerated field type to inspect, the metadata SLK type column. Omit to list types.");
        var gamedataEnum = new Command("enum",
            "Report the closed set of legal values for an enumerated object field type, from the "
            + "build's UnitEditorData.txt, in one build or all builds. Omit the type to list the "
            + "enumerated types a build defines. This is the authority a picker should offer, not "
            + "the values a stock object happens to use.")
            { gdEnumType, gdTarget, gdSchemas, jsonOption };
        gamedataEnum.SetHandler(
            (string? type, string? target, string? schemas, bool json) => RunSafely(() =>
            {
                if (string.IsNullOrWhiteSpace(type))
                {
                    var types = GamedataEnumCommand.Types(target, schemas);
                    Emit(json, types, () => Render.GamedataEnumTypes(types));
                    return;
                }
                var r = GamedataEnumCommand.Execute(type, target, schemas);
                Emit(json, r, () => Render.GamedataEnum(r));
            }),
            gdEnumType, gdTarget, gdSchemas, jsonOption);

        var gamedataCoverage = new Command("coverage",
            "List one row per schema folder with the build, its era, and which sections it holds "
            + "(objects, triggers, script), so a reader can tell a build carrying real object data "
            + "from one carrying only its script.")
            { gdSchemas, jsonOption };
        gamedataCoverage.SetHandler(
            (string? schemas, bool json) => RunSafely(() =>
            {
                var r = GamedataCoverageCommand.Execute(schemas);
                Emit(json, r, () => Render.GamedataCoverage(r));
            }),
            gdSchemas, jsonOption);

        var gdSourcesOut = new Option<string>(new[] { "-o", "--out" },
            "Path to write sources.json into.") { IsRequired = true };
        var gamedataSources = new Command("sources",
            "Write sources.json, a machine readable provenance index of the dataset. For every "
            + "schema folder it records the build, era, sections, the SHA-256 of each section file "
            + "and the input hashes from its manifest.")
            { gdSchemas, gdSourcesOut, jsonOption };
        gamedataSources.SetHandler(
            (string? schemas, string outFile, bool json) => RunSafely(() =>
            {
                var r = GamedataSourcesCommand.Execute(outFile, schemas);
                Emit(json, r, () => Render.GamedataSources(r, Path.GetFullPath(outFile)));
            }),
            gdSchemas, gdSourcesOut, jsonOption);

        var gdExportOut = new Option<string>(new[] { "-o", "--out" },
            "Target directory for the dataset, or a path ending in .zip to pack it into one archive.")
            { IsRequired = true };
        var gamedataExportSchemas = new Command("export-schemas",
            "Copy the schema dataset to a target directory, or pack it into one zip when the target "
            + "ends in .zip, for attaching to a release.")
            { gdSchemas, gdExportOut, jsonOption };
        gamedataExportSchemas.SetHandler(
            (string? schemas, string outPath, bool json) => RunSafely(() =>
            {
                var r = GamedataExportSchemasCommand.Execute(outPath, schemas);
                Emit(json, r, () => Render.GamedataExportSchemas(r));
            }),
            gdSchemas, gdExportOut, jsonOption);

        var gamedata = new Command("gamedata", "Base game data, for tracking what a patch changes.");
        gamedata.AddCommand(gamedataSnapshot);
        gamedata.AddCommand(gamedataSchema);
        gamedata.AddCommand(gamedataSchemaDiff);
        gamedata.AddCommand(gamedataSchemaIndex);
        gamedata.AddCommand(gamedataHistory);
        gamedata.AddCommand(gamedataChangelog);
        gamedata.AddCommand(gamedataFieldLookup);
        gamedata.AddCommand(gamedataBounds);
        gamedata.AddCommand(gamedataEnum);
        gamedata.AddCommand(gamedataCoverage);
        gamedata.AddCommand(gamedataSources);
        gamedata.AddCommand(gamedataExportSchemas);

        // ---- leak investigation, the static ranked list joined to a wc3watch runtime session ----
        var leak = new Command("leak",
            "Handle-leak investigation. The static ranked list from script leaks, an in-map harness "
            + "that drives the hot candidates, and the join to a wc3watch session's runtime samples.");

        var leakListCmd = new Command("list",
            "The ranked static leak list and the periodic drivers that feed it. Read-only.") { mapArg };
        leakListCmd.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = LeakListCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.LeakList(r));
        }));
        leak.AddCommand(leakListCmd);

        var leakLang = new Option<string>("--lang", "Harness language, jass or lua.") { IsRequired = true };
        var leakTarget = new Option<string>("--target",
            "A hot leak candidate function name, or all.") { IsRequired = true };
        var leakHarnessOut = new Option<string?>(new[] { "-o", "--out" },
            "Path to write the harness.") { IsRequired = true };
        var leakHarnessCmd = new Command("harness",
            "Emit a small in-map harness that drives the named hot leak candidates on a short timer "
            + "so the runtime slope is measurable in seconds, plus a handle count sampler. Emits valid "
            + "jass or lua and does not start the game.")
        { mapArg, leakLang, leakTarget, leakHarnessOut };
        leakHarnessCmd.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = LeakHarnessCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForOption(leakLang)!, p.GetValueForOption(leakTarget)!, p.GetValueForOption(leakHarnessOut));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.LeakHarness(r));
            if (!r.Ok) exitCode[0] = 1;
        }));
        leak.AddCommand(leakHarnessCmd);

        var leakSession = new Argument<string>("session-folder",
            "A wc3watch session folder holding samples.csv.");
        var leakJoinCmd = new Command("join",
            "Read a wc3watch session's handle and memory slope and align a rising slope with the "
            + "periodic candidates from the static list. Read-only.") { mapArg, leakSession };
        leakJoinCmd.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = LeakJoinCommand.Execute(MapDocument.Load(p.GetValueForArgument(mapArg)),
                p.GetValueForArgument(leakSession));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.LeakJoin(r));
            if (!r.Ok) exitCode[0] = 1;
        }));
        leak.AddCommand(leakJoinCmd);

        var abA = new Argument<string>("sessionA", "First wc3watch session folder.");
        var abB = new Argument<string>("sessionB", "Second wc3watch session folder.");
        var abMap = new Option<string?>("--map", "Map to attribute the drop to a periodic candidate.");
        var leakAbCmd = new Command("ab",
            "Diff two wc3watch sessions and report whether the handle and memory slope fell. With "
            + "--map, name the candidate whose predicted rate is nearest the drop. Read-only.")
        { abA, abB, abMap };
        leakAbCmd.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string? mapPath = p.GetValueForOption(abMap);
            var r = LeakAbCommand.Execute(p.GetValueForArgument(abA), p.GetValueForArgument(abB),
                mapPath is null ? null : MapDocument.Load(mapPath));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.LeakAb(r));
            if (!r.Ok) exitCode[0] = 1;
        }));
        leak.AddCommand(leakAbCmd);

        var leakBenchmarkCmd = new Command("benchmark",
            "Run the detector over synthetic maps with a known injected leak, one per creator kind, "
            + "and report true positive and false positive counts. Read-only.");
        leakBenchmarkCmd.SetHandler(ctx => RunSafely(() =>
        {
            var r = LeakBenchmarkCommand.Execute();
            Emit(ctx.ParseResult.GetValueForOption(jsonOption), r, () => Render.LeakBenchmark(r));
            if (!r.Ok) exitCode[0] = 1;
        }));
        leak.AddCommand(leakBenchmarkCmd);

        root.AddCommand(file);
        root.AddCommand(leak);
        root.AddCommand(info); root.AddCommand(ls); root.AddCommand(overview); root.AddCommand(rt);
        root.AddCommand(search); root.AddCommand(diff); root.AddCommand(obj);
        root.AddCommand(extract); root.AddCommand(render); root.AddCommand(renderModel);
        root.AddCommand(script); root.AddCommand(bundle); root.AddCommand(port);
        root.AddCommand(convert); root.AddCommand(validate);
        root.AddCommand(audit); root.AddCommand(deprotect);
        root.AddCommand(debug);
        root.AddCommand(place); root.AddCommand(placed); root.AddCommand(palette); root.AddCommand(terrain);
        root.AddCommand(sound); root.AddCommand(camera); root.AddCommand(pathing);
        root.AddCommand(mapInfo); root.AddCommand(player); root.AddCommand(force);
        root.AddCommand(region); root.AddCommand(newMap); trigger.AddCommand(triggerRead);
        trigger.AddCommand(trigRename); trigger.AddCommand(trigEnabled);
        trigger.AddCommand(trigInitiallyOn); trigger.AddCommand(trigRunOnInit);
        trigger.AddCommand(trigAddCat); trigger.AddCommand(trigAdd);
        trigger.AddCommand(trigRemove);
        trigger.AddCommand(ecaAdd); trigger.AddCommand(ecaRemove);
        trigger.AddCommand(ecaEnabled);
        root.AddCommand(repairHeroes); root.AddCommand(repair);
        root.AddCommand(trigger);
        root.AddCommand(editor); root.AddCommand(gamedata);
        // Standalone compat: check a map against a target build.
        var compatMapArg = new Argument<string>("map", "Path to a .w3x/.w3m map.");
        var compatTarget = new Option<string?>("--target",
            () => null, "Target build name or schema folder path. Omit to use the configured default.");
        var compatSchemas = new Option<string?>("--schemas",
            "Path to schemas directory containing schema folders with manifest.json.");
        var compat = new Command("compat",
            "Check a map against a target game build (a schema folder or build name). "
            + "Reports compile-time incompatibilities (missing natives, globals, types, functions) "
            + "and display-time incompatibilities (modified object fields the target build no longer defines). "
            + "Exits 2 if any error findings. Omit --target to use the configured default.");
        compat.AddArgument(compatMapArg);
        compat.AddOption(compatTarget);
        compat.AddOption(compatSchemas);
        compat.AddOption(jsonOption);
        compat.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string map = p.GetValueForArgument(compatMapArg);
            string? targetArg = p.GetValueForOption(compatTarget);
            string? schemas = p.GetValueForOption(compatSchemas);
            bool json = p.GetValueForOption(jsonOption);

            // Resolve target: option > default from settings.
            string actualTarget;
            TargetSource source;
            if (targetArg is not null)
            {
                actualTarget = targetArg;
                source = TargetSource.Option;
            }
            else
            {
                var settings = TargetBuildSettings.Get(option: null, schemasDir: schemas);
                actualTarget = settings.Build;
                source = settings.Source;
            }

            if (!json)
            {
                Console.WriteLine("target  " + actualTarget + "  (" + TargetSourceLabel(source) + "), game install " + GameInstallLine(schemas));
            }

            var result = TargetCompatCommand.Execute(map, actualTarget, schemas);
            Emit(json, result, () => Render.CompatResult(result));
            if (result.Findings.Any(f => f.Severity == "error")) exitCode[0] = 2;
        }));

        // compat matrix: a grid of maps against target builds, one pass or fail per cell.
        var matrixMaps = new Argument<string[]>("maps",
            "One or more .w3x/.w3m maps.") { Arity = ArgumentArity.OneOrMore };
        var matrixTargets = new Option<string[]>("--targets",
            "One or more target build names or schema folder paths.") { IsRequired = true };
        var matrixSchemas = new Option<string?>("--schemas",
            "Path to schemas directory containing schema folders with manifest.json.");
        var compatMatrix = new Command("matrix",
            "Check several maps against several target builds and print a table of pass or fail per "
            + "cell, with the finding count. Exits 2 if any cell fails.")
            { matrixMaps, matrixTargets, matrixSchemas };
        compatMatrix.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = CompatMatrixCommand.Execute(
                p.GetValueForArgument(matrixMaps),
                p.GetValueForOption(matrixTargets)!,
                p.GetValueForOption(matrixSchemas));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.CompatMatrix(r));
            if (r.Cells.Any(c => !c.Pass)) exitCode[0] = 2;
        }));
        compat.AddCommand(compatMatrix);

        // compat explain: one identifier against one target build.
        var explainName = new Argument<string>("name",
            "Identifier to explain, a native, global, type or function name.");
        var explainTarget = new Option<string?>("--target",
            () => null, "Target build name or schema folder path. Omit to use the configured default.");
        var explainSchemas = new Option<string?>("--schemas",
            "Path to schemas directory containing schema folders with manifest.json.");
        var compatExplain = new Command("explain",
            "Explain one identifier against a target build. Reports whether the target declares it, and if "
            + "not, the earliest build that added it and the builds that declare it.")
            { explainName, explainTarget, explainSchemas };
        compatExplain.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string? targetArg = p.GetValueForOption(explainTarget);
            string? schemas = p.GetValueForOption(explainSchemas);
            string target = targetArg ?? TargetBuildSettings.Get(option: null, schemasDir: schemas).Build;
            var r = CompatExplainCommand.Execute(p.GetValueForArgument(explainName), target, schemas);
            Emit(p.GetValueForOption(jsonOption), r, () => Render.CompatExplain(r));
        }));
        compat.AddCommand(compatExplain);

        root.AddCommand(compat);

        // ---- bench, measured benchmarks over a folder of maps or a schemas corpus ----
        var bench = new Command("bench",
            "Measured benchmarks. Each command prints a number rather than a claim, over a folder "
            + "of maps or a schemas corpus. Read-only unless noted.");

        // bench schema-coverage [--schemas <dir>]
        var benchSchemas = new Option<string?>("--schemas",
            "Schemas directory containing schema folders with manifest.json.");
        var benchSchemaCoverage = new Command("schema-coverage",
            "Measure object-data coverage of every schema folder. One row per build with its era and "
            + "whether object data is real, era-derived or absent, and the real field count, plus totals. "
            + "Read-only.") { benchSchemas };
        benchSchemaCoverage.SetHandler(ctx =>
        {
            var p = ctx.ParseResult;
            RunSafely(() =>
            {
                var r = BenchSchemaCoverageCommand.Execute(p.GetValueForOption(benchSchemas));
                Emit(p.GetValueForOption(jsonOption), r, () => Render.BenchSchemaCoverage(r));
            });
        });
        bench.AddCommand(benchSchemaCoverage);

        // bench compat <maps...> --targets <builds...> [--schemas <dir>]
        var benchCompatMaps = new Argument<string[]>("maps",
            "One or more .w3x/.w3m maps.") { Arity = ArgumentArity.OneOrMore };
        var benchCompatTargets = new Option<string[]>("--targets",
            "One or more target build names or schema folder paths.") { IsRequired = true };
        var benchCompatSchemas = new Option<string?>("--schemas",
            "Schemas directory containing schema folders with manifest.json.");
        var benchCompat = new Command("compat",
            "Check every map against every target build and report a pass or fail matrix with finding "
            + "counts, plus the aggregate. Exits 2 if any cell fails. Read-only.")
            { benchCompatMaps, benchCompatTargets, benchCompatSchemas };
        benchCompat.SetHandler(ctx =>
        {
            var p = ctx.ParseResult;
            RunSafely(() =>
            {
                var r = BenchCompatCommand.Execute(
                    p.GetValueForArgument(benchCompatMaps),
                    p.GetValueForOption(benchCompatTargets)!,
                    p.GetValueForOption(benchCompatSchemas));
                Emit(p.GetValueForOption(jsonOption), r, () => Render.BenchCompat(r));
                if (r.Aggregate.Failed > 0) exitCode[0] = 2;
            });
        });
        bench.AddCommand(benchCompat);

        // bench leaks <folder>
        var benchLeaksFolder = new Argument<string>("folder",
            "Folder of .w3x/.w3m maps, searched recursively.");
        var benchLeaks = new Command("leaks",
            "Run the static leak detector over every map in a folder and report the counts per map and "
            + "per leak kind, a sweep rather than one map. Read-only.") { benchLeaksFolder };
        benchLeaks.SetHandler(ctx =>
        {
            var p = ctx.ParseResult;
            RunSafely(() =>
            {
                var r = BenchLeaksCommand.Execute(p.GetValueForArgument(benchLeaksFolder));
                Emit(p.GetValueForOption(jsonOption), r, () => Render.BenchLeaks(r));
            });
        });
        bench.AddCommand(benchLeaks);

        // bench open <folder>
        var benchOpenFolder = new Argument<string>("folder",
            "Folder of .w3x/.w3m maps, searched recursively.");
        var benchOpen = new Command("open",
            "Open every map in a folder through the loader and record the per-map open time, entry "
            + "count and total bytes, reporting the slowest map and the median. Read-only.")
            { benchOpenFolder };
        benchOpen.SetHandler(ctx =>
        {
            var p = ctx.ParseResult;
            RunSafely(() =>
            {
                var r = BenchOpenCommand.Execute(p.GetValueForArgument(benchOpenFolder));
                Emit(p.GetValueForOption(jsonOption), r, () => Render.BenchOpen(r));
            });
        });
        bench.AddCommand(benchOpen);

        // bench perf-gate <baseline.json> <run.json> [--max-regression-pct N]
        var benchBaseline = new Argument<string>("baseline", "Path to the baseline benchmark JSON.");
        var benchRunArg = new Argument<string>("run", "Path to the benchmark JSON to compare against the baseline.");
        var benchMaxRegression = new Option<double>("--max-regression-pct",
            () => 10, "Fail when any metric regressed by more than this percent.");
        var benchPerfGate = new Command("perf-gate",
            "Compare two recorded benchmark results and exit non-zero when any metric regressed beyond "
            + "the threshold, for use as a CI gate. Read-only.")
            { benchBaseline, benchRunArg, benchMaxRegression };
        benchPerfGate.SetHandler(ctx =>
        {
            var p = ctx.ParseResult;
            RunSafely(() =>
            {
                var r = BenchPerfGateCommand.Execute(
                    p.GetValueForArgument(benchBaseline),
                    p.GetValueForArgument(benchRunArg),
                    p.GetValueForOption(benchMaxRegression));
                Emit(p.GetValueForOption(jsonOption), r, () => Render.BenchPerfGate(r));
                if (!r.Pass) exitCode[0] = 2;
            });
        });
        bench.AddCommand(benchPerfGate);

        root.AddCommand(bench);

        // ---- ci, pass or fail gates for a pipeline ----
        // Every ci command is read-only, prints what it checked, and exits non-zero on failure, so
        // a CI step can simply run it and read the exit code. They reuse the lint, validate, compat
        // and schema-coverage commands rather than re-implementing any check.
        var ci = new Command("ci",
            "CI gates. Each runs a check on a map or the schema dataset, prints what it checked, and "
            + "exits non-zero when it fails. Read-only.");
        var ciSchemas = new Option<string?>("--schemas",
            "Path to schemas directory containing schema folders with manifest.json.");

        // ci lint <map> [--baseline <file>] [--target <build>] [--schemas <dir>] [--against <map>]
        var ciBaseline = new Option<string?>("--baseline",
            "Baseline file written by 'lint baseline'. When given, the gate fails only on a finding "
            + "the baseline does not already hold.");
        var ciTarget = new Option<string?>("--target",
            "Target build name or schema folder path. When given, a compat check runs and fails on an "
            + "error-level finding.");
        var ciAgainst = new Option<string?>("--against",
            "Optional path to the ORIGINAL map this one was built from, so the lint also reports what "
            + "a rebuild lost or clobbered.");
        var ciLint = new Command("lint",
            "Run the lint as a gate. Fails on an error-level finding, or, with --baseline, only on a "
            + "finding the baseline does not already hold. With --target it also runs the compat check "
            + "and fails on an error-level finding.")
            { mapArg, ciBaseline, ciTarget, ciSchemas, ciAgainst, jsonOption };
        ciLint.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = CiLintCommand.Execute(
                p.GetValueForArgument(mapArg),
                p.GetValueForOption(ciBaseline),
                p.GetValueForOption(ciTarget),
                p.GetValueForOption(ciSchemas),
                p.GetValueForOption(ciAgainst));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.CiLint(r));
            if (!r.Passed) exitCode[0] = r.ExitCode;
        }));
        ci.AddCommand(ciLint);

        // ci validate <map> [--strict]
        var ciStrict = new Option<bool>("--strict",
            "Run the stricter validation, whose extra checks are too strict to be a default.");
        var ciValidate = new Command("validate",
            "Run the validation as a gate. Exits non-zero when the map cannot load or reports an "
            + "error-level issue. With --strict the extra strict checks are included.")
            { mapArg, ciStrict, jsonOption };
        ciValidate.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = CiValidateCommand.Execute(
                p.GetValueForArgument(mapArg),
                p.GetValueForOption(ciStrict),
                p.GetValueForOption(gameDirOption));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.CiValidate(r));
            if (!r.Passed) exitCode[0] = r.ExitCode;
        }));
        ci.AddCommand(ciValidate);

        // ci compat <map> --target <build> [--schemas <dir>]
        var ciCompatTarget = new Option<string>("--target",
            "Target build name or schema folder path.") { IsRequired = true };
        var ciCompat = new Command("compat",
            "Run the compat check as a gate. Exits non-zero on an error-level finding.")
            { mapArg, ciCompatTarget, ciSchemas, jsonOption };
        ciCompat.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = CiCompatCommand.Execute(
                p.GetValueForArgument(mapArg),
                p.GetValueForOption(ciCompatTarget)!,
                p.GetValueForOption(ciSchemas));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.CiCompat(r));
            if (!r.Passed) exitCode[0] = r.ExitCode;
        }));
        ci.AddCommand(ciCompat);

        // ci schema [--schemas <dir>] [--min-real N]
        var ciMinReal = new Option<int>("--min-real",
            () => 0, "Fail when fewer than this many builds carry real object data.");
        var ciSchema = new Command("schema",
            "Check the schema dataset for regressions. Fails when any build stops parsing or fewer "
            + "than --min-real builds carry real object data.")
            { ciSchemas, ciMinReal, jsonOption };
        ciSchema.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = CiSchemaCommand.Execute(
                p.GetValueForOption(ciSchemas),
                p.GetValueForOption(ciMinReal));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.CiSchema(r));
            if (!r.Passed) exitCode[0] = r.ExitCode;
        }));
        ci.AddCommand(ciSchema);

        // ci all <map> [--target <build>] [--schemas <dir>]
        var ciAll = new Command("all",
            "Run lint, validate and, when --target is given, compat in one pass. Prints a summary line "
            + "per gate and the overall pass or fail with the exit code.")
            { mapArg, ciTarget, ciSchemas, jsonOption };
        ciAll.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            var r = CiAllCommand.Execute(
                p.GetValueForArgument(mapArg),
                p.GetValueForOption(ciTarget),
                p.GetValueForOption(ciSchemas));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.CiAll(r));
            if (!r.Passed) exitCode[0] = r.ExitCode;
        }));
        ci.AddCommand(ciAll);

        root.AddCommand(ci);

        // ---- library: sweep a folder of maps the way a maintainer checks a whole set ----
        var library = new Command("library",
            "Sweep a folder of maps instead of one map, the way a maintainer checks a whole library. "
            + "Every subcommand is read-only and opens one map at a time.");

        // library scan <folder>
        var libraryScanFolder = new Argument<string>("folder",
            "Folder of .w3x/.w3m maps, searched recursively.");
        var libraryScan = new Command("scan",
            "Open every map in a folder and report per map its size, the build it targets if known, "
            + "whether it loads, its entry count and its diagnostic count. Read-only.")
            { libraryScanFolder };
        libraryScan.SetHandler(ctx =>
        {
            var p = ctx.ParseResult;
            RunSafely(() =>
            {
                var r = LibraryScanCommand.Execute(p.GetValueForArgument(libraryScanFolder));
                Emit(p.GetValueForOption(jsonOption), r, () => Render.LibraryScan(r));
            });
        });
        library.AddCommand(libraryScan);

        // library audit <folder>
        var libraryAuditFolder = new Argument<string>("folder",
            "Folder of .w3x/.w3m maps, searched recursively.");
        var libraryAudit = new Command("audit",
            "Run the behavioural audit over every map in a folder and report the findings per map and "
            + "the totals per check, so a whole library is ranked by how much it needs fixing. Read-only.")
            { libraryAuditFolder };
        libraryAudit.SetHandler(ctx =>
        {
            var p = ctx.ParseResult;
            RunSafely(() =>
            {
                var r = LibraryAuditCommand.Execute(
                    p.GetValueForArgument(libraryAuditFolder), p.GetValueForOption(gameDirOption));
                Emit(p.GetValueForOption(jsonOption), r, () => Render.LibraryAudit(r));
            });
        });
        library.AddCommand(libraryAudit);

        // library validate <folder>
        var libraryValidateFolder = new Argument<string>("folder",
            "Folder of .w3x/.w3m maps, searched recursively.");
        var libraryValidate = new Command("validate",
            "Run the load validation over every map in a folder and list the ones that fail, with the "
            + "reason. Read-only.") { libraryValidateFolder };
        libraryValidate.SetHandler(ctx =>
        {
            var p = ctx.ParseResult;
            RunSafely(() =>
            {
                var r = LibraryValidateCommand.Execute(p.GetValueForArgument(libraryValidateFolder));
                Emit(p.GetValueForOption(jsonOption), r, () => Render.LibraryValidate(r));
            });
        });
        library.AddCommand(libraryValidate);

        // library compare <folderA> <folderB>
        var libraryCompareA = new Argument<string>("folderA", "The first folder of maps.");
        var libraryCompareB = new Argument<string>("folderB", "The second folder of maps.");
        var libraryCompare = new Command("compare",
            "Compare two folders by map file name and content hash, listing the maps only in the first, "
            + "only in the second, and changed between them. Read-only.")
            { libraryCompareA, libraryCompareB };
        libraryCompare.SetHandler(ctx =>
        {
            var p = ctx.ParseResult;
            RunSafely(() =>
            {
                var r = LibraryCompareCommand.Execute(
                    p.GetValueForArgument(libraryCompareA), p.GetValueForArgument(libraryCompareB));
                Emit(p.GetValueForOption(jsonOption), r, () => Render.LibraryCompare(r));
            });
        });
        library.AddCommand(libraryCompare);

        // library report <folder> -o <file.md>
        var libraryReportFolder = new Argument<string>("folder",
            "Folder of .w3x/.w3m maps, searched recursively.");
        var libraryReportOut = new Option<string>(new[] { "-o", "--out" },
            "Path to write the Markdown report to.") { IsRequired = true };
        var libraryReport = new Command("report",
            "Write one Markdown report over a folder, a summary line, a per-map table and the "
            + "aggregated findings, an artifact a maintainer can attach to a review.")
            { libraryReportFolder, libraryReportOut };
        libraryReport.SetHandler(ctx =>
        {
            var p = ctx.ParseResult;
            RunSafely(() =>
            {
                var r = LibraryReportCommand.Execute(
                    p.GetValueForArgument(libraryReportFolder),
                    p.GetValueForOption(libraryReportOut)!,
                    p.GetValueForOption(gameDirOption));
                Emit(p.GetValueForOption(jsonOption), r, () => Render.LibraryReport(r));
            });
        });
        library.AddCommand(libraryReport);

        root.AddCommand(library);        root.AddCommand(McpCommand.Build(jsonOption));

        // ---- target: set and view the current target build ----
        var targetBuildArg = new Argument<string>("build",
            "Target build name or schema folder path.");
        var targetListFull = new Option<bool>("--full-only",
            "Skip scripts-only builds.");
        var targetSchemas = new Option<string?>("--schemas",
            "Path to schemas directory containing schema folders with manifest.json.");
        // The root target command shows the current target (no args needed).
        var target = new Command("target",
            "View or set the target game build used by compat and lint when --target is omitted.");
        target.AddOption(targetSchemas);
        target.SetHandler(ctx => RunSafely(() =>
        {
            var schemas = ctx.ParseResult.GetValueForOption(targetSchemas);
            var result = TargetBuildSettings.Get(schemasDir: schemas);
            Console.WriteLine("target  " + result.Build + "  (" + TargetSourceLabel(result.Source) + "), game install " + GameInstallLine(schemas));
        }));
        // target set -> set the target.
        var targetSet = new Command("set", "Set the target build.");
        targetSet.AddArgument(targetBuildArg);
        targetSet.AddOption(targetSchemas);
        targetSet.SetHandler(ctx => RunSafely(() =>
        {
            var schemas = ctx.ParseResult.GetValueForOption(targetSchemas);
            string build = ctx.ParseResult.GetValueForArgument(targetBuildArg);
            try
            {
                TargetBuildSettings.Set(build, schemasDir: schemas);
                Console.WriteLine("set target " + build);
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                exitCode[0] = 1;
            }
        }));
        target.AddCommand(targetSet);
        // target list -> list builds.
        var targetList = new Command("list", "List all available builds.");
        targetList.AddOption(targetSchemas);
        targetList.AddOption(targetListFull);
        targetList.SetHandler(ctx =>
        {
            try
            {
                var schemas = ctx.ParseResult.GetValueForOption(targetSchemas);
                var builds = TargetBuildSettings.List(
                    schemasDir: schemas, fullOnly: ctx.ParseResult.GetValueForOption(targetListFull));
                foreach (var b in builds)
                {
                    Console.Write(b.Build);
                    if (b.Sections.Count > 0)
                    {
                        Console.Write("  sections " + string.Join(", ", b.Sections));
                    }
                    Console.WriteLine();
                }
            }
            catch (DirectoryNotFoundException)
            {
                Console.Error.WriteLine("error: could not find schemas directory");
                exitCode[0] = 1;
            }
        });
        target.AddCommand(targetList);
        // target clear -> clear the setting.
        var targetClear = new Command("clear", "Clear the target setting.");
        targetClear.SetHandler(() =>
        {
            TargetBuildSettings.Clear();
            Console.WriteLine("cleared target setting");
        });
        target.AddCommand(targetClear);
        root.AddCommand(target);

        // ---- version: what this build is, and the one command that bumps it ----
        var versionSet = new Option<string?>("--set",
            "Write this x.y.z version into Directory.Build.props instead of reporting the current one.");
        var versionProps = new Option<string?>("--props",
            "Path to Directory.Build.props. Default, the repository file.") { IsHidden = true };
        var version = new Command("version",
            "Print the product version, the git short hash and build time stamped into this build, "
            + "and the MCP server name and version. With --set, write a new version into "
            + "Directory.Build.props. Read-only unless --set is given.")
        { versionSet, versionProps };
        version.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            bool json = p.GetValueForOption(jsonOption);
            string? set = p.GetValueForOption(versionSet);
            if (set is not null)
            {
                string? props = p.GetValueForOption(versionProps) ?? RepoLayout.FindBuildProps();
                if (props is null)
                {
                    const string noProps = "could not locate Directory.Build.props, pass --props <path>";
                    Emit(json, new { Ok = false, Message = noProps }, () => noProps);
                    exitCode[0] = 1;
                    return;
                }
                var written = VersionSetCommand.Execute(props, set);
                Emit(json, written, () => written.Message);
                if (!written.Ok) exitCode[0] = 1;
                return;
            }
            var info = ReleaseVersionCommand.Read(typeof(Program).Assembly,
                Wc3.Mcp.ServerIdentity.ServerName, Wc3.Mcp.ServerIdentity.Version);
            Emit(json, info, () => Render.ReleaseVersion(info));
        }));
        root.AddCommand(version);

        // ---- changelog: release notes from git log, grouped by Conventional Commit type ----
        var changelogFrom = new Option<string?>("--from",
            "Git ref to start after. Default, the most recent tag reachable from the end ref.");
        var changelogTo = new Option<string?>("--to", "Git ref to end at. Default, HEAD.");
        var changelogRepo = new Option<string?>("--repo",
            "Repository directory. Default, the located repository.") { IsHidden = true };
        var changelogOut = new Option<string>(new[] { "-o", "--out" },
            "Path to write the release notes markdown to.") { IsRequired = true };
        var changelog = new Command("changelog",
            "Generate release notes from git log between two refs, grouped by Conventional Commit "
            + "type (feat, fix, perf, docs, chore and the rest), a heading per group with its subjects "
            + "under it. Read-only over git.")
        { changelogFrom, changelogTo, changelogRepo, changelogOut };
        changelog.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string repo = p.GetValueForOption(changelogRepo)
                ?? RepoLayout.FindRoot() ?? Directory.GetCurrentDirectory();
            var r = ChangelogCommand.Execute(repo,
                p.GetValueForOption(changelogFrom), p.GetValueForOption(changelogTo));
            if (!r.Ok)
            {
                Emit(p.GetValueForOption(jsonOption), r, () => r.Message ?? "changelog failed");
                exitCode[0] = 1;
                return;
            }
            string outPath = p.GetValueForOption(changelogOut)!;
            if (Path.GetDirectoryName(Path.GetFullPath(outPath)) is { Length: > 0 } dir)
                Directory.CreateDirectory(dir);
            File.WriteAllText(outPath, r.Markdown);
            Emit(p.GetValueForOption(jsonOption), r, () => Render.Changelog(r, outPath));
        }));
        root.AddCommand(changelog);

        // ---- export: keep the public wc3-mcp checkout in step with this repository ----
        var exportTarget = new Argument<string>("path", "Path to the wc3-mcp checkout to check.");
        var exportSource = new Option<string?>("--source",
            "wc3ctl repository directory. Default, the located repository.");
        var export = new Command("export", "Export tooling for the public wc3-mcp repository.");
        var exportVerify = new Command("verify",
            "Verify a wc3-mcp checkout holds the same server source this repository would export, "
            + "reporting every file that differs. Writes nothing.")
        { exportTarget, exportSource };
        exportVerify.SetHandler(ctx => RunSafely(() =>
        {
            var p = ctx.ParseResult;
            string source = p.GetValueForOption(exportSource)
                ?? RepoLayout.FindRoot() ?? Directory.GetCurrentDirectory();
            var r = McpExportVerifyCommand.Execute(source, p.GetValueForArgument(exportTarget));
            Emit(p.GetValueForOption(jsonOption), r, () => Render.McpExportVerify(r));
            if (!r.InStep) exitCode[0] = 1;
        }));
        export.AddCommand(exportVerify);
        root.AddCommand(export);

        root.AddCommand(DocsCommand.Build());
        return root;
    }
}
