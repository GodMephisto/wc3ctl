// src/Wc3.MapDocument/MapDocument.cs
using War3Net.IO.Mpq;

namespace Wc3.Model;

public sealed class MapDocument
{
    private readonly byte[] _originalBytes;
    private readonly List<MapFileEntry> _files = new();
    private readonly List<Diagnostic> _diagnostics = new();

    public IReadOnlyList<MapFileEntry> Files => _files;
    public byte[] PreArchiveData { get; private set; } = Array.Empty<byte>();
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    static MapDocument() => DefaultParsers.RegisterDefaults();

    private MapDocument(byte[] originalBytes) => _originalBytes = originalBytes;

    public static MapDocument Load(string path) => Load(File.ReadAllBytes(path));

    public static MapDocument Load(byte[] fileBytes)
    {
        var doc = new MapDocument(fileBytes);

        int offset = MpqHeader.FindArchiveOffset(fileBytes);
        if (offset < 0)
            throw new InvalidDataException("No MPQ archive magic found in file.");
        doc.PreArchiveData = fileBytes[..offset];

        using var stream = new MemoryStream(fileBytes);
        using var archive = MpqArchive.Open(stream, loadListFile: true);

        int block = 0;
        foreach (var entry in archive)
        {
            string? name = entry.FileName;
            try
            {
                byte[] raw;
                using (var fs = archive.OpenFile(entry))
                {
                    using var ms = new MemoryStream();
                    fs.CopyTo(ms);
                    raw = ms.ToArray();
                }

                bool known = name is not null && MapFormatRegistry.IsKnown(name);
                doc._files.Add(new MapFileEntry
                {
                    FileName = name,
                    BlockIndex = block++,
                    RawBytes = raw,
                    IsKnown = known,
                });
            }
            catch (Exception ex)
            {
                // Unreadable entry (encrypted/unnamed/corrupt): keep a placeholder
                // instead of aborting Load. The real bytes are preserved because
                // Save rebuilds via MpqArchiveBuilder(originalArchive).
                doc._diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, name ?? "(unnamed)",
                    $"Could not read file data, preserved via archive rebuild: {ex.Message}"));
                doc._files.Add(new MapFileEntry
                {
                    FileName = name,
                    BlockIndex = block++,
                    RawBytes = Array.Empty<byte>(),
                    IsKnown = false,
                });
            }
        }

        doc.ParseKnownFiles();
        return doc;
    }

    private void ParseKnownFiles()
    {
        foreach (var entry in _files)
        {
            if (!entry.IsKnown) continue; // unreadable/placeholder entries have empty bytes
            if (entry.FileName is null || !MapFormatRegistry.TryGetParser(entry.FileName, out var parse))
                continue;
            try
            {
                entry.Model = parse(entry.RawBytes);
            }
            catch (Exception ex)
            {
                _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, entry.FileName,
                    $"Parse failed, preserved as raw: {ex.Message}"));
            }
        }
    }

    public MapFileEntry? GetFile(string fileName) =>
        _files.FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    public void Save(string path) => File.WriteAllBytes(path, SaveToBytes());

    public byte[] SaveToBytes()
    {
        using var source = new MemoryStream(_originalBytes);
        using var archive = MpqArchive.Open(source, loadListFile: true);
        var builder = new MpqArchiveBuilder(archive);

        foreach (var entry in _files.Where(f => f.IsDirty && f.FileName is not null))
        {
            builder.RemoveFile(entry.FileName!);
            builder.AddFile(MpqFile.New(new MemoryStream(SerializeEntry(entry)), entry.FileName!));
        }

        using var mpq = new MemoryStream();
        // SaveTo disposes the target stream unless leaveOpen — we still need to read it back.
        builder.SaveTo(mpq, leaveOpen: true);

        using var outStream = new MemoryStream();
        outStream.Write(PreArchiveData, 0, PreArchiveData.Length);
        mpq.Position = 0;
        mpq.CopyTo(outStream);
        return outStream.ToArray();
    }

    // No editing this slice, so dirty files never occur; serialization is a later slice.
    private static byte[] SerializeEntry(MapFileEntry entry) =>
        throw new NotSupportedException("Editing/serialization arrives in a later slice.");
}
