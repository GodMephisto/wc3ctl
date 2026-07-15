// src/Wc3.GameData/GameData.cs
namespace Wc3.GameData;

/// <summary>
/// One-call facade: locate the install, open CASC, build the base-game resolvers.
/// Never throws — callers degrade gracefully on false + diagnostic.
/// </summary>
public static class GameData
{
    private const string WorldEditStringsPath = @"war3.w3mod:_locales\enus.w3mod:ui\worldeditstrings.txt";

    /// <summary>Open the CASC once and build all four resolvers (units, abilities,
    /// editor strings, unit names). The source is disposed before returning.</summary>
    public static bool TryOpen(string? gameDirOverride, out GameDataContext? ctx, out string diagnostic)
    {
        ctx = null; diagnostic = "";
        var dir = GameInstall.Locate(gameDirOverride);
        if (dir is null) { diagnostic = "Warcraft III install not found (pass --game-dir <path>)"; return false; }
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
