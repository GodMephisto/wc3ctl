// src/Wc3.MapDocument/JassGlobals.cs
namespace Wc3.Model;

/// <summary>
/// The declarations inside a script's <c>globals</c> block(s). Pure text scan, the companion of
/// <see cref="JassFunctionIndex"/>. Shared so the script porter and the script checker can never
/// disagree about what counts as a global (they used to hold separate copies of this logic).
/// </summary>
public static class JassGlobals
{
    /// <summary>Global name to its full declaration line, plus the declaration order.
    /// First declaration wins on a duplicate name (illegal in JASS anyway).</summary>
    public static (Dictionary<string, string> ByName, List<string> Order) Parse(string[] lines)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();
        bool inBlock = false;
        foreach (var raw in lines)
        {
            var t = raw.Trim();
            if (!inBlock) { if (t == "globals") inBlock = true; continue; }
            if (t == "endglobals") { inBlock = false; continue; }
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal)) continue;
            var name = NameOf(t);
            if (name is not null && !map.ContainsKey(name)) { map[name] = t; order.Add(name); }
        }
        return (map, order);
    }

    /// <summary>The declared name in "[constant] type [array] name[= init]". Also serves a local
    /// declaration once its leading "local " is removed, the tail shape is identical.</summary>
    public static string? NameOf(string decl)
    {
        var toks = decl.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        if (i < toks.Length && toks[i] == "constant") i++;
        i++; // type
        if (i < toks.Length && toks[i] == "array") i++;
        if (i >= toks.Length) return null;
        var name = toks[i].Split('=')[0].Trim();
        return name.Length == 0 ? null : name;
    }
}
