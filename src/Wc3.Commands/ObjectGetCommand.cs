using System.Globalization;
using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.GameData;
using Wc3.Model;

namespace Wc3.Commands;

public static class ObjectGetCommand
{
    /// <summary>
    /// Full pipeline: map object deltas (units from w3u, abilities from w3a) ⊕ base
    /// game data. When the install/CASC is unavailable the result degrades to
    /// deltas-only plus a diagnostic (field names fall back to codes; the object
    /// name only resolves from a map delta).
    /// </summary>
    public static MergedObjectResult Execute(MapDocument doc, string rawcode, string? gameDirOverride)
    {
        return GameData.GameData.TryOpen(gameDirOverride, out var ctx, out var diagnostic)
            ? Execute(doc, rawcode, ctx, Array.Empty<string>())
            : Execute(doc, rawcode, null, new[] { diagnostic });
    }

    /// <summary>
    /// Core with an optional game-data context (null = degraded, deltas-only).
    /// </summary>
    internal static MergedObjectResult Execute(
        MapDocument doc, string rawcode, GameDataContext? ctx, IReadOnlyList<string> preDiagnostics)
    {
        // Confirmed War3Net shapes (reflection, 6.0.3):
        //   war3map.w3u → UnitObjectData.BaseUnits/NewUnits : List<SimpleObjectModification>
        //     { int OldId, int NewId, Modifications: List<SimpleObjectDataModification { Id, Value } } }
        //   war3map.w3a → AbilityObjectData.BaseAbilities/NewAbilities : List<LevelObjectModification>
        //     { int OldId, int NewId, Modifications: List<LevelObjectDataModification { Id, Value, Level, Pointer } } }
        // int ids are little-endian rawcodes (low byte = first char), via ToRawcode/FromRawcode.
        if (rawcode.Length == 4)
        {
            int id = rawcode.FromRawcode();

            if (doc.GetFile("war3map.w3u")?.Model is UnitObjectData w3u)
            {
                // Custom unit (NewId is its rawcode) or a modified standard unit (NewId=0).
                var unit = w3u.NewUnits.FirstOrDefault(u => u.NewId == id)
                    ?? w3u.BaseUnits.FirstOrDefault(u => u.OldId == id);
                if (unit is not null) return MergeUnit(rawcode, unit, ctx, preDiagnostics);
            }

            if (doc.GetFile("war3map.w3a")?.Model is AbilityObjectData w3a)
            {
                var ability = w3a.NewAbilities.FirstOrDefault(a => a.NewId == id)
                    ?? w3a.BaseAbilities.FirstOrDefault(a => a.OldId == id);
                if (ability is not null) return MergeAbility(rawcode, ability, ctx, preDiagnostics);
            }
        }

        // Not in the map: may still be a plain standard unit or ability.
        var none = new Dictionary<string, string>();
        if (ctx is not null && ctx.Units.TryGetUnit(rawcode, out var unitFields))
        {
            string? name = ctx.UnitNames.TryGetName(rawcode, out var n) ? n : null;
            return Merge(rawcode, rawcode, definedInMap: false, name,
                unitFields, none, UnitFieldName(ctx), preDiagnostics);
        }
        if (ctx is not null && ctx.Abilities.TryGetAbility(rawcode, out var abilityFields))
            return Merge(rawcode, rawcode, definedInMap: false, name: null,
                abilityFields, none, AbilityFieldName(ctx), preDiagnostics);
        return Merge(rawcode, null, definedInMap: false, name: null, none, none, code => code, preDiagnostics);
    }

    private static MergedObjectResult MergeUnit(
        string rawcode, SimpleObjectModification unit, GameDataContext? ctx, IReadOnlyList<string> diagnostics)
    {
        // Modified standard unit is its own base; a from-scratch custom
        // unit (OldId=0) has none — never rawcode-ify the zero id.
        string? baseRawcode = unit.NewId == 0 ? rawcode : (unit.OldId == 0 ? null : unit.OldId.ToRawcode());

        var deltaFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in unit.Modifications)
            deltaFields[mod.Id.ToRawcode()] = FormatValue(mod.Value);

        IReadOnlyDictionary<string, string>? baseFields = null;
        if (baseRawcode is not null && ctx is not null && ctx.Units.TryGetUnit(baseRawcode, out var resolved))
            baseFields = resolved;

        // Object name: the map's unam delta wins; otherwise the base unit's localized name.
        string? name = deltaFields.TryGetValue("unam", out var mapName) ? mapName : null;
        if (name is null && baseRawcode is not null && ctx is not null
            && ctx.UnitNames.TryGetName(baseRawcode, out var baseName))
            name = baseName;

