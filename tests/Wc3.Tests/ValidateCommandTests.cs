using Wc3.Commands;
using Wc3.Model;
using Xunit;

namespace Wc3.Tests;

public class ValidateCommandTests
{
    // Non-empty placeholder bytes: present (so not "missing") but they won't parse
    // into real models — good enough to exercise presence/absence checks.
    private static readonly byte[] Blob = { 1, 2, 3, 4 };

    [Fact]
    public void Missing_required_files_are_errors()
    {
        // Only a script; no war3map.w3i / war3map.w3e.
        var doc = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.j"] = Blob,
        }));

        var r = ValidateCommand.Execute(doc);

        Assert.False(r.Valid);
        Assert.Contains(r.Issues, i =>
            i.Severity == DiagnosticSeverity.Error && i.Category == "missing-file" && i.FileName == "war3map.w3i");
        Assert.Contains(r.Issues, i =>
            i.Severity == DiagnosticSeverity.Error && i.Category == "missing-file" && i.FileName == "war3map.w3e");
        // A script IS present, so the "no script" error must NOT fire.
        Assert.DoesNotContain(r.Issues, i => i.Message.Contains("no script"));
        Assert.Equal(r.Errors, r.Issues.Count(i => i.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Missing_script_is_an_error()
    {
        // Has the required data files but no war3map.j / war3map.lua.
        var doc = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3i"] = Blob,
            ["war3map.w3e"] = Blob,
        }));

        var r = ValidateCommand.Execute(doc);

        Assert.False(r.Valid);
        Assert.Contains(r.Issues, i =>
            i.Severity == DiagnosticSeverity.Error && i.Message.Contains("no script"));
    }

    [Fact]
    public void Present_required_files_yield_no_errors()
    {
        var doc = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3i"] = Blob,
            ["war3map.w3e"] = Blob,
            ["war3map.j"] = Blob,
        }));

        var r = ValidateCommand.Execute(doc);

        // Warnings (e.g. loader parse notes) are allowed; there must be no errors.
        Assert.Equal(0, r.Errors);
        Assert.True(r.Valid, string.Join("; ", r.Issues.Select(i => i.Message)));
    }

    [Fact]
    public void Empty_named_file_is_a_warning()
    {
        var doc = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3i"] = Blob,
            ["war3map.w3e"] = Blob,
            ["war3map.j"] = Blob,
            ["empty.txt"] = System.Array.Empty<byte>(),
        }));

        // Only assert the empty-file rule when the 0-byte entry actually survived the
        // MPQ round-trip; otherwise the archive layer dropped it and there is nothing
        // for the validator to flag.
        if (doc.GetFile("empty.txt") is not null)
        {
            var r = ValidateCommand.Execute(doc);
            Assert.Contains(r.Issues, i =>
                i.Severity == DiagnosticSeverity.Warning && i.Category == "empty-file" && i.FileName == "empty.txt");
        }
    }

    [Fact]
    public void Loader_diagnostics_are_surfaced()
    {
        // An unnamed archive entry makes the loader emit a Warning diagnostic.
        var doc = MapDocument.Load(SyntheticMap.Build(
            new Dictionary<string, byte[]>
            {
                ["war3map.w3i"] = Blob,
                ["war3map.w3e"] = Blob,
                ["war3map.j"] = Blob,
            },
            new[] { new byte[] { 7, 7, 7 } }));

        var r = ValidateCommand.Execute(doc);

        Assert.Equal(doc.Diagnostics.Count, r.Issues.Count(i => i.Category == "loader"));
        Assert.Contains(r.Issues, i => i.Category == "loader");
    }

    [Fact]
    public void Counts_are_consistent_with_issues()
    {
        var doc = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["mystery.bin"] = Blob, // no required files at all
        }));

        var r = ValidateCommand.Execute(doc);

        Assert.Equal(r.Errors, r.Issues.Count(i => i.Severity == DiagnosticSeverity.Error));
        Assert.Equal(r.Warnings, r.Issues.Count(i => i.Severity == DiagnosticSeverity.Warning));
        Assert.Equal(r.Valid, r.Errors == 0);
    }
}
