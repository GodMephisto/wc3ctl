// src/Wc3.GameData/GameDataContext.cs
namespace Wc3.GameData;

/// <summary>
/// Everything the object commands need from the base game, built from a single
/// CASC open: field stores for all seven Object Editor types, localized editor
/// strings (WESTRING keys), and localized unit display names. Also the door to
/// arbitrary base-game files (<see cref="TryReadFile"/>) for callers that need
/// raw assets — base models and their textures.
/// </summary>
public sealed class GameDataContext
{
    public required BaseUnitStore Units { get; init; }
    public required BaseAbilityStore Abilities { get; init; }
    public required ObjectDataStore Items { get; init; }
    public required ObjectDataStore Destructables { get; init; }
    public required ObjectDataStore Doodads { get; init; }
    public required ObjectDataStore Buffs { get; init; }
    public required ObjectDataStore Upgrades { get; init; }
    public required WorldEditStrings Strings { get; init; }
    public required UnitNameTable UnitNames { get; init; }

    /// <summary>Per-type build failures: that type's store is left Empty and the reason
    /// recorded here — a missing type never fails the whole open.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    /// <summary>Install directory this context was built from; null for synthetic/test
    /// contexts, which then cannot read raw files.</summary>
    public string? InstallDir { get; init; }

    // The build-time CASC source is disposed once the stores are parsed; raw file
    // reads (base models/textures) lazily open their own handle on first use and
    // keep it for the context's lifetime (contexts are cached per install for the
    // process lifetime, so this is one read-only handle per install).
    private readonly object _cascLock = new();
    private CascGameDataSource? _casc;
    private bool _cascFailed;

    /// <summary>
    /// Reads an arbitrary base-game file. Plain game paths (e.g.
    /// "units\human\footman\footman.mdx") are addressed the way the SLK stores do —
    /// under the "war3.w3mod:" prefix; a path that already names a w3mod (contains
    /// ':') is used as given. Returns false when the install/CASC or the file is
    /// unavailable — never throws.
    /// </summary>
    public bool TryReadFile(string cascPath, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(cascPath) || InstallDir is null) return false;

        lock (_cascLock)
        {
            if (_casc is null)
            {
                if (_cascFailed) return false;
                if (!CascGameDataSource.TryOpen(InstallDir, out _casc, out _))
                {
                    _cascFailed = true;
                    return false;
                }
            }

            var normalized = cascPath.Replace('/', '\\');
            var candidates = normalized.Contains(':')
                ? new[] { normalized }
                : new[] { "war3.w3mod:" + normalized };
            foreach (var candidate in candidates)
            {
                byte[]? read = null;
                try { read = _casc!.ReadFile(candidate); }
                catch { /* unreadable entry — treat as absent */ }
                if (read is { Length: > 0 }) { bytes = read; return true; }
            }
            return false;
        }
    }
}
