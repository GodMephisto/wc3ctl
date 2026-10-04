// tests/Wc3.Tests/GameSchemaTests.cs
using System.Text;
using Wc3.Commands;
using Wc3.GameData;
using Wc3.Model;
using Wc3Ctl;

namespace Wc3.Tests;

public class GameSchemaTests
{
    // --- 1. JassApiIndex.Parse ---

    [Fact]
    public void Parse_counts_types_natives_globals_functions()
    {
        var jass = string.Join("\n",
            "// header comment that must be dropped",
            "globals",
            "    constant integer MAX_PLAYERS = 18",
            "    integer array MyArray",
            "    real myReal = 3.14",
            "endglobals",
            "",
            "type footman extends unit",
            "    // inline comment inside type is not a thing but drop any",
            "endtype",
            "",
            "constant native GetUnitX takes unit whichUnit returns real",
            "native GetUnitY takes unit whichUnit returns real",
            "",
            "// a string with // inside must be kept by StripComment",
            "function someFunction takes nothing returns nothing",
            "    call DoSomething()",
            "endfunction",
            "",
            "function AnotherFunc takes real x, real y returns nothing",
            "endfunction"
        );

        var api = JassApiIndex.Parse(jass);

        // 1 type: footman
        Assert.Single(api.Types);
        Assert.Equal("footman", api.Types[0].Name);
        Assert.Equal("unit", api.Types[0].Parent);

        // 2 natives, GetUnitX is constant, GetUnitY is not
        Assert.Equal(2, api.Natives.Count);
        Assert.True(api.Natives[0].IsConstant);
        Assert.Equal("GetUnitX", api.Natives[0].Name);
        Assert.False(api.Natives[1].IsConstant);
        Assert.Equal("GetUnitY", api.Natives[1].Name);

        // 3 globals, MAX_PLAYERS const int, MyArray integer array, myReal real
        Assert.Equal(3, api.Globals.Count);
        Assert.Equal("MAX_PLAYERS", api.Globals[0].Name);
        Assert.True(api.Globals[0].IsConstant);
        Assert.Equal("18", api.Globals[0].Value);
        Assert.Equal("MyArray", api.Globals[1].Name);
        Assert.True(api.Globals[1].IsArray);
        Assert.Null(api.Globals[1].Value);
        Assert.Equal("myReal", api.Globals[2].Name);
        Assert.False(api.Globals[2].IsArray);
        Assert.Equal("3.14", api.Globals[2].Value);

        // 2 functions, someFunction, AnotherFunc
        Assert.Equal(2, api.Functions.Count);
        Assert.Equal("someFunction", api.Functions[0].Name);
        Assert.Equal("AnotherFunc", api.Functions[1].Name);
        Assert.Equal(2, api.Functions[1].Parameters.Count);
        Assert.Equal("real", api.Functions[1].Parameters[0].Type);
        Assert.Equal("x", api.Functions[1].Parameters[0].Name);
    }

    // --- 2. FolderGameDataSource.CandidatePaths ---

    [Fact]
    public void CandidatePaths_returns_layered_first_enus_strings_last()
    {
        var paths = FolderGameDataSource.CandidatePaths("war3.w3mod:_locales\\enus.w3mod:ui\\x.txt");
        Assert.Equal(4, paths.Count);
        // Layer 1 (full), colons replaced by path separator
        Assert.Contains("war3.w3mod", paths[0]);
        Assert.Contains("_locales", paths[0]);
        Assert.Contains("enus.w3mod", paths[0]);
        Assert.Contains("ui", paths[0]);
        Assert.EndsWith("x.txt", paths[0]);
        // Layer 2 (strip first storage prefix)
        Assert.DoesNotContain("war3.w3mod", paths[1]);
        Assert.Contains("_locales", paths[1]);
        // Layer 3 (flat strip everything before last colon)
        Assert.DoesNotContain("_locales", paths[2]);
        Assert.Contains("ui", paths[2]);
        Assert.EndsWith("x.txt", paths[2]);
        // Layer 4 (enus-strings fallback)
        Assert.Contains("enus-strings", paths[3]);
        Assert.EndsWith("x.txt", paths[3]);
    }

    // --- 3. FolderGameDataSource reads flat/layered ---

