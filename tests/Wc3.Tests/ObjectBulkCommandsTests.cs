// tests/Wc3.Tests/ObjectBulkCommandsTests.cs
using System.Text;
using War3Net.Build.Extensions;
using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.Commands;
using Wc3.GameData;
using Wc3.Model;

namespace Wc3.Tests;

/// <summary>
/// Hermetic coverage for the object data commands added on the object2 branch: migrate,
/// bulk-set, export, import and rename. Every map is built in memory through
/// <see cref="SyntheticMap"/>, so nothing here needs a WC3 install.
/// The rename and migrate cases drive the CLI and swap process-wide
/// <see cref="System.Console.Out"/>, so this class joins the Serial collection that
/// owns every Console writer.
/// </summary>
[Collection("Serial")]
public class ObjectBulkCommandsTests
{
    // ---- synthetic base game data (units only) ---------------------------

    private sealed class FakeGameSource : IGameDataSource
    {
        private readonly Dictionary<string, byte[]> _files;
        public FakeGameSource(Dictionary<string, byte[]> files) =>
            _files = new Dictionary<string, byte[]>(files, StringComparer.OrdinalIgnoreCase);
        public byte[]? ReadFile(string name) => _files.TryGetValue(name, out var b) ? b : null;
        public void Dispose() { }
    }

    private const string UnitMetadataSlk =
        "ID;PWXL;N;E\n" +
        "C;Y1;X1;K\"ID\"\n" +
        "C;X2;K\"slk\"\n" +
        "C;X3;K\"field\"\n" +
        "C;X4;K\"displayName\"\n" +
        "C;X5;K\"type\"\n" +
        "C;Y2;X1;K\"uhpm\"\n" +
        "C;X2;K\"UnitBalance\"\n" +
        "C;X3;K\"HP\"\n" +
        "C;X4;K\"WESTRING_UHPM\"\n" +
        "C;X5;K\"int\"\n" +
        "E\n";

    private const string UnitBalanceSlk =
        "ID;PWXL;N;E\n" +
        "C;Y1;X1;K\"ID\"\n" +
        "C;X2;K\"HP\"\n" +
        "C;Y2;X1;K\"hfoo\"\n" +
        "C;X2;K\"420\"\n" +
        "E\n";

    private const string AbilityMetadataSlk =
        "ID;PWXL;N;E\n" +
        "C;Y1;X1;K\"ID\"\n" +
        "C;X2;K\"slk\"\n" +
        "C;X3;K\"field\"\n" +
        "C;X4;K\"displayName\"\n" +
        "C;X5;K\"type\"\n" +
        "E\n";

    /// <summary>A unit-only context: hfoo with an HP (uhpm) field defined in the metadata.</summary>
    private static GameDataContext BuildUnitContext()
    {
        using var src = new FakeGameSource(new Dictionary<string, byte[]>
        {
            [@"war3.w3mod:units\unitmetadata.slk"] = Encoding.UTF8.GetBytes(UnitMetadataSlk),
            [@"war3.w3mod:units\abilitymetadata.slk"] = Encoding.UTF8.GetBytes(AbilityMetadataSlk),
            [@"war3.w3mod:units\unitbalance.slk"] = Encoding.UTF8.GetBytes(UnitBalanceSlk),
        });
        return new GameDataContext
        {
            Units = BaseUnitStore.Build(src),
            Abilities = BaseAbilityStore.Build(src),
            Items = ObjectDataStore.Empty,
            Destructables = ObjectDataStore.Empty,
            Doodads = ObjectDataStore.Empty,
            Buffs = ObjectDataStore.Empty,
            Upgrades = ObjectDataStore.Empty,
            Strings = WorldEditStrings.Parse(""),
            UnitNames = UnitNameTable.FromSources(src),
        };
    }

    // ---- synthetic maps ---------------------------------------------------

