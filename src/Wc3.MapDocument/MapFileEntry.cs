// src/Wc3.MapDocument/MapFileEntry.cs
namespace Wc3.Model;

public sealed class MapFileEntry
{
    public required string? FileName { get; init; }   // null => unnamed (protected map)
    public required int BlockIndex { get; init; }
    public required byte[] RawBytes { get; init; }     // original DECOMPRESSED bytes
    public required bool IsKnown { get; init; }
    public object? Model { get; set; }
    public bool IsParsed => Model is not null;
    public bool IsDirty { get; set; }

    /// <summary>
    /// A raw byte payload that, when set, is written verbatim on the next Save
    /// (taking precedence over any <see cref="Model"/> serialization). Used to add
    /// imported assets, replace the script text, or inject a file whose format has
    /// no byte-faithful model writer. <see cref="RawBytes"/> stays the immutable
    /// original bytes; this is the pending replacement.
    /// </summary>
    public byte[]? OverrideBytes { get; set; }
}