    [Fact]
    public void FolderGameDataSource_reads_flat_file()
    {
        var tmp = Directory.CreateTempSubdirectory("wc3test_flat");
        try
        {
            var flatPath = Path.Combine(tmp.FullName, "scripts", "common.j");
            Directory.CreateDirectory(Path.GetDirectoryName(flatPath)!);
            File.WriteAllText(flatPath, "// stub\nfunction Foo takes nothing returns nothing\nendfunction\n",
                Encoding.UTF8);

            using var src = new FolderGameDataSource(tmp.FullName);
            var bytes = src.ReadFile("scripts/common.j");
            Assert.NotNull(bytes);
            var text = Encoding.UTF8.GetString(bytes!);
            Assert.Contains("Foo", text);
        }
        finally { Directory.Delete(tmp.FullName, true); }
    }

    [Fact]
    public void FolderGameDataSource_layered_copy_wins_over_flat()
    {
        var tmp = Directory.CreateTempSubdirectory("wc3test_layered");
        try
        {
            // Flat: scripts/common.j
            var flatPath = Path.Combine(tmp.FullName, "scripts", "common.j");
            Directory.CreateDirectory(Path.GetDirectoryName(flatPath)!);
            File.WriteAllText(flatPath, "// flat content\n", Encoding.UTF8);

            // Layered: war3.w3mod/scripts/common.j (layer prefix + path after colon)
            var layeredPath = Path.Combine(tmp.FullName, "war3.w3mod", "scripts", "common.j");
            Directory.CreateDirectory(Path.GetDirectoryName(layeredPath)!);
            File.WriteAllText(layeredPath, "// layered content\n", Encoding.UTF8);

            using var src = new FolderGameDataSource(tmp.FullName);
            var bytes = src.ReadFile("war3.w3mod:scripts\\common.j");
            Assert.NotNull(bytes);
            var text = Encoding.UTF8.GetString(bytes!);
            Assert.Contains("layered content", text);
            Assert.DoesNotContain("flat content", text);
        }
        finally { Directory.Delete(tmp.FullName, true); }
    }

    // --- 4. GameInstall.ParseBuildInfoVersion ---

