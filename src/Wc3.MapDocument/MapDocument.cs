// src/Wc3.MapDocument/MapDocument.cs
using War3Net.Build.Extensions;
using War3Net.Build.Object;
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

        // An added file with the same hashed name shadows the original at save;
        // RemoveFile must NOT be called first — its removal set also filters the
        // replacement, dropping the file from the archive entirely.
        foreach (var entry in _files.Where(f => f.IsDirty && f.FileName is not null))
            builder.AddFile(MpqFile.New(new MemoryStream(SerializeEntry(entry)), entry.FileName!));

        using var mpq = new MemoryStream();
        // SaveTo disposes the target stream unless leaveOpen — we still need to read it back.
        builder.SaveTo(mpq, leaveOpen: true);

        using var outStream = new MemoryStream();
        outStream.Write(PreArchiveData, 0, PreArchiveData.Length);
        mpq.Position = 0;
        mpq.CopyTo(outStream);
        return outStream.ToArray();
    }

    /// <summary>
    /// Installs an edited model on <paramref name="entry"/> and marks it dirty so the
    /// next Save re-serializes it (non-dirty entries keep their original bytes).
    /// </summary>
    public void ReplaceModel(MapFileEntry entry, object newModel)
    {
        if (!_files.Contains(entry))
            throw new ArgumentException("Entry does not belong to this document.", nameof(entry));
        entry.Model = newModel;
        entry.IsDirty = true;
    }

    // Only formats with a verified byte-faithful writer are serializable; everything
    // else must stay non-dirty so Save preserves its original bytes.
    private static byte[] SerializeEntry(MapFileEntry entry)
    {
        if (string.Equals(entry.FileName, "war3map.w3u", StringComparison.OrdinalIgnoreCase)
            && entry.Model is UnitObjectData w3u)
        {
            // Mirror of the ReadUnitObjectData parser; round-trips real maps
            // byte-identically (verified against the corpus).
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                writer.Write(w3u);
            return ms.ToArray();
        }
        throw new NotSupportedException(
            $"No serializer for '{entry.FileName ?? "(unnamed)"}' yet — only war3map.w3u is editable this slice.");
    }
}
