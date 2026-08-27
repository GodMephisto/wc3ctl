// src/Wc3.Commands/ObjectFormCommand.cs
using Wc3.GameData;
using Wc3.Model;

namespace Wc3.Commands;

/// <summary>One editable field, with everything a front end needs to render and validate it.</summary>
public sealed record FormField(
    string Code,
    string Name,
    string Value,
    string Display,
    string Source,
    string Type,
    string? MinValue,
    string? MaxValue,
    bool ForceNonNegative,
    bool CanBeEmpty,
    bool MultiLine,
    ObjectLayer Layer,
    bool LayerIsAuthoritative,
    IReadOnlyList<string> Options)
{
    /// <summary>Whether a candidate value satisfies the metadata's own constraints. The message
    /// is null when it passes.</summary>
    public string? Validate(string candidate)
    {
        if (string.IsNullOrEmpty(candidate))
            return CanBeEmpty ? null : $"{Name} cannot be empty";

        if (!double.TryParse(candidate, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
            return null;   // not numeric, so the numeric bounds do not apply

        if (ForceNonNegative && n < 0)
            return $"{Name} cannot be negative";
        if (MinValue is not null && double.TryParse(MinValue,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var min) && n < min)
            return $"{Name} is below the minimum of {MinValue}";
        if (MaxValue is not null && double.TryParse(MaxValue,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var max) && n > max)
            return $"{Name} is above the maximum of {MaxValue}";
        return null;
    }
}

/// <summary>A titled group of fields, the unit the World Editor shows as one collapsible section.</summary>
public sealed record FormGroup(string Key, string Title, IReadOnlyList<FormField> Fields);

/// <summary>An object rendered as a form rather than as a flat field dump.</summary>
public sealed record ObjectForm(
    string Rawcode,
    string? Name,
    string? BaseRawcode,
    ObjectKind Kind,
    IReadOnlyList<FormGroup> Groups,
    int FieldCount,
    int HiddenFieldCount,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Turns an object's merged fields into the grouped, ordered, validated form the World Editor
/// shows, using the metadata the game itself ships.
///
/// Without this a client can only render one flat list. A single unit resolves to 169 fields on a
/// real map and the kind defines 273, in no meaningful order, with no indication of which are
/// art and which are combat, which accept a blank, what the legal range is, or which of them even
/// apply to the object in hand. That is a data dump, not an editor, and it is unusable for the
/// job people actually open an object editor to do.
///
/// Every input here is a column of `unitmetadata.slk` and its siblings, read out of the retail
/// install. Nothing is invented and nothing is hardcoded per field, so the form tracks whatever
/// the installed patch says rather than a table that goes stale.
///
/// This lives in the command layer rather than in a panel because the house has three front ends
/// and a layout that lives in the GUI cannot be reached from the CLI or from MCP.
/// </summary>
public static class ObjectFormCommand
{
    // The category codes the metadata uses, in the order the World Editor shows them.
    private static readonly (string Key, string Title)[] CategoryOrder =
    {
        ("art", "Art"),
        ("combat", "Combat"),
        ("data", "Data"),
        ("abil", "Abilities"),
        ("move", "Movement"),
        ("path", "Pathing"),
        ("sound", "Sound"),
        ("stats", "Stats"),
        ("tech", "Techtree"),
        ("text", "Text"),
        ("editor", "Editor"),
    };

    public static ObjectForm Execute(
        MapDocument doc, ObjectKind kind, string rawcode, string? gameDirOverride)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var merged = ObjectGetCommand.Execute(doc, kind, rawcode, gameDirOverride);
        GameData.GameData.TryOpen(gameDirOverride, out var ctx, out _);
        return Build(doc, kind, rawcode, merged, ctx);
    }

    internal static ObjectForm Build(
        MapDocument doc, ObjectKind kind, string rawcode,
        MergedObjectResult merged, GameDataContext? ctx)
    {
        var diagnostics = new List<string>(merged.Diagnostics);
        var meta = ObjectKinds.FieldMeta(ctx, kind);
        if (meta is null)
            diagnostics.Add("no game data, so fields cannot be grouped, ordered or bounds-checked. "
                          + "Point --game-dir at a Warcraft III install for the full form.");

        // The measured split is the fallback for which layer a field lands in, and the only
        // answer available with no install present. See SkinFieldPartition.
        var partition = SkinFieldPartition.Learn(doc, kind);
        var byCode = ObjectKinds.ModsToDict(
            merged.Fields.Select(f => KeyValuePair.Create(f.Code, f.Value)).ToList());
        var (isHero, isBuilding, isItem) = Shape(kind, byCode, rawcode);

        int hidden = 0;
        var groups = new Dictionary<string, List<(string Sort, FormField Field)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in merged.Fields)
        {
            var bare = f.Code.Contains(':') ? f.Code[..f.Code.IndexOf(':')] : f.Code;
            ObjectFieldMeta? fm = null;
            if (meta is not null && meta.TryGet(bare, out var found)) fm = found;

            // A field the game says does not apply to this shape is noise, not data. The World
            // Editor hides them, which is most of the difference between 273 rows and a form.
            if (fm is not null && !fm.AppliesTo(isHero, isBuilding, isItem)) { hidden++; continue; }

            bool authoritative = fm is not null && fm.NetSafe.Length > 0;
            var layer = authoritative
                ? (fm!.IsSkinField ? ObjectLayer.Skin : ObjectLayer.Map)
                : partition.LayerFor(bare);

            ObjectKinds.TryGetFieldOptions(ctx, kind, bare, out _, out _, out var options);

            var field = new FormField(
                Code: f.Code,
                Name: f.Name,
                Value: f.Value,
                Display: f.Display,
                Source: f.Source,
                Type: fm?.Type ?? "",
                MinValue: Blank(fm?.MinValue),
                MaxValue: Blank(fm?.MaxValue),
                ForceNonNegative: fm?.ForceNonNegative ?? false,
                // Absent metadata must not invent a constraint, so a blank stays legal.
                CanBeEmpty: fm?.CanBeEmpty ?? true,
                MultiLine: fm is not null && fm.StringExt > 0,
                Layer: layer,
                LayerIsAuthoritative: authoritative,
                Options: options);

            var key = fm is null || fm.Category.Length == 0 ? "other" : fm.Category.ToLowerInvariant();
            // Sort key first, then the display name, so a group without sort keys is still ordered.
            var sort = (fm?.Sort ?? "") + "" + f.Name;
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new();
            list.Add((sort, field));
        }

        var ordered = new List<FormGroup>();
        foreach (var (key, title) in CategoryOrder)
            if (groups.TryGetValue(key, out var list))
            {
                ordered.Add(new FormGroup(key, title, Sorted(list)));
                groups.Remove(key);
            }
        // Anything the metadata did not categorise, or that arrived with no metadata at all.
        foreach (var key in groups.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            ordered.Add(new FormGroup(key, key == "other" ? "Other" : Title(key), Sorted(groups[key])));

        return new ObjectForm(rawcode, merged.Name, merged.BaseRawcode, kind, ordered,
            ordered.Sum(g => g.Fields.Count), hidden, diagnostics);
    }

    private static IReadOnlyList<FormField> Sorted(List<(string Sort, FormField Field)> list) =>
        list.OrderBy(x => x.Sort, StringComparer.OrdinalIgnoreCase).Select(x => x.Field).ToList();

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string Title(string key) =>
        key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key[1..];

    /// <summary>
    /// Which of the four applicability flags to read, derived from the object itself.
    /// </summary>
    /// <remarks>
    /// The metadata states applicability per shape but the object does not carry a "shape" field,
    /// so it is inferred. A building says so in <c>ubld</c>. A hero is recognised by carrying the
    /// hero-only fields, a primary attribute or a hero ability list, which is more reliable than
    /// the uppercase-first-letter convention because a custom object need not follow it.
    /// </remarks>
    private static (bool Hero, bool Building, bool Item) Shape(
        ObjectKind kind, IReadOnlyDictionary<string, string> fields, string rawcode)
    {
        if (kind == ObjectKind.Item) return (false, false, true);

        bool building = fields.TryGetValue("ubld", out var b)
            && b.Trim() is "1" or "true" or "True";

        bool hero = (fields.TryGetValue("upra", out var pra) && pra.Trim().Length > 0)
                 || (fields.TryGetValue("uhab", out var hab) && hab.Trim().Length > 0)
                 // Last resort, and the convention every base-game hero follows.
                 || (rawcode.Length == 4 && char.IsUpper(rawcode[0]));

        return (hero && !building, building, false);
    }
}