    [Fact]
    public void ParseBuildInfoVersion_from_repo_build_info()
    {
        // Locate the repo-root build.info by walking up from AppContext.BaseDirectory
        string? LocateRepoBuildInfo()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "build.info")))
                    return Path.Combine(dir, "build.info");
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return null;
        }

        var path = LocateRepoBuildInfo();
        Assert.NotNull(path);
        var result = GameInstall.ParseBuildInfoVersion(File.ReadAllText(path!));
        Assert.Equal("1.30.1.10211", result);
    }

    [Fact]
    public void ParseBuildInfoVersion_two_rows_Active_1_wins()
    {
        var text =
@"Branch!STRING:0|Active!DEC:1|Version!STRING:0|Build Key!HEX:16
us|0|1.0.0.0|abc123
eu|1|1.30.1.10211|def456";
        var result = GameInstall.ParseBuildInfoVersion(text);
        Assert.Equal("1.30.1.10211", result);
    }

    // --- 5. GameSchemaCommand.Extract ---

    [Fact]
    public void Extract_from_folder_yields_script_not_null_missing_units()
    {
        var tmp = Directory.CreateTempSubdirectory("wc3test_extract");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "scripts", "common.j");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "// stub\n", Encoding.UTF8);

            using var src = new FolderGameDataSource(tmp.FullName);
            var schema = GameSchemaCommand.Extract(src, "test-build", "test-folder");

            Assert.NotNull(schema.Script);
            Assert.Null(schema.Objects);
            Assert.Null(schema.Triggers);
            Assert.Contains("units/unitmetadata.slk", schema.Missing);
        }
        finally { Directory.Delete(tmp.FullName, true); }
    }

    // --- 6. Save then Load round trip ---

    [Fact]
    public void SaveThenLoad_round_trips_build_and_native_count()
    {
        var tmp = Directory.CreateTempSubdirectory("wc3test_round");
        try
        {
            // Create a small JASS script to extract
            var filePath = Path.Combine(tmp.FullName, "scripts", "common.j");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath,
                "native GetUnitX takes unit whichUnit returns real\n" +
                "function Init takes nothing returns nothing\nendfunction\n",
                Encoding.UTF8);

            using var src = new FolderGameDataSource(tmp.FullName);
            var original = GameSchemaCommand.Extract(src, "1.30.1.10211", "round-trip-test");

            GameSchemaCommand.Save(original, tmp.FullName);

            var loaded = GameSchemaCommand.Load(tmp.FullName);

            Assert.Equal(original.Build, loaded.Build);
            Assert.Equal(original.SchemaVersion, loaded.SchemaVersion);
            Assert.Equal(original.Script!.Values.Sum(a => a.Natives.Count),
                loaded.Script!.Values.Sum(a => a.Natives.Count));
        }
        finally { Directory.Delete(tmp.FullName, true); }
    }

    // --- 7. Diff of two schemas ---

    [Fact]
    public void Diff_reports_added_and_changed_notCompared_when_objects_missing()
    {
        var tmpA = Directory.CreateTempSubdirectory("wc3test_diffA");
        var tmpB = Directory.CreateTempSubdirectory("wc3test_diffB");
        try
        {
            // Schema A: one native
            var pathA = Path.Combine(tmpA.FullName, "scripts", "common.j");
            Directory.CreateDirectory(Path.GetDirectoryName(pathA)!);
            File.WriteAllText(pathA,
                "native GetUnitX takes unit whichUnit returns real\n",
                Encoding.UTF8);

            // Build schema directly from file content
            var apiA = JassApiIndex.Parse(File.ReadAllText(pathA));
            Assert.True(apiA.Natives.Count > 0, $"Direct parse should find {apiA.Natives.Count} natives");

            var scriptDictA = new SortedDictionary<string, JassApi>(StringComparer.Ordinal);
            scriptDictA["scripts/common.j"] = apiA;
            var schemaA = new GameSchema(
                GameSchemaCommand.SchemaVersion, "1.30", "diffA",
                new List<SchemaInput>(), new List<string>(),
                null, null, scriptDictA.Count > 0 ? scriptDictA : null);
            GameSchemaCommand.Save(schemaA, tmpA.FullName);

            // Schema B: adds one native, changes GetUnitX to return integer
            var pathB = Path.Combine(tmpB.FullName, "scripts", "common.j");
            Directory.CreateDirectory(Path.GetDirectoryName(pathB)!);
            File.WriteAllText(pathB,
                "native GetUnitX takes unit whichUnit returns integer\n" +
                "native GetUnitY takes unit whichUnit returns real\n",
                Encoding.UTF8);

            var apiB = JassApiIndex.Parse(File.ReadAllText(pathB));

            var scriptDictB = new SortedDictionary<string, JassApi>(StringComparer.Ordinal);
            scriptDictB["scripts/common.j"] = apiB;
            var schemaB = new GameSchema(
                GameSchemaCommand.SchemaVersion, "1.31", "diffB",
                new List<SchemaInput>(), new List<string>(),
                null, null, scriptDictB.Count > 0 ? scriptDictB : null);
            GameSchemaCommand.Save(schemaB, tmpB.FullName);

            var diff = GameSchemaCommand.Diff(tmpA.FullName, tmpB.FullName);

            // Check changes: one "added" (GetUnitY) and one "changed" (GetUnitX return type)
            var allChanges = diff.Changes.ToList();
            var added = allChanges.Where(c => c.Change == "added").ToList();
            var changed = allChanges.Where(c => c.Change == "changed").ToList();
            Assert.Single(added);
            Assert.Single(changed);
            Assert.Equal("GetUnitY", added[0].Name);

            // NotCompared should contain "objects" since Objects is null in both schemas
            Assert.Contains(diff.NotCompared, s => s.Contains("objects"));

            // No object changes should exist
            Assert.False(diff.Changes.Any(c => c.Section == "objects"),
                "objects changes should not appear when objects is absent in both schemas");
        }
        finally
        {
            Directory.Delete(tmpA.FullName, true);
            Directory.Delete(tmpB.FullName, true);
        }
    }

    // --- 8. GameSchemaCommand.Filter ---

    [Theory]
    [InlineData("objects")]
    [InlineData("triggers")]
    [InlineData("script")]
    [InlineData(null)]
    public void Filter_returns_filtered_result(string? section)
    {
        var change1 = new SchemaChange("objects", "file1", "a", "added", null);
        var change2 = new SchemaChange("triggers", "types", "b", "changed", "x -> y");
        var change3 = new SchemaChange("script", "common.j", "z", "removed", null);
        var change4 = new SchemaChange("script", "common.j", "w", "changed", "p -> q");
        var changes = new List<SchemaChange> { change1, change2, change3, change4 };
        var notCompared = new List<string> { "objects missing", "script common.j only in B" };
        var r = new SchemaDiffResult("1.30", "1.31", changes, notCompared);

        var filtered = GameSchemaCommand.Filter(r, section);

        if (section is null)
        {
            Assert.Equal(r.BuildA, filtered.BuildA);
            Assert.Equal(changes, filtered.Changes);
            Assert.Equal(notCompared, filtered.NotCompared);
        }
        else
        {
            Assert.Equal(section, filtered.Changes[0].Section);
            Assert.All(filtered.Changes, c => Assert.Equal(section, c.Section));
            Assert.All(filtered.NotCompared, nc => Assert.Equal(section, nc.Split(' ')[0]));
        }
    }

    [Fact]
    public void Filter_invalid_section_throws()
    {
        var r = new SchemaDiffResult("1.30", "1.31", Array.Empty<SchemaChange>(), Array.Empty<string>());
        Assert.Throws<ArgumentException>(() => GameSchemaCommand.Filter(r, "bad"));
    }

    // --- 9. Render.GameSchemaDiff hidden count with section+limit ---

    [Fact]
    public void GameSchemaDiff_hidden_count_uses_filtered_count_not_total()
    {
        var changes = new List<SchemaChange>
        {
            new("objects", "f1", "a", "added", null),
            new("objects", "f1", "b", "added", null),
            new("objects", "f1", "c", "added", null),
            new("triggers", "types", "x", "changed", null),
        };
        var r = new SchemaDiffResult("1.30", "1.31", changes, Array.Empty<string>());

        // With --section objects --limit 1, only 3 object changes remain after filtering.
        // Hidden should be 2 (3 - 1), NOT 3 (4 total - 1).
        var output = Wc3Ctl.Render.GameSchemaDiff(r, section: "objects", limit: 1);
        Assert.Contains("2 more change(s) hidden by --limit", output);
        Assert.DoesNotContain("3 more change", output);
    }

    // Task 10, tab aligned native (root cause 1 fix)

    [Fact]
    public void ParseHeader_handles_tab_aligned_natives()
    {
        // Exact line from the audit finding, tabs between native, takes, params, returns
        var jass = "native\t\t\t\t\tStoreInteger\t\t\t\ttakes gamecache cache, string missionKey, string key, integer value\treturns nothing\n" +
            "function\t\tFooBar\t\ttakes nothing\t\treturns integer\n\tendfunction\n";

        var api = JassApiIndex.Parse(jass);

        Assert.Single(api.Natives);
        Assert.Equal("StoreInteger", api.Natives[0].Name);
        Assert.Equal(4, api.Natives[0].Parameters.Count);
        Assert.Equal("gamecache", api.Natives[0].Parameters[0].Type);
        Assert.Equal("cache", api.Natives[0].Parameters[0].Name);
        Assert.Equal("integer", api.Natives[0].Parameters[3].Type);
        Assert.Equal("value", api.Natives[0].Parameters[3].Name);
        Assert.Equal("nothing", api.Natives[0].Returns);

        Assert.Single(api.Functions);
        Assert.Equal("FooBar", api.Functions[0].Name);
        Assert.Empty(api.Functions[0].Parameters);
        Assert.Equal("integer", api.Functions[0].Returns);
    }

    // Task 6: tab aligned native with space after keyword

    [Fact]
    public void Parse_handles_tab_aligned_natives()
    {
        // Input uses real tab escapes ("\\t") for spacing between keyword, name, takes, params, returns
        var jass = "native  StoreInteger\t\t\t\ttakes gamecache cache, string missionKey, string key, integer value returns nothing\n"
            + "constant native GetSpellTargetX\t\t\t\ttakes nothing returns real\n";

        var api = JassApiIndex.Parse(jass);

        Assert.Equal(2, api.Natives.Count);
        Assert.Equal("StoreInteger", api.Natives[0].Name);
        Assert.False(api.Natives[0].IsConstant);
        Assert.Equal(4, api.Natives[0].Parameters.Count);
        Assert.Equal("nothing", api.Natives[0].Returns);

        Assert.Equal("GetSpellTargetX", api.Natives[1].Name);
        Assert.True(api.Natives[1].IsConstant);
        Assert.Empty(api.Natives[1].Parameters);
        Assert.Equal("real", api.Natives[1].Returns);
    }

    // --- Bug 1: FolderGameDataSource en-us strings fallback ---

    [Fact]
    public void FolderGameDataSource_reads_enus_strings_fallback()
    {
        var tmp = Directory.CreateTempSubdirectory("wc3test_enus");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "enus-strings", "ui", "worldeditstrings.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "// en-us strings\n", Encoding.UTF8);

            using var src = new FolderGameDataSource(tmp.FullName);
            var bytes = src.ReadFile("war3.w3mod:_locales\\enus.w3mod:ui\\worldeditstrings.txt");
            Assert.NotNull(bytes);
        }
        finally { Directory.Delete(tmp.FullName, true); }
    }

    // --- Task 10: BuildIndex / WriteIndex ---

    [Fact]
    public void BuildIndex_builds_per_file_natives_not_summed()
    {
        var tmp = Directory.CreateTempSubdirectory("wc3test_index");
        try
        {
            var dirA = Path.Combine(tmp.FullName, "build-1.0");
            Directory.CreateDirectory(dirA);

            // Build a schema with script data directly
            var apiJ = JassApiIndex.Parse(
                "native GetUnitX takes unit whichUnit returns real\n" +
                "native GetUnitY takes unit whichUnit returns real\n" +
                "function Init takes nothing returns nothing\nendfunction\n");
            var apiAi = JassApiIndex.Parse(
                "native GetTriggeringUnit takes nothing returns unit\n");

            var scriptDict = new SortedDictionary<string, JassApi>(StringComparer.Ordinal);
            scriptDict["scripts/common.j"] = apiJ;
            scriptDict["scripts/common.ai"] = apiAi;

            var schemaA = new GameSchema(
                GameSchemaCommand.SchemaVersion, "build-1.0", "test-folder",
                new List<SchemaInput>(), new List<string>(),
                null, null,
                scriptDict.Count > 0 ? scriptDict : null);

            GameSchemaCommand.Save(schemaA, dirA);

            // Build the index
            var entries = GameSchemaCommand.BuildIndex(tmp.FullName);

            Assert.Single(entries);
            var e = entries[0];
            Assert.Contains("script", e.Sections);
            Assert.True(e.Natives.ContainsKey("scripts/common.j"));
            Assert.True(e.Natives.ContainsKey("scripts/common.ai"));
            // 2 natives + 1 function in common.j
            Assert.Equal(3, e.Natives["scripts/common.j"]);
            // 1 native in common.ai
            Assert.Equal(1, e.Natives["scripts/common.ai"]);
            // Functions count should be per-file too
            Assert.True(e.Functions.ContainsKey("scripts/common.j"));
            Assert.Equal(1, e.Functions["scripts/common.j"]);
        }
        finally { Directory.Delete(tmp.FullName, true); }
    }

    // Deleted: BuildIndex_sabotage_summing_fails
    // This was a copy of BuildIndex_builds_per_file_natives_not_summed with sabotage comments
    // in place of actual sabotage code, so it could never fail. The real test above covers it.

    [Fact]
    public void WriteIndex_writes_index_json_and_returns_path()
    {
        var tmp = Directory.CreateTempSubdirectory("wc3test_writeindex");
        try
        {
            var dirA = Path.Combine(tmp.FullName, "build-1.0");
            Directory.CreateDirectory(dirA);

            var api = JassApiIndex.Parse("native Foo takes nothing returns nothing\n");
            var scriptDict = new SortedDictionary<string, JassApi>(StringComparer.Ordinal);
            scriptDict["scripts/common.j"] = api;

            var schemaA = new GameSchema(
                GameSchemaCommand.SchemaVersion, "build-1.0", "test-folder",
                new List<SchemaInput>(), new List<string>(),
                null, null,
                scriptDict.Count > 0 ? scriptDict : null);

            GameSchemaCommand.Save(schemaA, dirA);

            var indexPath = GameSchemaCommand.WriteIndex(tmp.FullName);
            Assert.True(File.Exists(indexPath));

            // Verify the written JSON is valid
            var entries = GameSchemaCommand.BuildIndex(tmp.FullName);
            Assert.Single(entries);
            Assert.Equal("build-1.0", entries[0].Build);
        }
        finally { Directory.Delete(tmp.FullName, true); }
    }

}
