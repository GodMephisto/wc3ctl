namespace Wc3.GameData;

public sealed record ObjectFieldMeta(string Code, string SlkName, string Column, string DisplayName);

/// <summary>
/// Type-agnostic view of the metadata SLK shape shared by every Object Editor type
/// (units, items, abilities, buffs, destructables, doodads, upgrades). Confirmed shape:
/// row key = "ID" column = the 4-char field code (matches w3u/w3t/w3a/w3h/w3b/w3d/w3q
/// mod codes); "field" = the column name inside the data SLK; "slk" = which data table
/// holds it ("Profile" = TXT profile files, not resolved in v1); "displayName" = a
/// WESTRING key.
/// </summary>
public sealed class ObjectMetadata
{
    private readonly Dictionary<string, ObjectFieldMeta> _byCode = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ObjectFieldMeta> Fields => _byCode.Values.ToList();

    public bool TryGet(string code, out ObjectFieldMeta meta) => _byCode.TryGetValue(code, out meta!);

    public static ObjectMetadata FromSlk(SlkTable meta)
    {
        var m = new ObjectMetadata();
        foreach (var code in meta.RowKeys)
        {
            if (string.IsNullOrWhiteSpace(code) || !meta.TryGetRow(code, out var row)) continue;
            string slk = row.TryGetValue("slk", out var s) ? s : "";
            string col = row.TryGetValue("field", out var f) ? f : "";
            string disp = row.TryGetValue("displayname", out var d) ? d : code;
            if (col.Length > 0)
                m._byCode[code] = new ObjectFieldMeta(code, slk, col, disp);
        }
        return m;
    }
}
