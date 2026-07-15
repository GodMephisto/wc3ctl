// src/Wc3.Commands/ObjectSetCommand.cs
using System.Globalization;
using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.Model;

namespace Wc3.Commands;

public static class ObjectSetCommand
{
    public sealed record ObjectSetResult(bool Ok, string Message);

    /// <summary>
    /// Sets a field on a unit defined in the map's war3map.w3u and marks the file
    /// dirty so the next Save re-serializes it. An existing modification keeps its
    /// declared type (the value must parse as that type); a new modification infers
    /// its type from the value's shape (int → Int, decimal → Unreal, else String).
    /// </summary>
    public static ObjectSetResult Execute(MapDocument doc, string rawcode, string field, string value)
    {
        if (rawcode.Length != 4)
            return new(false, $"invalid rawcode '{rawcode}' — expected 4 characters");
        if (field.Length != 4)
            return new(false, $"invalid field code '{field}' — expected 4 characters");

        var entry = doc.GetFile("war3map.w3u");
        if (entry?.Model is not UnitObjectData w3u)
            return new(false, "map has no parseable war3map.w3u (unit object data)");

        // Same lookup as ObjectGetCommand: a custom unit's NewId is its rawcode; a
        // modified standard unit lives in BaseUnits keyed by OldId (NewId is 0).
        int id = rawcode.FromRawcode();
        var unit = w3u.NewUnits.FirstOrDefault(u => u.NewId == id)
            ?? w3u.BaseUnits.FirstOrDefault(u => u.OldId == id);
        if (unit is null)
            return new(false, $"unit {rawcode} not found in map");

        int fieldId = field.FromRawcode();
        var mod = unit.Modifications.FirstOrDefault(m => m.Id == fieldId);
        if (mod is not null)
        {
            if (!TryParseAs(value, mod.Type, out var typed))
                return new(false, $"'{value}' is not a valid {mod.Type} (field {field} is typed {mod.Type})");
            mod.Value = typed;
        }
        else
        {
            var (typed, type) = Infer(value);
            unit.Modifications.Add(new SimpleObjectDataModification { Id = fieldId, Type = type, Value = typed });
        }

        doc.ReplaceModel(entry, w3u);
        return new(true, $"set {field}={value} on {rawcode}");
    }

    private static bool TryParseAs(string value, ObjectDataType type, out object typed)
    {
        switch (type)
        {
            case ObjectDataType.Int
                when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i):
                typed = i; return true;
            case ObjectDataType.Real or ObjectDataType.Unreal
                when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f):
                typed = f; return true;
            case ObjectDataType.Bool when bool.TryParse(value, out var b):
                typed = b; return true;
            case ObjectDataType.Char when value.Length == 1:
                typed = value[0]; return true;
            case ObjectDataType.String:
                typed = value; return true;
            default:
                typed = value; return false;
        }
    }

    private static (object Typed, ObjectDataType Type) Infer(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? (i, ObjectDataType.Int)
        : value.Contains('.')
            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
            ? (f, ObjectDataType.Unreal)
        : (value, ObjectDataType.String);
}
