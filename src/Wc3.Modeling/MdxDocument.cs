// src/Wc3.Modeling/MdxDocument.cs
using System.Buffers.Binary;
using System.Text;

namespace Wc3.Modeling;

/// <summary>
/// A binary MDX as its chunk list, so a model can be edited and written back with every chunk it
/// does not touch preserved byte for byte. The reader the rest of the toolkit uses (<see
/// cref="MdxParser"/>) extracts to the renderer-neutral <see cref="Model3D"/> and drops what it
/// does not draw. That is lossy and cannot be written back. This keeps every chunk raw, so an
/// optimiser can replace one GEOS payload and leave materials, sequences, bones, emitters and
/// cameras exactly as they were.
///
/// Round-trip is exact for a well-formed file, because a chunk is a tag, a size and its payload and
/// MDX has no padding between them.
/// </summary>
public sealed class MdxDocument
{
    /// <summary>One chunk, its four character tag and its payload without the tag or the size field.</summary>
    public sealed record Chunk(string Tag, byte[] Payload);

    private static readonly byte[] Magic = "MDLX"u8.ToArray();

    private readonly List<Chunk> _chunks;

    private MdxDocument(List<Chunk> chunks) => _chunks = chunks;

    /// <summary>Every chunk in file order.</summary>
    public IReadOnlyList<Chunk> Chunks => _chunks;

    /// <summary>True when the bytes carry the MDLX magic.</summary>
    public static bool HasMagic(byte[] bytes) =>
        bytes is { Length: >= 4 } && bytes[0] == Magic[0] && bytes[1] == Magic[1]
        && bytes[2] == Magic[2] && bytes[3] == Magic[3];

    /// <summary>
    /// Reads a binary MDX into its chunk list. A truncated trailing chunk stops the walk and keeps
    /// the chunks read so far rather than throwing, matching the parser's tolerance.
    /// </summary>
    public static MdxDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!HasMagic(bytes))
            throw new InvalidDataException("not an MDX file, no MDLX magic");

        var chunks = new List<Chunk>();
        int p = 4;
        while (p + 8 <= bytes.Length)
        {
            var tag = Encoding.ASCII.GetString(bytes, p, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p + 4, 4));
            int start = p + 8;
            if (size > int.MaxValue || start + (long)size > bytes.Length) break; // truncated
            var payload = new byte[size];
            Array.Copy(bytes, start, payload, 0, (int)size);
            chunks.Add(new Chunk(tag, payload));
            p = start + (int)size;
        }

        return new MdxDocument(chunks);
    }

    /// <summary>
    /// Builds a document from an ordered chunk list, for a model created from scratch rather than
    /// parsed. <see cref="Write"/> then emits the MDLX magic and each tag and size.
    /// </summary>
    public static MdxDocument Create(IReadOnlyList<Chunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        return new MdxDocument(new List<Chunk>(chunks));
    }

    /// <summary>Replaces the payload of the chunk at <paramref name="index"/>, for an edit.</summary>
    public void Replace(int index, byte[] payload)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _chunks.Count);
        _chunks[index] = _chunks[index] with { Payload = payload };
    }

    /// <summary>Serializes the chunk list back to MDX bytes, exactly reversing <see cref="Parse"/>.</summary>
    public byte[] Write()
    {
        long total = 4;
        foreach (var c in _chunks) total += 8 + c.Payload.Length;
        var outBytes = new byte[total];

        Magic.CopyTo(outBytes, 0);
        int p = 4;
        foreach (var c in _chunks)
        {
            Encoding.ASCII.GetBytes(c.Tag, outBytes.AsSpan(p, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(outBytes.AsSpan(p + 4, 4), (uint)c.Payload.Length);
            c.Payload.CopyTo(outBytes, p + 8);
            p += 8 + c.Payload.Length;
        }
        return outBytes;
    }

    /// <summary>The indices of the GEOS chunks, the drawable geometry the optimiser reduces.</summary>
    public IReadOnlyList<int> GeosetIndices()
    {
        var list = new List<int>();
        for (int i = 0; i < _chunks.Count; i++)
            if (_chunks[i].Tag == "GEOS") list.Add(i);
        return list;
    }
}
