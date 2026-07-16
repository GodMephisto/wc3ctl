// src/Wc3.Commands/ObjectDataWriter.cs
using War3Net.Build.Object;
using Wc3.Model;

namespace Wc3.Commands;

/// <summary>The three War3Net modification shapes (see ObjectKinds for the mapping).</summary>
internal enum ObjectDataShape { Simple, Level, Variation }

/// <summary>
/// Write-side adapter over one kind's object-data model, hiding the three group/mod
/// type families (Simple/Level/Variation) behind delegates so ObjectSetCommand and
/// ObjectNewCommand can mutate any kind uniformly. FindGroup resolves like the read
/// side: customs by NewId first, then modified standards by OldId. The int slot in
/// FindMod/AddMod is the mod's Level (ability/upgrade) or Variation (doodad); simple
/// kinds ignore it.
/// </summary>
internal sealed record ObjectDataAccess(
    object Model,
    Func<int, object?> FindGroup,
    Func<int, int, object> CreateGroup,                        // (oldId, newId) → group in the right list
    Func<object, int, int, ObjectDataModification?> FindMod,   // (group, fieldId, slot)
    Action<object, int, int, ObjectDataType, object> AddMod);  // (group, fieldId, slot, type, value)

internal static class ObjectDataWriter
{
    internal static ObjectDataShape ShapeOf(ObjectKind kind) => kind switch
    {
        ObjectKind.Ability or ObjectKind.Upgrade => ObjectDataShape.Level,
        ObjectKind.Doodad => ObjectDataShape.Variation,
        _ => ObjectDataShape.Simple,
    };

    /// <summary>
    /// The kind's war3map.* model, or a fresh empty one when the map has no such file
    /// yet (format version mirrored from the skin twin when present, else v2). Null
    /// when the file exists but has no parsed model — rebuilding it blind would drop
    /// the original data, so callers must refuse to edit.
    /// </summary>
    internal static object? GetOrCreateMapModel(MapDocument doc, ObjectKind kind)
    {
        var info = ObjectKinds.Info(kind);
        if (doc.GetFile(info.MapFile) is { } entry)
            return entry.Model;
        var version = FormatVersionOf(doc.GetFile(info.SkinFile)?.Model) ?? ObjectDataFormatVersion.v2;
        return kind switch
        {
            ObjectKind.Unit => new UnitObjectData(version),
            ObjectKind.Item => new ItemObjectData(version),
            ObjectKind.Ability => new AbilityObjectData(version),
            ObjectKind.Destructable => new DestructableObjectData(version),
            ObjectKind.Doodad => new DoodadObjectData(version),
            ObjectKind.Buff => new BuffObjectData(version),
            ObjectKind.Upgrade => new UpgradeObjectData(version),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static ObjectDataFormatVersion? FormatVersionOf(object? model) => model switch
    {
        UnitObjectData m => m.FormatVersion,
        ItemObjectData m => m.FormatVersion,
        AbilityObjectData m => m.FormatVersion,
        DestructableObjectData m => m.FormatVersion,
        DoodadObjectData m => m.FormatVersion,
        BuffObjectData m => m.FormatVersion,
        UpgradeObjectData m => m.FormatVersion,
        _ => null,
    };

    /// <summary>Adapter for a model of any of the seven kinds; null for anything else
    /// (e.g. a file that parsed as an unexpected type).</summary>
    internal static ObjectDataAccess? AccessFor(object model) => model switch
    {
        UnitObjectData m => Simple(m, m.BaseUnits, m.NewUnits),
        ItemObjectData m => Simple(m, m.BaseItems, m.NewItems),
        DestructableObjectData m => Simple(m, m.BaseDestructables, m.NewDestructables),
        BuffObjectData m => Simple(m, m.BaseBuffs, m.NewBuffs),
        AbilityObjectData m => Level(m, m.BaseAbilities, m.NewAbilities),
        UpgradeObjectData m => Level(m, m.BaseUpgrades, m.NewUpgrades),
        DoodadObjectData m => Variation(m, m.BaseDoodads, m.NewDoodads),
        _ => null,
    };

    private static ObjectDataAccess Simple(
        object model, List<SimpleObjectModification> bases, List<SimpleObjectModification> news) => new(
        model,
        id => news.FirstOrDefault(g => g.NewId == id) ?? bases.FirstOrDefault(g => g.OldId == id),
        (oldId, newId) =>
        {
            var g = new SimpleObjectModification { OldId = oldId, NewId = newId };
            (newId != 0 ? news : bases).Add(g);
            return g;
        },
        (group, fieldId, _) =>
            ((SimpleObjectModification)group).Modifications.FirstOrDefault(m => m.Id == fieldId),
        (group, fieldId, _, type, value) => ((SimpleObjectModification)group).Modifications
            .Add(new SimpleObjectDataModification { Id = fieldId, Type = type, Value = value }));

    private static ObjectDataAccess Level(
        object model, List<LevelObjectModification> bases, List<LevelObjectModification> news) => new(
        model,
        id => news.FirstOrDefault(g => g.NewId == id) ?? bases.FirstOrDefault(g => g.OldId == id),
        (oldId, newId) =>
        {
            var g = new LevelObjectModification { OldId = oldId, NewId = newId };
            (newId != 0 ? news : bases).Add(g);
            return g;
        },
        (group, fieldId, level) => ((LevelObjectModification)group).Modifications
            .FirstOrDefault(m => m.Id == fieldId && m.Level == level),
        (group, fieldId, level, type, value) => ((LevelObjectModification)group).Modifications
            .Add(new LevelObjectDataModification
            { Level = level, Pointer = 0, Id = fieldId, Type = type, Value = value }));

    private static ObjectDataAccess Variation(
        object model, List<VariationObjectModification> bases, List<VariationObjectModification> news) => new(
        model,
        id => news.FirstOrDefault(g => g.NewId == id) ?? bases.FirstOrDefault(g => g.OldId == id),
        (oldId, newId) =>
        {
            var g = new VariationObjectModification { OldId = oldId, NewId = newId };
            (newId != 0 ? news : bases).Add(g);
            return g;
        },
        (group, fieldId, variation) => ((VariationObjectModification)group).Modifications
            .FirstOrDefault(m => m.Id == fieldId && m.Variation == variation),
        (group, fieldId, variation, type, value) => ((VariationObjectModification)group).Modifications
            .Add(new VariationObjectDataModification
            { Variation = variation, Pointer = 0, Id = fieldId, Type = type, Value = value }));
}
