using Wc3.GameData;
namespace Wc3.Tests;

public class WorldEditStringsTests
{
    [Fact]
    public void Parses_key_value_lines_and_skips_section_headers()
    {
        var wes = WorldEditStrings.Parse("[WorldEditStrings]\nWESTRING_RACE_HUMAN=Human\nWESTRING_X=\"quoted val\"\n");
        Assert.Equal(2, wes.Count);
        Assert.True(wes.TryGet("WESTRING_RACE_HUMAN", out var human));
        Assert.Equal("Human", human);
    }

    [Fact]
    public void Strips_surrounding_quotes_from_values()
    {
        var wes = WorldEditStrings.Parse("WESTRING_X=\"quoted val\"\n");
        Assert.True(wes.TryGet("WESTRING_X", out var val));
        Assert.Equal("quoted val", val);
    }

    [Fact]
    public void Unknown_key_returns_false()
    {
        var wes = WorldEditStrings.Parse("WESTRING_A=a\n");
        Assert.False(wes.TryGet("WESTRING_NOPE", out _));
    }

    [Fact]
    public void Keys_are_case_insensitive()
    {
        var wes = WorldEditStrings.Parse("WESTRING_RACE_HUMAN=Human\n");
        Assert.True(wes.TryGet("westring_race_human", out var val));
        Assert.Equal("Human", val);
    }

    [Fact]
    public void Splits_on_first_equals_only()
    {
        var wes = WorldEditStrings.Parse("WESTRING_EQ=a=b\n");
        Assert.True(wes.TryGet("WESTRING_EQ", out var val));
        Assert.Equal("a=b", val);
    }

    [Fact]
    public void Skips_blank_lines_and_lines_without_equals()
    {
        var wes = WorldEditStrings.Parse("\n  \nnotakeyvalue\nWESTRING_A=a\n");
        Assert.Equal(1, wes.Count);
    }

    [Fact]
    public void FromBytes_decodes_utf8()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("WESTRING_A=Héro\n");
        var wes = WorldEditStrings.FromBytes(bytes);
        Assert.True(wes.TryGet("WESTRING_A", out var val));
        Assert.Equal("Héro", val);
    }

    [Fact]
    public void FromBytes_skips_utf8_bom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(System.Text.Encoding.UTF8.GetBytes("WESTRING_A=a\n")).ToArray();
        var wes = WorldEditStrings.FromBytes(bytes);
        Assert.True(wes.TryGet("WESTRING_A", out var val));
        Assert.Equal("a", val);
    }
}
