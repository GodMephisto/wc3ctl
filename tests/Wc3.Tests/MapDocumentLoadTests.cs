// tests/Wc3.Tests/MapDocumentLoadTests.cs
namespace Wc3.Tests;

// Alias must sit INSIDE the namespace: in Wc3.* namespaces the identifier
// "MapDocument" otherwise binds to the Wc3.MapDocument NAMESPACE (member of
// enclosing Wc3) before file-level usings are consulted (CA1724 collision).
using MapDocument = Wc3.MapDocument.MapDocument;

public class MapDocumentLoadTests
{
    private static byte[] SampleMap() => SyntheticMap.Build(new Dictionary<string, byte[]>
    {
        ["war3map.w3i"] = new byte[] { 1, 2, 3, 4 },
        ["war3map.j"]   = new byte[] { 5, 6 },
        ["mystery.bin"] = new byte[] { 7, 8, 9 },
    });

    [Fact]
    public void Loads_all_named_files_with_raw_bytes()
    {
        var doc = MapDocument.Load(SampleMap());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, doc.GetFile("war3map.w3i")!.RawBytes);
        Assert.Equal(new byte[] { 7, 8, 9 }, doc.GetFile("mystery.bin")!.RawBytes);
    }

    [Fact]
    public void Preserves_pre_archive_header()
    {
        var doc = MapDocument.Load(SampleMap());
        Assert.Equal(0x200, doc.PreArchiveData.Length);
        Assert.Equal((byte)'H', doc.PreArchiveData[0]);
    }

    [Fact]
    public void Marks_known_files_known_and_others_unknown()
    {
        var doc = MapDocument.Load(SampleMap());
        Assert.True(doc.GetFile("war3map.w3i")!.IsKnown);
        Assert.False(doc.GetFile("mystery.bin")!.IsKnown);
    }
}
