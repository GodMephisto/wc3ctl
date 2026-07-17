// tests/Wc3.Tests/MapInfoCommandTests.cs
using System.Drawing;
using System.Numerics;
using System.Text;
using War3Net.Build.Common;
using War3Net.Build.Info;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Tests;

/// <summary>
/// Hermetic coverage of MapInfoCommand: the war3map.w3i is constructed
/// programmatically (a complete v25 MapInfo), serialized with the same War3Net
/// writer the command uses, and wrapped in an in-memory synthetic MPQ — no
/// corpus map or game install required.
/// </summary>
public class MapInfoCommandTests
{
    /// <summary>Minimal but complete v25 MapInfo: every field the v25 writer emits is
    /// set, so serialization never trips over a null.</summary>
    private static MapInfo BuildInfo()
    {
        var info = new MapInfo(MapInfoFormatVersion.v25)
        {
            MapVersion = 1,
            EditorVersion = (EditorVersion)6072,
            MapName = "Synthetic Test Map",
            MapAuthor = "wc3ctl",
            MapDescription = "A synthetic map for hermetic tests.",
            RecommendedPlayers = "1-2",
            CameraBounds = new Quadrilateral(-2048f, 2048f, 2048f, -2048f),
            CameraBoundsComplements = new RectangleMargins(6, 6, 4, 8),
            PlayableMapAreaWidth = 52,
            PlayableMapAreaHeight = 52,
            MapFlags = MapFlags.MeleeMap | MapFlags.HasMapPropertiesMenuBeenOpened,
            Tileset = Tileset.LordaeronSummer,
            LoadingScreenBackgroundNumber = -1,
            LoadingScreenPath = "",
            LoadingScreenText = "",
            LoadingScreenTitle = "",
            LoadingScreenSubtitle = "",
            GameDataSet = GameDataSet.Default,
            PrologueScreenPath = "",
            PrologueScreenText = "",
            PrologueScreenTitle = "",
            PrologueScreenSubtitle = "",
            FogStyle = FogStyle.Linear,
            FogStartZ = 3000f,
            FogEndZ = 5000f,
            FogDensity = 0.5f,
            FogColor = Color.FromArgb(255, 0, 0, 0),
            GlobalWeather = WeatherType.None,
            SoundEnvironment = "",
            LightEnvironment = Tileset.Unspecified,
            WaterTintingColor = Color.FromArgb(255, 255, 255, 255),
        };
        info.Players.Add(new PlayerData
        {
            Id = 0,
            Controller = PlayerController.User,
            Race = PlayerRace.Human,
            Flags = 0,
            Name = "Player 1",
            StartPosition = new Vector2(0f, 0f),
            AllyLowPriorityFlags = new Bitmask32(0),
            AllyHighPriorityFlags = new Bitmask32(0),
            EnemyLowPriorityFlags = new Bitmask32(0),
            EnemyHighPriorityFlags = new Bitmask32(0),
        });
        info.Forces.Add(new ForceData { Flags = 0, Players = new Bitmask32(-1), Name = "Force 1" });
        return info;
    }

    private static MapDocument LoadSynthetic(MapInfo info, IDictionary<string, byte[]>? extraFiles = null)
    {
        var files = new Dictionary<string, byte[]> { ["war3map.w3i"] = MapInfoCommand.Serialize(info) };
        foreach (var (name, bytes) in extraFiles ?? new Dictionary<string, byte[]>())
            files[name] = bytes;
        return MapDocument.Load(SyntheticMap.Build(files));
    }

    [Fact]
    public void Read_returns_the_w3i_fields()
    {
        var fields = MapInfoCommand.Read(LoadSynthetic(BuildInfo()));

        Assert.Equal("Synthetic Test Map", fields.MapName);
        Assert.Equal("wc3ctl", fields.Author);
        Assert.Equal("A synthetic map for hermetic tests.", fields.Description);
        Assert.Equal("1-2", fields.RecommendedPlayers);
        Assert.Equal(52, fields.PlayableWidth);
        Assert.Equal(52, fields.PlayableHeight);
        Assert.Equal(1, fields.Players);
    }

