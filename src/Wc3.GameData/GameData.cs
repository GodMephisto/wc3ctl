// src/Wc3.GameData/GameData.cs
namespace Wc3.GameData;

/// <summary>
/// One-call facade: locate the install, open CASC, build the base unit store.
/// Never throws — callers degrade gracefully on false + diagnostic.
/// </summary>
public static class GameData
{
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
