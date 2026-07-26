// tests/Wc3.Tests/PortTriageCorpusTests.cs
// Sweeps a whole library of real custom maps, ports several heroes out of each into a fresh blank
// map, and checks whether the result would still compile. The point is to find the porting patterns
// that break BEFORE they are hit in game, rather than one hero at a time in a host lobby.
//
// Needs real maps on disk, so it is Category=Corpus (excluded from the hermetic suite) and skips
// silently when the library is absent. Writes a ranked report next to the maps.
//
//   dotnet test --filter "Category=Corpus&FullyQualifiedName~PortTriage"
using System.Diagnostics;
using System.Text;
using Wc3.Commands;
using Wc3.Model;
using Xunit;
using Xunit.Abstractions;

namespace Wc3.Tests;

public class PortTriageCorpusTests
{
    private const string MapDir = @"C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download";

    /// <summary>Heroes to port per map. A few is enough to surface a map's patterns, and the
    /// library is several gigabytes, so this trades exhaustiveness for a run that finishes.</summary>
    private const int HeroesPerMap = 4;

    private readonly ITestOutputHelper _out;
    public PortTriageCorpusTests(ITestOutputHelper output) => _out = output;

    [Fact]
    [Trait("Category", "Corpus")]
    public void Triage_ports_across_the_map_library()
    {
        if (!Directory.Exists(MapDir)) return;

        var maps = UniqueCustomMaps(MapDir);
        if (maps.Count == 0) return;

        var report = new StringBuilder();
        var failures = new List<(string Kind, string Map, string Hero, string Detail)>();
        int attempted = 0, compiled = 0, refused = 0, repaired = 0, mapErrors = 0;
        var sw = Stopwatch.StartNew();

        report.AppendLine($"# Port triage over {maps.Count} unique custom maps");
        report.AppendLine();

        foreach (var map in maps)
        {
            string name = Path.GetFileName(map);
            MapDocument source;
            try { source = MapDocument.Load(map); }
            catch (Exception ex)
            {
                mapErrors++;
                failures.Add(("map-load-failed", name, "", Trunc(ex.Message)));
                report.AppendLine($"## {name}\n  MAP LOAD FAILED: {Trunc(ex.Message)}\n");
                continue;
            }

            var heroes = Heroes(source).Take(HeroesPerMap).ToList();
            report.AppendLine($"## {name}  ({heroes.Count} hero(es) sampled)");
            if (heroes.Count == 0) report.AppendLine("  no custom heroes found");

            foreach (var (rawcode, heroName) in heroes)
            {
                attempted++;
                try
                {
                    var target = MapDocument.Load(BlankMap.Create().SaveToBytes());
                    var bundle = BundleCommand.ResolveObject(source, ObjectKind.Unit, rawcode, gameDirOverride: null);
                    var result = PortCommand.PortUnit(source, bundle, target);

                    // The gate's verdict, then an independent check of what actually landed.
                    var script = result.Script;
                    if (script is { Written: false })
                    {
                        refused++;
                        var why = script.Notes.FirstOrDefault(n => n.Contains("REFUSED", StringComparison.Ordinal)) ?? "";
                        failures.Add(("port-refused-uncompilable", name, rawcode, Trunc(why)));
                        report.AppendLine($"  {rawcode} {heroName}: REFUSED (would not compile)");
                        continue;
                    }
                    if (script?.Notes.Any(n => n.Contains("repaired", StringComparison.OrdinalIgnoreCase)) == true)
                        repaired++;

                    var entry = target.GetFile("war3map.j");
                    string jass = Encoding.Latin1.GetString(entry!.OverrideBytes ?? entry.RawBytes);
                    var issues = JassScriptCheck.Check(jass);
                    if (!JassScriptCheck.IsCompilable(issues))
                    {
                        var worst = issues.First(i =>
                            i.Severity == DiagnosticSeverity.Error && JassScriptCheck.BlocksCompilation(i.Kind));
                        failures.Add((worst.Kind.ToString(), name, rawcode, Trunc(worst.Message)));
                        report.AppendLine($"  {rawcode} {heroName}: BROKEN {worst.Kind} - {Trunc(worst.Message)}");
                        continue;
                    }

                    compiled++;
                    report.AppendLine($"  {rawcode} {heroName}: ok"
                        + (script is null ? " (no script carried)" : $" ({script.Functions} fn)"));
                }
                catch (Exception ex)
                {
                    failures.Add((ex.GetType().Name, name, rawcode, Trunc(ex.Message)));
                    report.AppendLine($"  {rawcode} {heroName}: THREW {ex.GetType().Name} - {Trunc(ex.Message)}");
                }
            }
            report.AppendLine();
        }

        report.AppendLine("# Ranked failure classes");
        foreach (var g in failures.GroupBy(f => f.Kind).OrderByDescending(g => g.Count()))
        {
            report.AppendLine($"\n## {g.Key}  ({g.Count()})");
            foreach (var f in g.Take(12))
                report.AppendLine($"  - {f.Map} {f.Hero}: {f.Detail}");
        }

        string summary = $"maps={maps.Count} heroes attempted={attempted} compiled={compiled} "
            + $"auto-repaired={repaired} refused={refused} broken={failures.Count} "
            + $"map-load-failed={mapErrors} elapsed={sw.Elapsed:hh\\:mm\\:ss}";
        report.Insert(0, summary + "\n\n");

        string outPath = Path.Combine(Path.GetTempPath(), "wc3ctl-port-triage.md");
        File.WriteAllText(outPath, report.ToString());
        _out.WriteLine(summary);
        _out.WriteLine("report: " + outPath);

        // Reporting is the deliverable, so a broken hero does not fail the run. Only the guarantee
        // this work exists to enforce is asserted: a port never writes a script that cannot compile.
        var shipped = failures.Where(f => !f.Kind.StartsWith("port-refused", StringComparison.Ordinal)
                                       && !f.Kind.Contains("map-load", StringComparison.Ordinal)).ToList();
        Assert.True(shipped.Count == 0,
            $"{shipped.Count} port(s) wrote a script that cannot compile, see {outPath}:\n"
            + string.Join("\n", shipped.Take(10).Select(f => $"  {f.Map} {f.Hero}: {f.Kind} {f.Detail}")));
    }

