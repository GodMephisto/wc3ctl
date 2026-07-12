using System.Globalization;
using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.GameData;
using Wc3.Model;

namespace Wc3.Commands;

public static class ObjectGetCommand
{
    /// <summary>
    /// Full pipeline: map w3u deltas ⊕ base game data. When the install/CASC is
    /// unavailable the result degrades to deltas-only plus a diagnostic.
    /// </summary>
    public static MergedObjectResult Execute(MapDocument doc, string rawcode, string? gameDirOverride)
    {
        if (!GameData.GameData.TryOpenUnits(gameDirOverride, out var store, out var diagnostic))
            return Execute(doc, rawcode, _ => null, new[] { diagnostic });

        return Execute(doc, rawcode,
            code => store!.TryGetUnit(code, out var fields) ? fields : null,
            Array.Empty<string>(),
            code => store!.Metadata.TryGet(code, out var m) && m.DisplayName.Length > 0 ? m.DisplayName : code);
    }

    /// <summary>
    /// Seam overload: base data comes from an injectable lookup so tests need no install.
    /// </summary>
    public static MergedObjectResult Execute(
        MapDocument doc, string rawcode,
        Func<string, IReadOnlyDictionary<string, string>?> baseLookup,
        IReadOnlyList<string> preDiagnostics,
        Func<string, string>? nameLookup = null)
    {
        // Confirmed War3Net shape (reflection, 6.0.3): UnitObjectData.BaseUnits/NewUnits
        // are List<SimpleObjectModification> { int OldId, int NewId, List<SimpleObjectDataModification>
        // Modifications { int Id, object Value } }; int ids are little-endian rawcodes
        // (low byte = first char), converted via War3Net's ToRawcode/FromRawcode.
        var w3u = doc.GetFile("war3map.w3u")?.Model as UnitObjectData;

        string? baseRawcode = null;
        SimpleObjectModification? unit = null;
        if (w3u is not null && rawcode.Length == 4)
        {
            int id = rawcode.FromRawcode();
            // Custom unit (NewId is its rawcode) or a modified standard unit (NewId=0).
            unit = w3u.NewUnits.FirstOrDefault(u => u.NewId == id)
                ?? w3u.BaseUnits.FirstOrDefault(u => u.OldId == id);
            if (unit is not null)
                baseRawcode = unit.NewId == 0 ? rawcode : unit.OldId.ToRawcode();
        }

        var deltaFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (unit is not null)
            foreach (var mod in unit.Modifications)
                deltaFields[mod.Id.ToRawcode()] = Convert.ToString(mod.Value, CultureInfo.InvariantCulture) ?? "";

        // Not defined in the map at all? It may still be a plain standard unit.
        var baseFields = baseLookup(baseRawcode ?? rawcode);
        if (baseFields is not null) baseRawcode ??= rawcode;

        return Merge(rawcode, baseRawcode,
            baseFields ?? new Dictionary<string, string>(),
            deltaFields,
            nameLookup ?? (code => code),
            preDiagnostics);
    }

    /// <summary>Pure merge: base fields overlaid by map deltas, each labeled with its source.</summary>
    public static MergedObjectResult Merge(
        string rawcode, string? baseRawcode,
        IReadOnlyDictionary<string, string> baseFields,
        IReadOnlyDictionary<string, string> deltaFields,
        Func<string, string> nameLookup,
        IReadOnlyList<string> diagnostics)
    {
        string NameOf(string code)
        {
            var name = nameLookup(code);
            return string.IsNullOrEmpty(name) ? code : name;
        }

        var merged = new Dictionary<string, MergedField>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, value) in baseFields)
            merged[code] = new MergedField(code, NameOf(code), value, "base");
        foreach (var (code, value) in deltaFields)
            merged[code] = new MergedField(code, NameOf(code), value, "map");

        var fields = merged.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new MergedObjectResult(rawcode, fields.Count > 0, baseRawcode, fields, diagnostics);
    }
}