    private static byte[] Ser(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) write(bw);
        return ms.ToArray();
    }

    /// <summary>A map with one custom unit: base hfoo, the given field deltas.</summary>
    private static byte[] UnitMapBytes(string rawcode, string baseRawcode, params (string Code, string Value)[] fields)
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        var unit = new SimpleObjectModification
        { OldId = baseRawcode.FromRawcode(), NewId = rawcode.FromRawcode() };
        foreach (var (code, value) in fields)
            unit.Modifications.Add(new SimpleObjectDataModification
            { Id = code.FromRawcode(), Type = ObjectDataType.String, Value = value });
        w3u.NewUnits.Add(unit);
        return SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.w3u"] = Ser(w => w.Write(w3u)) });
    }

    private static MapDocument UnitMap(string rawcode, string baseRawcode, params (string Code, string Value)[] fields) =>
        MapDocument.Load(UnitMapBytes(rawcode, baseRawcode, fields));

    // ---- schemas ----------------------------------------------------------

    private static SchemaField Field(string code) =>
        new(code, "UnitBalance", "HP", "int", "WESTRING", code, "data", 0, 0, 0,
            "", "", "", "", true, true, true, true, true);

    private static GameSchema UnitSchema(params string[] unitFields) => new(
        1, "test-build", "synthetic",
        Array.Empty<SchemaInput>(), Array.Empty<string>(),
        new Dictionary<string, IReadOnlyList<SchemaField>>
        { ["units/unitmetadata.slk"] = unitFields.Select(Field).ToList() },
        null, null);

    // ---- migrate ----------------------------------------------------------

    [Fact]
    public void Migrate_drops_a_field_the_target_lacks_and_reports_it()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", "999"), ("unam", "Hero"));
        var r = ObjectMigrateCommand.Execute(doc, UnitSchema("unam"));

        Assert.True(r.Ok, r.Message);
        Assert.Equal("test-build", r.TargetBuild);
        Assert.Equal(1, r.FieldsDropped);
        Assert.Equal(1, r.FieldsKept);
        var drop = Assert.Single(r.Drops);
        Assert.Equal("unit", drop.Kind);
        Assert.Equal("u000", drop.Rawcode);
        Assert.Equal("uhpm", drop.FieldCode);

        var model = (UnitObjectData)doc.GetFile("war3map.w3u")!.Model!;
        var mod = Assert.Single(model.NewUnits[0].Modifications);
        Assert.Equal("unam".FromRawcode(), mod.Id);
    }

    [Fact]
    public void Migrate_without_an_object_section_refuses()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", "999"));
        var schema = new GameSchema(1, "test-build", "synthetic",
            Array.Empty<SchemaInput>(), Array.Empty<string>(), null, null, null);

        var r = ObjectMigrateCommand.Execute(doc, schema);
        Assert.False(r.Ok);
        // The map is untouched: the field the target cannot judge stays.
        var model = (UnitObjectData)doc.GetFile("war3map.w3u")!.Model!;
        Assert.Single(model.NewUnits[0].Modifications);
    }

    // ---- bulk-set ---------------------------------------------------------

    [Fact]
    public void Bulk_set_writes_only_the_objects_matching_every_where_clause()
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        w3u.NewUnits.Add(Simple("u000", "hfoo", "uhpm", "420"));
        w3u.NewUnits.Add(Simple("u001", "hfoo", "uhpm", "999"));
        var doc = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        { ["war3map.w3u"] = Ser(w => w.Write(w3u)) }));

        var r = ObjectBulkSetCommand.Execute(doc, ObjectKind.Unit, "uhpm", "1000",
            new[] { new BulkSetWhere("uhpm", "420") }, ctx: null, Array.Empty<string>());

        Assert.True(r.Ok, r.Message);
        Assert.Equal(1, r.Matched);
        Assert.Equal(1, r.Changed);
        var model = (UnitObjectData)doc.GetFile("war3map.w3u")!.Model!;
        Assert.Equal("1000", (string)model.NewUnits.Single(g => g.NewId == "u000".FromRawcode())
            .Modifications.Single().Value);
        Assert.Equal("999", (string)model.NewUnits.Single(g => g.NewId == "u001".FromRawcode())
            .Modifications.Single().Value);
    }

    [Fact]
    public void Bulk_set_where_clause_tests_the_base_value_when_context_is_present()
    {
        // The map holds no uhpm delta, but the base unit hfoo has HP 420.
        var doc = UnitMap("u000", "hfoo");
        var r = ObjectBulkSetCommand.Execute(doc, ObjectKind.Unit, "uhpm", "1000",
            new[] { new BulkSetWhere("uhpm", "420") }, BuildUnitContext(), Array.Empty<string>());

        Assert.Equal(1, r.Matched);
        Assert.Equal(1, r.Changed);
    }

    [Fact]
    public void Bulk_set_with_no_matching_object_changes_nothing()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", "420"));
        var r = ObjectBulkSetCommand.Execute(doc, ObjectKind.Unit, "uhpm", "1000",
            new[] { new BulkSetWhere("uhpm", "7") }, ctx: null, Array.Empty<string>());

        Assert.Equal(0, r.Matched);
        Assert.Equal(0, r.Changed);
    }

    // ---- export -----------------------------------------------------------

    [Fact]
    public void Export_writes_one_row_per_object_with_a_header()
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        w3u.NewUnits.Add(Simple("u000", "hfoo", "uhpm", "420"));
        w3u.NewUnits.Add(Simple("u001", "hfoo", "unam", "Grunt"));
        var doc = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]> 
        { ["war3map.w3u"] = Ser(w => w.Write(w3u)) }));

        var r = ObjectExportCommand.Execute(doc, ObjectKind.Unit, ctx: null, Array.Empty<string>());

        Assert.True(r.Ok, r.Message);
        Assert.Equal(2, r.ObjectCount);
        Assert.Contains("uhpm", r.Columns);
        Assert.Contains("unam", r.Columns);
        var lines = r.Csv.TrimEnd('\n').Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("rawcode,", lines[0]);
    }

    [Fact]
    public void Export_then_import_round_trips_the_values()
    {
        var doc = MapDocument.Load(UnitMapBytes("u000", "hfoo", ("uhpm", "1234")));
        var export = ObjectExportCommand.Execute(doc, ObjectKind.Unit, ctx: null, Array.Empty<string>());

        // A fresh map with the same object but a different value, so the import must overwrite it.
        var target = UnitMap("u000", "hfoo", ("uhpm", "1"));
        var import = ObjectImportCommand.Execute(target, ObjectKind.Unit, export.Csv, ctx: null, Array.Empty<string>());

        Assert.True(import.Ok, import.Message);
        Assert.Equal(1, import.Updated);
        var model = (UnitObjectData)target.GetFile("war3map.w3u")!.Model!;
        Assert.Equal("1234", (string)model.NewUnits[0].Modifications.Single().Value);
    }

    // ---- import -----------------------------------------------------------

    [Fact]
    public void Import_adds_a_new_object_the_map_lacks()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", "420"));
        string csv = "rawcode,uhpm\n" +
                     "u000,420\n" +
                     "u009,555\n";

        var r = ObjectImportCommand.Execute(doc, ObjectKind.Unit, csv, ctx: null, Array.Empty<string>());

        Assert.True(r.Ok, r.Message);
        Assert.Equal(1, r.Added);
        Assert.Equal(1, r.Updated);
        var model = (UnitObjectData)doc.GetFile("war3map.w3u")!.Model!;
        Assert.Contains(model.NewUnits, g => g.NewId == "u009".FromRawcode()
            && g.Modifications.Any(m => m.Id == "uhpm".FromRawcode() && m.Value?.ToString() == "555"));
    }

    [Fact]
    public void Import_refuses_an_unknown_column_and_changes_nothing()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", "420"));
        var before = ((UnitObjectData)doc.GetFile("war3map.w3u")!.Model!).NewUnits.Count;
        // zzzz is a well-shaped code but the unit metadata does not define it.
        string csv = "rawcode,zzzz\n" +
                     "u000,555\n";

        var r = ObjectImportCommand.Execute(doc, ObjectKind.Unit, csv, BuildUnitContext(), Array.Empty<string>());

        Assert.False(r.Ok);
        Assert.Contains("zzzz", r.Message);
        Assert.Equal(before, ((UnitObjectData)doc.GetFile("war3map.w3u")!.Model!).NewUnits.Count);
    }

    [Fact]
    public void Import_refuses_a_malformed_column_name()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", "420"));
        string csv = "rawcode,toolong\n" +
                     "u000,555\n";
        var r = ObjectImportCommand.Execute(doc, ObjectKind.Unit, csv, ctx: null, Array.Empty<string>());
        Assert.False(r.Ok);
        Assert.Contains("not a valid field code", r.Message);
    }

    // ---- rename -----------------------------------------------------------

    [Fact]
    public void Rename_changes_the_object_rawcode()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", "420"));
        var r = ObjectRenameCommand.Execute(doc, ObjectKind.Unit, "u000", "u001", rewriteRefs: false);

        Assert.True(r.Ok, r.Message);
        var model = (UnitObjectData)doc.GetFile("war3map.w3u")!.Model!;
        Assert.Contains(model.NewUnits, g => g.NewId == "u001".FromRawcode());
        Assert.DoesNotContain(model.NewUnits, g => g.NewId == "u000".FromRawcode());
    }

    [Fact]
    public void Rename_with_rewrite_refs_repoints_a_field_value()
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        w3u.NewUnits.Add(Simple("u000", "hfoo", "uhpm", "420"));
        w3u.NewUnits.Add(Simple("u001", "hfoo", "uabi", "u000,A000"));
        var doc = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        { ["war3map.w3u"] = Ser(w => w.Write(w3u)) }));

        var r = ObjectRenameCommand.Execute(doc, ObjectKind.Unit, "u000", "u002", rewriteRefs: true);

        Assert.True(r.Ok, r.Message);
        Assert.Equal(1, r.FieldsRewritten);
        var model = (UnitObjectData)doc.GetFile("war3map.w3u")!.Model!;
        var abil = model.NewUnits.Single(g => g.NewId == "u001".FromRawcode())
            .Modifications.Single(m => m.Id == "uabi".FromRawcode());
        Assert.Equal("u002,A000", (string)abil.Value);
    }

    [Fact]
    public void Rename_with_rewrite_refs_repoints_a_script_literal()
    {
        var script = Encoding.Latin1.GetBytes("function f takes nothing returns nothing\n" +
            "    call SetUnitType(\"u000\")\nendfunction\n");
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        w3u.NewUnits.Add(Simple("u000", "hfoo", "uhpm", "420"));
        var doc = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(w3u)),
            ["war3map.j"] = script,
        }));

        var r = ObjectRenameCommand.Execute(doc, ObjectKind.Unit, "u000", "u002", rewriteRefs: true);

        Assert.True(r.Ok, r.Message);
        Assert.Equal(1, r.ScriptLiteralsRewritten);
        string text = ScriptText.GetString(((MapFileEntry)doc.GetFile("war3map.j")!).CurrentBytes);
        Assert.Contains("\"u002\"", text);
        Assert.DoesNotContain("\"u000\"", text);
    }

    [Fact]
    public void Rename_refuses_a_new_rawcode_already_taken()
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        w3u.NewUnits.Add(Simple("u000", "hfoo", "uhpm", "420"));
        w3u.NewUnits.Add(Simple("u001", "hfoo", "uhpm", "999"));
        var doc = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        { ["war3map.w3u"] = Ser(w => w.Write(w3u)) }));

        var r = ObjectRenameCommand.Execute(doc, ObjectKind.Unit, "u000", "u001", rewriteRefs: true);
        Assert.False(r.Ok);
    }

    // ---- CLI wiring -------------------------------------------------------

    [Fact]
    public async Task Cli_object_rename_writes_an_edited_copy_and_never_touches_the_input()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wc3ctl_rename_{Guid.NewGuid():N}.w3x");
        var edited = Path.Combine(
            Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".edited.w3x");
        File.WriteAllBytes(path, UnitMapBytes("u000", "hfoo", ("uhpm", "420")));
        var before = Sha(path);
        try
        {
            var sw = new StringWriter();
            var console = System.Console.Out;
            System.Console.SetOut(sw);
            int code = await Wc3Ctl.Program.Main(
                new[] { "object", "rename", path, "u000", "u001" });
            System.Console.SetOut(console);

            Assert.True(code == 0, sw.ToString());
            Assert.Contains("renamed unit u000 to u001", sw.ToString());
            Assert.True(File.Exists(edited), "default output <map>.edited.w3x must exist");
            Assert.Equal(before, Sha(path));

            var reloaded = MapDocument.Load(edited);
            var model = (UnitObjectData)reloaded.GetFile("war3map.w3u")!.Model!;
            Assert.Contains(model.NewUnits, g => g.NewId == "u001".FromRawcode());
        }
        finally
        {
            File.Delete(path);
            if (File.Exists(edited)) File.Delete(edited);
        }
    }

    [Fact]
    public async Task Cli_object_migrate_refuses_to_overwrite_the_input_in_place()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wc3ctl_migrate_{Guid.NewGuid():N}.w3x");
        var schemaDir = Path.Combine(Path.GetTempPath(), $"wc3ctl_schema_{Guid.NewGuid():N}");
        File.WriteAllBytes(path, UnitMapBytes("u000", "hfoo", ("uhpm", "420")));
        GameSchemaCommand.Save(UnitSchema("unam"), schemaDir);
        var before = Sha(path);
        try
        {
            var sw = new StringWriter();
            var console = System.Console.Out;
            System.Console.SetOut(sw);
            int code = await Wc3Ctl.Program.Main(
                new[] { "object", "migrate", path, "--to", schemaDir, "-o", path });
            System.Console.SetOut(console);

            Assert.Equal(1, code);
            Assert.Contains("refusing to overwrite the input map", sw.ToString());
            Assert.Equal(before, Sha(path));
        }
        finally
        {
            File.Delete(path);
            if (Directory.Exists(schemaDir)) Directory.Delete(schemaDir, true);
        }
    }

    private static SimpleObjectModification Simple(string rawcode, string baseRawcode, string code, string value)
    {
        var g = new SimpleObjectModification
        { OldId = baseRawcode.FromRawcode(), NewId = rawcode.FromRawcode() };
        g.Modifications.Add(new SimpleObjectDataModification
        { Id = code.FromRawcode(), Type = ObjectDataType.String, Value = value });
        return g;
    }

    private static string Sha(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
}
