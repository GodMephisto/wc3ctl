// src/Wc3.Commands/ObjectSetCommand.cs
using System.Globalization;
using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.Model;

namespace Wc3.Commands;

public static class ObjectSetCommand
{
    public sealed record ObjectSetResult(bool Ok, string Message, string? Warning = null);

    /// <summary>Backward-compatible unit overload (CLI default, original API).</summary>
    public static ObjectSetResult Execute(MapDocument doc, string rawcode, string field, string value) =>
        Execute(doc, ObjectKind.Unit, rawcode, field, value);

    /// <summary>
    /// Sets a field on an object of any Object Editor kind and marks the kind's
    /// war3map.* file dirty so the next Save re-serializes it (that file only).
    ///
    /// Field syntax: a bare 4-char code, or "code:N" selecting level N (ability/
    /// upgrade) or variation N (doodad). A bare code on a leveled kind edits an
    /// existing non-leveled (level 0) modification when present, else level 1; a
    /// brand-new modification lands at level 1 (variation 0 for doodads).
    ///
    /// Edits always target the war3map.* layer: an object that lives only in the
    /// Reforged war3mapSkin.* twin gets its modification group mirrored into
    /// war3map.* (created on demand) — the skin file itself is never rewritten.
    /// An existing modification keeps its declared type (the value must parse as
    /// that type); a new modification infers its type from the value's shape
    /// (int → Int, decimal → Unreal, else String) and reports a warning.
    /// </summary>
    public static ObjectSetResult Execute(
        MapDocument doc, ObjectKind kind, string rawcode, string field, string value)
    {
        if (rawcode.Length != 4)
            return new(false, $"invalid rawcode '{rawcode}' — expected 4 characters");

        var (code, level, fieldError) = ParseFieldToken(field);
        if (fieldError is not null)
            return new(false, fieldError);
        var shape = ObjectDataWriter.ShapeOf(kind);
        if (shape == ObjectDataShape.Simple && level is not null)
            return new(false, $"'{field}': level syntax (code:N) only applies to leveled kinds "
                + "(ability/upgrade) and doodad variations");

        var info = ObjectKinds.Info(kind);
        string kindName = kind.ToString().ToLowerInvariant();
        int id = rawcode.FromRawcode();

        // The object must already be defined by the map (either layer); the merged
        // entry also supplies the base id when the group must be mirrored from skin.
        var merged = ObjectKinds.MergedEntries(doc, info).FirstOrDefault(e => e.Id == id);
        if (merged is null)
            return new(false, $"{kindName} {rawcode} not found in map");

        var model = ObjectDataWriter.GetOrCreateMapModel(doc, kind);
        var access = model is null ? null : ObjectDataWriter.AccessFor(model);
        if (access is null)
            return new(false, $"map has no parseable {info.MapFile} ({kindName} object data)");

        var group = access.FindGroup(id);
        if (group is null)
        {
            // Defined only in the skin layer: mirror the group into war3map.*. A
            // custom keeps its base as OldId (0 = base-less); a modified standard
            // is keyed by OldId with NewId 0, same as the read side.
            bool isCustom = merged.OldId != id;
            group = isCustom ? access.CreateGroup(merged.OldId, id) : access.CreateGroup(id, 0);
        }

        int fieldId = code.FromRawcode();
        ObjectDataModification? mod;
        int slot; // level/variation a newly created modification is stored at
        if (shape == ObjectDataShape.Simple)
        {
            mod = access.FindMod(group, fieldId, 0);
            slot = 0;
        }
        else if (level is int n)
        {
            mod = access.FindMod(group, fieldId, n);
            slot = n;
        }
        else if (shape == ObjectDataShape.Level)
        {
            // Bare code on a leveled kind: non-leveled fields live at level 0,
            // per-level data starts at 1 — prefer whichever already exists.
            mod = access.FindMod(group, fieldId, 0) ?? access.FindMod(group, fieldId, 1);
            slot = 1;
        }
        else // Variation with a bare code → variation 0
        {
            mod = access.FindMod(group, fieldId, 0);
            slot = 0;
        }

        string? warning = null;
        if (mod is not null)
        {
            if (!TryParseAs(value, mod.Type, out var typed))
                return new(false, $"'{value}' is not a valid {mod.Type} (field {field} is typed {mod.Type})");
            mod.Value = typed;
        }
        else
        {
            var (typed, type) = Infer(value);
            access.AddMod(group, fieldId, slot, type, typed);
            // New field (no prior modification): the type is guessed from the value's
            // shape, which is not authoritative. Int vs Real are not byte-compatible, so
            // a wrong guess stores a wrong in-game value (structure stays valid).
            warning = $"field {field} was newly added with inferred type {type} (guessed from the value); "
                    + "verify that matches the field's real type — an Int written where the game expects Real "
                    + "(or vice-versa) stores a wrong value.";
        }

        doc.AddOrReplaceModelFile(info.MapFile, model!);
        return new(true, $"set {field}={value} on {rawcode}", warning);
    }

    /// <summary>Splits "code" / "code:N" (N ≥ 0, digits only).</summary>
    private static (string Code, int? Level, string? Error) ParseFieldToken(string field)
    {
        int colon = field.IndexOf(':');
        if (colon < 0)
            return field.Length == 4
                ? (field, null, null)
                : (field, null, $"invalid field code '{field}' — expected 4 characters");
        var code = field[..colon];
        return code.Length == 4
            && int.TryParse(field[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? (code, n, null)
            : (code, null, $"invalid field '{field}' — expected a 4-character code or code:N");
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
