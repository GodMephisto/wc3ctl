// src/Wc3.GameData/GameData.cs
using System.Collections.Concurrent;

namespace Wc3.GameData;

/// <summary>
/// One-call facade: locate the install, open CASC, build the base-game resolvers.
/// Never throws — callers degrade gracefully on false + diagnostic.
/// </summary>
public static class GameData
{
    private const string WorldEditStringsPath = @"war3.w3mod:_locales\enus.w3mod:ui\worldeditstrings.txt";

    // Base game data (SLK tables, editor strings) is assumed stable for the process
    // lifetime, so successful builds are cached per resolved install directory and
    // reused across object queries. Failures are never cached.
    private static readonly ConcurrentDictionary<string, GameDataContext> ContextCache = new();

    /// <summary>Build all four resolvers (units, abilities, editor strings, unit names)
    /// from a single CASC open, caching the result per install for the process lifetime.
    /// The source is disposed before returning; the context holds only parsed data.</summary>
    public static bool TryOpen(string? gameDirOverride, out GameDataContext? ctx, out string diagnostic)
    {
        ctx = null; diagnostic = "";
        var dir = GameInstall.Locate(gameDirOverride);
        if (dir is null) { diagnostic = "Warcraft III install not found (pass --game-dir <path>)"; return false; }
        if (ContextCache.TryGetValue(dir, out var cached)) { ctx = cached; return true; }
        if (!CascGameDataSource.TryOpen(dir, out var src, out var err))
        { diagnostic = $"could not open game data at {dir}: {err}"; return false; }
        // The builders copy all bytes into parsed tables, so the source is safe to
        // dispose as soon as they return — success or failure.
        try
        {
            var wesBytes = src!.ReadFile(WorldEditStringsPath);
            ctx = new GameDataContext
            {
                Units = BaseUnitStore.Build(src!),
                Abilities = BaseAbilityStore.Build(src!),
                Strings = wesBytes is null ? WorldEditStrings.Parse("") : WorldEditStrings.FromBytes(wesBytes),
                UnitNames = UnitNameTable.FromSources(src!),
            };
            // Concurrent builders may race; whichever lands first wins and both
            // contexts are equivalent, so GetOrAdd keeps callers consistent.
            ctx = ContextCache.GetOrAdd(dir, ctx);
            return true;
        }
        catch (Exception ex) { diagnostic = $"could not read base game data: {ex.Message}"; return false; }
        finally { src!.Dispose(); }
    }

    public static bool TryOpenUnits(string? gameDirOverride, out BaseUnitStore? store, out string diagnostic)
    {
        store = null; diagnostic = "";
        var dir = GameInstall.Locate(gameDirOverride);
        if (dir is null) { diagnostic = "Warcraft III install not found (pass --game-dir <path>)"; return false; }
        if (!CascGameDataSource.TryOpen(dir, out var src, out var err))
        { diagnostic = $"could not open game data at {dir}: {err}"; return false; }
        // Build copies all SLK bytes into parsed tables, so the source is safe to
        // dispose as soon as it returns — success or failure.
        try { store = BaseUnitStore.Build(src!); return true; }
        catch (Exception ex) { diagnostic = $"could not read base unit data: {ex.Message}"; return false; }
        finally { src!.Dispose(); }
    }
}
