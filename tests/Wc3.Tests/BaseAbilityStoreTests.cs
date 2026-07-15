using Wc3.GameData;
namespace Wc3.Tests;

public class BaseAbilityStoreTests
{
    private sealed class FakeSource : IGameDataSource
    {
        private readonly Dictionary<string, byte[]> _f;
        public FakeSource(Dictionary<string, byte[]> f) => _f = f;
        public byte[]? ReadFile(string name) => _f.TryGetValue(name, out var b) ? b : null;
        public void Dispose() { }
    }

    private static byte[] A(string s) => System.Text.Encoding.ASCII.GetBytes(s);

    // Real shape: metadata row key = 4-char code; "field" = data-SLK column; "slk" = data SLK name.
    private const string Meta =
        "ID;PWXL;N;E\r\n" +
        "C;X1;Y1;K\"ID\"\r\nC;X2;K\"field\"\r\nC;X3;K\"slk\"\r\nC;X4;K\"displayName\"\r\n" +
        "C;X1;Y2;K\"aher\"\r\nC;X2;K\"hero\"\r\nC;X3;K\"AbilityData\"\r\nC;X4;K\"WESTRING_AEVAL_AHER\"\r\n" +
        "C;X1;Y3;K\"anam\"\r\nC;X2;K\"Name\"\r\nC;X3;K\"Profile\"\r\nC;X4;K\"WESTRING_AEVAL_ANAM\"\r\nE\r\n";

    // abilitydata.slk keyed by alias; AHbz is a hero ability.
    private const string Data =
        "ID;PWXL;N;E\r\n" +
        "C;X1;Y1;K\"alias\"\r\nC;X2;K\"hero\"\r\n" +
        "C;X1;Y2;K\"AHbz\"\r\nC;X2;K1\r\nE\r\n";

    private static BaseAbilityStore BuildStore() => BaseAbilityStore.Build(new FakeSource(new()
    {
        [@"war3.w3mod:units\abilitymetadata.slk"] = A(Meta),
        [@"war3.w3mod:units\abilitydata.slk"] = A(Data),
    }));

    [Fact]
    public void Resolves_base_ability_field_via_metadata()
    {
        var store = BuildStore();
        Assert.True(store.TryGetAbility("AHbz", out var fields));
        Assert.Equal("1", fields["aher"]);
        // "anam" lives in Profile (a TXT, not one of the ability SLKs) — not resolvable in v1.
        Assert.False(fields.ContainsKey("anam"));
    }

    [Fact]
    public void Unknown_rawcode_returns_false_and_empty()
    {
        var store = BuildStore();
        Assert.False(store.TryGetAbility("Axxx", out var fields));
        Assert.Empty(fields);
    }

    [Fact]
    public void Missing_metadata_throws()
    {
        var src = new FakeSource(new());
        Assert.Throws<InvalidDataException>(() => BaseAbilityStore.Build(src));
    }
}
