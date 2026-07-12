namespace Wc3.Commands;

public sealed record FileEntryInfo(string? Name, int SizeBytes, bool Known, bool Parsed);
public sealed record FileListResult(IReadOnlyList<FileEntryInfo> Files);
public sealed record MapInfoResult(
    string Name, string Author, int Players, int? Width, int? Height, IReadOnlyList<string> Diagnostics);
