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
    };

    public static bool IsKnown(string fileName) => _parsers.ContainsKey(fileName);

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

    public static object ReadWith<T>(byte[] raw, Func<BinaryReader, T> read)
    {
        using var ms = new MemoryStream(raw);
        using var reader = new BinaryReader(ms);
        return read(reader) ?? throw new InvalidDataException($"Parser for {typeof(T).Name} returned null.");
    }
}
