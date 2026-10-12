// tests/Wc3.Tests/McpToolSmokeTests.Mpq.cs
// Smoke coverage for the mpq container tools. Serial, because mpq_strip_bookkeeping
// flips a process-wide environment variable for the length of one save.
using Wc3.Model;

namespace Wc3.Tests;

[Collection("Serial")]
public sealed class McpSmokeMpqTests : McpSmokeBase
{
    public McpSmokeMpqTests(SmokeFixture f) : base(f) { }

    [Fact, Covers("mpq_verify")]
    public async Task Mpq_verify_reports_the_fixture_as_readable()
    {
        var r = await Call("mpq_verify", Args(("map", Fixture)));
        Assert.True(Prop(r, "totalEntries").GetInt32() > 0);
        Assert.Equal(0, Prop(r, "unreadableEntries").GetInt32());
    }

    [Fact, Covers("mpq_listfile")]
    public async Task Mpq_listfile_counts_named_probe_recovered_and_unnamed()
    {
        var r = await Call("mpq_listfile", Args(("map", Fixture)));
        Assert.Equal(
            Prop(r, "totalEntries").GetInt32(),
            Prop(r, "namedByListfile").GetInt32()
            + Prop(r, "recoveredByStandardProbe").GetInt32()
            + Prop(r, "withoutName").GetInt32());
    }

    [Fact, Covers("mpq_name_recover")]
    public async Task Mpq_name_recover_reports_a_dictionary_hit()
    {
        var map = Path.Combine(_f.Dir, $"protected-{Guid.NewGuid():N}.w3x");
        File.WriteAllBytes(map, SyntheticMap.BuildProtected(
            new Dictionary<string, byte[]> { ["war3map.j"] = new byte[] { 1 } },
            new Dictionary<string, byte[]> { ["hidden.w3e"] = new byte[] { 2 } }));
        var dict = Path.Combine(_f.Dir, $"dict-{Guid.NewGuid():N}.txt");
        File.WriteAllText(dict, "hidden.w3e\n");
        try
        {
            var r = await Call("mpq_name_recover", Args(("map", map), ("dictionary", dict)));
            var names = Rows(Prop(r, "names"));
            Assert.Contains(names, n => Prop(n, "name").GetString() == "hidden.w3e");
        }
        finally { File.Delete(map); File.Delete(dict); }
    }

    [Fact, Covers("mpq_strip_bookkeeping")]
    public async Task Mpq_strip_bookkeeping_writes_a_copy_without_a_listfile()
    {
        var outPath = await Write("mpq_strip_bookkeeping", Args());
        // The input still has it, the copy does not.
        Assert.True(MapDocument.Load(Fixture).HasFileByName("(listfile)"));
        Assert.False(MapDocument.Load(outPath).HasFileByName("(listfile)"));
    }

    [Fact, Covers("mpq_bytes_diff")]
    public async Task Mpq_bytes_diff_reports_a_map_against_itself_identical()
    {
        var r = await Call("mpq_bytes_diff", Args(("map", Fixture), ("other", Fixture)));
        Assert.True(Prop(r, "identical").GetBoolean());
    }

    [Fact, Covers("mpq_bytes_diff")]
    public async Task Mpq_bytes_diff_reports_a_real_change()
    {
        var changed = await Write("place_unit", Args(
            ("type_rawcode", "hfoo"), ("owner_id", 0), ("x", 64f), ("y", -64f)));
        var r = await Call("mpq_bytes_diff", Args(("map", Fixture), ("other", changed)));
        Assert.False(Prop(r, "identical").GetBoolean());
    }
}