    [Fact]
    public void Set_map_name_round_trips_through_save_and_reload()
    {
        var doc = LoadSynthetic(BuildInfo());
        MapInfoCommand.Set(doc, "MapName", "Renamed Map");

        var reloaded = MapDocument.Load(doc.SaveToBytes());
        var fields = MapInfoCommand.Read(reloaded);

        Assert.Equal("Renamed Map", fields.MapName);
        // Every unrelated field survives the rename untouched.
        Assert.Equal("wc3ctl", fields.Author);
        Assert.Equal("A synthetic map for hermetic tests.", fields.Description);
        Assert.Equal("1-2", fields.RecommendedPlayers);
        Assert.Equal(52, fields.PlayableWidth);
        Assert.Equal(52, fields.PlayableHeight);
        Assert.Equal(1, fields.Players);
    }

    [Fact]
    public void Set_is_visible_in_memory_without_a_save()
    {
        var doc = LoadSynthetic(BuildInfo());

        var returned = MapInfoCommand.Set(doc, "Author", "someone else");

        Assert.Equal("someone else", returned.Author);
        // The entry's Model is restored after the raw write-back, so a fresh Read
        // (and InfoCommand) sees the mutation immediately.
        Assert.Equal("someone else", MapInfoCommand.Read(doc).Author);
        Assert.Equal("Synthetic Test Map", MapInfoCommand.Read(doc).MapName);
    }

    [Fact]
    public void Field_names_match_case_insensitively()
    {
        var doc = LoadSynthetic(BuildInfo());
        MapInfoCommand.Set(doc, "recommendedplayers", "2-4");
        Assert.Equal("2-4", MapInfoCommand.Read(doc).RecommendedPlayers);
    }

    /// <summary>The fidelity invariant behind "untouched fields preserved": parsing a
    /// w3i through MapDocument and re-serializing it UNCHANGED reproduces the original
    /// bytes exactly, so a field edit can only ever affect that field's encoding.</summary>
    [Fact]
    public void Unchanged_reserialize_is_byte_identical()
    {
        var original = MapInfoCommand.Serialize(BuildInfo());
        var doc = MapDocument.Load(SyntheticMap.Build(
            new Dictionary<string, byte[]> { ["war3map.w3i"] = original }));

        var reparsed = Assert.IsType<MapInfo>(doc.GetFile("war3map.w3i")!.Model);

        Assert.Equal(original, MapInfoCommand.Serialize(reparsed));
    }

    [Fact]
    public void Set_leaves_other_archive_files_untouched()
    {
        var script = Encoding.UTF8.GetBytes(
            "function main takes nothing returns nothing\nendfunction\n");
        var doc = LoadSynthetic(BuildInfo(),
            new Dictionary<string, byte[]> { ["war3map.j"] = script });

        MapInfoCommand.Set(doc, "Description", "changed description");
        var reloaded = MapDocument.Load(doc.SaveToBytes());

        Assert.Equal(script, reloaded.GetFile("war3map.j")!.RawBytes);
        Assert.Equal("changed description", MapInfoCommand.Read(reloaded).Description);
    }

    [Fact]
    public void Set_rejects_unknown_and_read_only_fields()
    {
        var doc = LoadSynthetic(BuildInfo());
        Assert.Throws<ArgumentException>(() => MapInfoCommand.Set(doc, "PlayableWidth", "10"));
        Assert.Throws<ArgumentException>(() => MapInfoCommand.Set(doc, "NoSuchField", "x"));
        // Rejection happens before any mutation or write-back.
        Assert.Equal("Synthetic Test Map", MapInfoCommand.Read(doc).MapName);
    }

    [Fact]
    public void Missing_or_unparseable_w3i_throws()
    {
        var noW3i = MapDocument.Load(SyntheticMap.Build(
            new Dictionary<string, byte[]> { ["readme.txt"] = new byte[] { 9 } }));
        Assert.Throws<InvalidOperationException>(() => MapInfoCommand.Read(noW3i));
        Assert.Throws<InvalidOperationException>(() => MapInfoCommand.Set(noW3i, "MapName", "x"));

        // Garbage w3i: load degrades to raw (Model == null) and the command refuses.
        var badW3i = MapDocument.Load(SyntheticMap.Build(
            new Dictionary<string, byte[]> { ["war3map.w3i"] = new byte[] { 1, 2, 3 } }));
        Assert.Throws<InvalidOperationException>(() => MapInfoCommand.Read(badW3i));
    }
}
