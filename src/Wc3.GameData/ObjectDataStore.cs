namespace Wc3.GameData;

/// <summary>
/// Generic base-object resolver: joins one Object Editor type's metadata SLK with its
/// data SLK(s) to map rawcode -> field-code -> default value. The per-type factories
/// encode the CASC paths and table-name -> file conventions confirmed against the live
/// install; fields whose slk is "Profile" live in TXT profile files and are not
/// resolved in v1.
/// </summary>
public sealed class ObjectDataStore
{
    private const string UnitsDir = @"war3.w3mod:units\";
    private const string DoodadsDir = @"war3.w3mod:doodads\";

    private readonly ObjectMetadata _meta;
    private readonly Dictionary<string, SlkTable> _slks; // slk table name -> parsed table

    internal ObjectDataStore(ObjectMetadata meta, Dictionary<string, SlkTable> slks) { _meta = meta; _slks = slks; }

    /// <summary>A store that resolves nothing — used when a type's SLKs can't be read.</summary>
    public static ObjectDataStore Empty { get; } = new(new ObjectMetadata(), new(StringComparer.OrdinalIgnoreCase));

    /// <summary>Field-code metadata (display names) for consumers building lookups.</summary>
    public ObjectMetadata Metadata => _meta;

    public bool IsEmpty => _slks.Count == 0;

    /// <summary>
    /// Reads and joins the metadata SLK at <paramref name="metadataPath"/> with its data
    /// SLKs. Data tables come from <paramref name="slkNames"/> when given, otherwise from
    /// the metadata's distinct "slk" values; <paramref name="slkPath"/> maps a table name
    /// to its CASC path (null = skip). Missing data SLKs are skipped; a missing metadata
    /// SLK throws.
    /// </summary>
    public static ObjectDataStore Build(
        IGameDataSource src, string metadataPath, Func<string, string?> slkPath,
        IReadOnlyCollection<string>? slkNames = null)
    {
        var metaBytes = src.ReadFile(metadataPath)
            ?? throw new InvalidDataException($"{metadataPath} not found in game data");
        return Build(src, ObjectMetadata.FromSlk(SlkTable.Parse(metaBytes)), slkPath, slkNames);
    }

    internal static ObjectDataStore Build(
        IGameDataSource src, ObjectMetadata meta, Func<string, string?> slkPath,
        IReadOnlyCollection<string>? slkNames = null)
    {
        var names = slkNames
            ?? (IReadOnlyCollection<string>)meta.Fields.Select(f => f.SlkName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var slks = new Dictionary<string, SlkTable>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (name.Length == 0 || name.Equals("Profile", StringComparison.OrdinalIgnoreCase)) continue;
            var path = slkPath(name);
            if (path is null) continue;
            var bytes = src.ReadFile(path);
            if (bytes != null) slks[name] = SlkTable.Parse(bytes);
        }
        return new ObjectDataStore(meta, slks);
    }

    /// <summary>Field-code -> base value for the given object; false if no field resolves.</summary>
    public bool TryGet(string rawcode, out IReadOnlyDictionary<string, string> fieldsByCode)
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

    /// <summary>Default table-name -> CASC path convention: lowercased + ".slk" under units\.</summary>
    public static string UnitsDirSlk(string name) => UnitsDir + name.ToLowerInvariant() + ".slk";

    // Per-type factories. Paths, data-table names, and rawcode key columns confirmed
    // against the live install:
    //   items:         unitmetadata.slk (item fields share it; slk "ItemData")
    //                    + units\itemdata.slk              (key column "itemID")
    //   destructables: units\destructablemetadata.slk ("DestructableData")
    //                    + units\destructabledata.slk      (key "DestructableID")
    //   doodads:       doodads\doodadmetadata.slk ("DoodadData")
    //                    + doodads\doodads.slk             (key "doodID")
    //   buffs:         units\abilitybuffmetadata.slk ("AbilityBuffData")
    //                    + units\abilitybuffdata.slk       (key "alias")
    //   upgrades:      units\upgrademetadata.slk ("UpgradeData")
    //                    + units\upgradedata.slk           (key "upgradeID")

    /// <summary>Item fields live in unitmetadata.slk; only the ItemData table is loaded so
    /// the store stays item-only (unit tables belong to BaseUnitStore).</summary>
    public static ObjectDataStore BuildItems(IGameDataSource src) =>
        Build(src, UnitsDir + "unitmetadata.slk", UnitsDirSlk, new[] { "ItemData" });

    public static ObjectDataStore BuildDestructables(IGameDataSource src) =>
        Build(src, UnitsDir + "destructablemetadata.slk", UnitsDirSlk);

    /// <summary>Doodads live under doodads\; the "DoodadData" table's file is doodads.slk
    /// (not the lowercased table name).</summary>
    public static ObjectDataStore BuildDoodads(IGameDataSource src) =>
        Build(src, DoodadsDir + "doodadmetadata.slk",
            name => name.Equals("DoodadData", StringComparison.OrdinalIgnoreCase)
                ? DoodadsDir + "doodads.slk" : UnitsDirSlk(name));

    public static ObjectDataStore BuildBuffs(IGameDataSource src) =>
        Build(src, UnitsDir + "abilitybuffmetadata.slk", UnitsDirSlk);

    public static ObjectDataStore BuildUpgrades(IGameDataSource src) =>
        Build(src, UnitsDir + "upgrademetadata.slk", UnitsDirSlk);
}
