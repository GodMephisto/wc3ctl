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

        void Emit(bool json, object result, Func<string> human) =>
            Console.WriteLine(json ? Render.AsJson(result) : human());

        var info = new Command("info", "Show map metadata.") { mapArg };
        info.SetHandler((string map, bool json) =>
        {
            var r = InfoCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.Info(r));
        }, mapArg, jsonOption);

        var ls = new Command("ls", "List internal files.") { mapArg };
        ls.SetHandler((string map, bool json) =>
        {
            var r = ListCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.List(r));
        }, mapArg, jsonOption);

        var rt = new Command("roundtrip", "Verify byte-faithful round-trip.") { mapArg };
        rt.SetHandler((string map, bool json) =>
        {
            var r = RoundtripCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.Roundtrip(r));
        }, mapArg, jsonOption);

        var queryArg = new Argument<string>("query", "Substring to search for.");
        var search = new Command("search", "Search map contents.") { mapArg, queryArg };
        search.SetHandler((string map, string query, bool json) =>
        {
            var r = SearchCommand.Execute(MapDocument.Load(map), query);
            Emit(json, r, () => Render.Search(r));
        }, mapArg, queryArg, jsonOption);

        var mapB = new Argument<string>("mapB", "Path to the second map.");
        var diff = new Command("diff", "Diff two maps.") { mapArg, mapB };
        diff.SetHandler((string a, string b, bool json) =>
        {
            var r = DiffCommand.Execute(MapDocument.Load(a), MapDocument.Load(b));
            Emit(json, r, () => Render.Diff(r));
        }, mapArg, mapB, jsonOption);

        var objRawcode = new Argument<string>("rawcode", "Four-character object rawcode.");
        var objField = new Option<string?>("--field", "Restrict output to a single field.");
        var objGet = new Command("object", "Object data queries.");
        var objGetSub = new Command("get", "Get an object's fields.") { mapArg, objRawcode, objField };
        objGetSub.SetHandler((string map, string rawcode, string? field, bool json) =>
        {
            var r = ObjectGetCommand.Execute(MapDocument.Load(map), rawcode, field);
            Emit(json, r, () => Render.ObjectGet(r));
        }, mapArg, objRawcode, objField, jsonOption);
        objGet.AddCommand(objGetSub);

        root.AddCommand(info); root.AddCommand(ls); root.AddCommand(rt);
        root.AddCommand(search); root.AddCommand(diff); root.AddCommand(objGet);

        return await root.InvokeAsync(args);
    }
}
