// src/Wc3.GameData/EditorEnumData.cs
namespace Wc3.GameData;

/// <summary>One legal value of an enumerated object-data field, and what it is called.</summary>
public sealed record EnumOption(string Value, string DisplayName)
{
    /// <summary>What a picker should show. Falls back to the raw value when the display name
    /// could not be resolved, since a token is more use than an empty row.</summary>
    public string Label => DisplayName.Length > 0 ? DisplayName : Value;

    public override string ToString() =>
        DisplayName.Length > 0 && DisplayName != Value ? $"{DisplayName} ({Value})" : Value;
}

/// <summary>
/// The enumerated field types the World Editor offers as dropdowns, read from the game's own
/// <c>UI\UnitEditorData.txt</c> and <c>UI\WorldEditData.txt</c>.
///
/// Each section is one field TYPE, as named by the <c>type</c> column of a metadata SLK, and holds
/// an ordered list of <c>NN=storedValue,WESTRING_KEY</c>. So this is simultaneously the authority
/// for which values a field may legally take and the only place the human-readable names live.
///
/// <code>
/// [attackType]
/// 00=unknown,WESTRING_NONE
/// 01=normal,WESTRING_UE_ATTACKTYPE_NORMAL
/// NumValues=8
/// </code>
///
/// WHY this exists. Options were previously derived by collecting the distinct values the base
/// game data happened to use for a field. That is wrong in two directions at once. It misses legal
/// values no stock object uses, and it produces raw tokens, so a team colour field offered
/// "-1, 0, 1, 2" where the editor offers "None, Red, Blue, Teal". Deriving a closed set by
/// observation, when the closed set is shipped in a file, is guesswork dressed as measurement.
/// </summary>
public sealed class EditorEnumData
{
    private readonly Dictionary<string, List<EnumOption>> _byType =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nothing loaded. Every lookup misses, so callers fall back cleanly.</summary>
    public static EditorEnumData Empty { get; } = new();

    /// <summary>Field type names that resolve to a closed set of values.</summary>
    public IReadOnlyCollection<string> Types => _byType.Keys;

    /// <summary>
    /// The ordered legal values for a field type, or false when the type is not enumerated.
    /// </summary>
    public bool TryGet(string type, out IReadOnlyList<EnumOption> options)
    {
        if (type.Length > 0 && _byType.TryGetValue(type, out var list))
        {
            options = list;
            return true;
        }
        options = Array.Empty<EnumOption>();
        return false;
    }

    /// <summary>
    /// Parses one or more editor data files. <paramref name="strings"/> resolves the WESTRING
    /// display keys; pass the same table the rest of the game data uses so a name never differs
    /// between two parts of the UI.
    /// </summary>
    public static EditorEnumData FromByteSources(WorldEditStrings strings, params byte[]?[] files)
    {
        var data = new EditorEnumData();
        foreach (var bytes in files)
        {
            if (bytes is null || bytes.Length == 0) continue;
            data.Ingest(System.Text.Encoding.UTF8.GetString(bytes), strings);
        }
        return data;
    }

    private void Ingest(string text, WorldEditStrings strings)
    {
        string? section = null;
        List<EnumOption>? current = null;

        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                // A later file may extend a section a earlier one opened, so reuse the list.
                if (!_byType.TryGetValue(section, out current))
                    _byType[section] = current = new List<EnumOption>();
                continue;
            }
            if (current is null) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();

            // Sort and NumValues are metadata about the section, not values in it.
            if (key.Equals("NumValues", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Sort", StringComparison.OrdinalIgnoreCase)) continue;
            // The World Editor skips the "_Alt" duplicates, which are alternate labels.
            if (key.EndsWith("_Alt", StringComparison.OrdinalIgnoreCase)) continue;

            var rest = line[(eq + 1)..].Trim();
            if (rest.Length == 0) continue;

            // "storedValue,WESTRING_KEY". A value containing no comma is its own label.
            int comma = rest.IndexOf(',');
            var value = (comma < 0 ? rest : rest[..comma]).Trim();
            var nameKey = comma < 0 ? "" : rest[(comma + 1)..].Trim();

            var display = nameKey.Length > 0 && strings.TryGet(nameKey, out var resolved)
                ? resolved
                : nameKey;
            // An unresolved WESTRING is worse than nothing as a label, so drop back to the value.
            if (display.StartsWith("WESTRING", StringComparison.OrdinalIgnoreCase)) display = "";

            if (value.Length > 0 && !current.Any(o =>
                    string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase)))
                current.Add(new EnumOption(value, display));
        }
    }
}
