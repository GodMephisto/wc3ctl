using Wc3.Commands;
using Wc3.Model;
namespace Wc3.Tests;

public class CommandTests
{
    private static MapDocument Doc() => MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
    {
        ["war3map.j"] = new byte[] { 1, 2, 3 },
        ["mystery.bin"] = new byte[] { 9 },
    }));

    [Fact]
    public void List_reports_all_files_with_metadata()
    {
        var result = ListCommand.Execute(Doc());
        Assert.Contains(result.Files, f => f.Name == "war3map.j" && f.Known);
        Assert.Contains(result.Files, f => f.Name == "mystery.bin" && !f.Known);
    }
}
