// tests/Wc3.Studio.Tests/FilesViewTests.cs
using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Panels;
using Wc3.Tests;

namespace Wc3.Studio.Tests;

/// <summary>
/// The Files panel on a map with nameless entries, the shape a protected map presents. The
/// behaviour worth pinning is that a nameless entry's row describes what the entry IS (its
/// sniffed content type and size) while still saying plainly that the name is unknown, and
/// that named rows are left alone.
/// </summary>
public class FilesViewTests
{
    private static byte[] Tagged(string tag, int length = 64)
    {
        var b = new byte[length];
        Encoding.ASCII.GetBytes(tag).CopyTo(b, 0);
        return b;
    }

    private static (FilesView View, Window Window) Shown(MapDocument doc)
    {
        var view = new FilesView();
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.UpdateLayout();
        view.ShowMap(new MapSession { Current = doc });
        UiWork.WaitForFileTypes(view, Stopwatch.StartNew());
        window.UpdateLayout();
        return (view, window);
    }

    private static List<FileRow> Rows(FilesView view)
    {
        var list = view.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "FileList");
        return (list.ItemsSource as IEnumerable<FileRow>)!.ToList();
    }

    [AvaloniaFact]
    public void A_nameless_entry_shows_its_content_type_and_size_but_no_invented_name()
    {
        var blp = Tagged("BLP1", 4096);
        var doc = MapDocument.Load(SyntheticMap.Build(
            new Dictionary<string, byte[]> { ["war3map.j"] = Encoding.ASCII.GetBytes("// x\n") },
            new[] { blp }));

        var (view, window) = Shown(doc);
        try
        {
            var rows = Rows(view);
            var unnamed = rows.Single(r => r.Name is null);
            Assert.Equal("BLP texture", unnamed.TypeText);
            Assert.Equal(blp.Length, unnamed.SizeBytes);
            // The name stays visibly unknown, no filename that looks real is invented.
            Assert.StartsWith("(unnamed", unnamed.DisplayName);

            // The rendered row actually carries the type, not just the row model.
            var cellTexts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);
            Assert.Contains("BLP texture", cellTexts);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_named_entry_types_by_its_extension_without_reading_bytes()
    {
        var doc = MapDocument.Load(SyntheticMap.Build(
            new Dictionary<string, byte[]> { ["war3map.j"] = Encoding.ASCII.GetBytes("// x\n") }));

        var (view, window) = Shown(doc);
        try
        {
            var named = Rows(view).Single(r => r.Name == "war3map.j");
            Assert.Equal("j", named.TypeText);
            Assert.Null(named.ContentType);
        }
        finally
        {
            window.Close();
        }
    }
}
