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
}
