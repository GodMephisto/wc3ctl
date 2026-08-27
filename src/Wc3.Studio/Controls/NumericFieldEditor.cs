// src/Wc3.Studio/Controls/NumericFieldEditor.cs
using System.Globalization;
using Wc3.Commands;

namespace Wc3.Studio.Controls;

/// <summary>
/// The rules behind the object editor's bounded numeric editor, kept as pure functions so
/// they are testable without a window. The field metadata already states the legal range
/// (minval, maxval, forcenonneg on <see cref="FormField"/>), and until now the panel showed
/// it as a hint while the editor accepted anything, so an out-of-range value only surfaced
/// when Apply rejected it. HiveWE reads the same metadata columns and leaves the code that
/// would apply them commented out, its spin boxes run INT_MIN to INT_MAX, which is worth
/// knowing because enforcing the bounds at input time is exactly the gap this closes.
/// </summary>
public static class NumericFieldEditor
{
    /// <summary>Metadata types the game stores as one number. Everything else, lists
    /// included, stays with whatever editor its own type earns.</summary>
    public static bool IsNumericType(string type) => type.ToLowerInvariant() switch
    {
        "int" or "real" or "unreal" => true,
        _ => false,
    };

    /// <summary>Whole-number types. They round on write and refuse decimal input.</summary>
    public static bool IsIntType(string type) =>
        string.Equals(type, "int", StringComparison.OrdinalIgnoreCase);

    /// <summary>A stored value the spin editor can hold, empty or one plain number. A comma
    /// list or any other shape keeps the free text editor, because loading it into a spin
    /// box would destroy the parts a number cannot carry.</summary>
    public static bool CanEdit(string? storedValue) =>
        string.IsNullOrWhiteSpace(storedValue) || Parse(storedValue) is not null;

    /// <summary>Invariant-culture numeric parse, null when the text is not one number.
    /// Object data is written invariant, so the editor must read and write it the same
    /// way whatever the OS locale says a decimal point looks like.</summary>
    public static decimal? Parse(string? value) =>
        decimal.TryParse((value ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    /// <summary>
    /// The spin bounds, the metadata range widened to include the stored value. Widening is
    /// the same courtesy the combo editor extends to an unlisted stored value, opening the
    /// editor must never change data by itself, so a stored -1 under a declared minimum of 0
    /// stays visible and editable. Apply still validates against the metadata proper, so the
    /// widening never lets a NEW out-of-range value through to the map.
    /// </summary>
    public static (decimal Min, decimal Max) Bounds(FormField? form, decimal? storedValue)
    {
        var min = Parse(form?.MinValue) ?? decimal.MinValue;
        var max = Parse(form?.MaxValue) ?? decimal.MaxValue;
        if (form?.ForceNonNegative == true && min < 0)
            min = 0;
        if (storedValue is { } s)
        {
            if (s < min) min = s;
            if (s > max) max = s;
        }
        if (max < min) // corrupt metadata guard, an empty range would wedge the control
            max = min;
        return (min, max);
    }

    /// <summary>
    /// The text Apply writes, from the editor's visible text so an uncommitted keystroke
    /// still counts (NumericUpDown commits its text on focus loss, which a click on Apply
    /// races). Clamped into the spin bounds, whole types round away from zero, and empty
    /// stays empty so canbeempty fields can be cleared. Unparsable text falls back to the
    /// last committed value rather than writing garbage through the numeric path.
    /// </summary>
    public static string ValueText(string? text, decimal? committedValue, decimal min, decimal max, bool isInt)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0)
            return "";
        var v = Parse(t) ?? committedValue;
        if (v is null)
            return "";
        var clamped = Math.Clamp(v.Value, min, max);
        if (!isInt)
            // Up to eight decimals, trailing zeros dropped. The game reads these as
            // 32-bit floats, so nothing real survives past that precision anyway.
            return clamped.ToString("0.########", CultureInfo.InvariantCulture);
        clamped = Math.Clamp(clamped, long.MinValue, long.MaxValue);
        return ((long)decimal.Round(clamped, 0, MidpointRounding.AwayFromZero))
            .ToString(CultureInfo.InvariantCulture);
    }
}
