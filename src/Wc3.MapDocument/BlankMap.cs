// src/Wc3.MapDocument/BlankMap.cs
using System.Reflection;
using System.Text;
using War3Net.Build;
using War3Net.Build.Common;
using War3Net.Build.Environment;
using War3Net.Build.Extensions;
using War3Net.Build.Info;
using War3Net.IO.Mpq;

namespace Wc3.Model;

/// <summary>
/// Options controlling blank-map synthesis. Defaults produce a small
/// 32x32-tile Lordaeron Summer map named "Blank Map".
/// </summary>
public sealed record BlankMapOptions
{
    public string MapName { get; init; } = "Blank Map";
    public string MapAuthor { get; init; } = "wc3ctl";
    public string MapDescription { get; init; } = "Created with wc3ctl.";
    public string RecommendedPlayers { get; init; } = "1";

    /// <summary>Playable area is TileEdge x TileEdge tiles (TileEdge+1 vertices per side).</summary>
    public int TileEdge { get; init; } = 32;

    public MapInfoFormatVersion InfoVersion { get; init; } = MapInfoFormatVersion.v28;
    public MapEnvironmentFormatVersion EnvironmentVersion { get; init; } = MapEnvironmentFormatVersion.v11;

    /// <summary>Tileset code; 'L' = Lordaeron Summer.</summary>
    public char TilesetCode { get; init; } = 'L';
}

/// <summary>
/// Synthesizes a minimal, valid Warcraft III map archive from scratch.
///
/// <para>MapDocument.Load/Save operate on real MPQ archive bytes (Save rebuilds via
/// MpqArchiveBuilder over the original archive), so the cleanest way to make a blank
/// map a first-class MapDocument is to synthesize valid archive bytes here and feed
/// them straight through <see cref="MapDocument.Load(byte[])"/>. The resulting document
/// then Saves, parses and renders through every existing code path unchanged.</para>
///
/// <para>The recipe — a war3map.w3i (info) file plus a war3map.w3e (terrain) file packed
/// into an MPQ with a generated listfile — was verified empirically against the pinned
/// War3Net build by tests/Wc3.Tests/War3NetApiProbe.cs.</para>
///
/// <para>Follow-up increment for full external-World-Editor openability: prepend the
/// HM3W header (PreArchiveData) and add war3map.wpm (pathing) + a stub war3map.j script.</para>
/// </summary>
public static class BlankMap
{
    /// <summary>Synthesizes a blank map and returns it as a loaded <see cref="MapDocument"/>.</summary>
    public static MapDocument Create(BlankMapOptions? options = null)
        => MapDocument.Load(CreateArchiveBytes(options));

    /// <summary>Synthesizes the raw MPQ archive bytes for a blank map.</summary>
    public static byte[] CreateArchiveBytes(BlankMapOptions? options = null)
    {
        var o = options ?? new BlankMapOptions();
        if (o.TileEdge < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "TileEdge must be >= 1.");

        var enc = Encoding.UTF8;

        var info = new MapInfo(o.InfoVersion)
        {
            MapName = o.MapName,
            MapAuthor = o.MapAuthor,
            MapDescription = o.MapDescription,
            RecommendedPlayers = o.RecommendedPlayers,
        };
        EnsureSerializable(info);

        var env = BuildEnvironment(o.EnvironmentVersion, o.TileEdge, o.TilesetCode);

        var map = new Map { Info = info, Environment = env };
        var files = new List<MpqFile>
        {
            map.GetInfoFile(enc)!,
            map.GetEnvironmentFile(enc)!,
        };

        // Generate/overwrite the (listfile) so the named entries (war3map.w3i / .w3e)
        // are discoverable when the archive is re-opened by MapDocument.Load.
        var createOpts = new MpqArchiveCreateOptions
        {
            ListFileCreateMode = MpqFileCreateMode.Overwrite,
        };

        using var ms = new MemoryStream();
        using (MpqArchive.Create(ms, files, createOpts, leaveOpen: true)) { }
        return ms.ToArray();
    }

    private static MapEnvironment BuildEnvironment(MapEnvironmentFormatVersion version, int tileEdge, char tilesetCode)
    {
        // Width/Height are the map size in TILES; the terrain grid the serializer
        // writes (and the reader expects) is (Width+1) x (Height+1) corner points.
        // Setting Width=verts here would make the reader expect (verts+1)^2 points and
        // overrun the stream — the count must be tileEdge, with (tileEdge+1)^2 tiles.
        uint verts = (uint)(tileEdge + 1);
        var env = new MapEnvironment(version)
        {
            IsCustomTileset = false,
            Width = (uint)tileEdge,
            Height = (uint)tileEdge,
            Left = -tileEdge * 64f,
            Bottom = -tileEdge * 64f,
            Right = tileEdge * 64f,
            Top = tileEdge * 64f,
        };
        env.Tileset = (Tileset)(byte)tilesetCode;

        // War3Net's TerrainTile has no public parameterless ctor surfaced for direct
        // `new`; construct via reflection (approach verified in the API probe).
        var tileType = typeof(MapEnvironment).Assembly.GetType("War3Net.Build.Environment.TerrainTile")!;
        for (int i = 0; i < verts * verts; i++)
            env.TerrainTiles.Add((TerrainTile)Activator.CreateInstance(tileType)!);

        return env;
    }

