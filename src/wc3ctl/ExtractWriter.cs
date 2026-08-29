// src/wc3ctl/ExtractWriter.cs — file writing lives in the CLI layer only;
// Wc3.Commands selects entries, this class puts their bytes on disk.
using Wc3.Commands;
using Wc3.Model;

namespace Wc3Ctl;

public sealed record ExtractManifestEntry(string? Name, int Bytes, string Path);
public sealed record ExtractManifest(IReadOnlyList<ExtractManifestEntry> Files, int Count, long TotalBytes);

public static class ExtractWriter
{
    /// <summary>Writes all items under <paramref name="outDir"/>, preserving internal folder structure.</summary>
    public static ExtractManifest WriteAll(ExtractResult r, string outDir)
    {
        var root = Path.GetFullPath(outDir);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var entries = new List<ExtractManifestEntry>();
        long total = 0;
        foreach (var item in r.Items)
        {
            var dest = Path.GetFullPath(Path.Combine(root, SafeRelativePath(item.Name, item.BlockIndex, UnnamedExtension(item))));
            if (!dest.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"refusing to write outside output directory: {item.Name}");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, item.Bytes);
            entries.Add(new ExtractManifestEntry(item.Name, item.Bytes.Length, dest));
            total += item.Bytes.Length;
        }
        return new ExtractManifest(entries, entries.Count, total);
    }

    /// <summary>Writes one item to an explicit output file path.</summary>
    public static ExtractManifest WriteSingle(ExtractedItem item, string outFile)
    {
        var dest = Path.GetFullPath(outFile);
        var dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(dest, item.Bytes);
        return new ExtractManifest(
            new[] { new ExtractManifestEntry(item.Name, item.Bytes.Length, dest) }, 1, item.Bytes.Length);
    }

    /// <summary>The extension an unnamed entry's file should get, sniffed from the bytes the
    /// extraction already holds ("bin" when the content matches nothing known).</summary>
    private static string UnnamedExtension(ExtractedItem item) =>
        item.Name is null ? ContentTypeSniffer.Sniff(item.Bytes).Extension : "bin";

    /// <summary>
    /// Maps an internal name to a safe relative path: splits on '\'/'/', drops
    /// '.'/'..'/empty segments, replaces invalid characters. Unnamed entries
    /// (FileName == null) go to _unnamed\block_&lt;n&gt;.&lt;ext&gt;, the extension taken
    /// from the entry's sniffed content type so a nameless BLP extracts as a .blp.
    /// </summary>
    public static string SafeRelativePath(string? name, int blockIndex, string unnamedExtension = "bin")
    {
        if (name is null) return Path.Combine("_unnamed", $"block_{blockIndex}.{unnamedExtension}");
        var segments = name.Split('\\', '/')
            .Where(s => s.Length > 0 && s != "." && s != "..")
            .Select(Sanitize)
            .Where(s => s.Length > 0)
            .ToArray();
        return segments.Length == 0
            ? Path.Combine("_unnamed", $"block_{blockIndex}.bin")
            : Path.Combine(segments);
    }

    private static string Sanitize(string segment)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(segment.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.TrimEnd(' ', '.');   // Windows rejects trailing dots/spaces
    }
}
