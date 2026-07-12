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
            var r = ObjectGetCommand.Execute(MapDocument.Load(map), rawcode, field);
            Emit(json, r, () => Render.ObjectGet(r));
        }), mapArg, objRawcode, objField, jsonOption);
        objGet.AddCommand(objGetSub);

        root.AddCommand(info); root.AddCommand(ls); root.AddCommand(rt);
        root.AddCommand(search); root.AddCommand(diff); root.AddCommand(objGet);

        int parseResult = await root.InvokeAsync(args);
        return parseResult != 0 ? parseResult : exitCode;
    }
}
