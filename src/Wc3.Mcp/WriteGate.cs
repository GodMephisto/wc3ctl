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
        // Resolve first, so the check runs against the file the writer would really touch. The
        // resolve step is inside Map, so an OUT_PATH_IS_INPUT refusal keeps its ToolError shape.
        RefuseReparsePoint(Map(() => Resolve(map, outPath)));

        var result = Map(() => ArtifactWriter.Save(
            doc, map, outPath, overwrite, expectInputSha256, loadedInputSha, Permissions.Current.Require));

        return new WriteOutcome(
            result.SavedTo,
            result.InputSha256,
            result.OutputSha256,
            result.FilesRewritten,
            result.BytesWritten);
    }

    internal static void Commit(string temp, string full, bool overwrite)
    {
        RefuseReparsePoint(full);
        Map(() => ArtifactWriter.Commit(temp, full, overwrite));
    }

    /// <summary>
    /// Refuse a destination whose path crosses a reparse point, a symbolic link or a junction, so a
    /// write cannot follow a link out of the folder the caller named. Every existing component is
    /// checked, the destination itself included, and the refusal names the link it found.
    /// </summary>
    internal static void RefuseReparsePoint(string full)
    {
        foreach (var part in Components(full))
        {
            if (!File.Exists(part) && !Directory.Exists(part)) continue;
            FileAttributes attributes;
            try { attributes = File.GetAttributes(part); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new ToolError("OUTPUT_IS_LINK",
                    "the output path crosses a symbolic link at '" + part + "'",
                    "name an output path whose folders are real directories, or write beside the link");
        }
    }

    /// <summary>The full path and each of its parents, outermost first, so a link anywhere is seen.</summary>
    private static IEnumerable<string> Components(string full)
    {
        var parts = new List<string>();
        string? current = Path.GetFullPath(full);
        while (!string.IsNullOrEmpty(current))
        {
            parts.Add(current);
            string? parent = Path.GetDirectoryName(current);
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) break;
            current = parent;
        }
        parts.Reverse();
        return parts;
    }

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
