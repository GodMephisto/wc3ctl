// src/Wc3.MapDocument/MapDocument.cs
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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

        // A protected map's (listfile) is often stripped or curated down to a couple of
        // decoy names, but the hash table that actually locates a file is untouched, a
        // standard WC3 file name still resolves without it. AddFileNames only sets
        // FileName on entries that are still unnamed, so a healthy map (everything already
        // named from its own listfile) is unaffected.
        archive.AddFileNames(StandardMapFileNames.All);

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
            if (entry.IsKnown) TryParse(entry); // unreadable/placeholder entries have empty bytes
    }

    // Shared with HarvestAssetNames, which calls this for an entry that only became known
    // (in the essentially-never case a harvested asset name collides with a structural one)
    // after Load already ran its own pass.
    private void TryParse(MapFileEntry entry)
    {
        if (entry.FileName is null || !MapFormatRegistry.TryGetParser(entry.FileName, out var parse))
            return;
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

    /// <summary>
    /// Second-pass name recovery for imported assets. <see cref="Load"/> unconditionally probes
    /// <see cref="StandardMapFileNames"/>, there are only a few dozen of those and a healthy
    /// map's already-fully-named archive makes the probe a no-op either way. Assets are
    /// different, an import folder can carry thousands of author-chosen names a fixed list
    /// could never guess. So instead this harvests CANDIDATE names from content the map itself
    /// already makes readable, every asset-shaped string literal in the script, and every
    /// asset-shaped field value in the map's own object data, expands each into the spellings
    /// Warcraft actually loads (<see cref="AssetPathCandidates"/>), and probes the archive for
    /// those the same way Load probes for standard names.
    ///
    /// This can only find something once the script and/or object data are themselves already
    /// named, nothing to scan otherwise, so it is a deliberate second pass a caller opts into,
    /// not part of Load. Scanning a multi-megabyte script and a map's full object data is real
    /// work, worth paying only when a caller actually wants this map's assets (a port, an
    /// extract, a Studio asset browse), so a plain ls/info/object query never pays for it, and
    /// neither does a map whose script and object data are still unreadable.
    /// </summary>
    /// <returns>How many previously unnamed entries this call named.</returns>
    public int HarvestAssetNames()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Harvest(string? raw)
        {
            if (string.IsNullOrEmpty(raw) || !AssetPathCandidates.LooksLikeAssetPath(raw)) return;
            foreach (var c in AssetPathCandidates.Expand(raw)) candidates.Add(c);
        }

        // Source 1: string literals in the script. Whichever of the two script languages, and
        // whichever spelling the archive stores it under (see StandardMapFileNames), only the
        // text matters here — deliberately unscoped (every literal, not one hero's reachable
        // code, the way Wc3.Commands.BundleCommand's closure-scoped scan must be to avoid
        // mis-attributing an asset to the wrong hero). Over-collecting a candidate here costs
        // nothing worse than a failed hash lookup below.
        var script = GetFile("war3map.j") ?? GetFile("scripts\\war3map.j")
            ?? GetFile("war3map.lua") ?? GetFile("scripts\\war3map.lua");
        if (script is { RawBytes.Length: > 0 })
        {
            var text = Encoding.UTF8.GetString(script.RawBytes);
            foreach (Match m in AssetStringLiteral.Matches(text))
                Harvest(AssetPathCandidates.Unescape(m.Groups[1].Value));
        }

        // Source 2: path-like field values inside the map's own object data (all seven kinds,
        // Reforged skin twins included, they parse into the same models). No game-data
        // metadata is available at this layer, unlike the porter's field scan this only needs
        // a coarse "might be a path" shape test, never attribution to a specific field or hero.
        foreach (var entry in _files)
            if (entry.Model is not null)
                foreach (var value in ObjectDataFieldValues(entry.Model))
                    Harvest(value);

        if (candidates.Count == 0) return 0;

        using var stream = new MemoryStream(_originalBytes);
        using var archive = MpqArchive.Open(stream, loadListFile: true);
        archive.AddFileNames(StandardMapFileNames.All); // keep this archive's naming in step with Load's
        archive.AddFileNames(candidates);

        // Fold newly resolved names back by BlockIndex (not position — a caller may have added
        // entries beyond the archive's own since Load, those carry no BlockIndex this archive
        // enumerates and are correctly left alone).
        var byBlock = _files.Where(f => f.BlockIndex < archive.Count).ToDictionary(f => f.BlockIndex);
        int named = 0, i = 0;
        foreach (var entry in archive)
        {
            if (entry.FileName is not null && byBlock.TryGetValue(i, out var mine) && mine.FileName is null)
            {
                mine.FileName = entry.FileName;
                mine.IsKnown = MapFormatRegistry.IsKnown(entry.FileName);
                if (mine.IsKnown) TryParse(mine);
                named++;
            }
            i++;
        }
        return named;
    }

    /// <summary>A double-quoted JASS/Lua string literal (captures the inner text), used by
    /// <see cref="HarvestAssetNames"/> to pull candidate asset paths out of the whole script.</summary>
    private static readonly Regex AssetStringLiteral = new("\"([^\"]*)\"", RegexOptions.Compiled);

    // War3Net shape confirmed against the same three modification kinds Wc3.Commands.ObjectKinds
    // normalizes (Simple: w3u/w3t/w3b/w3h, Level: w3a/w3q, Variation: w3d); read directly here
    // rather than through that layer, Wc3.MapDocument must not depend on Wc3.Commands.
    private static IEnumerable<string> ObjectDataFieldValues(object model) => model switch
    {
        UnitObjectData m => m.BaseUnits.Concat(m.NewUnits).SelectMany(x => x.Modifications)
            .Select(x => Convert.ToString(x.Value, CultureInfo.InvariantCulture) ?? ""),
        ItemObjectData m => m.BaseItems.Concat(m.NewItems).SelectMany(x => x.Modifications)
            .Select(x => Convert.ToString(x.Value, CultureInfo.InvariantCulture) ?? ""),
        AbilityObjectData m => m.BaseAbilities.Concat(m.NewAbilities).SelectMany(x => x.Modifications)
            .Select(x => Convert.ToString(x.Value, CultureInfo.InvariantCulture) ?? ""),
        DestructableObjectData m => m.BaseDestructables.Concat(m.NewDestructables).SelectMany(x => x.Modifications)
            .Select(x => Convert.ToString(x.Value, CultureInfo.InvariantCulture) ?? ""),
        DoodadObjectData m => m.BaseDoodads.Concat(m.NewDoodads).SelectMany(x => x.Modifications)
            .Select(x => Convert.ToString(x.Value, CultureInfo.InvariantCulture) ?? ""),
        BuffObjectData m => m.BaseBuffs.Concat(m.NewBuffs).SelectMany(x => x.Modifications)
            .Select(x => Convert.ToString(x.Value, CultureInfo.InvariantCulture) ?? ""),
        UpgradeObjectData m => m.BaseUpgrades.Concat(m.NewUpgrades).SelectMany(x => x.Modifications)
            .Select(x => Convert.ToString(x.Value, CultureInfo.InvariantCulture) ?? ""),
        _ => Enumerable.Empty<string>(),
    };

    public MapFileEntry? GetFile(string fileName) =>
        _files.FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    public void Save(string path) => File.WriteAllBytes(path, SaveToBytes());

    public byte[] SaveToBytes()
    {
        using var source = new MemoryStream(_originalBytes);
        using var archive = MpqArchive.Open(source, loadListFile: true);
        // Same probing as Load, kept in sync so a dirty entry on a protected map (named
        // only via the probe, never via this archive's own listfile) still shadows its
        // original by the same hashed name when the builder adds the replacement.
        archive.AddFileNames(StandardMapFileNames.All);
        var builder = new MpqArchiveBuilder(archive);

        // An added file with the same hashed name shadows the original at save;
        // RemoveFile must NOT be called first — its removal set also filters the
        // replacement, dropping the file from the archive entirely.
        foreach (var entry in _files.Where(f => f.IsDirty && f.FileName is not null))
            builder.AddFile(MpqFile.New(new MemoryStream(SerializeEntry(entry)), entry.FileName!));

        var grown = GrownHashTableSize(OriginalHashTableSize(), _files.Count);

        using var mpq = new MemoryStream();
        // SaveTo disposes the target stream unless leaveOpen — we still need to read it back.
        // The two overloads do NOT agree on their bookkeeping defaults: passing an options
        // object changes the emitted (listfile)/(attributes) even when only HashTableSize
        // is set on it. So take the plain overload untouched whenever the inherited table
        // is safe, which keeps every already-working map byte-for-byte as it was.
        if (grown is null)
            builder.SaveTo(mpq, leaveOpen: true);
        else
            builder.SaveTo(mpq, new MpqArchiveCreateOptions { HashTableSize = grown }, leaveOpen: true);

        using var outStream = new MemoryStream();
        outStream.Write(PreArchiveData, 0, PreArchiveData.Length);
        mpq.Position = 0;
        mpq.CopyTo(outStream);
        return outStream.ToArray();
    }

    // MPQ hash tables are power-of-two sized and resolve names by linear probing, so a
    // nearly-full table makes every lookup miss walk a long chain of occupied slots.
    // Inheriting the source archive's size is the trap: a big map can ship at ~96%
    // occupancy and still work, because its entries were placed by whatever tool built
    // it, but a rebuild re-places all of them and the new chains can be catastrophic.
    // The symptom is specific and was hard to find - the map hosts fine (the lobby reads
    // only a handful of files) and then stalls forever on the loading screen, while every
    // content-level check passes because no file's bytes changed. Keeping the table at
    // most half full means a rebuild can never inherit that failure mode.
    // 32768 is the ceiling here, not a judgement call. War3Net types HashTableSize as
    // ushort, so 65536 is unrepresentable and 2^15 is the largest valid power of two.
    const ushort MaxHashTableSize = 32768;

    // Above this occupancy a rebuild is unsafe (3/4, expressed as a ratio to stay integer).
    const int OccupancyNumerator = 3;
    const int OccupancyDenominator = 4;

    /// <summary>
    /// The source archive's hash table capacity (slot count), or 0 if it cannot be read.
    /// </summary>
    /// <remarks>
    /// Read straight from the MPQ header rather than via <c>EnumerateHashes</c>, which
    /// yields the occupied entries and so reports occupancy, not capacity. The archive
    /// begins immediately after <see cref="PreArchiveData"/>, and the header lays out
    /// hash table size as a little-endian uint32 at offset 0x18.
    /// </remarks>
    uint OriginalHashTableSize()
    {
        const int HashTableSizeOffset = 0x18;
        var start = PreArchiveData.Length;
        if (start + HashTableSizeOffset + sizeof(uint) > _originalBytes.Length)
            return 0;
        if (BinaryPrimitives.ReadUInt32LittleEndian(_originalBytes.AsSpan(start, 4)) != MpqSignature)
            return 0;
        return BinaryPrimitives.ReadUInt32LittleEndian(
            _originalBytes.AsSpan(start + HashTableSizeOffset, sizeof(uint)));
    }

    // 'MPQ\x1A', the archive header magic.
    const uint MpqSignature = 0x1A51504D;

    /// <summary>
    /// The hash table size a rebuild should use, or <c>null</c> to keep the source's own.
    /// </summary>
    /// <remarks>
    /// MPQ resolves names by linear probing a power-of-two hash table, so a nearly-full
    /// table makes every lookup miss walk a long chain of occupied slots. Inheriting the
    /// source archive's size is the trap. A big map can ship at ~96% occupancy and still
    /// work, because its entries were placed by whatever tool built it, but a rebuild
    /// re-places all of them and the new chains can be catastrophic. The symptom is
    /// specific and was expensive to find. The map hosts fine (a lobby reads only a
    /// handful of files) and then stalls forever on the loading screen, while every
    /// content-level check passes because no file's bytes changed.
    /// <para>
    /// Returning null below the threshold is deliberate. Resizing every archive would
    /// change the bytes of maps that were never at risk and cost the byte-faithful
    /// guarantee for nothing, so this only intervenes where a rebuild would be unsafe.
    /// </para>
    /// </remarks>
    internal static ushort? GrownHashTableSize(uint originalSize, int fileCount)
    {
        if (originalSize == 0)
            return null;

        static bool Fits(long entries, long slots) =>
            entries * OccupancyDenominator <= slots * OccupancyNumerator;

        if (Fits(fileCount, originalSize))
            return null;

        // Already at the format ceiling: nothing to grow into, so leave it untouched
        // rather than emit a size the writer cannot represent.
        if (originalSize >= MaxHashTableSize)
            return null;

        var size = (ushort)originalSize;
        while (size < MaxHashTableSize && !Fits(fileCount, size))
            size <<= 1;
        return size;
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
    public MapFileEntry AddOrReplaceRawFile(string fileName, byte[] bytes)
    {
        if (GetFile(fileName) is { } existing)
        {
            existing.OverrideBytes = bytes;
            existing.Model = null; // raw payload wins; drop any stale parsed model
            existing.IsDirty = true;
            return existing;
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
