// tests/Wc3.Tests/ReleaseToolingTests.cs
// Hermetic tests for the release tooling: the version block, the version write, the changelog
// grouping, and the wc3-mcp export check. Everything runs against temp trees and temp git repos,
// never the real checkout.
using Wc3.Commands;

namespace Wc3.Tests;

public class ReleaseToolingTests
{
    // ---- version ----

    [Fact]
    public void Release_version_reads_assembly_metadata_and_mcp_identity()
    {
        var info = ReleaseVersionCommand.Read(typeof(ReleaseToolingTests).Assembly, "wc3ctl", "9.9.9");
        Assert.False(string.IsNullOrWhiteSpace(info.ProductVersion));
        Assert.False(string.IsNullOrWhiteSpace(info.GitHash));
        Assert.False(string.IsNullOrWhiteSpace(info.BuildTimeUtc));
        Assert.Equal("wc3ctl", info.McpServerName);
        Assert.Equal("9.9.9", info.McpVersion);
    }

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("0.0.1", true)]
    [InlineData("1.2", false)]
    [InlineData("1.2.3.4", false)]
    [InlineData("v1.2.3", false)]
    [InlineData("1.2.x", false)]
    [InlineData("", false)]
    public void Version_set_validates_the_shape(string version, bool expected) =>
        Assert.Equal(expected, VersionSetCommand.IsValid(version));

    [Fact]
    public void Version_set_writes_the_single_element_and_refuses_malformed()
    {
        using var tmp = new TempDir();
        string props = Path.Combine(tmp.Dir, "Directory.Build.props");
        File.WriteAllText(props,
            "<Project>\n  <PropertyGroup>\n    <Version>0.2.1</Version>\n  </PropertyGroup>\n</Project>\n");

        var ok = VersionSetCommand.Execute(props, "0.3.0");
        Assert.True(ok.Ok);
        Assert.Contains("<Version>0.3.0</Version>", File.ReadAllText(props));

        var bad = VersionSetCommand.Execute(props, "0.3");
        Assert.False(bad.Ok);
        Assert.Contains("<Version>0.3.0</Version>", File.ReadAllText(props));
    }

    [Fact]
    public void Version_set_refuses_when_the_element_is_missing_or_duplicated()
    {
        using var tmp = new TempDir();
        string props = Path.Combine(tmp.Dir, "Directory.Build.props");

        File.WriteAllText(props, "<Project><PropertyGroup></PropertyGroup></Project>");
        Assert.False(VersionSetCommand.Execute(props, "1.0.0").Ok);

        File.WriteAllText(props, "<Project><Version>1.0.0</Version><Version>2.0.0</Version></Project>");
        Assert.False(VersionSetCommand.Execute(props, "3.0.0").Ok);
    }

    // ---- changelog ----

    [Fact]
    public void Changelog_groups_by_conventional_type_in_order()
    {
        using var repo = new TempGitRepo();
        repo.Write("a.txt", "1");
        repo.Commit("no conventional prefix");
        repo.Tag("v1");
        repo.Write("b.txt", "2");
        repo.Commit("fix(parser): correct beta");
        repo.Write("c.txt", "3");
        repo.Commit("feat: add alpha");
        repo.Write("d.txt", "4");
        repo.Commit("docs: rewrite guide");

        var r = ChangelogCommand.Execute(repo.Dir, "v1", "HEAD");
        Assert.True(r.Ok, r.Message);
        Assert.Equal(new[] { "feat", "fix", "docs" }, r.Groups.Select(g => g.Type).ToArray());
        Assert.Equal("add alpha", r.Groups[0].Entries.Single().Subject);
        Assert.Equal("parser", r.Groups[1].Entries.Single().Scope);
        Assert.Contains("## feat", r.Markdown);
        Assert.Contains("* add alpha", r.Markdown);
        Assert.Contains("* correct beta (parser)", r.Markdown);
    }

    [Fact]
    public void Changelog_puts_a_non_conventional_subject_under_other()
    {
        using var repo = new TempGitRepo();
        repo.Write("a.txt", "1");
        repo.Commit("merged from somewhere");
        var r = ChangelogCommand.Execute(repo.Dir, null, "HEAD");
        Assert.True(r.Ok, r.Message);
        Assert.Equal("other", r.Groups.Single().Type);
    }

    [Fact]
    public void Changelog_reports_a_bad_directory_without_throwing()
    {
        string missing = Path.Combine(Path.GetTempPath(), "wc3ctl-nope-" + Guid.NewGuid().ToString("N"));
        var r = ChangelogCommand.Execute(missing, null, null);
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Message));
    }

    // ---- export verify ----

    [Fact]
    public void Export_verify_is_in_step_for_a_mirrored_tree()
    {
        using var tmp = new TempDir();
        string source = Path.Combine(tmp.Dir, "source");
        string target = Path.Combine(tmp.Dir, "target");
        BuildSourceTree(source);
        BuildTargetTree(target);

        var r = McpExportVerifyCommand.Execute(source, target);
        Assert.True(r.InStep, string.Join("; ", r.Differences.Select(d => d.Status + " " + d.Path)));
        Assert.True(r.CheckedFiles > 0);
    }

    [Fact]
    public void Export_verify_flags_added_changed_and_removed()
    {
        using var tmp = new TempDir();
        string source = Path.Combine(tmp.Dir, "source");
        string target = Path.Combine(tmp.Dir, "target");
        BuildSourceTree(source);
        BuildTargetTree(target);
        File.WriteAllText(Path.Combine(target, "src", "Wc3.Commands", "A.cs"), "different");
        File.Delete(Path.Combine(target, "tests", "Wc3.Tests", "GoodTest.cs"));
        File.WriteAllText(Path.Combine(target, "src", "Wc3.Commands", "Extra.cs"), "stray");

        var r = McpExportVerifyCommand.Execute(source, target);
        Assert.False(r.InStep);
        var byPath = r.Differences.ToDictionary(d => d.Path, d => d.Status, StringComparer.Ordinal);
        Assert.Equal("changed", byPath["src/Wc3.Commands/A.cs"]);
        Assert.Equal("added", byPath["tests/Wc3.Tests/GoodTest.cs"]);
        Assert.Equal("removed", byPath["src/Wc3.Commands/Extra.cs"]);
    }

    [Fact]
    public void Export_verify_reports_a_missing_target_without_throwing()
    {
        using var tmp = new TempDir();
        string source = Path.Combine(tmp.Dir, "source");
        BuildSourceTree(source);

        var r = McpExportVerifyCommand.Execute(source, Path.Combine(tmp.Dir, "missing"));
        Assert.False(r.InStep);
        Assert.Contains("no directory", r.Message);
    }

    [Fact]
    public void Export_verify_refuses_a_source_root_that_holds_no_wc3ctl_source()
    {
        // A wrong source root (the home directory, say, when the wc3ctl checkout could not be
        // located and the caller fell back to the process cwd) exists on disk but holds none of the
        // exported projects. It used to compare against nothing and report every target file as
        // "removed" (measured: 723 removed, 0 checked). It must refuse with a clear message.
        using var tmp = new TempDir();
        string emptySource = Path.Combine(tmp.Dir, "empty-source");
        Directory.CreateDirectory(emptySource);
        string target = Path.Combine(tmp.Dir, "target");
        BuildTargetTree(target);

        var r = McpExportVerifyCommand.Execute(emptySource, target);
        Assert.False(r.InStep);
        Assert.Empty(r.Differences);
        Assert.Equal(0, r.CheckedFiles);
        Assert.Contains("no wc3ctl source", r.Message);
    }

    // ---- tree builders ----

    private const string CliProjectReference =
        "          <ProjectReference Include=\"..\\..\\src\\wc3ctl\\wc3ctl.csproj\" />\n";
    private const string CommandProjectReference =
        "          <ProjectReference Include=\"..\\..\\src\\Wc3.Commands\\Wc3.Commands.csproj\" />\n";

    private static void BuildSourceTree(string root)
    {
        Write(root, "src/Wc3.Commands/A.cs", "namespace A;\n");
        Write(root, "src/Wc3.Mcp/README.md", "owned by the checkout\n");
        Write(root, "tests/Wc3.Tests/GoodTest.cs", "class GoodTest {}\n");
        Write(root, "tests/Wc3.Tests/CliTest.cs", "class CliTest { void M() { Wc3Ctl.Program.BuildRoot(); } }\n");
        Write(root, "tests/Wc3.Tests/Wc3.Tests.csproj",
            "<Project>\n  <ItemGroup>\n" + CliProjectReference + CommandProjectReference + "  </ItemGroup>\n</Project>\n");
    }

    private static void BuildTargetTree(string root)
    {
        Write(root, "src/Wc3.Commands/A.cs", "namespace A;\n");
        Write(root, "src/Wc3.Mcp/README.md", "a different readme the checkout owns\n");
        Write(root, "tests/Wc3.Tests/GoodTest.cs", "class GoodTest {}\n");
        Write(root, "tests/Wc3.Tests/Wc3.Tests.csproj",
            "<Project>\n  <ItemGroup>\n" + CommandProjectReference + "  </ItemGroup>\n</Project>\n");
    }

    private static void Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}

