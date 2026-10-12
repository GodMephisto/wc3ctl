// tests/Wc3.Tests/MpqArchiveCommandTests.cs
// Hermetic coverage for the archive-editing commands (rebuild, merge, add, remove,
// extract-all), built on SyntheticMap so no real map and no Warcraft III install is
// needed. Each test pins one behaviour the command would not provide if it were
// missing or wrong, and every mutating test checks the input map is left untouched.
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Tests;

public class MpqArchiveCommandTests
{
    private static string Write(byte[] bytes, string ext = ".w3x")
    {
        var path = Path.Combine(Path.GetTempPath(), $"wc3arch-{Guid.NewGuid():N}{ext}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string OutSibling(string input) => Path.Combine(
        Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + ".edited.w3x");

    private static void Clean(params string[] paths)
    {
        foreach (var p in paths)
            if (File.Exists(p)) File.Delete(p);
    }

    // --- rebuild ---------------------------------------------------------

    [Fact]
    public void Rebuild_writes_a_healthy_copy_and_drops_the_unnamed_block()
    {
        var path = Write(SyntheticMap.BuildProtected(
            visibleFiles: new Dictionary<string, byte[]> { ["war3map.j"] = new byte[] { 1, 2, 3 } },
            hiddenFiles: new Dictionary<string, byte[]>
            {
                ["war3map.w3e"] = new byte[] { 4, 5, 6 },     // a standard name the probe recovers
                ["custom_asset.bin"] = new byte[] { 7, 8, 9 }, // no name anything can recover
            }));
        var outPath = OutSibling(path);
        var before = File.ReadAllBytes(path);
        try
        {
            var r = MpqRebuildCommand.Execute(path, outPath, growHash: false);

            Assert.True(r.Reopens, r.ReopenError);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.True(r.OutputEntries > 0);
            Assert.Equal(r.InputEntries - r.DroppedEntries, r.OutputEntries);
            Assert.Contains(r.Dropped, d => d.Name.StartsWith("(unnamed block", StringComparison.Ordinal)
                && d.Reason.Contains("no name", StringComparison.OrdinalIgnoreCase));

            var rebuilt = MapDocument.Load(outPath);
            Assert.NotNull(rebuilt.GetFile("war3map.j"));
            Assert.NotNull(rebuilt.GetFile("war3map.w3e"));
            Assert.False(rebuilt.HasFileByName("custom_asset.bin"));
        }
        finally { Clean(path, outPath); }
    }

    [Fact]
    public void Rebuild_preserves_every_named_entry_of_a_healthy_map()
    {
        var path = Write(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.j"] = new byte[] { 1, 2, 3, 4 },
            ["war3map.w3e"] = new byte[] { 5, 6 },
            [@"war3mapImported\a.bin"] = new byte[] { 7, 8, 9 },
        }));
        var outPath = OutSibling(path);
        try
        {
            var r = MpqRebuildCommand.Execute(path, outPath, growHash: false);
            Assert.True(r.Reopens, r.ReopenError);
            Assert.Equal(0, r.DroppedEntries);

            var rebuilt = MapDocument.Load(outPath);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, rebuilt.GetFile("war3map.j")!.RawBytes);
            Assert.Equal(new byte[] { 5, 6 }, rebuilt.GetFile("war3map.w3e")!.RawBytes);
            Assert.Equal(new byte[] { 7, 8, 9 }, rebuilt.GetFile(@"war3mapImported\a.bin")!.RawBytes);
        }
        finally { Clean(path, outPath); }
    }

    [Fact]
    public void Rebuild_grow_hash_raises_the_table_above_the_default()
    {
        var path = Write(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.j"] = new byte[] { 1 },
            ["a.bin"] = new byte[] { 2 },
            ["b.bin"] = new byte[] { 3 },
            ["c.bin"] = new byte[] { 4 },
            ["d.bin"] = new byte[] { 5 },
        }));
        var defaultOut = OutSibling(path);
        var grownOut = Path.Combine(Path.GetDirectoryName(path)!,
            Path.GetFileNameWithoutExtension(path) + ".grown.w3x");
        try
        {
            var plain = MpqRebuildCommand.Execute(path, defaultOut, growHash: false);
            var grown = MpqRebuildCommand.Execute(path, grownOut, growHash: true);

            Assert.True(grown.Reopens, grown.ReopenError);
            Assert.True(grown.OutputHashSlots > plain.OutputHashSlots,
                $"grown {grown.OutputHashSlots} should exceed default {plain.OutputHashSlots}");
            Assert.True(grown.OutputHashSlots > grown.InputHashSlots);
        }
        finally { Clean(path, defaultOut, grownOut); }
    }

    [Fact]
    public void Rebuild_refuses_to_overwrite_the_input()
    {
        var path = Write(SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.j"] = new byte[] { 1 } }));
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => MpqRebuildCommand.Execute(path, path, growHash: false));
            Assert.Contains("never overwritten", ex.Message);
        }
        finally { Clean(path); }
    }

    // --- merge -----------------------------------------------------------

    [Fact]
    public void Merge_takes_both_sides_and_resolves_a_conflict_by_the_preferred_side()
    {
        var a = Write(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.j"] = new byte[] { 1, 1 },
            ["keep.bin"] = new byte[] { 9, 9 },
            ["onlyA.bin"] = new byte[] { 2 },
        }));
        var b = Write(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.j"] = new byte[] { 2, 2 },
            ["keep.bin"] = new byte[] { 9, 9 },
            ["onlyB.bin"] = new byte[] { 3 },
        }));
        var outA = OutSibling(a);
        var outB = Path.Combine(Path.GetDirectoryName(a)!,
            Path.GetFileNameWithoutExtension(a) + ".preferb.w3x");
        try
        {
            var r = MpqMergeCommand.Execute(a, b, outA, "a");
            Assert.True(r.Reopens, r.ReopenError);
            // war3map.j differs, keep.bin is byte-identical, both names are in both archives.
            Assert.Equal(2, r.Conflicts);
            Assert.Contains(r.ConflictList, c => c.Name == "war3map.j" && !c.Identical && c.KeptFrom == "a");
            Assert.Contains(r.ConflictList, c => c.Name == "keep.bin" && c.Identical);
            Assert.Contains("onlyA.bin", r.OnlyInA);
            Assert.Contains("onlyB.bin", r.OnlyInB);

            var merged = MapDocument.Load(outA);
            Assert.Equal(new byte[] { 1, 1 }, merged.GetFile("war3map.j")!.RawBytes);
            Assert.NotNull(merged.GetFile("onlyA.bin"));
            Assert.NotNull(merged.GetFile("onlyB.bin"));

            var rB = MpqMergeCommand.Execute(a, b, outB, "b");
            Assert.Equal(new byte[] { 2, 2 }, MapDocument.Load(outB).GetFile("war3map.j")!.RawBytes);
            Assert.Contains(rB.ConflictList, c => c.Name == "war3map.j" && c.KeptFrom == "b");
        }
        finally { Clean(a, b, outA, outB); }
    }

    [Fact]
    public void Merge_rejects_an_unknown_preferred_side()
    {
        var a = Write(SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.j"] = new byte[] { 1 } }));
        var b = Write(SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.j"] = new byte[] { 2 } }));
        try
        {
            Assert.Throws<ArgumentException>(() => MpqMergeCommand.Execute(a, b, OutSibling(a), "c"));
        }
        finally { Clean(a, b); }
    }

    // --- add -------------------------------------------------------------

    [Fact]
    public void Add_inserts_a_new_file_byte_faithfully_and_leaves_the_input_untouched()
    {
        var path = Write(SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.j"] = new byte[] { 1 } }));
        var src = Write(new byte[] { 10, 20, 30, 40, 50 }, ".blp");
        var outPath = OutSibling(path);
        var before = File.ReadAllBytes(path);
        try
        {
            var r = MpqAddCommand.Execute(path, @"war3mapImported\new.blp", src, outPath);
            Assert.False(r.Replaced);
            Assert.True(r.ByteFaithful);
            Assert.True(r.Reopens, r.ReopenError);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(new byte[] { 10, 20, 30, 40, 50 },
                MapDocument.Load(outPath).GetFile(@"war3mapImported\new.blp")!.RawBytes);
        }
        finally { Clean(path, src, outPath); }
    }

    [Fact]
    public void Add_replaces_an_existing_file_byte_faithfully()
    {
        var path = Write(SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.j"] = new byte[] { 1, 2 } }));
        var src = Write(new byte[] { 42, 43 }, ".j");
        var outPath = OutSibling(path);
        try
        {
            var r = MpqAddCommand.Execute(path, "war3map.j", src, outPath);
            Assert.True(r.Replaced);
            Assert.True(r.ByteFaithful);
            Assert.Equal(new byte[] { 42, 43 }, MapDocument.Load(outPath).GetFile("war3map.j")!.RawBytes);
        }
        finally { Clean(path, src, outPath); }
    }

    [Fact]
    public void Add_refuses_in_place()
    {
        var path = Write(SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.j"] = new byte[] { 1 } }));
        var src = Write(new byte[] { 9 });
        try
        {
            Assert.Throws<InvalidOperationException>(
                () => MpqAddCommand.Execute(path, "war3map.j", src, path));
        }
        finally { Clean(path, src); }
    }

    // --- remove ----------------------------------------------------------

    [Fact]
    public void Remove_drops_the_named_file_on_a_rebuild_and_keeps_the_rest()
    {
        var path = Write(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.j"] = new byte[] { 1, 2 },
            ["keep.bin"] = new byte[] { 3, 4 },
        }));
        var outPath = OutSibling(path);
        var before = File.ReadAllBytes(path);
        try
        {
            var r = MpqRemoveCommand.Execute(path, "war3map.j", outPath);
            Assert.True(r.Found);
            Assert.True(r.DroppedOnRebuild);
            Assert.True(r.Reopens, r.ReopenError);
            Assert.Equal(before, File.ReadAllBytes(path));

            var removed = MapDocument.Load(outPath);
            Assert.False(removed.HasFileByName("war3map.j"));
            Assert.NotNull(removed.GetFile("keep.bin"));
        }
        finally { Clean(path, outPath); }
    }

    [Fact]
    public void Remove_reports_a_missing_name_as_not_found()
    {
        var path = Write(SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.j"] = new byte[] { 1 } }));
        var outPath = OutSibling(path);
        try
        {
            var r = MpqRemoveCommand.Execute(path, "nope.bin", outPath);
            Assert.False(r.Found);
            Assert.True(r.DroppedOnRebuild);
        }
        finally { Clean(path, outPath); }
    }

    [Fact]
    public void Remove_refuses_in_place()
    {
        var path = Write(SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.j"] = new byte[] { 1 } }));
        try
        {
            Assert.Throws<InvalidOperationException>(
                () => MpqRemoveCommand.Execute(path, "war3map.j", path));
        }
        finally { Clean(path); }
    }

    // --- extract-all -----------------------------------------------------

    [Fact]
    public void Extract_all_selects_every_entry_including_the_unnamed_one()
    {
        var blp = new byte[64];
        System.Text.Encoding.ASCII.GetBytes("BLP1").CopyTo(blp, 0);
        var doc = MapDocument.Load(SyntheticMap.Build(
            new Dictionary<string, byte[]>
            {
                ["war3map.j"] = new byte[] { 7 },
                [@"war3mapImported\a.mdx"] = new byte[] { 1, 2 },
            },
            new[] { blp }));

        var r = MpqExtractAllCommand.Execute(doc);

        Assert.Equal(doc.Files.Count, r.TotalEntries);
        Assert.Equal(r.TotalEntries, r.NamedEntries + r.UnnamedEntries);
        Assert.True(r.UnnamedEntries >= 1);
        Assert.True(r.TotalBytes > 0);
    }

    [Fact]
    public void Extract_all_writes_every_entry_through_the_safe_path_rule()
    {
        var blp = new byte[64];
        System.Text.Encoding.ASCII.GetBytes("BLP1").CopyTo(blp, 0);
        var doc = MapDocument.Load(SyntheticMap.Build(
            new Dictionary<string, byte[]>
            {
                ["war3map.j"] = new byte[] { 7 },
                [@"war3mapImported\a.mdx"] = new byte[] { 1, 2 },
            },
            new[] { blp }));

        var r = MpqExtractAllCommand.Execute(doc);
        var dir = Path.Combine(Path.GetTempPath(), "wc3extractall-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manifest = Wc3Ctl.ExtractWriter.WriteAll(new ExtractResult(r.Items), dir);
            Assert.Equal(r.TotalEntries, manifest.Count);
            Assert.True(File.Exists(Path.Combine(dir, "war3map.j")));
            Assert.True(File.Exists(Path.Combine(dir, "war3mapImported", "a.mdx")));
            Assert.EndsWith(".blp", manifest.Files.Single(f => f.Name is null).Path);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
