// tests/Wc3.Tests/McpToolSmokeTests.Lua.cs
// JASS to Lua conversion through the MCP tool. The converted copy must carry a script that
// parses as Lua 5.3, and the input map must come back unchanged.
namespace Wc3.Tests;

public sealed partial class McpToolSmokeTests
{
    [Fact, Covers("script_to_lua")]
    public async Task Script_to_lua_writes_a_lua_map_and_refuses_one_already_lua()
    {
        var before = File.ReadAllBytes(Fixture);
        var outPath = NewOut("to-lua");
        var r = await Call("script_to_lua", Args(("map", Fixture), ("out_path", outPath)));
        Assert.True(Prop(r, "functions").GetInt32() > 0);
        Assert.Equal(before, File.ReadAllBytes(Fixture));

        var check = await Call("script_check", Args(("map", outPath)));
        Assert.Equal("lua", Prop(check, "language").GetString());
        Assert.True(Prop(check, "ok").GetBoolean());

        Assert.Contains("already Lua", await CallError("script_to_lua", Args(("map", outPath), ("out_path", NewOut("to-lua-again")))));
    }
}
