// src/Wc3.Studio/GameDataWarmup.cs
using Wc3.GameData;

namespace Wc3.Studio;

/// <summary>
/// Opens the base game data in the background, so no panel ever pays for it on the UI thread.
/// </summary>
/// <remarks>
/// Measured, on Anime_WOS2_0.30a1.w3x. The Objects tab took 1,704ms to open and 263ms to reopen,
/// which made it by far the worst block in the app. None of that was the map. Listing its objects
/// costs 3ms cold and building an object's 223 field form costs another 3ms. The whole 1,704ms was
/// the first <see cref="GameData.TryOpen"/>, a CASC open plus a parse of all seven object types,
/// cached per install for the process lifetime. Whichever panel touched it first paid it, on the
/// UI thread, and the app looked hung.
///
/// So it is paid before anyone asks. The cache is a static keyed by resolved install directory, so
/// warming it here is enough for every later caller in the process, whatever front-end reaches it.
/// A failure is deliberately ignored, a missing install is a normal state that every caller
/// already degrades for, and warming must never be the thing that reports it.
/// </remarks>
public static class GameDataWarmup
{
    private static readonly object Gate = new();
    private static Task? _running;
    private static string? _forDir;

    /// <summary>
    /// Starts the open on a background thread if it is not already running for this install.
    /// Returns immediately. Safe to call repeatedly, and on every settings change.
    /// </summary>
    public static void Begin(string? gameDir)
    {
        lock (Gate)
        {
            var key = gameDir ?? "";
            if (_running is { IsCompleted: false } && _forDir == key) return;
            _forDir = key;
            _running = Task.Run(() =>
            {
                try { GameData.GameData.TryOpen(gameDir, out _, out _); }
                catch { /* a warm-up never reports, see the remarks */ }
            });
        }
    }

    /// <summary>The in-flight warm-up. Public so a measurement can wait for it, since a
    /// panel timing table that raced the warm-up would blame a panel for its cost.</summary>
    public static Task? Running
    {
        get { lock (Gate) return _running; }
    }
}
