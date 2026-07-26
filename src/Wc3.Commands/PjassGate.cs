// src/Wc3.Commands/PjassGate.cs
using System.Diagnostics;
using System.Text;

namespace Wc3.Commands;

/// <summary>Result of the pjass pass. <paramref name="Ran"/> false means pjass or the game's
/// script headers were unavailable, which is never itself a failure, the built-in
/// <see cref="Wc3.Model.JassScriptCheck"/> remains the guaranteed line of defense.</summary>
public sealed record PjassResult(
    bool Ran,
    bool Passed,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    string Note);

/// <summary>
/// The deep script check, delegating to <c>pjass</c>, the real JASS parser, instead of guessing.
/// Warcraft III ships it with the World Editor toolchain (<c>x86_64\JassHelper\pjass.exe</c>), and
/// <c>jasshelper.conf</c> shows the required invocation is "pjass common.j Blizzard.j war3map.j".
/// Reforged keeps those two headers inside CASC, so they are read through
/// <see cref="GameData.GameDataContext.TryReadFile"/> and staged to temporary files. Supplying them
/// matters, without them every native reads as undeclared and pjass drowns in false errors.
///
/// Best-effort by construction. A missing install, a missing pjass, or a timeout all report
/// <c>Ran = false</c> rather than failing a map, so this can never block a port on a machine that
/// simply has no game data.
/// </summary>
public static class PjassGate
{
    /// <summary>Where the toolchain sits, relative to the install root, newest layout first.</summary>
    private static readonly string[] PjassRelativePaths =
    {
        Path.Combine("_retail_", "x86_64", "JassHelper", "pjass.exe"),
        Path.Combine("x86_64", "JassHelper", "pjass.exe"),
        Path.Combine("JassHelper", "pjass.exe"),
        "pjass.exe",
    };

    /// <summary>Runs pjass over <paramref name="jass"/>. <paramref name="timeoutMs"/> guards against
    /// a pathological script (a 145k-line map with no headers can run for minutes).</summary>
    public static PjassResult Check(string jass, string? gameDirOverride = null, int timeoutMs = 120_000)
    {
        string? install = GameData.GameInstall.Locate(gameDirOverride);
        if (install is null)
            return new(false, false, Array.Empty<string>(), Array.Empty<string>(), "no Warcraft III install found, pjass skipped");

        string? pjass = PjassRelativePaths
            .Select(rel => Path.Combine(install, rel))
            .FirstOrDefault(File.Exists);
        if (pjass is null)
            return new(false, false, Array.Empty<string>(), Array.Empty<string>(), "pjass.exe not found in the install, skipped");

        if (!GameData.GameData.TryOpen(gameDirOverride, out var ctx, out var diag) || ctx is null)
            return new(false, false, Array.Empty<string>(), Array.Empty<string>(),
                $"could not open game data for common.j{(string.IsNullOrWhiteSpace(diag) ? "" : $" ({diag})")}, pjass skipped");

        if (!TryReadHeader(ctx, "common.j", out var commonJ) ||
            !TryReadHeader(ctx, "Blizzard.j", out var blizzardJ))
            return new(false, false, Array.Empty<string>(), Array.Empty<string>(),
                "common.j / Blizzard.j not found in the installed game data, pjass skipped");

        string temp = Path.Combine(Path.GetTempPath(), "wc3ctl-pjass-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(temp);
            string commonPath = Path.Combine(temp, "common.j");
            string blizzardPath = Path.Combine(temp, "Blizzard.j");
            string scriptPath = Path.Combine(temp, "war3map.j");
            File.WriteAllBytes(commonPath, commonJ);
            File.WriteAllBytes(blizzardPath, blizzardJ);
            File.WriteAllBytes(scriptPath, Encoding.Latin1.GetBytes(jass));

            var psi = new ProcessStartInfo(pjass)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = temp,
            };
            psi.ArgumentList.Add(commonPath);
            psi.ArgumentList.Add(blizzardPath);
            psi.ArgumentList.Add(scriptPath);

            using var proc = Process.Start(psi);
            if (proc is null)
                return new(false, false, Array.Empty<string>(), Array.Empty<string>(), "could not start pjass, skipped");

            // Read before waiting so a large error dump cannot fill the pipe and deadlock the child.
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return new(false, false, Array.Empty<string>(), Array.Empty<string>(),
                    $"pjass did not finish within {timeoutMs / 1000}s, skipped");
            }

            var output = (stdout + "\n" + stderr).Replace("\r\n", "\n");

            // Report only the map's own diagnostics, not the staged headers'. Paths are absolute in
            // pjass output, so key off the staged script name and shorten it back to war3map.j.
            var mine = output.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && l.Contains(scriptPath, StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Replace(scriptPath, "war3map.j", StringComparison.OrdinalIgnoreCase))
                // pjass also prints a per-file "Parse successful: N lines" line, which names the
                // script and would otherwise read as a diagnostic about it.
                .Where(l => !l.StartsWith("Parse ", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // "Variable x is uninitialized" is advisory. JASS zero-initializes locals, real shipped
            // maps are full of them, and the repair this tool applies deliberately leaves a variable
            // at its type default. Only the genuinely fatal diagnostics decide the verdict.
            bool IsAdvisory(string line) =>
                line.Contains("is uninitialized", StringComparison.OrdinalIgnoreCase)
                || line.Contains("failed with", StringComparison.OrdinalIgnoreCase);

            var errors = mine.Where(l => !IsAdvisory(l)).Take(50).ToList();
            var warnings = mine.Where(IsAdvisory)
                .Where(l => !l.Contains("failed with", StringComparison.OrdinalIgnoreCase))
                .Take(50).ToList();

            bool passed = errors.Count == 0;
            string summary = output.Split('\n').Select(l => l.Trim())
                .LastOrDefault(l => l.StartsWith("Parse ", StringComparison.OrdinalIgnoreCase)) ?? "";

            return new(true, passed, errors, warnings,
                (passed ? "pjass found no fatal problem in the script" : $"pjass reported {errors.Count} error(s)")
                + (warnings.Count == 0 ? "" : $", plus {warnings.Count} uninitialized-variable notice(s)")
                + (summary.Length == 0 ? "" : $" [{summary}]"));
        }
        catch (Exception ex)
        {
            return new(false, false, Array.Empty<string>(), Array.Empty<string>(), $"pjass could not run ({ex.Message}), skipped");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Reads a script header out of CASC, trying the documented location first and then
    /// falling back to a listfile search, since the layout differs across game versions.</summary>
    private static bool TryReadHeader(GameData.GameDataContext ctx, string fileName, out byte[] bytes)
    {
        if (ctx.TryReadFile(Path.Combine("scripts", fileName), out bytes)) return true;
        foreach (var candidate in ctx.FindFiles(fileName, 20))
            if (candidate.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)
                && ctx.TryReadFile(candidate, out bytes))
                return true;
        bytes = Array.Empty<byte>();
        return false;
    }
}