    /// <summary>Custom heroes, by the World Editor convention that a hero's rawcode starts with an
    /// uppercase letter while an ordinary unit's starts lowercase (Hpal/H000 vs hfoo/h000).</summary>
    private static IEnumerable<(string Rawcode, string Name)> Heroes(MapDocument doc)
    {
        var list = ObjectListCommand.Execute(doc, ObjectKind.Unit, gameDirOverride: null);
        return list.Items
            .Where(i => i.Rawcode.Length == 4 && char.IsUpper(i.Rawcode[0]))
            .Where(i => !string.Equals(i.Rawcode, i.BaseRawcode, StringComparison.Ordinal)) // custom, not a tweaked standard
            .Select(i => (i.Rawcode, i.Name ?? ""));
    }

    /// <summary>One map per title, newest-largest file wins, Blizzard melee maps excluded. Version
    /// suffixes vary wildly across this library, so the key is the name with trailing version-ish
    /// tokens stripped.</summary>
    private static List<string> UniqueCustomMaps(string dir)
    {
        var best = new Dictionary<string, (string Path, long Size)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(dir, "*.w3?", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(path);
            if (!ext.Equals(".w3x", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".w3m", StringComparison.OrdinalIgnoreCase)) continue;

            string file = Path.GetFileNameWithoutExtension(path);
            if (file.StartsWith("(", StringComparison.Ordinal)) continue;                 // (2)EchoIsles, ladder melee
            if (file.Contains("NewMap", StringComparison.OrdinalIgnoreCase)) continue;    // our own scratch targets
            if (file.EndsWith(".ported", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".edited", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".repaired", StringComparison.OrdinalIgnoreCase)) continue;

            string key = System.Text.RegularExpressions.Regex.Replace(
                file, @"[ _.\-]*[vV]?[0-9]+([._\-][0-9A-Za-z]+)*(\[[A-Za-z]\])?$", "").Trim();
            if (key.Length == 0) key = file;

            long size = new FileInfo(path).Length;
            if (!best.TryGetValue(key, out var cur) || size > cur.Size) best[key] = (path, size);
        }
        return best.Values.OrderBy(v => v.Size).Select(v => v.Path).ToList();
    }

    private static string Trunc(string s, int max = 160)
    {
        s = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return s.Length <= max ? s : s[..max] + "...";
    }
}
