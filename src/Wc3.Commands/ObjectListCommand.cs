using System.Globalization;
using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.GameData;
using Wc3.Model;

namespace Wc3.Commands;

public static class ObjectListCommand
{
    private static readonly int UnamId = "unam".FromRawcode();

    /// <summary>
    /// Enumerate the map's unit entries from war3map.w3u: modified standard units
    /// (BaseUnits, identified by OldId — their own base) and custom units (NewUnits,
    /// identified by NewId, derived from OldId; 0 = base-less). No w3u → empty list.
    /// Names resolve from the map's unam delta, else the base game's localized unit
    /// names (null when game data is unavailable); the game data is only opened when
    /// the map actually has entries.
    /// </summary>
    public static ObjectListResult Execute(MapDocument doc, string? gameDirOverride)
    {
        if (doc.GetFile("war3map.w3u")?.Model is not UnitObjectData w3u
            || w3u.BaseUnits.Count + w3u.NewUnits.Count == 0)
            return new ObjectListResult(Array.Empty<ObjectListItem>());

        GameData.GameData.TryOpen(gameDirOverride, out var ctx, out _);
        return Execute(w3u, ctx);
    }

    /// <summary>Core with an optional game-data context (null = no name fallback).</summary>
    internal static ObjectListResult Execute(UnitObjectData w3u, GameDataContext? ctx)
    {
        var items = new List<ObjectListItem>(w3u.BaseUnits.Count + w3u.NewUnits.Count);
        foreach (var u in w3u.BaseUnits)
            items.Add(Item(u.OldId.ToRawcode(), u, ctx));
        foreach (var u in w3u.NewUnits)
            items.Add(Item(u.NewId.ToRawcode(), u, ctx));
        return new ObjectListResult(items);
    }

    private static ObjectListItem Item(string rawcode, SimpleObjectModification u, GameDataContext? ctx)
    {
        string? baseRawcode = u.OldId == 0 ? null : u.OldId.ToRawcode();

        // Map's unam delta wins; otherwise the base unit's localized name.
        string? name = null;
        foreach (var mod in u.Modifications)
            if (mod.Id == UnamId) { name = Convert.ToString(mod.Value, CultureInfo.InvariantCulture); break; }
        if (name is null && baseRawcode is not null && ctx is not null
            && ctx.UnitNames.TryGetName(baseRawcode, out var baseName))
            name = baseName;

        return new ObjectListItem(rawcode, baseRawcode, name);
    }
}
