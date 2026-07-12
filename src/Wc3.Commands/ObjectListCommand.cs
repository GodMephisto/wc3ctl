using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.Model;

namespace Wc3.Commands;

public static class ObjectListCommand
{
    /// <summary>
    /// Enumerate the map's unit entries from war3map.w3u: modified standard units
    /// (BaseUnits, identified by OldId — their own base) and custom units (NewUnits,
    /// identified by NewId, derived from OldId; 0 = base-less). No w3u → empty list.
    /// </summary>
    public static ObjectListResult Execute(MapDocument doc)
    {
        if (doc.GetFile("war3map.w3u")?.Model is not UnitObjectData w3u)
            return new ObjectListResult(Array.Empty<ObjectListItem>());

        var items = new List<ObjectListItem>(w3u.BaseUnits.Count + w3u.NewUnits.Count);
        foreach (var u in w3u.BaseUnits)
            items.Add(new ObjectListItem(u.OldId.ToRawcode(), u.OldId == 0 ? null : u.OldId.ToRawcode()));
        foreach (var u in w3u.NewUnits)
            items.Add(new ObjectListItem(u.NewId.ToRawcode(), u.OldId == 0 ? null : u.OldId.ToRawcode()));
        return new ObjectListResult(items);
    }
}
