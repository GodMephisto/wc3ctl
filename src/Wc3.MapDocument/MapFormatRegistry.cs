// src/Wc3.MapDocument/MapFormatRegistry.cs
namespace Wc3.Model;

public delegate object ParseFn(byte[] raw);

public static class MapFormatRegistry
{
    // Known WC3 map files. Parse delegates are wired in Task 8; a null delegate
    // means "known but not yet parsed into a typed model" (degrades to raw).
    private static readonly Dictionary<string, ParseFn?> _parsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["war3map.w3e"] = null,          // terrain
        ["war3map.wpm"] = null,          // pathing
        ["war3map.doo"] = null,          // doodads
        ["war3mapUnits.doo"] = null,     // units/items
        ["war3map.w3i"] = null,          // info
        ["war3map.w3r"] = null,          // regions
        ["war3map.w3c"] = null,          // cameras
        ["war3map.w3s"] = null,          // sounds
        ["war3map.wtg"] = null,          // triggers
        ["war3map.wct"] = null,          // custom text triggers
        ["war3map.j"] = null,            // JASS script
        ["war3map.lua"] = null,          // Lua script
        ["war3map.wts"] = null,          // trigger strings
        ["war3map.imp"] = null,          // imports
        ["war3map.w3u"] = null,          // unit object data
        ["war3map.w3a"] = null,          // ability object data
        ["war3map.w3t"] = null,          // item object data
        ["war3map.w3b"] = null,          // destructable object data
        ["war3map.w3d"] = null,          // doodad object data
        ["war3map.w3h"] = null,          // buff object data
        ["war3map.w3q"] = null,          // upgrade object data
        // Reforged skin object-data layer: same binary formats as war3map.*.
        ["war3mapSkin.w3u"] = null,      // unit object data (skin)
        ["war3mapSkin.w3a"] = null,      // ability object data (skin)
        ["war3mapSkin.w3t"] = null,      // item object data (skin)
        ["war3mapSkin.w3b"] = null,      // destructable object data (skin)
        ["war3mapSkin.w3d"] = null,      // doodad object data (skin)
        ["war3mapSkin.w3h"] = null,      // buff object data (skin)
        ["war3mapSkin.w3q"] = null,      // upgrade object data (skin)
    };

    public static bool IsKnown(string fileName) => _parsers.ContainsKey(fileName);

    /// <summary>Every file name this registry recognizes. Reused as the base of the standard
    /// name list <see cref="StandardMapFileNames"/> probes into a protected archive, so the two
    /// lists cannot drift apart.</summary>
    public static IReadOnlyCollection<string> KnownFileNames => _parsers.Keys;

    public static bool TryGetParser(string fileName, out ParseFn parser)
    {
        if (_parsers.TryGetValue(fileName, out var p) && p is not null)
        {
            parser = p;
            return true;
        }
        parser = _ => throw new InvalidOperationException();
        return false;
    }

    internal static void Register(string fileName, ParseFn parser) => _parsers[fileName] = parser;

    /// <summary>
    /// A parsed model plus the bytes the parser never consumed.
    ///
    /// Real maps carry trailing bytes past the end of what the format's reader understands, and
    /// they are not noise to be discarded. Measured across the map library: six object tables on
    /// three maps end with a trailing int32 zero (an empty table count the World Editor writes and
    /// War3Net's writer omits), and one war3mapUnits.doo ends with a stray 0x0A. Re-serializing
    /// those models produces a strict PREFIX of the original file, so a user who edited a single
    /// unit would silently shorten the file.
    ///
    /// Carrying the tail turns byte-faithfulness into something stronger and simpler to state:
    /// everything we read is written back, and so is everything we did not understand.
    /// </summary>
    public sealed record ParsedModel(object Model, byte[] UnreadTail);

    public static object ReadWith<T>(byte[] raw, Func<BinaryReader, T> read)
    {
        using var ms = new MemoryStream(raw);
        using var reader = new BinaryReader(ms);
        var model = read(reader) ?? throw new InvalidDataException(
            $"Parser for {typeof(T).Name} returned null.");

        // Position is where the reader stopped, so anything after it was never looked at. Free to
        // capture here, and impossible to recover later once the model has been edited.
        long consumed = ms.Position;
        if (consumed >= raw.LongLength) return model;
        return new ParsedModel(model, raw.AsSpan((int)consumed).ToArray());
    }
}
