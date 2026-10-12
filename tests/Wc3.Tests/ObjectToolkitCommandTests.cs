// tests/Wc3.Tests/ObjectToolkitCommandTests.cs
using System.Text;
using War3Net.Build.Extensions;
using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.Commands;
using Wc3.GameData;
using Wc3.Model;

namespace Wc3.Tests;

/// <summary>
/// Hermetic coverage for the object toolkit commands added on the object branch:
/// search, noop-fields, deprecated, rawcode-suggest and clone. Every map is built in
/// memory through <see cref="SyntheticMap"/>, so nothing here needs a WC3 install.
/// </summary>
public class ObjectToolkitCommandTests
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

    private const string AbilityMetadataSlk =
        "ID;PWXL;N;E\n" +
        "C;Y1;X1;K\"ID\"\n" +
        "C;X2;K\"slk\"\n" +
        "C;X3;K\"field\"\n" +
        "C;X4;K\"displayName\"\n" +
        "C;X5;K\"type\"\n" +
        "E\n";

    // Two base units: hfoo with HP 420, and u000 (deliberately colliding with the
    // custom in the map, to prove a suggestion avoids the base catalog too).
    private const string UnitBalanceSlk =
        "ID;PWXL;N;E\n" +
        "C;Y1;X1;K\"ID\"\n" +
        "C;X2;K\"HP\"\n" +
        "C;Y2;X1;K\"hfoo\"\n" +
        "C;X2;K\"420\"\n" +
        "C;Y3;X1;K\"u000\"\n" +
        "C;X2;K\"500\"\n" +
        "E\n";

    /// <summary>A unit-only GameDataContext: hfoo and u000 with an HP field.</summary>
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

    private static MapDocument UnitMap(string rawcode, string baseRawcode, params (string Code, int Value)[] fields)
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        var unit = new SimpleObjectModification
        { OldId = baseRawcode.FromRawcode(), NewId = rawcode.FromRawcode() };
        foreach (var (code, value) in fields)
            unit.Modifications.Add(new SimpleObjectDataModification
            { Id = code.FromRawcode(), Type = ObjectDataType.Int, Value = value });
        w3u.NewUnits.Add(unit);
        return MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        { ["war3map.w3u"] = Ser(w => w.Write(w3u)) }));
    }

    private static MapDocument AbilityMap()
    {
        var w3a = new AbilityObjectData(ObjectDataFormatVersion.v2);
        var ability = new LevelObjectModification
        { OldId = "ANcl".FromRawcode(), NewId = "A000".FromRawcode() };
        ability.Modifications.Add(new LevelObjectDataModification
        { Level = 1, Pointer = 0, Id = "aran".FromRawcode(), Type = ObjectDataType.Unreal, Value = 500f });
        ability.Modifications.Add(new LevelObjectDataModification
        { Level = 2, Pointer = 3, Id = "aran".FromRawcode(), Type = ObjectDataType.Unreal, Value = 600f });
        w3a.NewAbilities.Add(ability);
        return MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        { ["war3map.w3a"] = Ser(w => w.Write(w3a)) }));
    }

    private static byte[] Ser(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) write(bw);
        return ms.ToArray();
    }

    private static SchemaField Field(string code) =>
        new(code, "UnitBalance", "HP", "int", "WESTRING", code, "data", 0, 0, 0,
            "", "", "", "", true, true, true, true, true);

    private static GameSchema SchemaWith(params string[] unitFields) => new(
        1, "test-build", "synthetic",
        Array.Empty<SchemaInput>(), Array.Empty<string>(),
        new Dictionary<string, IReadOnlyList<SchemaField>>
        { ["units/unitmetadata.slk"] = unitFields.Select(Field).ToList() },
        null, null);

    // ---- search -----------------------------------------------------------

    [Fact]
    public void Search_finds_a_map_delta_value()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", 999));
        var r = ObjectSearchCommand.Execute(doc, "999", null, null, ctx: null);

        var hit = Assert.Single(r.Hits);
        Assert.Equal("unit", hit.Kind);
        Assert.Equal("u000", hit.Rawcode);
        Assert.Equal("uhpm", hit.FieldCode);
        Assert.Equal("999", hit.MapValue);
        Assert.Null(hit.BaseValue);
    }

    [Fact]
    public void Search_respects_the_field_filter()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", 999));
        Assert.Empty(ObjectSearchCommand.Execute(doc, "999", null, "zzzz", ctx: null).Hits);
        Assert.Single(ObjectSearchCommand.Execute(doc, "999", null, "uhpm", ctx: null).Hits);
    }

    [Fact]
    public void Search_reports_both_base_and_map_values()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", 420));
        var ctx = BuildUnitContext();
        var r = ObjectSearchCommand.Execute(doc, "420", ObjectKind.Unit, null, ctx);

        var hit = Assert.Single(r.Hits);
        Assert.Equal("420", hit.BaseValue);
        Assert.Equal("420", hit.MapValue);
    }

    [Fact]
    public void Search_with_an_empty_query_returns_nothing_and_a_note()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", 999));
        var r = ObjectSearchCommand.Execute(doc, "   ", null, null, ctx: null);
        Assert.Empty(r.Hits);
        Assert.NotEmpty(r.Diagnostics);
    }

    // ---- noop-fields ------------------------------------------------------

    [Fact]
    public void Noop_detects_a_delta_equal_to_the_base_value()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", 420));
        var r = ObjectNoopFieldsCommand.Execute(doc, ObjectKind.Unit, BuildUnitContext(), Array.Empty<string>());

        var f = Assert.Single(r.Fields);
        Assert.Equal("u000", f.Rawcode);
        Assert.Equal("uhpm", f.FieldCode);
        Assert.Equal("420", f.Value);
    }

    [Fact]
    public void Noop_ignores_a_delta_that_changes_the_value()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", 999));
        var r = ObjectNoopFieldsCommand.Execute(doc, ObjectKind.Unit, BuildUnitContext(), Array.Empty<string>());
        Assert.Empty(r.Fields);
    }

    [Fact]
    public void Noop_without_game_data_reports_map_only()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", 420));
        var r = ObjectNoopFieldsCommand.Execute(doc, ObjectKind.Unit, @"Z:\no_such_install");
        Assert.Empty(r.Fields);
        Assert.NotEmpty(r.Diagnostics);
    }

    [Fact]
    public void Noop_value_comparison_treats_numbers_numerically()
    {
        Assert.True(ObjectNoopFieldsCommand.ValuesEqual("420", "420.0"));
        Assert.False(ObjectNoopFieldsCommand.ValuesEqual("420", "421"));
    }

    // ---- deprecated -------------------------------------------------------

    [Fact]
    public void Deprecated_reports_a_field_the_target_schema_lacks()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", 999), ("unam", 0));
        // unam is Int here but the schema only cares about the field code existing.
        var r = ObjectDeprecatedCommand.Execute(doc, SchemaWith("unam"));

        var f = Assert.Single(r.Fields);
        Assert.Equal("u000", f.Rawcode);
        Assert.Equal("uhpm", f.FieldCode);
        Assert.Equal("test-build", r.TargetBuild);
        Assert.Equal(2, r.FieldsChecked);
    }

    [Fact]
    public void Deprecated_without_an_object_schema_says_so()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", 999));
        var schema = new GameSchema(1, "test-build", "synthetic",
            Array.Empty<SchemaInput>(), Array.Empty<string>(), null, null, null);

        var r = ObjectDeprecatedCommand.Execute(doc, schema);
        Assert.Empty(r.Fields);
        Assert.Equal(0, r.FieldsChecked);
        Assert.NotEmpty(r.Diagnostics);
    }

    // ---- rawcode-suggest --------------------------------------------------

    [Fact]
    public void Rawcode_suggest_avoids_map_and_base_rawcodes()
    {
        // The map defines custom u000 and the base catalog also has u000, so the first
        // unit suggestion must move past both.
        var doc = UnitMap("u000", "hfoo", ("uhpm", 999));
        var r = ObjectRawcodeSuggestCommand.Execute(
            doc, ObjectKind.Unit, 3, BuildUnitContext(), Array.Empty<string>());

        Assert.Equal(3, r.Rawcodes.Count);
        Assert.DoesNotContain("u000", r.Rawcodes);
        Assert.Equal(r.Rawcodes.Count, r.Rawcodes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Rawcode_suggest_with_zero_count_returns_none()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", 999));
        var r = ObjectRawcodeSuggestCommand.Execute(
            doc, ObjectKind.Unit, 0, BuildUnitContext(), Array.Empty<string>());
        Assert.Empty(r.Rawcodes);
    }

    // ---- clone ------------------------------------------------------------

    [Fact]
    public void Clone_copies_a_units_field_deltas()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", 999));
        var r = ObjectCloneCommand.Execute(doc, ObjectKind.Unit, "u000", "u001");

        Assert.True(r.Ok, r.Message);
        Assert.Equal("u001", r.NewRawcode);
        Assert.Equal(1, r.FieldsCopied);

        var model = (UnitObjectData)doc.GetFile("war3map.w3u")!.Model!;
        var clone = Assert.Single(model.NewUnits, g => g.NewId == "u001".FromRawcode());
        Assert.Equal("hfoo".FromRawcode(), clone.OldId);
        var mod = Assert.Single(clone.Modifications);
        Assert.Equal("uhpm".FromRawcode(), mod.Id);
        Assert.Equal(999, (int)mod.Value);
    }

    [Fact]
    public void Clone_copies_ability_level_data_and_pointer()
    {
        var doc = AbilityMap();
        var r = ObjectCloneCommand.Execute(doc, ObjectKind.Ability, "A000", "A001");

        Assert.True(r.Ok, r.Message);
        var model = (AbilityObjectData)doc.GetFile("war3map.w3a")!.Model!;
        var clone = Assert.Single(model.NewAbilities, g => g.NewId == "A001".FromRawcode());
        Assert.Equal("ANcl".FromRawcode(), clone.OldId);
        Assert.Equal(2, clone.Modifications.Count);
        var level2 = Assert.Single(clone.Modifications, m => m.Level == 2);
        Assert.Equal(3, level2.Pointer);
        Assert.Equal(600f, (float)level2.Value);
    }

    [Fact]
    public void Clone_refuses_a_rawcode_already_taken()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", 999));
        var r = ObjectCloneCommand.Execute(doc, ObjectKind.Unit, "u000", "u000");
        Assert.False(r.Ok);
    }

    [Fact]
    public void Clone_refuses_a_source_not_in_the_map()
    {
        var doc = UnitMap("u000", "hfoo", ("uhpm", 999));
        var r = ObjectCloneCommand.Execute(doc, ObjectKind.Unit, "zzzz", "u002");
        Assert.False(r.Ok);
    }

    // ---- CLI wiring -------------------------------------------------------

    [Fact]
    public async Task Cli_object_clone_writes_an_edited_copy_and_never_touches_the_input()
    {
        var map = UnitMap("u000", "hfoo", ("uhpm", 999));
        var path = Path.Combine(Path.GetTempPath(), $"wc3ctl_clone_{Guid.NewGuid():N}.w3x");
        var edited = Path.Combine(
            Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".edited.w3x");
        File.WriteAllBytes(path, SyntheticMap.Build(new Dictionary<string, byte[]>
        { ["war3map.w3u"] = ExtractW3u(map) }));
        var before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        try
        {
            var sw = new StringWriter();
            var console = System.Console.Out;
            System.Console.SetOut(sw);
            int code = await Wc3Ctl.Program.Main(new[] { "object", "clone", path, "unit", "u000", "u001" });
            System.Console.SetOut(console);

            Assert.Equal(0, code);
            Assert.Contains("cloned unit u000 to u001", sw.ToString());
            Assert.True(File.Exists(edited), "default output <map>.edited.w3x must exist");
            Assert.Equal(before,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));

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

    private static byte[] ExtractW3u(MapDocument doc) =>
        ((MapFileEntry)doc.GetFile("war3map.w3u")!).CurrentBytes;
}
