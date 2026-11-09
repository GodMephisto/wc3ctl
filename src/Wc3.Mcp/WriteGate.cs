// src/Wc3.Mcp/WriteGate.cs
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Mcp;

/// <summary>An expected failure with a stable code and an optional next step (ERR-02).</summary>
internal sealed class ToolError : Exception
{
    public ToolError(string code, string message, string? next = null) : base(message)
    {
        Code = code;
        Next = next;
    }

    public string Code { get; }
    public string? Next { get; }
}

/// <summary>What one write produced, including the digests a caller can verify (SAF-06).</summary>
internal sealed record WriteOutcome(
    string SavedTo,
    string? InputSha256,
    string OutputSha256,
    int FilesRewritten,
    long BytesWritten);

/// <summary>
/// The MCP front end of the shared writer (see Wc3.Commands.ArtifactWriter), kept as a thin wrapper
/// so both the CLI and the MCP server take one write path and every existing call site and result
/// shape stays unchanged (SAF-14).
///
/// The only MCP-specific part is the guard, which is the user's permission check. The shared writer
/// calls it when an existing destination would be replaced, and this wrapper maps the writer's
/// refusals onto <see cref="ToolError"/> without changing their codes.
/// </summary>
internal static class WriteGate
{
    internal static string Resolve(string map, string? outPath) => ArtifactWriter.Resolve(map, outPath);

    internal static string Sha256File(string path) => ArtifactWriter.Sha256File(path);

    internal static WriteOutcome Save(
        MapDocument doc,
        string map,
        string? outPath,
        bool overwrite = false,
        string? expectInputSha256 = null,
        string? loadedInputSha = null)
    {
        var result = Map(() => ArtifactWriter.Save(
            doc, map, outPath, overwrite, expectInputSha256, loadedInputSha, Permissions.Current.Require));

        return new WriteOutcome(
            result.SavedTo,
            result.InputSha256,
            result.OutputSha256,
            result.FilesRewritten,
            result.BytesWritten);
    }

    internal static void Commit(string temp, string full, bool overwrite) =>
        Map(() => ArtifactWriter.Commit(temp, full, overwrite));

    /// <summary>Translate a shared refusal into the MCP error shape. The code is carried through
    /// unchanged, including PERMISSION_DENIED from the guard.</summary>
    private static T Map<T>(Func<T> body)
    {
        try
        {
            return body();
        }
        catch (ArtifactWriteException ex)
        {
            throw new ToolError(ex.Code, ex.Message, ex.Next);
        }
    }

    private static void Map(Action body)
    {
        try
        {
            body();
        }
        catch (ArtifactWriteException ex)
        {
            throw new ToolError(ex.Code, ex.Message, ex.Next);
        }
    }
}
