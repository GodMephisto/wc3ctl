namespace Wc3.GameData;

/// <summary>
/// Resolves a base ability rawcode (e.g. "AHbz") to its default field values by joining
/// abilitymetadata.slk with the ability data SLKs. The data SLK set is derived from the
/// metadata's "slk" column (currently just AbilityData -> abilitydata.slk, keyed by "alias");
/// fields whose slk is "Profile" live in TXT profile files and are not resolved in v1.
/// </summary>
public sealed class BaseAbilityStore
{
    private const string Dir = @"war3.w3mod:units\";

    private readonly AbilityMetadata _meta;
    private readonly Dictionary<string, SlkTable> _slks; // slk name (lowercase) -> table

    private BaseAbilityStore(AbilityMetadata meta, Dictionary<string, SlkTable> slks) { _meta = meta; _slks = slks; }

    /// <summary>Field-code metadata (display names) for consumers building lookups.</summary>
    public AbilityMetadata Metadata => _meta;

    public static BaseAbilityStore Build(IGameDataSource src)
    {
        var metaBytes = src.ReadFile(Dir + "abilitymetadata.slk")
            ?? throw new InvalidDataException("abilitymetadata.slk not found in game data");
        var meta = AbilityMetadata.FromSlk(SlkTable.Parse(metaBytes));

        var slks = new Dictionary<string, SlkTable>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in meta.Fields.Select(f => f.SlkName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (name.Length == 0 || name.Equals("Profile", StringComparison.OrdinalIgnoreCase)) continue;
            var bytes = src.ReadFile(Dir + name.ToLowerInvariant() + ".slk");
            if (bytes != null) slks[name] = SlkTable.Parse(bytes);
        }
        return new BaseAbilityStore(meta, slks);
    }

    /// <summary>Field-code -> base value for the given ability; false if no field resolves.</summary>
    public bool TryGetAbility(string rawcode, out IReadOnlyDictionary<string, string> fieldsByCode)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fm in _meta.Fields)
        {
            if (!_slks.TryGetValue(fm.SlkName, out var table)) continue;
            if (table.TryGetRow(rawcode, out var row) && row.TryGetValue(fm.Column, out var val) && val.Length > 0)
                result[fm.Code] = val;
        }
        fieldsByCode = result;
        return result.Count > 0;
    }
}
