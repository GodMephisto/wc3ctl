using Wc3.Model;

namespace Wc3.Commands;

public static class ListCommand
{
    public static FileListResult Execute(MapDocument doc) => new(
        // RawSize, not RawBytes.Length. A listing wants the SIZE of every entry, and asking for
        // the bytes to get it decompresses the whole archive, which Load deliberately deferred.
        // On a 240 MB map that is 1617 ms against 1 ms, paid every time anything lists files.
        doc.Files.Select(f => new FileEntryInfo(f.FileName, f.RawSize, f.IsKnown, f.IsParsed)).ToList());
}
