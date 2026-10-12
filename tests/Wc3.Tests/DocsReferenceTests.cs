// tests/Wc3.Tests/DocsReferenceTests.cs
// The CI guard for the generated reference documents. Both files are regenerated in memory from
// the live command tree and the live MCP tool surface, then compared against the committed text.
// A new command or tool, an edited description or a changed annotation changes the render and this
// test fails until the committed document is regenerated with 'wc3ctl docs all --force'.
using Wc3Ctl;

namespace Wc3.Tests;

public class DocsReferenceTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 })
        {
            if (File.Exists(Path.Combine(dir, "Wc3.sln"))) return dir;
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        throw new InvalidOperationException("could not locate the repository root from " + AppContext.BaseDirectory);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>First line that differs, so a stale document points at what moved.</summary>
    private static string FirstDifference(string committed, string generated)
    {
        var a = Normalize(committed).Split('\n');
        var b = Normalize(generated).Split('\n');
        int max = Math.Max(a.Length, b.Length);
        for (int i = 0; i < max; i++)
        {
            string left = i < a.Length ? a[i] : "(no line)";
            string right = i < b.Length ? b[i] : "(no line)";
            if (!string.Equals(left, right, StringComparison.Ordinal))
                return $"first difference at line {i + 1}\n  committed. {left}\n  generated. {right}";
        }
        return "no difference";
    }

    private static void AssertDocument(string relativePath, string generated)
    {
        string path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{relativePath} is missing, run 'wc3ctl docs all --force'.");
        string committed = File.ReadAllText(path);
        bool identical = string.Equals(Normalize(committed), Normalize(generated), StringComparison.Ordinal);
        Assert.True(identical,
            $"{relativePath} is stale, run 'wc3ctl docs all --force'. {FirstDifference(committed, generated)}");
    }

    [Fact]
    public void Command_reference_matches_the_live_tree()
        => AssertDocument(DocsCommand.CliRelativePath, DocsCommand.BuildCliMarkdown());

    [Fact]
    public void Mcp_reference_matches_the_live_tool_surface()
        => AssertDocument(DocsCommand.McpRelativePath, DocsCommand.BuildMcpMarkdown());

    [Fact]
    public void Command_reference_documents_every_top_level_verb()
    {
        // A second, direct check so the guard above cannot pass on a render that silently dropped a
        // verb or a whole branch, and so a rename is caught as clearly as an addition.
        var text = DocsCommand.BuildCliMarkdown();
        foreach (var verb in Wc3Ctl.Program.BuildRoot().Subcommands.Select(c => c.Name))
            Assert.Contains("\n## " + verb + "\n", text);
    }

    [Fact]
    public void Mcp_reference_documents_every_tool()
    {
        var text = DocsCommand.BuildMcpMarkdown();
        var names = Wc3.Mcp.Wc3McpServer.CreateTools()
            .Select(t => t.ProtocolTool.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.NotEmpty(names);
        foreach (var name in names)
            Assert.Contains("\n## " + name + "\n", text);
    }

    [Fact]
    public void Diff_reports_identical_text_and_a_change()
    {
        var same = Wc3.Commands.DocsReference.Diff("a\nb\n", "a\r\nb\r\n");
        Assert.True(same.Identical);
        Assert.Equal(0, same.Added);
        Assert.Equal(0, same.Removed);

        var changed = Wc3.Commands.DocsReference.Diff("a\nb\n", "a\nc\n");
        Assert.False(changed.Identical);
        Assert.Equal(1, changed.Added);
        Assert.Equal(1, changed.Removed);
    }
}
