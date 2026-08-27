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

    // The archive Load opened, kept alive for the document's lifetime so a deferred
    // entry read (see MapFileEntry.RawBytes) can decompress on first access without
    // reopening, which costs ~70 ms on the 240 MB corpus map. It wraps a MemoryStream
    // over _originalBytes, so holding it pins no OS handle and no memory beyond what
    // _originalBytes already retains. Deferred reads seek this one shared stream, so
    // they serialize on _archiveLock.
    private MpqArchive? _archive;
    private readonly object _archiveLock = new();

    public IReadOnlyList<MapFileEntry> Files => _files;
    public byte[] PreArchiveData { get; private set; } = Array.Empty<byte>();

    /// <summary>
    /// Snapshot of the loader's diagnostics. A copy rather than the live list, because
    /// a deferred entry read can append from any thread (see <see cref="ReadEntryBytes"/>)
    /// while a caller enumerates.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics
    {
        get { lock (_diagnostics) return _diagnostics.ToArray(); }
    }

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

        // Not disposed here on purpose, the document keeps it for deferred entry
        // reads (see _archive). Disposing it would only dispose the MemoryStream.
        var stream = new MemoryStream(fileBytes);
        var archive = MpqArchive.Open(stream, loadListFile: true);
        doc._archive = archive;

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

            // Open-and-close classifies readability without decompressing anything.
            // OpenFile builds the sector table and derives the decryption key, which is
            // where an encrypted entry with an unrecoverable name fails, so this probe
            // reports at Load what the old eager read reported, at 9 ms for the corpus
            // map's 7914 entries where eagerly decompressing them cost 2.7 s. A failure
            // that only appears mid-decompression is still caught, by the deferred read,
            // which records the same diagnostic and serves the same empty placeholder.
            bool readable = true;
            try
            {
                archive.OpenFile(entry).Dispose();
            }
            catch (Exception ex)
            {
                // Unreadable entry (encrypted/unnamed/corrupt): keep a placeholder
                // instead of aborting Load. The real bytes are preserved because
                // Save rebuilds via MpqArchiveBuilder(originalArchive).
                readable = false;
                lock (doc._diagnostics)
                    doc._diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, name ?? "(unnamed)",
                        $"Could not read file data, preserved via archive rebuild: {ex.Message}"));
            }

            var file = new MapFileEntry
            {
                FileName = name,
                BlockIndex = block++,
                IsKnown = readable && name is not null && MapFormatRegistry.IsKnown(name),
            };
            if (readable)
                file.DeferRawBytes(new Lazy<byte[]>(
                        () => doc.ReadEntryBytes(entry),
                        LazyThreadSafetyMode.ExecutionAndPublication),
                    (int)entry.FileSize);
            doc._files.Add(file);
        }

        doc.ParseKnownFiles();
        return doc;
    }

    /// <summary>
    /// The deferred read behind <see cref="MapFileEntry.RawBytes"/>. Serialized on
    /// <see cref="_archiveLock"/> because every MpqStream pulls its sectors from the
    /// archive's one underlying stream, and a Studio panel on the UI thread can race
    /// a port running in a background task. A failure here records the diagnostic the
    /// eager Load used to record, just at first access instead of at Load.
    /// </summary>
    private byte[] ReadEntryBytes(MpqEntry entry)
    {
        lock (_archiveLock)
        {
            try
            {
                using var fs = _archive!.OpenFile(entry);
                using var ms = new MemoryStream((int)entry.FileSize);
                fs.CopyTo(ms);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                lock (_diagnostics)
                    _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, entry.FileName ?? "(unnamed)",
                        $"Could not read file data, preserved via archive rebuild: {ex.Message}"));
                return Array.Empty<byte>();
            }
        }
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
            lock (_diagnostics)
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
            // Latin-1: this scan looks for asset PATHS in string literals, and a path containing
            // a non-ASCII character would decode to U+FFFD under UTF-8 and silently never become a
            // candidate. That presents as a missing imported asset, not as a decoding fault, which
            // is a bad failure to debug. Nothing is written back here, so this only affects what
            // the scan can see.
            var text = Encoding.Latin1.GetString(script.RawBytes);
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

    /// <summary>
    /// The named entry, falling back to the same file under a directory a compiler is known to
    /// move it to. An exact match always wins, so a map holding both keeps the root one.
    /// </summary>
    /// <remarks>
    /// Measured over 34 maps in the user's Maps folder, 13 store their script as
    /// <c>scripts\war3map.j</c> rather than at the archive root, which is what the widely used map
    /// optimizer emits. The prober in <see cref="StandardMapFileNames"/> already knew that and
    /// recovered the name, so <c>ls</c> listed the file, but every caller that asked for
    /// "war3map.j" got nothing. <c>extract war3map.j</c> answered "file not found" for a file it
    /// had just listed, and <c>validate</c> answered "map has no script file, it cannot run" and
    /// exited 2, on 13 working maps.
    ///
    /// The alias list is deliberately narrow and derived from measurement, not a general search of
    /// every directory for a matching leaf name. Only two known map files ever appear under a
    /// directory in that corpus, this one and <c>war3mapImported\war3mapMap.blp</c>, and the
    /// second is a genuine second copy of the minimap rather than the same file moved.
    /// </remarks>
    public MapFileEntry? GetFile(string fileName)
    {
        if (GetFileExact(fileName) is { } exact) return exact;

        foreach (var alias in StandardMapFileNames.AliasesFor(fileName))
        {
            var hit = _files.FirstOrDefault(
                f => string.Equals(f.FileName, alias, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>
    /// The named entry by its exact name, with no alias fallback. What every WRITE must use.
    /// </summary>
    /// <remarks>
    /// A read may reasonably answer "war3map.j" with the script stored under scripts\. A write
    /// may not. Adding scripts\war3map.j to a map that already has a root war3map.j would resolve
    /// through the alias and silently overwrite the root file instead of adding the nested one,
    /// which is how a map that carries both, and two in the measured corpus do, would lose the one
    /// the game actually reads. Caught by a test the same hour the alias was added.
    /// </remarks>
    private MapFileEntry? GetFileExact(string fileName) =>
        _files.FirstOrDefault(
            f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));

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
        var (dropListFile, dropAttributes) = BookkeepingToDrop();
        if (dropListFile || dropAttributes)
            builder.SaveTo(mpq, new MpqArchiveCreateOptions
            {
                HashTableSize = grown,
                ListFileCreateMode = dropListFile ? MpqFileCreateMode.Prune : MpqFileCreateMode.Overwrite,
                AttributesCreateMode = dropAttributes ? MpqFileCreateMode.Prune : MpqFileCreateMode.Overwrite,
            }, leaveOpen: true);
        else if (grown is null)
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
    /// Drops the optional MPQ bookkeeping files, <c>(listfile)</c> and <c>(attributes)</c>,
    /// instead of regenerating them. Warcraft III needs neither: it resolves a map's files
    /// through the hash table by name, and treats attributes as advisory.
    /// </summary>
    /// <remarks>
    /// Diagnostic switch, off by default. Regenerating <c>(attributes)</c> rewrites a CRC32
    /// per file, and a rebuild that recompresses even a handful of entries changes their
    /// stored bytes, so a stale or disagreeing CRC is a load-time failure no content-level
    /// check can see. Dropping both files removes that whole class of doubt from a rebuild.
    /// Env var so it can be flipped for a single run without a rebuild or an API change.
    /// </remarks>
    internal static (bool ListFile, bool Attributes) BookkeepingToDrop() =>
        ParseBookkeeping(Environment.GetEnvironmentVariable("WC3CTL_DROP_MPQ_BOOKKEEPING"));

    /// <summary>
    /// Parses the drop selection. <c>attributes</c> is the one worth reaching for, it holds the
    /// per-file CRC32 a rebuild can invalidate. Dropping <c>listfile</c> costs real capability:
    /// an entry with a non-standard name becomes unnameable, so ls cannot list it and extract
    /// cannot find it, even though the game still resolves it by hashing the name.
    /// </summary>
    internal static (bool ListFile, bool Attributes) ParseBookkeeping(string? value) =>
        (value?.Trim().ToLowerInvariant()) switch
        {
            "attributes" => (false, true),
            "listfile" => (true, false),
            "both" or "1" => (true, true),
            _ => (false, false),
        };

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

        // Escape hatch. Growing the table is a real change to the container, and a protected
        // archive can hold entries whose names we never recover, which cannot be re-placed into a
        // table of a different size because the slot is derived from the name. Set this to keep
        // the source's own capacity untouched.
        if (Environment.GetEnvironmentVariable("WC3CTL_NO_HASH_GROWTH") == "1")
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
        // Exact, never aliased. See GetFileExact.
        if (GetFileExact(fileName) is { } existing)
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
        // Exact, never aliased. See GetFileExact.
        if (GetFileExact(fileName) is { } existing)
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

        // A Reforged (version 3) object-data model must carry the per-object modification
        // set prefix on EVERY group, including the ones a caller just built in memory.
        // Without it War3Net emits setCount 0 and the game's parser derails at the first
        // added object, which crashed Warcraft III before main ran (see ObjectDataSets).
        // Seeded here rather than at each group-creating call site because this is the one
        // choke point every object-data write passes through, so no future writer can
        // reintroduce the crash. No-op for other models and for already-prefixed groups.
        ObjectDataSets.EnsureSetPrefixes(entry.Model);

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
            // GUI trigger tree (war3map.wtg) and the custom-text bodies (war3map.wct). Read via
            // ReadMapTriggers/ReadMapCustomTextTriggers (Parsers.cs); the inverse writers exist as
            // BinaryWriterExtensions.Write(BinaryWriter, MapTriggers) and (..., MapCustomTextTriggers),
            // confirmed by reflection against War3Net.Build.Core 6.0.3 and pinned for
            // byte-faithfulness by MapWriteTests.
            //
            // Worth stating why this case matters beyond completeness. Without it, editing a map's
            // compiled script while leaving its trigger tree behind is a ONE-WAY operation: the two
            // disagree, and the next World Editor save regenerates the script from the stale tree and
            // silently discards the edit.
            case War3Net.Build.Script.MapTriggers m: return WriteBinary(w => w.Write(m));
            // war3map.wct is deliberately NOT here. War3Net ships a writer for it, and that writer
            // is not byte-faithful: a real map measured 549,168 bytes in and 549,170 out, a 2 byte
            // gain on an untouched file. Adding the case would grow the file a little on every
            // save of a map nobody had even edited. Pinned by
            // TriggerWriteTests.Custom_text_triggers_have_no_model_writer_because_it_is_not_faithful.
            // The custom-text bodies stay raw, which loses nothing, since they are read for
            // inspection and edited through the script rather than through this model.
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
