// src/Wc3.MapDocument/MapFileEntry.cs
namespace Wc3.Model;

public sealed class MapFileEntry
{
    // FileName/IsKnown are settable, not init-only: MapDocument.HarvestAssetNames names an
    // entry after construction, when a second-pass probe resolves a name Load could not.
    public required string? FileName { get; set; }     // null => unnamed (protected map)
    public required int BlockIndex { get; init; }

    private byte[]? _rawBytes;
    private Lazy<byte[]>? _deferred;
    private int _deferredSize;

    /// <summary>
    /// The entry's original DECOMPRESSED bytes. Decompression is deferred until first
    /// access, because measurement on a 240 MB map put 2.7 of Load's 3.3 seconds in
    /// decompressing all 7914 entries (339 MB, mostly models and music) that most
    /// callers never touch. Load parses the known formats eagerly, so those entries
    /// materialize during Load exactly as before, everything else materializes on
    /// first read. Fidelity is unaffected, Save rebuilds untouched entries from the
    /// original archive bytes and never reads this property for them.
    /// </summary>
    public byte[] RawBytes
    {
        get => _rawBytes ?? _deferred?.Value ?? Array.Empty<byte>();
        init => _rawBytes = value;
    }

    /// <summary>
    /// The size <see cref="RawBytes"/> has or will have, answered without triggering
    /// decompression (the deferred size comes from the archive's block table). Callers
    /// that sweep every entry for a size listing should use this, an all-entries sweep
    /// of <c>RawBytes.Length</c> silently pays the full decompression Load deferred.
    /// </summary>
    public int RawSize => _rawBytes?.Length
        ?? (_deferred is { IsValueCreated: true } d ? d.Value.Length : _deferredSize);

    /// <summary>True once the raw bytes exist in memory. Test hook for the laziness
    /// contract, production code should never need to ask.</summary>
    internal bool IsMaterialized => _rawBytes is not null || _deferred is { IsValueCreated: true };

    /// <summary>Arms deferred decompression. <paramref name="read"/> must be idempotent
    /// and safe to call from any thread (Lazy with ExecutionAndPublication runs it once).
    /// <paramref name="size"/> is the block table's decompressed size, served by
    /// <see cref="RawSize"/> until the bytes actually materialize.</summary>
    internal void DeferRawBytes(Lazy<byte[]> read, int size)
    {
        _deferred = read;
        _deferredSize = size;
    }

    public required bool IsKnown { get; set; }
    public object? Model { get; set; }

    /// <summary>
    /// Bytes past the end of what this file's parser consumed, preserved so that rebuilding the
    /// file from its model does not truncate it.
    ///
    /// Empty for almost every file. Not empty for the ones that matter: an object table ending in
    /// a trailing int32 zero that War3Net's writer omits, or a war3mapUnits.doo ending in a stray
    /// newline. Without this, editing one unit in such a map shortens the file by four bytes that
    /// nobody asked to remove. See MapFormatRegistry.ParsedModel.
    /// </summary>
    public byte[] UnreadTail { get; set; } = Array.Empty<byte>();
    public bool IsParsed => Model is not null;
    public bool IsDirty { get; set; }

    /// <summary>
    /// A raw byte payload that, when set, is written verbatim on the next Save
    /// (taking precedence over any <see cref="Model"/> serialization). Used to add
    /// imported assets, replace the script text, or inject a file whose format has
    /// no byte-faithful model writer. <see cref="RawBytes"/> stays the immutable
    /// original bytes; this is the pending replacement.
    /// </summary>
    public byte[]? OverrideBytes { get; set; }

    /// <summary>
    /// What this entry's bytes are NOW, the pending replacement if there is one and the
    /// original bytes otherwise. This is what Save will write, so it is what a reader that
    /// asks "what does this file contain" almost always means.
    /// </summary>
    /// <remarks>
    /// This exists because the choice was being made 67 times by hand. 39 sites wrote
    /// <c>OverrideBytes ?? RawBytes</c> inline, one of them behind a private helper of its
    /// own, and 28 read <see cref="RawBytes"/> bare. The bare reads are not a style
    /// difference, they are a stale read, and several were user-visible. The Files panel
    /// previewed and hex-dumped the pre-edit content of any file another panel had just
    /// changed. Lint asked "does the script compile" of the script on disk rather than the
    /// script about to be saved, so a broken edit passed. Extract wrote the old bytes.
    ///
    /// Reach for <see cref="RawBytes"/> only when the ORIGINAL bytes are the point, which
    /// means round-trip fidelity checks and nothing else. Every other reader wants this.
    /// </remarks>
    public byte[] CurrentBytes => OverrideBytes ?? RawBytes;

    /// <summary>
    /// The size <see cref="CurrentBytes"/> has, answered without triggering decompression
    /// when nothing has replaced the entry. The deferred-decompression counterpart of
    /// <see cref="RawSize"/> for callers that sweep every entry.
    /// </summary>
    public int CurrentSize => OverrideBytes?.Length ?? RawSize;
}