    /// <summary>
    /// War3Net's MapInfo serialization throws "Cannot serialize MapInfo, because
    /// &lt;Member&gt; is null." for each required-but-null member (camera bounds,
    /// count-prefixed lists such as players/forces/tables, etc.). A pristine MapInfo
    /// with only identity fields set leaves several such members null.
    ///
    /// Rather than hard-code the (version-dependent) member set, we drive it off the
    /// serializer itself: attempt a throwaway serialize, fill the member it names with
    /// a sane default, and retry until it serializes cleanly. Empty/default values are
    /// the correct semantics for a blank map. This reactive recipe was verified in
    /// tests/Wc3.Tests/War3NetApiProbe.cs.
    /// </summary>
    private static void EnsureSerializable(MapInfo info)
    {
        SanitizeVersions(info);

        for (int attempt = 0; attempt < 64; attempt++)
        {
            try
            {
                using var ms = new MemoryStream();
                using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
                    w.Write(info); // War3Net.Build.Extensions serializer
                return;            // serialized cleanly — all required members present
            }
            catch (InvalidOperationException ex) when (TryExtractNullMember(ex.Message, out var member))
            {
                var prop = typeof(MapInfo).GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
                var value = prop is { CanWrite: true } ? MakeDefault(prop.PropertyType) : null;
                if (value is null)
                    throw new InvalidOperationException(
                        $"Blank-map synthesis could not satisfy required MapInfo member '{member}'.", ex);
                prop!.SetValue(info, value);
            }
        }

        throw new InvalidOperationException(
            "Blank-map synthesis exceeded its fill-attempt budget while building MapInfo.");
    }

    /// <summary>Parses the member name out of "...because &lt;Member&gt; is null.".</summary>
    private static bool TryExtractNullMember(string message, out string member)
    {
        member = string.Empty;
        const string head = "because ";
        const string tail = " is null";
        int i = message.IndexOf(head, StringComparison.Ordinal);
        int j = message.IndexOf(tail, StringComparison.Ordinal);
        if (i < 0 || j < 0 || j <= i) return false;
        i += head.Length;
        member = message[i..j].Trim();
        return member.Length > 0;
    }

    /// <summary>
    /// War3Net reconstructs version members via <c>new Version(major, minor, build,
    /// revision)</c>, which throws when Build/Revision are negative. A pristine MapInfo
    /// (or one whose null Version members we filled) can carry a version with the -1
    /// components System.Version uses for unspecified fields. Rewrite any such member to
    /// a fully specified, non-negative version so the round-trip parses.
    /// </summary>
    private static void SanitizeVersions(MapInfo info)
    {
        foreach (var prop in typeof(MapInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(Version) || !prop.CanRead || !prop.CanWrite) continue;
            if (prop.GetValue(info) is Version v && (v.Build < 0 || v.Revision < 0))
                prop.SetValue(info, new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0)));
        }
    }

    /// <summary>
    /// Builds an empty/default value for a member type: "" for strings, empty arrays,
    /// default(T) for value types (unwrapping Nullable&lt;T&gt;), and for reference
    /// types a parameterless instance when available, else the shortest constructor
    /// invoked with default arguments (matches the probe's confirmed MakeDefault).
    /// </summary>
    private static object? MakeDefault(Type t)
    {
        if (t == typeof(string)) return string.Empty;
        // System.Version left with < 4 components stores Build/Revision as -1, which
        // War3Net's reader rejects (`new Version(..., build, ...)` throws). Use a fully
        // specified, non-negative version.
        if (t == typeof(Version)) return new Version(1, 0, 0, 0);

        var underlying = Nullable.GetUnderlyingType(t);
        if (underlying is not null) t = underlying;

        if (t.IsValueType) return Activator.CreateInstance(t);
        if (t.IsArray) return Array.CreateInstance(t.GetElementType()!, 0);

        if (t.GetConstructor(Type.EmptyTypes) is not null)
            return Activator.CreateInstance(t);

        var ctor = t.GetConstructors().OrderBy(c => c.GetParameters().Length).FirstOrDefault();
        if (ctor is null) return null;

        var args = ctor.GetParameters()
            .Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
            .ToArray();
        try { return ctor.Invoke(args); }
        catch { return null; }
    }
}
