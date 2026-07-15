// src/Wc3.GameData/GameDataContext.cs
namespace Wc3.GameData;

/// <summary>
/// Everything the object commands need from the base game, built from a single
/// CASC open: field stores for all seven Object Editor types, localized editor
/// strings (WESTRING keys), and localized unit display names.
/// </summary>
public sealed class GameDataContext
{
    public required BaseUnitStore Units { get; init; }
    public required BaseAbilityStore Abilities { get; init; }
    public required ObjectDataStore Items { get; init; }
    public required ObjectDataStore Destructables { get; init; }
    public required ObjectDataStore Doodads { get; init; }
    public required ObjectDataStore Buffs { get; init; }
    public required ObjectDataStore Upgrades { get; init; }
    public required WorldEditStrings Strings { get; init; }
    public required UnitNameTable UnitNames { get; init; }

    /// <summary>Per-type build failures: that type's store is left Empty and the reason
    /// recorded here — a missing type never fails the whole open.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}
