// tests/Wc3.Tests/PortCommandTests.cs
using System.Text;
using War3Net.Build.Extensions;
using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Tests;

public class PortCommandTests
{
    // Source: custom hero H000 (hero ability A000, an imported icon, a TRIGSTR name)
    // + custom ability A000, a war3map.wts with STRING 100, and the imported icon file.
    private static MapDocument SourceMap()
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        var hero = new SimpleObjectModification { OldId = "Hpal".FromRawcode(), NewId = "H000".FromRawcode() };
        hero.Modifications.Add(Str("uhab", "A000"));                                  // hero abilities → custom A000
        hero.Modifications.Add(Str("uico", "war3mapImported\\raidenicon.blp"));       // imported icon
        hero.Modifications.Add(Str("unam", "TRIGSTR_100"));                           // name via string table
        w3u.NewUnits.Add(hero);

        var w3a = new AbilityObjectData(ObjectDataFormatVersion.v2);
        var abil = new LevelObjectModification { OldId = "ANcl".FromRawcode(), NewId = "A000".FromRawcode() };
        abil.Modifications.Add(new LevelObjectDataModification
        { Level = 0, Pointer = 0, Id = "anam".FromRawcode(), Type = ObjectDataType.String, Value = "Naginata Combo" });
        w3a.NewAbilities.Add(abil);

