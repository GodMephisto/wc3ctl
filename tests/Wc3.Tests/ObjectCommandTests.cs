// tests/Wc3.Tests/ObjectCommandTests.cs
using Wc3.Commands;
namespace Wc3.Tests;

public class ObjectCommandTests
{
    [Fact]
    public void Merges_base_and_delta_with_source_labels()
    {
        // No real map needed for the merge core: exercise the pure Merge with fakes.
        var baseFields = new Dictionary<string, string> { ["unam"] = "Footman", ["uhpm"] = "420" };
        var result = ObjectGetCommand.Merge(
            rawcode: "H001", baseRawcode: "hfoo", definedInMap: true,
            baseFields: baseFields,
            deltaFields: new Dictionary<string, string> { ["uhpm"] = "999" },
            nameLookup: code => code == "unam" ? "Name" : code == "uhpm" ? "Hit Points" : code,
            diagnostics: Array.Empty<string>());

        Assert.True(result.Found);
        Assert.Equal("hfoo", result.BaseRawcode);
        var hp = result.Fields.Single(f => f.Code == "uhpm");
        Assert.Equal("999", hp.Value);
        Assert.Equal("map", hp.Source);
        Assert.Equal("base", result.Fields.Single(f => f.Code == "unam").Source);
        // Base-only field retained; output ordered by display name.
        Assert.Equal(new[] { "Hit Points", "Name" }, result.Fields.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void Map_defined_unit_with_zero_fields_is_still_found()
    {
        // Base-less custom unit with no mods and no base data: presence in the
        // map's w3u alone must yield Found=true.
        var result = ObjectGetCommand.Merge(
            rawcode: "H001", baseRawcode: null, definedInMap: true,
            baseFields: new Dictionary<string, string>(),
            deltaFields: new Dictionary<string, string>(),
            nameLookup: code => code,
            diagnostics: Array.Empty<string>());

        Assert.True(result.Found);
        Assert.Null(result.BaseRawcode);
        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Unknown_rawcode_with_zero_fields_is_not_found()
    {
        var result = ObjectGetCommand.Merge(
            rawcode: "Xxxx", baseRawcode: null, definedInMap: false,
            baseFields: new Dictionary<string, string>(),
            deltaFields: new Dictionary<string, string>(),
            nameLookup: code => code,
            diagnostics: Array.Empty<string>());

        Assert.False(result.Found);
        Assert.Empty(result.Fields);
    }
}
