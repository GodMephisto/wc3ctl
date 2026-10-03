// tests/Wc3.Studio.Tests/ScriptPanelByteFidelityTests.cs
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Panels;
using Xunit;
using Xunit.Abstractions;

namespace Wc3.Studio.Tests;

/// <summary>
/// The Script panel's own save path, which is where this bug actually reached a user.
///
/// A map script has no declared encoding and real maps carry bytes that are not valid UTF-8. The
/// panel decoded them as UTF-8, which turns each invalid sequence into U+FFFD, and wrote back with
/// UTF-8, which emits the three bytes EF BF BD in their place. Opening the Script tab and pressing
/// Apply, without typing anything, permanently destroyed those bytes.
///
/// Worth pinning at THIS level rather than only on the command layer, because the panel wrote its
/// bytes directly rather than through ScriptCommand.Write. Fixing the command left the panel
/// untouched, and the command-layer test still passed. Only a test that presses the panel's own
/// button covers the path the user takes.
/// </summary>
public class ScriptPanelByteFidelityTests
{
    private readonly ITestOutputHelper _out;
    public ScriptPanelByteFidelityTests(ITestOutputHelper output) => _out = output;

    /// <summary>A script whose string literal holds bytes no UTF-8 decoder can accept.</summary>
    private static byte[] ScriptWithHighBytes()
    {
        var head = Encoding.Latin1.GetBytes(
            "function main takes nothing returns nothing\n    call BJDebugMsg(\"");
        var high = new byte[] { 0xE3, 0x29, 0xB5, 0xF1, 0x80 };
        var tail = Encoding.Latin1.GetBytes("\")\nendfunction\n");
        return head.Concat(high).Concat(tail).ToArray();
    }

    private static (ScriptView View, MapSession Session, Window Window) Shown(byte[] script)
    {
        // BlankMap ships a war3map.j, which this replaces. Saving and reloading matters, because
        // the point is what the archive holds, not what an in-memory override holds.
        var seed = BlankMap.Create();
        seed.AddOrReplaceRawFile("war3map.j", script);
        var doc = MapDocument.Load(seed.SaveToBytes());

        var view = new ScriptView();
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.UpdateLayout();

        var session = new MapSession { Current = doc };
        view.ShowMap(session);

        // The panel indexes in the background, so let it settle before pressing anything.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            if (!Named<TextBlock>(view, "HeaderText").Text!.EndsWith("indexing…", StringComparison.Ordinal))
                break;
            Thread.Sleep(5);
        }
        return (view, session, window);
    }

    private static T Named<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [AvaloniaFact]
    public void Applying_an_untouched_script_changes_no_bytes()
    {
        byte[] original = ScriptWithHighBytes();
        var (view, session, window) = Shown(original);

        var apply = Named<Button>(view, "ApplyButton");
        apply.IsEnabled = true;   // the panel gates this on a text change, and the point is no change
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        byte[] after = session.Current!.GetFile("war3map.j")!.CurrentBytes;
        _out.WriteLine($"{original.Length} bytes in, {after.Length} bytes out");
        _out.WriteLine("in  " + string.Join(" ", original.Select(b => b.ToString("X2"))));
        _out.WriteLine("out " + string.Join(" ", after.Select(b => b.ToString("X2"))));

        Assert.Equal(original, after);

        window.Close();
    }

    [AvaloniaFact]
    public void The_editor_shows_the_script_without_replacement_characters()
    {
        // If the text in the editor already carries U+FFFD then the bytes are lost before the user
        // touches anything, and no write path can recover them.
        var (view, _, window) = Shown(ScriptWithHighBytes());

        var editor = view.GetVisualDescendants()
            .OfType<Wc3.Studio.Controls.JassEditorView>().First();
        string shown = editor.Text ?? string.Empty;

        int replacements = shown.Count(c => c == '�');
        _out.WriteLine($"{replacements} replacement character(s) in the editor text");
        Assert.Equal(0, replacements);

        window.Close();
    }
}
