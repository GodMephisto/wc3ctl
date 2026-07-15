// src/Wc3.GameData/GameDataContext.cs
namespace Wc3.GameData;

/// <summary>
/// Everything the object commands need from the base game, built from a single
/// CASC open: unit/ability field stores, localized editor strings (WESTRING keys),
/// and localized unit display names.
/// </summary>
public sealed class GameDataContext
{
    public required BaseUnitStore Units { get; init; }
    public required BaseAbilityStore Abilities { get; init; }
    public required WorldEditStrings Strings { get; init; }
    public required UnitNameTable UnitNames { get; init; }
}
