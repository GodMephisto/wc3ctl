using Wc3.GameData;
namespace Wc3.Tests;
public class GameDataIntegrationTests
{
    private const string Install = @"D:\Warcraft III";

    [Fact]
    [Trait("Category", "GameData")]
    public void Opens_casc_and_reads_unit_metadata()
    {
        if (!Directory.Exists(Install)) return;
        Assert.True(CascGameDataSource.TryOpen(Install, out var src, out var err), err);
        using (src)
        {
            var bytes = src!.ReadFile(@"war3.w3mod:units\unitmetadata.slk");
            Assert.NotNull(bytes);
            Assert.True(bytes!.Length > 1000);
        }
    }
}