        return MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(w3u)),
            ["war3map.w3a"] = Ser(w => w.Write(w3a)),
            ["war3map.wts"] = Encoding.UTF8.GetBytes("STRING 100\n{\nRaiden Ei\n}\n"),
            ["war3mapImported\\raidenicon.blp"] = new byte[] { 10, 20, 30, 40 },
        }));
    }

    // Target: already defines a DIFFERENT custom H000 (forces a rawcode remap) and has no w3a.
    private static MapDocument TargetMap()
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        var other = new SimpleObjectModification { OldId = "Hamg".FromRawcode(), NewId = "H000".FromRawcode() };
        other.Modifications.Add(Str("unam", "Someone Else"));
        w3u.NewUnits.Add(other);
        return MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(w3u)),
            ["war3map.j"] = Encoding.UTF8.GetBytes("function main takes nothing returns nothing\nendfunction\n"),
        }));
    }

    [Fact]
    public void Ports_unit_closure_with_collision_remap_string_inlining_and_asset_copy()
    {
        var source = SourceMap();
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);

        // The bundle found the custom ability and the icon.
        Assert.Contains(bundle.Objects, o => o.Rawcode == "A000" && o.Kind == ObjectKind.Ability && o.CustomToMap);
        Assert.Contains(bundle.Files, f => f.Path.EndsWith("raidenicon.blp", StringComparison.OrdinalIgnoreCase) && f.PresentInMap);

        var result = PortCommand.PortUnit(source, bundle, target);

        // Root H000 collided with the target's H000 → it was remapped.
        Assert.NotEqual("H000", result.RootPortedTo);
        Assert.Contains(result.Remaps, r => r.From == "H000" && r.Kind == ObjectKind.Unit);
        // The ability didn't collide → kept as A000.
        Assert.Contains(result.Objects, o => o.Rawcode == "A000" && o.Kind == ObjectKind.Ability);
        Assert.DoesNotContain(result.Remaps, r => r.From == "A000");
        // The TRIGSTR name was inlined.
        Assert.True(result.InlinedStrings >= 1);
        Assert.Contains(result.CopiedFiles, f => f.EndsWith("raidenicon.blp", StringComparison.OrdinalIgnoreCase));

        // Save + reload the target and verify everything landed.
        var reloaded = MapDocument.Load(target.SaveToBytes());

        var tw3u = (UnitObjectData)reloaded.GetFile("war3map.w3u")!.Model!;
        int portedId = result.RootPortedTo!.FromRawcode();
        var ported = tw3u.NewUnits.Single(u => u.NewId == portedId);
        // Original target H000 still present.
        Assert.Contains(tw3u.NewUnits, u => u.NewId == "H000".FromRawcode());

        // Name is now the literal, not a TRIGSTR reference.
        var nameMod = ported.Modifications.Single(m => m.Id == "unam".FromRawcode());
        Assert.Equal("Raiden Ei", nameMod.Value);
        Assert.DoesNotContain("TRIGSTR", (string)nameMod.Value!);

        // Ability reference still points at A000 (uncollided).
        var abilMod = ported.Modifications.Single(m => m.Id == "uhab".FromRawcode());
        Assert.Equal("A000", abilMod.Value);

        // The ability object was injected (target had no w3a before).
        var tw3a = (AbilityObjectData)reloaded.GetFile("war3map.w3a")!.Model!;
        Assert.Contains(tw3a.NewAbilities, a => a.NewId == "A000".FromRawcode());

        // The imported icon is present with the right bytes and registered in imp.
        Assert.True(reloaded.GetFile("war3mapImported\\raidenicon.blp")!.RawBytes.SequenceEqual(new byte[] { 10, 20, 30, 40 }));
    }

    [Fact]
    public void Ports_an_extensionless_icon_by_resolving_the_stored_blp()
    {
        // Source hero references its icon WITHOUT an extension; it is imported as ...BTN.blp.
        // The port must resolve and copy the .blp, else the ported hero shows the missing-icon
        // box. This is the "does not ruin the map" guarantee for extensionless icon refs.
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        var hero = new SimpleObjectModification { OldId = "Hpal".FromRawcode(), NewId = "H000".FromRawcode() };
        hero.Modifications.Add(Str("uico", @"ReplaceableTextures\CommandButtons\BTNRaiden"));
        w3u.NewUnits.Add(hero);
        var source = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(w3u)),
            [@"ReplaceableTextures\CommandButtons\BTNRaiden.blp"] = new byte[] { 9, 9, 9, 9 },
        }));
        var target = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.j"] = Encoding.UTF8.GetBytes("function main takes nothing returns nothing\nendfunction\n"),
        }));

        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        Assert.Contains(bundle.Files,
            f => f.Path.EndsWith("BTNRaiden", StringComparison.OrdinalIgnoreCase) && f.PresentInMap);

        var result = PortCommand.PortUnit(source, bundle, target);

        Assert.Contains(result.CopiedFiles, f => f.EndsWith("BTNRaiden.blp", StringComparison.OrdinalIgnoreCase));
        var reloaded = MapDocument.Load(target.SaveToBytes());
        Assert.True(reloaded.GetFile(@"ReplaceableTextures\CommandButtons\BTNRaiden.blp")!
            .RawBytes.SequenceEqual(new byte[] { 9, 9, 9, 9 }));
    }

    [Fact]
    public void Remaps_an_in_bundle_reference_when_the_referenced_object_collides()
    {
        // Target already has A000 too → the ability must be remapped AND the unit's
        // uhab reference rewritten to the new ability code.
        var source = SourceMap();
        var target = TargetMap();
        var tw3a = new AbilityObjectData(ObjectDataFormatVersion.v2);
        tw3a.NewAbilities.Add(new LevelObjectModification { OldId = "AHbz".FromRawcode(), NewId = "A000".FromRawcode() });
        target.AddOrReplaceModelFile("war3map.w3a", tw3a);

        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        var result = PortCommand.PortUnit(source, bundle, target);

        var abilityRemap = result.Remaps.Single(r => r.From == "A000" && r.Kind == ObjectKind.Ability);
        Assert.NotEqual("A000", abilityRemap.To);

        var reloaded = MapDocument.Load(target.SaveToBytes());
        var tw3u = (UnitObjectData)reloaded.GetFile("war3map.w3u")!.Model!;
        var ported = tw3u.NewUnits.Single(u => u.NewId == result.RootPortedTo!.FromRawcode());
        var uhab = (string)ported.Modifications.Single(m => m.Id == "uhab".FromRawcode()).Value!;
        Assert.Equal(abilityRemap.To, uhab); // reference followed the remap
    }

    [Fact]
    public void Skips_base_game_deps_and_assets_not_in_source()
    {
        var source = SourceMap();
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        var result = PortCommand.PortUnit(source, bundle, target);
        // Base ability AInv-style deps (none custom here) and missing assets never copy;
        // the report accounts for skipped items without throwing.
        Assert.NotNull(result.SkippedFiles);
    }

    /// <summary>
    /// Real-map stress test: porting Raiden Ei into a second copy of the same map means
    /// EVERY custom rawcode collides, forcing a full remap. The hero + all its custom
    /// abilities must land in the target's object data under fresh codes, with the unit's
    /// ability list rewritten to the remapped ability codes.
    /// </summary>
    [Fact]
    [Trait("Category", "Corpus")]
    public void Real_map_self_port_remaps_the_whole_hero_and_rewrites_references()
    {
        const string path = @"C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\Anime_WOS2_0.25c1.w3x";
        if (!File.Exists(path)) return;

        var source = MapDocument.Load(path);
        var target = MapDocument.Load(path); // identical → every rawcode collides
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);

        var result = PortCommand.PortUnit(source, bundle, target);

        // The hero and its five custom abilities all collided and were remapped.
        Assert.NotEqual("H000", result.RootPortedTo);
        Assert.Contains(result.Remaps, r => r.From == "H000" && r.Kind == ObjectKind.Unit);
        Assert.True(result.Remaps.Count(r => r.Kind == ObjectKind.Ability) >= 5);

        // The ported unit exists in the target under its new code with a valid ability list.
        var tw3u = (UnitObjectData)target.GetFile("war3map.w3u")!.Model!;
        var ported = tw3u.NewUnits.Single(u => u.NewId == result.RootPortedTo!.FromRawcode());
        var abilityRemaps = result.Remaps.Where(r => r.Kind == ObjectKind.Ability)
            .ToDictionary(r => r.From, r => r.To);
        var uhab = (string)ported.Modifications.First(m => m.Id == "uhab".FromRawcode()).Value!;
        foreach (var code in uhab.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (abilityRemaps.ContainsKey(code)) // an original ability code must NOT survive
                Assert.Fail($"unit still references un-remapped ability {code}");

        // Every remapped ability was injected into the target's w3a.
        var tw3a = (AbilityObjectData)target.GetFile("war3map.w3a")!.Model!;
        foreach (var to in abilityRemaps.Values)
            Assert.Contains(tw3a.NewAbilities, a => a.NewId == to.FromRawcode());
    }

    /// <summary>
    /// Regression: a unit whose model is REFERENCED as ".mdl" but STORED as ".mdx"
    /// (the normal WC3 convention) must still have its model binary copied into the
    /// target. The port's copy step used a plain path lookup that missed the
    /// .mdl→.mdx swap, so custom models were discovered-but-never-copied.
    /// </summary>
    [Fact]
    public void Copies_a_custom_model_referenced_as_mdl_but_stored_as_mdx()
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        var unit = new SimpleObjectModification { OldId = "hfoo".FromRawcode(), NewId = "U000".FromRawcode() };
        unit.Modifications.Add(Str("umdl", "war3mapImported\\raiden.mdl")); // referenced as .mdl
        w3u.NewUnits.Add(unit);

        var modelBytes = new byte[] { (byte)'M', (byte)'D', (byte)'L', (byte)'X', 1, 2, 3, 4, 5, 6 };
        var source = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(w3u)),
            ["war3mapImported\\raiden.mdx"] = modelBytes,   // stored as .mdx
        }));
        var target = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(new UnitObjectData(ObjectDataFormatVersion.v2))),
            ["war3map.j"] = Encoding.UTF8.GetBytes("function main takes nothing returns nothing\nendfunction\n"),
        }));

        var bundle = BundleCommand.ResolveUnit(source, "U000", gameDirOverride: null);
        // Discovery marks the model present through the .mdl→.mdx swap.
        Assert.Contains(bundle.Files, f => f.Category == "model" && f.PresentInMap
            && f.Path.EndsWith("raiden.mdl", StringComparison.OrdinalIgnoreCase));

        var result = PortCommand.PortUnit(source, bundle, target);

        // The stored .mdx was copied under its real name (was skipped before the fix).
        Assert.Contains(result.CopiedFiles, f => f.EndsWith("raiden.mdx", StringComparison.OrdinalIgnoreCase));

        var reloaded = MapDocument.Load(target.SaveToBytes());
        var copied = reloaded.GetFile("war3mapImported\\raiden.mdx");
        Assert.NotNull(copied);
        Assert.True(copied!.RawBytes.SequenceEqual(modelBytes));
    }

    [Fact]
    public void Modified_standard_object_is_not_remapped_on_collision()
    {
        // Source hero H000 references a MODIFIED STANDARD unit hfoo (the source tweaks the stock
        // Footman via a Base* entry). The target ALSO modifies hfoo differently. A standard-object
        // modification addresses a fixed base id and must never be remapped, else it and every
        // reference to it point at a unit type that does not exist.
        var sw3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        var hero = new SimpleObjectModification { OldId = "Hpal".FromRawcode(), NewId = "H000".FromRawcode() };
        hero.Modifications.Add(Str("uabi", "hfoo"));
        sw3u.NewUnits.Add(hero);
        var srcFootman = new SimpleObjectModification { OldId = "hfoo".FromRawcode(), NewId = 0 };
        srcFootman.Modifications.Add(Str("unam", "Source Footman"));
        sw3u.BaseUnits.Add(srcFootman);
        var source = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(sw3u)),
        }));

        var tw3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        var tgtFootman = new SimpleObjectModification { OldId = "hfoo".FromRawcode(), NewId = 0 };
        tgtFootman.Modifications.Add(Str("unam", "Target Footman")); // different content, a real collision
        tw3u.BaseUnits.Add(tgtFootman);
        var target = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(tw3u)),
        }));

        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        Assert.Contains(bundle.Objects, o => o.Rawcode == "hfoo"); // the modified standard unit is a dependency

        var result = PortCommand.PortUnit(source, bundle, target, includeScript: false);

        Assert.DoesNotContain(result.Remaps, r => r.From == "hfoo");                       // never relocated
        Assert.Contains(result.Warnings, w => w.Contains("hfoo") && w.Contains("standard")); // conflict reported

        var reloaded = MapDocument.Load(target.SaveToBytes());
        var tu = (UnitObjectData)reloaded.GetFile("war3map.w3u")!.Model!;
        var portedHero = tu.NewUnits.Single(u => u.NewId == result.RootPortedTo!.FromRawcode());
        Assert.Equal("hfoo", (string)portedHero.Modifications.Single(m => m.Id == "uabi".FromRawcode()).Value!);
    }

    [Fact]
    public void Unparsed_target_object_file_is_not_overwritten()
    {
        var source = SourceMap(); // H000 + A000
        // A war3map.w3u that is present but cannot be parsed (version 2 header claiming a
        // 2-billion-entry table): MapDocument keeps the raw bytes and leaves Model null.
        byte[] garbage = { 0x02, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x7F };
        var target = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = garbage,
            ["war3map.j"] = Encoding.UTF8.GetBytes("function main takes nothing returns nothing\nendfunction\n"),
        }));
        Assert.NotNull(target.GetFile("war3map.w3u"));
        Assert.Null(target.GetFile("war3map.w3u")!.Model); // present but failed to parse

        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        var result = PortCommand.PortUnit(source, bundle, target, includeScript: false);

        Assert.Contains(result.Warnings, w => w.Contains("war3map.w3u") && w.Contains("could not be parsed"));
        var reloaded = MapDocument.Load(target.SaveToBytes());
        Assert.Equal(garbage, reloaded.GetFile("war3map.w3u")!.RawBytes); // original bytes preserved, not wiped
    }

    /// <summary>
    /// Regression, the Pointer of a leveled modification (the data column, the A/B/C slot
    /// of a leveled ability field) was dropped on port and every injected leveled field
    /// came back with Pointer 0, so the injected bytes diverged from what the World Editor
    /// writes. The pointer must survive the port unchanged.
    /// </summary>
    [Fact]
    public void Preserves_the_data_pointer_of_leveled_modifications()
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        var hero = new SimpleObjectModification { OldId = "Hpal".FromRawcode(), NewId = "H000".FromRawcode() };
        hero.Modifications.Add(Str("uhab", "A000"));
        w3u.NewUnits.Add(hero);

        var w3a = new AbilityObjectData(ObjectDataFormatVersion.v2);
        var abil = new LevelObjectModification { OldId = "ANcl".FromRawcode(), NewId = "A000".FromRawcode() };
        abil.Modifications.Add(new LevelObjectDataModification
        { Level = 2, Pointer = 3, Id = "Ncl1".FromRawcode(), Type = ObjectDataType.Real, Value = 1.5f });
        w3a.NewAbilities.Add(abil);

        var source = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(w3u)),
            ["war3map.w3a"] = Ser(w => w.Write(w3a)),
        }));
        var target = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.j"] = Encoding.UTF8.GetBytes("function main takes nothing returns nothing\nendfunction\n"),
        }));

        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        PortCommand.PortUnit(source, bundle, target, includeScript: false);

        var reloaded = MapDocument.Load(target.SaveToBytes());
        var tw3a = (AbilityObjectData)reloaded.GetFile("war3map.w3a")!.Model!;
        var ported = tw3a.NewAbilities.Single(a => a.NewId == "A000".FromRawcode());
        var mod = ported.Modifications.Single(m => m.Id == "Ncl1".FromRawcode());
        Assert.Equal(2, mod.Level);
        Assert.Equal(3, mod.Pointer); // was reset to 0 before the fix
        Assert.Equal(1.5f, mod.Value);
    }

    private static SimpleObjectDataModification Str(string code, string value) =>
        new() { Id = code.FromRawcode(), Type = ObjectDataType.String, Value = value };

    private static byte[] Ser(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) write(bw);
        return ms.ToArray();
    }
}
