// tests/Wc3.Tests/McpWriteGateTests.cs
using Wc3.Commands;
using Wc3.Mcp;
using Wc3.Model;

namespace Wc3.Tests;

/// <summary>
/// Slice one of the safe chaining work. The gate is the one path a write takes, so these tests
/// pin its refusals, its digests and its atomic write without going through the protocol.
///
/// A replacement now needs the user's overwrite grant (see Permissions), so the fixture writes a
/// permission file into its own temporary folder and points WC3_MCP_PERMISSIONS at it. The
/// machine's own permissions.json is never read or touched. The class joins the Serial
/// collection because an environment variable is process wide.
/// </summary>
[Collection("Serial")]
public sealed class McpWriteGateTests : IDisposable
{
    private readonly string _dir;
    private readonly string _map;
    private readonly string? _savedPermissions;

    public McpWriteGateTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "wc3gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _map = Path.Combine(_dir, "in.w3x");
        File.WriteAllBytes(_map, BlankMap.CreateArchiveBytes());

        // A test process is the user here, so it grants overwrite. Denial is proven in
        // PermissionsTests, which is the suite that owns that behaviour.
        File.WriteAllText(Path.Combine(_dir, "permissions.json"), "{\"overwrite\": true}");
        _savedPermissions = Environment.GetEnvironmentVariable(Permissions.EnvironmentVariable);
        Environment.SetEnvironmentVariable(Permissions.EnvironmentVariable,
            Path.Combine(_dir, "permissions.json"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Permissions.EnvironmentVariable, _savedPermissions);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private MapDocument Load() => MapDocument.Load(_map);

    private static string Code(Action action)
    {
        var ex = Assert.Throws<ToolError>(action);
        return ex.Code;
    }

    [Fact]
    public void An_output_equal_to_the_input_is_refused()
    {
        Assert.Equal("OUT_PATH_IS_INPUT", Code(() => WriteGate.Save(Load(), _map, _map)));
    }

    [Fact]
    public void An_existing_output_is_refused_without_overwrite()
    {
        string output = Path.Combine(_dir, "out.w3x");
        File.WriteAllBytes(output, new byte[] { 1 });
        Assert.Equal("OUTPUT_EXISTS", Code(() => WriteGate.Save(Load(), _map, output)));
    }

    [Fact]
    public void Overwrite_writes_the_file_and_reports_both_digests()
    {
        string output = Path.Combine(_dir, "out.w3x");
        File.WriteAllBytes(output, new byte[] { 1 });

        var w = WriteGate.Save(Load(), _map, output, overwrite: true);

        Assert.True(File.Exists(output));
        Assert.Equal(WriteGate.Sha256File(_map), w.InputSha256);
        Assert.Equal(WriteGate.Sha256File(output), w.OutputSha256);
        Assert.Equal(new FileInfo(output).Length, w.BytesWritten);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void A_stale_input_digest_is_refused()
    {
        string output = Path.Combine(_dir, "out.w3x");
        Assert.Equal("STALE_INPUT",
            Code(() => WriteGate.Save(Load(), _map, output, expectInputSha256: new string('0', 64))));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void The_matching_input_digest_passes_and_is_reported()
    {
        string output = Path.Combine(_dir, "out.w3x");
        string sha = WriteGate.Sha256File(_map);

        var w = WriteGate.Save(Load(), _map, output, expectInputSha256: sha);

        Assert.Equal(sha, w.InputSha256);
        Assert.True(File.Exists(output));
    }
    [Fact]
    public void An_existing_output_folder_is_refused_without_overwrite()
    {
        string output = Path.Combine(_dir, "already-a-folder.w3x");
        Directory.CreateDirectory(output);

        Assert.Equal("OUTPUT_EXISTS", Code(() => WriteGate.Save(Load(), _map, output)));
    }

    [Fact]
    public void An_input_replaced_after_load_is_refused_rather_than_saved()
    {
        string output = Path.Combine(_dir, "out.w3x");
        string loadedSha = WriteGate.Sha256File(_map);
        var doc = Load();

        // Something else rewrote the base while the document was open.
        File.WriteAllBytes(_map, BlankMap.CreateArchiveBytes().Concat(new byte[] { 0 }).ToArray());

        Assert.Equal("STALE_INPUT",
            Code(() => WriteGate.Save(doc, _map, output, loadedInputSha: loadedSha)));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void An_input_untouched_since_load_saves_and_reports_the_loaded_digest()
    {
        string output = Path.Combine(_dir, "out.w3x");
        string loadedSha = WriteGate.Sha256File(_map);

        var w = WriteGate.Save(Load(), _map, output, loadedInputSha: loadedSha);

        Assert.Equal(loadedSha, w.InputSha256);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void A_destination_that_appears_before_the_commit_is_reported_as_existing()
    {
        string temp = Path.Combine(_dir, "staged.tmp");
        string output = Path.Combine(_dir, "raced.w3x");
        File.WriteAllBytes(temp, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(output, new byte[] { 4, 5, 6 });

        Assert.Equal("OUTPUT_EXISTS", Code(() => WriteGate.Commit(temp, output, overwrite: false)));
        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(output));
        Assert.True(File.Exists(temp), "a refused commit leaves the staged file for the caller to clean up");
    }

    [Fact]
    public void A_commit_with_overwrite_replaces_the_destination()
    {
        string temp = Path.Combine(_dir, "staged2.tmp");
        string output = Path.Combine(_dir, "replaced.w3x");
        File.WriteAllBytes(temp, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(output, new byte[] { 4, 5, 6 });

        WriteGate.Commit(temp, output, overwrite: true);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(output));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void A_folder_map_is_written_as_a_folder_and_keeps_the_default_outside_it()
    {
        string folder = Path.Combine(_dir, "folder.w3x");
        var src = MapDocument.Load(BlankMap.CreateArchiveBytes());
        foreach (var f in src.Files)
        {
            if (f.FileName is null || f.FileName.StartsWith('(')) continue;
            string path = Path.Combine(folder, f.FileName.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, f.RawBytes);
        }

        Assert.False(File.Exists(folder));
        var doc = MapDocument.Load(folder);
        string output = Path.Combine(_dir, "folder-out.w3x");
        var w = WriteGate.Save(doc, folder, output);

        Assert.True(Directory.Exists(output));
        Assert.Equal("", w.OutputSha256);
    }

}
