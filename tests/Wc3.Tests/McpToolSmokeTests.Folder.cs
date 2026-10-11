// tests/Wc3.Tests/McpToolSmokeTests.Folder.cs
// The tools on a map saved in folder mode. Reading goes through map_info, writing through
// map_info_set, and the edited copy must come back as a folder holding the change.
using Wc3.Model;

namespace Wc3.Tests;

public sealed partial class McpToolSmokeTests
{
    private string FolderFixture()
    {
        var folder = Path.Combine(_f.Dir, $"folder-{Guid.NewGuid():N}.w3x");
        foreach (var f in MapDocument.Load(Fixture).Files)
        {
            if (f.FileName is null || f.FileName.StartsWith('(')) continue;
            var path = Path.Combine(folder, f.FileName.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, f.RawBytes);
        }
        return folder;
    }

    [Fact]
    public async Task A_folder_map_reads_and_edits_through_the_tools()
    {
        var folder = FolderFixture();
        var info = await Call("map_info", Args(("map", folder)));
        Assert.Equal("Smoke Fixture", Prop(info, "name").GetString());

        var outPath = NewOut("folder-edit");
        await Call("map_info_set", Args(("map", folder), ("out_path", outPath), ("field", "Author"), ("value", "Folder Tester")));
        Assert.True(Directory.Exists(outPath), "a folder map's edited copy should be a folder");
        var fields = await Call("map_info_get", Args(("map", outPath)));
        Assert.Equal("Folder Tester", Prop(fields, "author").GetString());
    }

    [Fact]
    public async Task A_folder_maps_copy_cannot_go_inside_it()
    {
        var folder = FolderFixture();
        var message = await CallError("map_info_set",
            Args(("map", folder), ("out_path", Path.Combine(folder, "inner.w3x")), ("field", "Author"), ("value", "x")));
        Assert.Contains("inside the input map folder", message);
    }
}
