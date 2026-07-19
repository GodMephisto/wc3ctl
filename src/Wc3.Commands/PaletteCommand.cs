// src/Wc3.Commands/PaletteCommand.cs
using War3Net.Common.Extensions;
using Wc3.GameData;
using Wc3.Model;

namespace Wc3.Commands;

/// <summary>
/// Enumerates the doodad types placeable on a map — the "palette" a caller consults
/// before <see cref="PlacementCommand.PlaceDoodad"/>. The base-game doodad catalog
/// (installed GameData) is unioned with the map's own object-data (war3map.w3d overlaid
/// by war3mapSkin.w3d): custom doodads (New*) and modified base doodads (Base*). Each
/// entry carries its four-char rawcode, a resolved display name (the map's name-field
/// delta wins over the base game's name; null when unresolvable), its source, and the
/// base it derives from. Deterministic order: base catalog by rawcode, then map-only
/// customs by rawcode. Without a WC3 install the base catalog is unavailable, so the
/// palette is map-only and the open diagnostic is surfaced (never throws).
/// </summary>
public static class PaletteCommand
{
    /// <summary>CLI/default overload: opens game data by locating the install (or override).</summary>
    public static DoodadPaletteResult DoodadPalette(MapDocument doc, string? gameDirOverride)
    {
        GameData.GameData.TryOpen(gameDirOverride, out var ctx, out var diag);
        return DoodadPalette(doc, ctx, string.IsNullOrEmpty(diag) ? null : diag);
    }

    /// <summary>Core with an explicit (optionally null) game-data context — null = map-only,
    /// no base catalog and no base-name fallback. Reused by Studio, which holds an open ctx.</summary>
    internal static DoodadPaletteResult DoodadPalette(MapDocument doc, GameDataContext? ctx, string? openDiag = null)
    {
        var (entries, diagnostics, msg) =
            BuildPalette(doc, ctx, ObjectKind.Doodad, ctx?.Doodads.Rawcodes, "doodad", openDiag);
        return new DoodadPaletteResult(true, msg, entries, diagnostics);
    }

    /// <summary>CLI default overload for the unit palette — locates the install (or override).</summary>
    public static UnitPaletteResult UnitPalette(MapDocument doc, string? gameDirOverride)
    {
        GameData.GameData.TryOpen(gameDirOverride, out var ctx, out var diag);
        return UnitPalette(doc, ctx, string.IsNullOrEmpty(diag) ? null : diag);
    }

    /// <summary>Unit palette — same union semantics as <see cref="DoodadPalette(MapDocument,
    /// GameDataContext?, string?)"/> over the unit catalog (war3map.w3u overlaid by
    /// war3mapSkin.w3u), with base names via the localized unit name table.</summary>
    internal static UnitPaletteResult UnitPalette(MapDocument doc, GameDataContext? ctx, string? openDiag = null)
    {
        var (entries, diagnostics, msg) =
            BuildPalette(doc, ctx, ObjectKind.Unit, ctx?.Units.Rawcodes, "unit", openDiag);
        return new UnitPaletteResult(true, msg, entries, diagnostics);
    }

    /// <summary>Kind-generic palette builder: unions the base-game catalog (<paramref
    /// name="baseRawcodes"/>) with the map's own object-data for <paramref name="kind"/>,
    /// folding the map's name/source in where they overlap. Deterministic order: base catalog
    /// by rawcode, then map-only codes by rawcode.</summary>
    private static (List<PaletteEntry> Entries, List<string> Diagnostics, string Message) BuildPalette(
        MapDocument doc, GameDataContext? ctx, ObjectKind kind,
        IEnumerable<string>? baseRawcodes, string noun, string? openDiag)
    {
        var info = ObjectKinds.Info(kind);
        var strings = MapStrings.From(doc);
        var diagnostics = new List<string>();
        if (openDiag != null) diagnostics.Add(openDiag);

        // Map object-data, indexed by code so base-catalog rows can adopt the map's name
        // and source. A New* custom has Id != OldId (its own distinct code); a Base*
        // modification has Id == OldId (the same code as the base doodad it edits).
        var mapName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mapSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mapBase = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in ObjectKinds.MergedEntries(doc, info))
        {
            var code = e.Id.ToRawcode();
            var baseRawcode = e.OldId == 0 ? null : e.OldId.ToRawcode();
            mapSource[code] = e.Id != e.OldId ? "map-custom" : "map-modified";
            mapBase[code] = baseRawcode;
            var name = ObjectKinds.DeltaName(ObjectKinds.ModsToDict(e.Mods), info, strings)
                ?? ObjectKinds.BaseName(ctx, kind, baseRawcode);
            if (name != null) mapName[code] = name;
        }

        var entries = new List<PaletteEntry>();
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Base-game catalog (sorted), with the map's name/source folded in where they overlap.
        var baseCodes = baseRawcodes?.ToList() ?? new List<string>();
        foreach (var code in baseCodes.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
        {
            if (!emitted.Add(code)) continue; // Rawcodes is already Distinct; guard anyway
            var name = mapName.TryGetValue(code, out var mn) ? mn : ObjectKinds.BaseName(ctx, kind, code);
            var source = mapSource.TryGetValue(code, out var ms) ? ms : "base";
            var baseRawcode = source == "map-modified" ? code : (string?)null;
            entries.Add(new PaletteEntry(code, name, source, baseRawcode));
        }

        // Map-only codes absent from the base catalog (all of them when base is unavailable).
        foreach (var code in mapSource.Keys.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
        {
            if (!emitted.Add(code)) continue;
            entries.Add(new PaletteEntry(
                code, mapName.TryGetValue(code, out var mn) ? mn : null, mapSource[code], mapBase[code]));
        }

        var msg = baseCodes.Count == 0
            ? $"{entries.Count} placeable {noun} type(s) — map only (base catalog unavailable)"
            : $"{entries.Count} placeable {noun} type(s): {baseCodes.Count} base-game, {mapSource.Count} defined/overridden by map";
        return (entries, diagnostics, msg);
    }
}
