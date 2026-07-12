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

    [Fact]
    [Trait("Category", "GameData")]
    public void Resolves_footman_base_stats()
    {
        if (!Directory.Exists(Install)) return;
        Assert.True(CascGameDataSource.TryOpen(Install, out var src, out var err), err);
        using (src)
        {
            var store = BaseUnitStore.Build(src!);
            Assert.True(store.TryGetUnit("hfoo", out var fields), "Footman hfoo should resolve");
            Assert.NotEmpty(fields);
            // Recognizable values: uhpm = max HP (positive int), uabi = default ability list.
            Assert.True(int.TryParse(fields["uhpm"], out var hp) && hp > 0, $"uhpm should be a positive int, got '{fields.GetValueOrDefault("uhpm")}'");
            Assert.Contains("Adef", fields["uabi"]);
        }
    }
}
