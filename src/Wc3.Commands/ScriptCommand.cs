// src/Wc3.Commands/ScriptCommand.cs
using System.Text;
using Wc3.Model;

namespace Wc3.Commands;

public sealed record ScriptFunctionsResult(string ScriptFile, IReadOnlyList<JassFunction> Functions);

public static class ScriptCommand
{
    /// <summary>
    /// Lists the functions declared in the map's script (war3map.j, else war3map.lua).
    /// Lua scripts go through the same JASS scanner best-effort — typically zero hits,
    /// but the result still names the file so the caller knows what was inspected.
    /// </summary>
    public static ScriptFunctionsResult Functions(MapDocument doc)
    {
        var entry = doc.GetFile("war3map.j")
            ?? doc.GetFile("scripts\\war3map.j")
            ?? doc.GetFile("war3map.lua")
            ?? throw new FileNotFoundException("map contains no war3map.j or war3map.lua", "war3map.j");

        var source = Encoding.UTF8.GetString(entry.RawBytes);
        var functions = JassFunctionIndex.Parse(source).OrderBy(f => f.StartLine).ToList();
        return new ScriptFunctionsResult(entry.FileName!, functions);
    }
}
