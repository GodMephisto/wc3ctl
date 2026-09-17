// src/Wc3.MapDocument/MapDocument.cs
using War3Net.Build.Audio;
using War3Net.Build.Environment;
using War3Net.Build.Extensions;
using War3Net.Build.Import;
using War3Net.Build.Object;
using War3Net.Build.Widget;
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

        // The locale of the hash slot each block is reached through. Needed because a protected
        // map can point two slots at two different blocks under one name, and an edit written
        // under the wrong locale lands in a slot the name does not resolve to.
        // Deliberately NOT filtered on IsAvailable. The slot carrying the odd locale in a
        // protected map does not report as available, and filtering it out made every entry read
        // back as Neutral, which is exactly the bug this exists to fix. Empty and deleted slots
        // hold a block index of 0xFFFFFFFF or 0xFFFFFFFE, which never matches a real block, so
        // letting them through costs nothing.
        var localeByBlock = new Dictionary<uint, uint>();
        foreach (var hash in archive.EnumerateHashes())
            localeByBlock.TryAdd(hash.BlockIndex, (uint)hash.Locale);

        int block = 0;
        foreach (var entry in archive)
        {
            string? name = entry.FileName;
            uint locale = localeByBlock.TryGetValue((uint)block, out var l) ? l : 0u;
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
                    Locale = locale,
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
                    Locale = locale,
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

        // An added file with the same hashed name shadows the original at save. RemoveFile must
        // NOT be called first, because its removal set also filters the replacement, dropping the
        // file from the archive entirely.
        //
        // The hash covers the locale as well as the name, so the entry's own locale has to go with
        // it. A protected map can hold two slots for one name, one Neutral and one carrying
        // 0xFF000000, and adding under Neutral alone shadowed only one of them. The other kept its
        // original bytes, the name resolved to that stale copy, and the edit vanished on reload
        // while every command still reported it applied.
        foreach (var entry in _files.Where(f => f.IsDirty && f.FileName is not null))
            builder.AddFile(MpqFile.New(
                new MemoryStream(SerializeEntry(entry)), entry.FileName!, (MpqLocale)entry.Locale));

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
        entry.OverrideBytes = null; // a fresh model supersedes any pending raw override
        entry.IsDirty = true;
    }

    /// <summary>
    /// Adds a file with the given raw bytes, or replaces an existing file's bytes
    /// verbatim (the payload is written unchanged on Save). Used for imported assets,
    /// script text, or any format without a byte-faithful model writer.
    /// </summary>
    /// <summary>
    /// Writes a raw payload under <paramref name="fileName"/>, adding the file when it is absent.
    /// </summary>
    /// <remarks>
    /// An MPQ addresses a file by the hash of its name AND its locale, so one archive can hold
    /// several entries that all report this same name. This used to resolve the name with GetFile,
    /// which is FirstOrDefault, attach the override to whichever entry loaded first, and leave the
    /// rest holding their original bytes. Save writes every entry, the name then resolves to one of
    /// the untouched copies, and the edit is gone while having been reported as applied.
    ///
    /// Measured on Naruto Autobattle ENGv3ch11.w3x, where six files are duplicated this way. The
    /// Reforged 3.0.0 repair reported 973 button positions completed, saved, and a second pass over
    /// its own output found the same 973 still malformed.
    ///
    /// So every entry sharing the name is written. Which one the game resolves to depends on its
    /// locale, which is not knowable here, and the only safe answer is that they all agree.
    /// </remarks>
    public MapFileEntry AddOrReplaceRawFile(string fileName, byte[] bytes)
    {
        var existing = _files
            .Where(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (existing.Count > 0)
        {
            foreach (var duplicate in existing)
            {
                duplicate.OverrideBytes = bytes;
                duplicate.Model = null; // raw payload wins; drop any stale parsed model
                duplicate.IsDirty = true;
            }
            return existing[0];
        }
        var entry = new MapFileEntry
        {
            FileName = fileName,
            BlockIndex = NextBlockIndex(),
            RawBytes = bytes,
            IsKnown = MapFormatRegistry.IsKnown(fileName),
            OverrideBytes = bytes,
            IsDirty = true,
        };
        _files.Add(entry);
        return entry;
    }

    /// <summary>
    /// Adds a file backed by a typed model, or replaces an existing file's model.
    /// Serialized on Save via <see cref="SerializeEntry"/> (only byte-faithful formats
    /// are supported — see that method). Used to inject/replace object-data files, etc.
    /// </summary>
    public MapFileEntry AddOrReplaceModelFile(string fileName, object model)
    {
        if (GetFile(fileName) is { } existing)
        {
            existing.Model = model;
            existing.OverrideBytes = null;
            existing.IsDirty = true;
            return existing;
        }
        var entry = new MapFileEntry
        {
            FileName = fileName,
            BlockIndex = NextBlockIndex(),
            RawBytes = Array.Empty<byte>(),
            IsKnown = MapFormatRegistry.IsKnown(fileName),
            Model = model,
            IsDirty = true,
        };
        _files.Add(entry);
        return entry;
    }

    private int NextBlockIndex() => _files.Count == 0 ? 0 : _files.Max(f => f.BlockIndex) + 1;

    // Serialization by model type. A raw OverrideBytes payload always wins; otherwise
    // the model's War3Net writer is used. Only formats with a byte-faithful writer are
    // handled — anything else must stay non-dirty (raw) so Save preserves original bytes.
    private static byte[] SerializeEntry(MapFileEntry entry)
    {
        // Raw override (added assets, replaced script text, non-faithful formats).
        if (entry.OverrideBytes is not null) return entry.OverrideBytes;

        // Typed models with a byte-faithful War3Net writer (all seven object-data
        // kinds share the ReadXxxObjectData/Write(xxx) round-trip; skin twins carry
        // the same model types, so dispatch on the model, not the file name).
        switch (entry.Model)
        {
            case UnitObjectData m: return WriteBinary(w => w.Write(m));
            case AbilityObjectData m: return WriteBinary(w => w.Write(m));
            case ItemObjectData m: return WriteBinary(w => w.Write(m));
            case DestructableObjectData m: return WriteBinary(w => w.Write(m));
            case DoodadObjectData m: return WriteBinary(w => w.Write(m));
            case BuffObjectData m: return WriteBinary(w => w.Write(m));
            case UpgradeObjectData m: return WriteBinary(w => w.Write(m));
            case ImportedFiles m: return WriteBinary(w => w.Write(m));
            // Widget placement files (war3mapUnits.doo / war3map.doo). The read path
            // (Parsers.cs) parses these into MapUnits/MapDoodads, so the write path must
            // match or a dirtied placement edit would hit the throw below. Confirmed via
            // reflection: BinaryWriterExtensions.Write(BinaryWriter, MapUnits/MapDoodads).
            case MapUnits m: return WriteBinary(w => w.Write(m));
            case MapDoodads m: return WriteBinary(w => w.Write(m));
            // Pathing map (war3map.wpm). Read via ReadMapPathingMap (Parsers.cs); write
            // confirmed via reflection: BinaryWriterExtensions.Write(BinaryWriter, MapPathingMap).
            case MapPathingMap m: return WriteBinary(w => w.Write(m));
            // Regions (war3map.w3r). Read via ReadMapRegions (Parsers.cs); write
            // confirmed via reflection: BinaryWriterExtensions.Write(BinaryWriter, MapRegions).
            case MapRegions m: return WriteBinary(w => w.Write(m));
            // Terrain environment (war3map.w3e). Read via ReadMapEnvironment (Parsers.cs);
            // byte-faithfulness of the War3Net writer is pinned by
            // MapWriteTests.Environment_writer_is_byte_faithful_on_real_map.
            case MapEnvironment m: return WriteBinary(w => w.Write(m));
            // Sound definitions (war3map.w3s). Read via ReadMapSounds (Parsers.cs); the
            // byte-faithful writer BinaryWriterExtensions.Write(BinaryWriter, MapSounds)
            // was confirmed by MapSoundsProbe (reflection dump) and is pinned by
            // MapWriteTests.Sounds_writer_is_byte_faithful_on_real_map.
            case MapSounds m: return WriteBinary(w => w.Write(m));
        }

        // A dirty entry with no override and no supported model would silently lose
        // the caller's intent if we passed the original bytes through — fail loudly.
        // Formats without a byte-faithful model writer (e.g. war3map.wts) must be
        // written via AddOrReplaceRawFile instead.
        throw new NotSupportedException(
            $"No byte-faithful serializer for '{entry.FileName ?? "(unnamed)"}' " +
            $"(model: {entry.Model?.GetType().Name ?? "none"}). Use AddOrReplaceRawFile for raw payloads.");
    }

    private static byte[] WriteBinary(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            write(writer);
        return ms.ToArray();
    }
}
