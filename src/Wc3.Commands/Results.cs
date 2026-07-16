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

// A rawcode that had to be reassigned because it collided with the target map.
public sealed record RawcodeRemap(ObjectKind Kind, string From, string To);
public sealed record PortedObject(ObjectKind Kind, string Rawcode, string? Name, bool ModifiesStandard);
// Outcome of the best-effort JASS script closure append (null when no script was ported).
public sealed record ScriptPortInfo(
    int Functions, int Globals, int Renamed, bool InitHooked, IReadOnlyList<string> Notes);
public sealed record PortResult(
    string RootRawcode,
    string? RootPortedTo,          // the root's rawcode in the target (== RootRawcode if no collision)
    string? RootName,
    IReadOnlyList<RawcodeRemap> Remaps,
    IReadOnlyList<PortedObject> Objects,
    IReadOnlyList<string> CopiedFiles,
    IReadOnlyList<string> SkippedFiles,
    int InlinedStrings,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Diagnostics,
    ScriptPortInfo? Script = null);
// Combined outcome of porting several units into one target: per-unit results in input
// order (their Script is always null — the batch splices the merged closure once), the
// single merged script summary, and batch-level warnings.
public sealed record BatchPortResult(
    IReadOnlyList<PortResult> Units,
    ScriptPortInfo? Script,
    IReadOnlyList<string> Warnings);
public sealed record BundleNode(string Rawcode, ObjectKind Kind, string? Name, bool CustomToMap);
public sealed record BundleFile(string Path, string Category, bool PresentInMap); // Category: model|texture|icon|sound|other
public sealed record BundleEdge(string From, string To, string Via); // From/To = rawcode or file path; Via = field code
// Reason: "references '<rawcode>'[, ...]" (call-graph seed) or "called by <FunctionName>" (first discoverer).
public sealed record BundleFunction(string Name, int StartLine, int EndLine, string Reason);
// Despite the name, this is a generic object bundle: the root may be ANY ObjectKind
// (BundleCommand.ResolveObject). "Unit" is kept for API/JSON compatibility this pass.
public sealed record UnitBundle(string RootRawcode, string? RootName,
    IReadOnlyList<BundleNode> Objects, IReadOnlyList<BundleFile> Files,
    IReadOnlyList<string> Strings, IReadOnlyList<BundleEdge> Edges, IReadOnlyList<string> Diagnostics,
    IReadOnlyList<BundleFunction> Functions);