        return Merge(rawcode, baseRawcode, definedInMap: true, name,
            baseFields ?? new Dictionary<string, string>(), deltaFields, UnitFieldName(ctx), diagnostics);
    }

    private static MergedObjectResult MergeAbility(
        string rawcode, LevelObjectModification ability, GameDataContext? ctx, IReadOnlyList<string> diagnostics)
    {
        string? baseRawcode = ability.NewId == 0 ? rawcode : (ability.OldId == 0 ? null : ability.OldId.ToRawcode());

        // Ability deltas are leveled; keep per-level values distinct by keying
        // Level 0 (non-leveled) as the bare code and Level N as "code:N".
        var deltaFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in ability.Modifications)
        {
            var code = mod.Id.ToRawcode();
            deltaFields[mod.Level == 0 ? code : $"{code}:{mod.Level}"] = FormatValue(mod.Value);
        }

        IReadOnlyDictionary<string, string>? baseFields = null;
        if (baseRawcode is not null && ctx is not null && ctx.Abilities.TryGetAbility(baseRawcode, out var resolved))
            baseFields = resolved;

        // Ability names only come from the map's anam delta — base ability names
        // live in profile TXT files the store doesn't read yet, so they stay null.
        string? name = deltaFields.TryGetValue("anam", out var mapName) ? mapName
            : deltaFields.TryGetValue("anam:1", out var lvl1Name) ? lvl1Name : null;

        return Merge(rawcode, baseRawcode, definedInMap: true, name,
            baseFields ?? new Dictionary<string, string>(), deltaFields, AbilityFieldName(ctx), diagnostics);
    }

    /// <summary>Field-name pipeline: metadata displayName (a WESTRING key) → localized
    /// English string; fallbacks: the WESTRING key itself, then the raw field code.</summary>
    private static Func<string, string> UnitFieldName(GameDataContext? ctx) => code =>
        ctx is not null && ctx.Units.Metadata.TryGet(code, out var m) && m.DisplayName.Length > 0
            ? (ctx.Strings.TryGet(m.DisplayName, out var s) ? s : m.DisplayName)
            : code;

    private static Func<string, string> AbilityFieldName(GameDataContext? ctx) => key =>
    {
        // Leveled delta keys carry a ":N" suffix; metadata is keyed by the bare code.
        int colon = key.IndexOf(':');
        var code = colon < 0 ? key : key[..colon];
        return ctx is not null && ctx.Abilities.Metadata.TryGet(code, out var m) && m.DisplayName.Length > 0
            ? (ctx.Strings.TryGet(m.DisplayName, out var s) ? s : m.DisplayName)
            : key;
    };

    private static string FormatValue(object? value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    /// <summary>
    /// Pure merge: base fields overlaid by map deltas, each labeled with its source.
    /// A rawcode counts as Found when the map defines it (even with zero resolvable
    /// fields) or when any field resolved. Fields sort by display name, then bare
    /// code, then numeric level (leveled ability keys like "Ncl4:10").
    /// </summary>
    public static MergedObjectResult Merge(
        string rawcode, string? baseRawcode, bool definedInMap, string? name,
        IReadOnlyDictionary<string, string> baseFields,
        IReadOnlyDictionary<string, string> deltaFields,
        Func<string, string> nameLookup,
        IReadOnlyList<string> diagnostics)
    {
        string NameOf(string code)
        {
            var fieldName = nameLookup(code);
            return string.IsNullOrEmpty(fieldName) ? code : fieldName;
        }

        var merged = new Dictionary<string, MergedField>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, value) in baseFields)
            merged[code] = new MergedField(code, NameOf(code), value, "base");
        foreach (var (code, value) in deltaFields)
            merged[code] = new MergedField(code, NameOf(code), value, "map");

        var fields = merged.Values
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => BareCode(f.Code), StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => LevelOf(f.Code))
            .ToList();
        return new MergedObjectResult(
            rawcode, definedInMap || fields.Count > 0, baseRawcode, name, fields, diagnostics);
    }

    private static string BareCode(string code)
    {
        int colon = code.IndexOf(':');
        return colon < 0 ? code : code[..colon];
    }

    private static int LevelOf(string code)
    {
        int colon = code.IndexOf(':');
        return colon >= 0 && int.TryParse(code[(colon + 1)..], out var level) ? level : 0;
    }
}
