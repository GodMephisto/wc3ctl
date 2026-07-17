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

    /// <summary>
    /// Char offset of the start of each 1-based line, following JassFunctionIndex line
    /// numbering (lines are '\n'-separated). Result[0] is always 0; a source of length N
    /// with K newlines yields K+1 entries. Empty source yields a single entry [0].
    /// </summary>
    public static int[] ComputeLineStarts(string source)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < source.Length; i++)
            if (source[i] == '\n') starts.Add(i + 1);
        return starts.ToArray();
    }

    /// <summary>
    /// Returns the substring of <paramref name="source"/> spanning the 1-based line range
    /// [<paramref name="startLine"/>, <paramref name="endLine"/>] (inclusive), with the
    /// trailing newline excluded (and a preceding '\r' trimmed for CRLF sources). The last
    /// line runs to EOF. Returns "" when startLine is out of range. endLine is clamped into
    /// [startLine, lineCount] so a function whose EndLine overruns the file is still safe.
    /// <paramref name="lineStarts"/> must come from <see cref="ComputeLineStarts"/>.
    /// </summary>
    public static string SliceFunction(string source, int[] lineStarts, int startLine, int endLine)
    {
        if (lineStarts is null || lineStarts.Length == 0) return string.Empty;
        if (startLine < 1 || startLine > lineStarts.Length) return string.Empty;

        var clampedEnd = Math.Clamp(endLine, startLine, lineStarts.Length);
        var start = lineStarts[startLine - 1];
        var end = clampedEnd < lineStarts.Length
            ? lineStarts[clampedEnd] - 1   // up to (not including) the '\n'
            : source.Length;               // last line runs to EOF
        if (end > start && source[end - 1] == '\r') end--;
        if (end < start) end = start;
        return source.Substring(start, end - start);
    }
}
