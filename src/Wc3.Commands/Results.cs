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
public sealed record MergedField(string Code, string Name, string Value, string Source); // Source: "base" | "map"
public sealed record MergedObjectResult(
    string Rawcode, bool Found, string? BaseRawcode, string? Name,
    IReadOnlyList<MergedField> Fields, IReadOnlyList<string> Diagnostics);
public sealed record ObjectListItem(string Rawcode, string? BaseRawcode, string? Name);
public sealed record ObjectListResult(IReadOnlyList<ObjectListItem> Items);
public sealed record ExtractedItem(string? Name, int BlockIndex, byte[] Bytes);
public sealed record ExtractResult(IReadOnlyList<ExtractedItem> Items);
public sealed record BundleNode(string Rawcode, ObjectKind Kind, string? Name, bool CustomToMap);
public sealed record BundleFile(string Path, string Category, bool PresentInMap); // Category: model|texture|icon|sound|other
public sealed record BundleEdge(string From, string To, string Via); // From/To = rawcode or file path; Via = field code
// Reason: "references '<rawcode>'[, ...]" (call-graph seed) or "called by <FunctionName>" (first discoverer).
public sealed record BundleFunction(string Name, int StartLine, int EndLine, string Reason);
public sealed record UnitBundle(string RootRawcode, string? RootName,
    IReadOnlyList<BundleNode> Objects, IReadOnlyList<BundleFile> Files,
    IReadOnlyList<string> Strings, IReadOnlyList<BundleEdge> Edges, IReadOnlyList<string> Diagnostics,
    IReadOnlyList<BundleFunction> Functions);
