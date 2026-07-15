using Wc3.GameData;
namespace Wc3.Tests;
public class AbilityGameDataIntegrationTests
{
    private const string Install = @"D:\Warcraft III";

    [Fact]
    [Trait("Category", "GameData")]
    public void Resolves_blizzard_base_fields()
    {
        if (!Directory.Exists(Install)) return;
        Assert.True(CascGameDataSource.TryOpen(Install, out var src, out var err), err);
        using (src)
        {
            var store = BaseAbilityStore.Build(src!);
            Assert.True(store.TryGetAbility("AHbz", out var fields), "Blizzard AHbz should resolve");
            Assert.NotEmpty(fields);
            // Recognizable values: aher = is hero ability, arac = race.
            Assert.Equal("1", fields["aher"]);
            Assert.Equal("human", fields["arac"]);
        }
    }
}