[Collection("Serial")]
public class ReleaseToolingCliTests
{
    [Fact]
    public async Task Cli_version_json_reports_the_build_and_mcp_identity()
    {
        var (code, output) = await Run("version", "--json");
        Assert.Equal(0, code);
        Assert.Contains("\"ProductVersion\"", output);
        Assert.Contains("\"GitHash\"", output);
        Assert.Contains("\"BuildTimeUtc\"", output);
        Assert.Contains("\"McpServerName\"", output);
        Assert.Contains("\"McpVersion\"", output);
    }

    [Fact]
    public async Task Cli_changelog_writes_notes_for_a_temp_repo()
    {
        using var repo = new TempGitRepo();
        repo.Write("a.txt", "1");
        repo.Commit("feat: add alpha");
        repo.Write("b.txt", "2");
        repo.Commit("fix: correct beta");
        string outPath = Path.Combine(repo.Dir, "notes.md");

        var (code, _) = await Run("changelog", "--repo", repo.Dir, "-o", outPath);
        Assert.Equal(0, code);
        string text = File.ReadAllText(outPath);
        Assert.Contains("## feat", text);
        Assert.Contains("* add alpha", text);
        Assert.Contains("## fix", text);
    }

    [Fact]
    public async Task Cli_export_verify_exits_nonzero_when_out_of_step()
    {
        using var tmp = new TempDir();
        var (code, output) = await Run("export", "verify", Path.Combine(tmp.Dir, "nope"));
        Assert.Equal(1, code);
        Assert.Contains("no directory", output);
    }

    [Fact]
    public async Task Cli_version_set_writes_the_props_and_refuses_malformed()
    {
        using var tmp = new TempDir();
        string props = Path.Combine(tmp.Dir, "Directory.Build.props");
        File.WriteAllText(props,
            "<Project>\n  <PropertyGroup>\n    <Version>0.2.1</Version>\n  </PropertyGroup>\n</Project>\n");

        var (ok, _) = await Run("version", "--set", "0.4.0", "--props", props);
        Assert.Equal(0, ok);
        Assert.Contains("<Version>0.4.0</Version>", File.ReadAllText(props));

        var (bad, _) = await Run("version", "--set", "0.4", "--props", props);
        Assert.NotEqual(0, bad);
        Assert.Contains("<Version>0.4.0</Version>", File.ReadAllText(props));
    }

    private static async Task<(int Code, string Output)> Run(params string[] args)
    {
        var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);
        try
        {
            int code = await Wc3Ctl.Program.Main(args);
            return (code, sw.ToString());
        }
        finally
        {
            Console.SetOut(orig);
        }
    }
}

/// <summary>A throwaway empty directory removed on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "wc3ctl-tempdir", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Dir);

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); } catch (IOException) { }
    }
}
