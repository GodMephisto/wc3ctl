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
}
