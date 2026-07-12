namespace Wc3.GameData;

/// <summary>
/// Resolves a base unit rawcode (e.g. "hfoo") to its default field values by joining
/// unitmetadata.slk with the unit data SLKs. Metadata "slk" values name the table
/// (e.g. "UnitBalance" -> unitbalance.slk); fields whose slk is "Profile" live in TXT
/// profile files and are not resolved in v1.
/// </summary>
public sealed class BaseUnitStore
{
    private const string Dir = @"war3.w3mod:units\";
    private static readonly string[] UnitSlkNames = { "unitdata", "unitbalance", "unitweapons", "unitui", "unitabilities" };

    private readonly UnitMetadata _meta;
    private readonly Dictionary<string, SlkTable> _slks; // slk name (lowercase) -> table

    private BaseUnitStore(UnitMetadata meta, Dictionary<string, SlkTable> slks) { _meta = meta; _slks = slks; }

    public static BaseUnitStore Build(IGameDataSource src)
    {
        var metaBytes = src.ReadFile(Dir + "unitmetadata.slk")
            ?? throw new InvalidDataException("unitmetadata.slk not found in game data");
        var meta = UnitMetadata.FromSlk(SlkTable.Parse(metaBytes));

        var slks = new Dictionary<string, SlkTable>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in UnitSlkNames)
        {
            var bytes = src.ReadFile(Dir + name + ".slk");
            if (bytes != null) slks[name] = SlkTable.Parse(bytes);
        }
        return new BaseUnitStore(meta, slks);
    }

    /// <summary>Field-code -> base value for the given unit; false if no field resolves.</summary>
    public bool TryGetUnit(string rawcode, out IReadOnlyDictionary<string, string> fieldsByCode)
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
