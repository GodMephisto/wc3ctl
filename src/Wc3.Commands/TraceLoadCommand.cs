// src/Wc3.Commands/TraceLoadCommand.cs
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Wc3.Model;

namespace Wc3.Commands;

public sealed record TraceStep(int Index, string Label, bool Reached);

public sealed record TraceLoadResult(
    string Verdict,
    string Detail,
    int StepsInstrumented,
    int StepsReached,
    string? LastReached,
    string? FirstMissed,
    double SecondsElapsed,
    string TestMapPath,
    IReadOnlyList<TraceStep> Steps);

/// <summary>
/// Records HOW FAR a map gets through initialisation, instead of only whether it finished.
///
/// A map that hangs on the loading screen gives a human nothing: no error, no log entry, no crash
/// dump, and Windows shows "Not Responding" whether the game is looping or waiting. Diagnosing that
/// from the outside consumed an entire session and produced nine dead theories.
///
/// The way in is that JASS can write a file (<c>PreloadGenEnd</c>, the same trick every save-code
/// system uses). So this drops a NUMBERED marker after each initialisation step. Every step that
/// executed leaves its own file behind, so the highest-numbered file present is the last step
/// reached, and the next one names exactly where it stopped. That converts "stuck on loading" into
/// "it died between CreateAllUnits and InitCustomTriggers".
///
/// A marker per step rather than one accumulating log is deliberate: the preload buffer is cleared
/// on each flush, so per-step files are the only form that survives a hang mid-sequence.
/// </summary>
public static class TraceLoadCommand
{
    private static readonly string[] ExeRelativePaths =
    {
        Path.Combine("_retail_", "x86_64", "Warcraft III.exe"),
        Path.Combine("x86_64", "Warcraft III.exe"),
        "Warcraft III.exe",
    };

    /// <summary>A statement worth marking: a call at the top level of an init function.</summary>
    private static readonly Regex CallStatement =
        new(@"^\s*call\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

    public static TraceLoadResult Run(string mapPath, string gameDir, string documentsDir,
        double timeoutSeconds = 180)
    {
        var exe = ExeRelativePaths.Select(p => Path.Combine(gameDir, p)).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"Warcraft III.exe not found under '{gameDir}'.");

        var traceDir = Path.Combine(documentsDir, "CustomMapData", "wc3trace");
        if (Directory.Exists(traceDir)) Directory.Delete(traceDir, recursive: true);
        Directory.CreateDirectory(traceDir);

        var doc = MapDocument.Load(mapPath);
        var script = doc.GetFile("war3map.j") ?? doc.GetFile("scripts\\war3map.j")
            ?? throw new InvalidOperationException("Map has no war3map.j to instrument.");
        // Latin-1 so a script that is not valid UTF-8 round-trips unchanged.
        var text = Encoding.Latin1.GetString(script.OverrideBytes ?? script.RawBytes);
        var (instrumented, labels) = Instrument(text);
        FileEditCommand.AddOrReplace(doc, script.FileName!, Encoding.Latin1.GetBytes(instrumented));

        var testDir = Path.Combine(documentsDir, "Maps", "Test");
        Directory.CreateDirectory(testDir);
        var testMap = Path.Combine(testDir, "WorldEditTestMap" + Path.GetExtension(mapPath));
        doc.Save(testMap);

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var a in new[] { "-launch", "-uid", "w3", "-window", "-nowfpause",
                                  "-loadfile", Path.Combine("Maps", "Test", Path.GetFileName(testMap)),
                                  "-testmapprofile", "WorldEdit" })
            psi.ArgumentList.Add(a);

        var sw = Stopwatch.StartNew();
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Warcraft III.");
        try
        {
            int lastSeen = -1, idleTicks = 0;
            while (sw.Elapsed.TotalSeconds < timeoutSeconds)
            {
                int high = HighestMarker(traceDir);
                if (high == labels.Count - 1) break;                 // finished the sequence
                if (high == lastSeen) idleTicks++; else { lastSeen = high; idleTicks = 0; }
                // Stopped advancing for ~20s with steps remaining: that is where it died.
                if (idleTicks > 40 && high >= 0) break;
                if (proc.HasExited) break;
                Thread.Sleep(500);
            }

            int reachedTo = HighestMarker(traceDir);
            var steps = labels.Select((l, i) => new TraceStep(i, l, i <= reachedTo)).ToList();
            string verdict = reachedTo < 0 ? "NOTHING_RAN"
                : reachedTo >= labels.Count - 1 ? "COMPLETED" : "STOPPED";
            string detail = reachedTo < 0
                ? "no marker at all: the script never executed, which is what a script that does "
                  + "not compile looks like"
                : reachedTo >= labels.Count - 1
                    ? $"all {labels.Count} instrumented step(s) ran"
                    : $"stopped after step {reachedTo} ({labels[reachedTo]}); the next step, "
                      + $"'{labels[reachedTo + 1]}', never ran";

            return new(verdict, detail, labels.Count, reachedTo + 1,
                reachedTo >= 0 ? labels[reachedTo] : null,
                reachedTo + 1 < labels.Count ? labels[reachedTo + 1] : null,
                Math.Round(sw.Elapsed.TotalSeconds, 1), testMap, steps);
        }
        finally
        {
            try { if (!proc.HasExited) { proc.Kill(entireProcessTree: true); proc.WaitForExit(5000); } }
            catch { }
        }
    }

    private static int HighestMarker(string dir)
    {
        int high = -1;
        if (!Directory.Exists(dir)) return high;
        foreach (var f in Directory.EnumerateFiles(dir, "s*.txt"))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (name.Length > 1 && int.TryParse(name[1..], out var n) && n > high) high = n;
        }
        return high;
    }

    /// <summary>
    /// Inserts a numbered marker after each top-level call inside the engine's init functions.
    /// Those are the only places a load can stall, and marking them is enough to bracket the
    /// failure to a single step without instrumenting 200,000 lines.
    /// </summary>
    private static (string script, List<string> labels) Instrument(string text)
    {
        var entryPoints = new HashSet<string>(StringComparer.Ordinal)
        {
            "main", "config", "InitGlobals", "InitCustomTriggers", "RunInitializationTriggers",
            "CreateAllUnits", "CreateRegions", "InitSounds", "InitCustomTeams", "InitUpgrades",
            "InitTechTree", "CreateAllItems", "CreateNeutralPassiveBuildings",
        };

        var lines = text.Split('\n').ToList();
        var labels = new List<string>();
        var output = new List<string>(lines.Count + 256);
        string? inFunction = null;

        foreach (var raw in lines)
        {
            var trimmed = raw.TrimStart();
            var decl = Regex.Match(raw, @"^\s*function\s+([A-Za-z_][A-Za-z0-9_]*)");
            if (decl.Success) inFunction = decl.Groups[1].Value;
            output.Add(raw);
            if (trimmed.StartsWith("endfunction", StringComparison.Ordinal)) { inFunction = null; continue; }
            if (inFunction is null || !entryPoints.Contains(inFunction)) continue;

            var call = CallStatement.Match(raw);
            if (!call.Success) continue;

            int index = labels.Count;
            labels.Add($"{inFunction} -> {call.Groups[1].Value}");
            output.Add("    call PreloadGenClear()");
            output.Add("    call PreloadGenStart()");
            output.Add($"    call Preload(\"{index}\")");
            output.Add($"    call PreloadGenEnd(\"wc3trace\\\\s{index}.txt\")");
        }
        return (string.Join("\n", output), labels);
    }
}
