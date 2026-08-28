// tests/Wc3.Studio.Tests/UiWork.cs
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// Shared timing helpers for panel tests. Extracted from PanelLoadCostTests when a second
/// measurement test needed the same settle loop, so the two agree on what "settled" means.
/// </summary>
internal static class UiWork
{
    /// <summary>Runs queued UI work until nothing more arrives, so background loads land.</summary>
    public static void Settle()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        int quiet = 0;
        while (DateTime.UtcNow < deadline && quiet < 3)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            quiet++;
            if (Dispatcher.UIThread.HasJobsWithPriority(DispatcherPriority.Background)) quiet = 0;
        }
    }

    public static long Time(Action a)
    {
        var sw = Stopwatch.StartNew();
        a();
        return sw.ElapsedMilliseconds;
    }

    /// <summary>
    /// Pumps the dispatcher until the Files panel's rows carry content types (its background
    /// typing pass has landed), returning the elapsed milliseconds on <paramref name="clock"/>.
    /// A map with no nameless entries is typed by definition and returns immediately.
    /// </summary>
    public static long WaitForFileTypes(FilesView panel, Stopwatch clock)
    {
        var list = panel.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "FileList");
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            var rows = (list.ItemsSource as IEnumerable<FileRow>)?.ToList() ?? new List<FileRow>();
            bool anyNameless = rows.Any(r => r.Name is null);
            if (rows.Count > 0 && (!anyNameless || rows.Any(r => r.Name is null && r.ContentType is not null)))
                return clock.ElapsedMilliseconds;
            Thread.Sleep(20);
        }
        return clock.ElapsedMilliseconds;
    }
}
