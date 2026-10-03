// src/Wc3.Studio/Controls/JassHighlighting.cs
using System.Text;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using Wc3.Model;

namespace Wc3.Studio.Controls;

/// <summary>
/// Builds AvaloniaEdit's JASS highlighting definition from the shared
/// <see cref="JassSyntax"/> vocabulary, so the word lists have exactly one owner.
/// </summary>
/// <remarks>
/// AvaloniaEdit wants an .xshd document. Writing one by hand would mean a second copy of every
/// keyword and every type, drifting from the first the moment either changed, which is the thing
/// the reuse rule exists to stop. Generating it costs a few dozen lines once and cannot drift.
///
/// The natives set is optional and comes from the game's own common.j. A caller with no install
/// gets keywords and types and nothing worse, which matters because a missing install must never
/// be the difference between a readable script and an unreadable one.
/// </remarks>
public static class JassHighlighting
{
    private static readonly object Gate = new();
    private static IHighlightingDefinition? _cached;
    private static int _cachedNativeCount = -1;

    /// <summary>
    /// The JASS definition. Rebuilt only when the natives set changes size, since the keyword and
    /// type lists are constants and a rebuild parses XML.
    /// </summary>
    public static IHighlightingDefinition For(IReadOnlySet<string>? natives = null)
    {
        int count = natives?.Count ?? 0;
        lock (Gate)
        {
            if (_cached is not null && _cachedNativeCount == count)
                return _cached;

            using var reader = XmlReader.Create(new StringReader(BuildXshd(natives)));
            var xshd = HighlightingLoader.LoadXshd(reader);
            _cached = HighlightingLoader.Load(xshd, HighlightingManager.Instance);
            _cachedNativeCount = count;
            return _cached;
        }
    }

    private static string BuildXshd(IReadOnlySet<string>? natives)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<SyntaxDefinition name="JASS" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">""");

        // Colours picked to read on the dark shell the Studio uses, and to make the three things
        // a reader of a merged arena script actually looks for stand apart, a rawcode literal, a
        // string, and the boundary of a function.
        sb.AppendLine("""  <Color name="Comment"    foreground="#6A9955" />""");
        sb.AppendLine("""  <Color name="String"     foreground="#CE9178" />""");
        sb.AppendLine("""  <Color name="Rawcode"    foreground="#D7BA7D" fontWeight="bold" />""");
        sb.AppendLine("""  <Color name="Number"     foreground="#B5CEA8" />""");
        sb.AppendLine("""  <Color name="Keyword"    foreground="#569CD6" fontWeight="bold" />""");
        sb.AppendLine("""  <Color name="Type"       foreground="#4EC9B0" />""");
        sb.AppendLine("""  <Color name="Native"     foreground="#DCDCAA" />""");
        sb.AppendLine("""  <Color name="FuncName"   foreground="#C586C0" fontWeight="bold" />""");

        sb.AppendLine("""  <RuleSet ignoreCase="false">""");

        // A line comment wins over everything, so it comes first.
        sb.AppendLine("""    <Span color="Comment" begin="//" />""");

        // A JASS string cannot span lines, so the span is line-bounded. Without that a single
        // unbalanced quote would colour the rest of the file as a string.
        sb.AppendLine("""    <Span color="String" multiline="false">""");
        sb.AppendLine("""      <Begin>"</Begin>""");
        sb.AppendLine("""      <End>"</End>""");
        sb.AppendLine("""      <RuleSet><Span begin="\\" end="." /></RuleSet>""");
        sb.AppendLine("""    </Span>""");

        // A four character rawcode literal. Highlighted distinctly because in this toolkit it is
        // the single most important token in a script, it is what a port carries or misses.
        sb.AppendLine("""    <Rule color="Rawcode">'[^'\n]{1,4}'</Rule>""");

        // The declared function's own name, so a body's boundaries are visible while scrolling.
        sb.AppendLine("""    <Rule color="FuncName">(?&lt;=\bfunction\s)[A-Za-z_][A-Za-z0-9_]*</Rule>""");

        AppendKeywords(sb, "Keyword", JassSyntax.Keywords);
        AppendKeywords(sb, "Type", JassSyntax.Types);
        if (natives is { Count: > 0 })
            AppendKeywords(sb, "Native", natives);

        sb.AppendLine("""    <Rule color="Number">\b0[xX][0-9a-fA-F]+\b|\b[0-9]+(\.[0-9]*)?\b|\.[0-9]+\b</Rule>""");

        sb.AppendLine("""  </RuleSet>""");
        sb.AppendLine("""</SyntaxDefinition>""");
        return sb.ToString();
    }

    private static void AppendKeywords(StringBuilder sb, string color, IEnumerable<string> words)
    {
        // Interpolated, not a raw string literal. A raw literal ending in four quotes reads as
        // three closing plus one content quote to a human and resolves the other way in the
        // compiler, so the attribute's opening quote vanished and the XML failed to parse.
        sb.AppendLine($"    <Keywords color=\"{color}\">");
        foreach (var w in words.OrderBy(w => w, StringComparer.Ordinal))
        {
            // A native name is an identifier, so no escaping is needed, but a hostile or odd name
            // must never be able to break the document.
            if (!IsIdentifier(w)) continue;
            sb.Append("      <Word>").Append(w).AppendLine("</Word>");
        }
        sb.AppendLine("    </Keywords>");
    }

    private static bool IsIdentifier(string s)
    {
        if (s.Length == 0) return false;
        if (!char.IsLetter(s[0]) && s[0] != '_') return false;
        foreach (var c in s)
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        return true;
    }
}
