namespace Wc3.Commands;

public sealed record FileEntryInfo(string? Name, int SizeBytes, bool Known, bool Parsed);
public sealed record FileListResult(IReadOnlyList<FileEntryInfo> Files);
public sealed record MapInfoResult(
    string Name, string Author, int Players, int? Width, int? Height, IReadOnlyList<string> Diagnostics);
public sealed record SearchHit(string FileName, string Context);
public sealed record SearchResult(IReadOnlyList<SearchHit> Hits);
public sealed record DiffEntry(string Name, string Change);
public sealed record DiffResult(IReadOnlyList<DiffEntry> Entries);
public sealed record RoundtripResult(
    bool Faithful, IReadOnlyList<string> Mismatches, IReadOnlyList<string> ExcludedNotes);
public sealed record ObjectGetResult(string Rawcode, bool Found, IReadOnlyDictionary<string, string> Fields);
public sealed record ExtractedItem(string? Name, int BlockIndex, byte[] Bytes);
public sealed record ExtractResult(IReadOnlyList<ExtractedItem> Items);
