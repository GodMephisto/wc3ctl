using Wc3.Model;
using War3Net.Build.Info;

namespace Wc3.Commands;

/// <summary>
/// Static, read-only sanity check ("lint") over a loaded map. Surfaces the loader's
/// own diagnostics plus missing/empty required files and obviously-wrong map-info
/// values. A map is "valid" when it has zero Error-severity issues; warnings never
/// flip the verdict. Pure over <see cref="MapDocument"/> — writes nothing.
/// </summary>
public static class ValidateCommand
{
    // A runnable, playable map is expected to contain these:
    //   war3map.w3i - map info header (name, players, bounds)
    //   war3map.w3e - terrain / environment
    private static readonly string[] RequiredFiles = { "war3map.w3i", "war3map.w3e" };

    // A map needs at least one of these to run at all.
    private static readonly string[] ScriptFiles = { "war3map.j", "war3map.lua" };

    public static ValidateResult Execute(MapDocument doc)
    {
        var issues = new List<ValidationIssue>();

        // 1. Surface the loader's own diagnostics (unreadable / unparseable entries).
        foreach (var d in doc.Diagnostics)
            issues.Add(new ValidationIssue(d.Severity, "loader", d.FileName, d.Message));

        // 2. Required files must be present.
        foreach (var f in RequiredFiles)
            if (doc.GetFile(f) is null)
                issues.Add(new ValidationIssue(
                    DiagnosticSeverity.Error, "missing-file", f, $"required file '{f}' is missing"));

        // 3. The map must carry at least one script.
        if (!ScriptFiles.Any(s => doc.GetFile(s) is not null))
            issues.Add(new ValidationIssue(
                DiagnosticSeverity.Error, "missing-file", "war3map.j",
                "map has no script file (war3map.j or war3map.lua); it cannot run"));

        // 4. Empty named files are almost always a packaging mistake.
        foreach (var e in doc.Files)
            if (e.FileName is not null && e.RawBytes.Length == 0)
                issues.Add(new ValidationIssue(
                    DiagnosticSeverity.Warning, "empty-file", e.FileName, "internal file is empty (0 bytes)"));

        // 5. Map-info sanity (only when war3map.w3i parsed successfully).
        if (doc.GetFile("war3map.w3i")?.Model is MapInfo info)
        {
            if ((info.Players?.Count ?? 0) == 0)
                issues.Add(new ValidationIssue(
                    DiagnosticSeverity.Warning, "map-info", "war3map.w3i", "map declares no players"));
            if (info.PlayableMapAreaWidth <= 0 || info.PlayableMapAreaHeight <= 0)
                issues.Add(new ValidationIssue(
                    DiagnosticSeverity.Warning, "map-info", "war3map.w3i",
                    $"non-positive playable area ({info.PlayableMapAreaWidth}x{info.PlayableMapAreaHeight})"));
        }

        int errors = issues.Count(i => i.Severity == DiagnosticSeverity.Error);
        int warnings = issues.Count(i => i.Severity == DiagnosticSeverity.Warning);
        return new ValidateResult(errors == 0, errors, warnings, issues);
    }
}
