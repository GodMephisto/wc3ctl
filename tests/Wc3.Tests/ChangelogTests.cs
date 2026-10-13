// tests/Wc3.Tests/ChangelogTests.cs
using System.IO;
using System.Text.RegularExpressions;
using Wc3.Commands;
using Xunit;

namespace Wc3.Tests;

/// <summary>
/// Guards the changelog layout: one file per release under <c>changelog/</c>, with
/// <c>CHANGELOG.md</c> showing the latest. The version this build carries must have its own file,
/// so bumping the version without adding one fails here rather than at review time or, worse, at
/// a release that ships with no notes for it.
/// </summary>
public class ChangelogTests
{
    private static string RepoRoot()
    {
        var root = RepoLayout.FindRoot();
        Assert.False(string.IsNullOrEmpty(root), "could not locate the repository root (no Wc3.sln)");
        return root!;
    }

    private static string CurrentVersion(string root)
    {
        var match = Regex.Match(
            File.ReadAllText(Path.Combine(root, "Directory.Build.props")),
            @"<Version>([^<]+)</Version>");
        Assert.True(match.Success, "no <Version> element in Directory.Build.props");
        return match.Groups[1].Value;
    }

    [Fact]
    public void The_current_version_has_a_changelog_file()
    {
        string root = RepoRoot();
        string version = CurrentVersion(root);
        string file = Path.Combine(root, "changelog", version + ".md");
        Assert.True(File.Exists(file),
            $"version {version} has no changelog/{version}.md, so a release would ship without "
            + "its own notes file");
    }

    [Fact]
    public void The_root_changelog_shows_the_latest_version()
    {
        // CHANGELOG.md is the entry point, so it must carry a section for the version this build
        // is. If it drifts behind, the newest release is only visible one file deep.
        string root = RepoRoot();
        string version = CurrentVersion(root);
        Assert.Contains($"## {version} - ", File.ReadAllText(Path.Combine(root, "CHANGELOG.md")));
    }
}
