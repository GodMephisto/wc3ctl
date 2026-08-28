// tests/Wc3.Studio.Tests/FilesPanelNamelessCostTests.cs
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Panels;
using Xunit.Abstractions;

namespace Wc3.Studio.Tests;

/// <summary>
/// What the Files panel costs to open on a map that carries tens of thousands of nameless
/// entries. That is the shape where describing entries by content is both most valuable and
/// most expensive, so this table is where an eager or slow typing pass would show up as a
/// number rather than as a laggy panel. The budget is generous on purpose, it exists to catch
/// an order of magnitude, not to police a loaded machine's mood.
/// </summary>
public class FilesPanelNamelessCostTests
{
    private readonly ITestOutputHelper _out;
    public FilesPanelNamelessCostTests(ITestOutputHelper output) => _out = output;

    private static string NamelessHeavyMapOrEmpty()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                               "Warcraft III", "Maps", "Download");
        if (!Directory.Exists(dir)) return "";
        // The protected maps with the most nameless entries in the library, measured by a
        // Load-only sweep. ORDR and PumpkinTD each carry about 65,500 nameless entries (a full
        // 65,536-slot hash table, three quarters of it protection padding), an order of
        // magnitude beyond every other map, so they are the worst case this panel can meet.
        foreach (var pattern in new[] { "ORDR_S2*.w3x", "PumpkinTD_v2.3b*.w3x", "NCD S1*.w3x" })
            if (Directory.EnumerateFiles(dir, pattern).FirstOrDefault() is { } hit)
                return hit;
        return "";
    }

    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Files_panel_opens_within_budget_on_a_nameless_heavy_map()
    {
        var path = NamelessHeavyMapOrEmpty();
        if (path.Length == 0) { _out.WriteLine("no nameless-heavy map on this machine, skipped"); return; }

        var sw = Stopwatch.StartNew();
        var doc = MapDocument.Load(path);
        _out.WriteLine($"map               {Path.GetFileName(path)}");
        _out.WriteLine($"MapDocument.Load  {sw.ElapsedMilliseconds,6}ms  {doc.Files.Count:N0} entries, "
                     + $"{doc.Files.Count(f => f.FileName is null):N0} nameless");

        var panel = new FilesView();
        var window = new Window { Width = 1280, Height = 800, Content = panel };
        window.Show();
        window.UpdateLayout();
        try
        {
            var session = new MapSession { Current = doc, MapPath = path };
            var clock = Stopwatch.StartNew();
            panel.ShowMap(session);
            long blocked = clock.ElapsedMilliseconds;
            UiWork.Settle();
            long settled = clock.ElapsedMilliseconds;
            window.UpdateLayout();

            // The panel paints first and types the nameless entries in the background, so the
            // Type column fills in after "settled". Wait for it, that lag is a real number a
            // user sees and a regression could hide in it.
            long typed = UiWork.WaitForFileTypes(panel, clock);
            window.UpdateLayout();

            long reopen = UiWork.Time(() => { panel.ShowMap(session); UiWork.Settle(); });
            window.UpdateLayout();
            long retyped = UiWork.WaitForFileTypes(panel, Stopwatch.StartNew());

            _out.WriteLine("");
            _out.WriteLine("panel         blocked   settled     typed    reopen   retyped");
            _out.WriteLine($"{"Files",-12} {blocked,7}ms {settled,7}ms {typed,7}ms {reopen,7}ms {retyped,7}ms");

            Assert.True(blocked < 1500,
                $"Files panel blocked the UI thread for {blocked}ms on {Path.GetFileName(path)}, "
                + "the nameless-entry typing pass belongs off the UI thread");
            Assert.True(typed < 30_000,
                $"nameless entries were still untyped after {typed}ms on {Path.GetFileName(path)}");
        }
        finally
        {
            window.Close();
        }
    }
}
