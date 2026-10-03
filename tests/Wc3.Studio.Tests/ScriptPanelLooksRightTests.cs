// tests/Wc3.Studio.Tests/ScriptPanelLooksRightTests.cs
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Panels;
using Xunit.Abstractions;

namespace Wc3.Studio.Tests;

/// <summary>
/// The complaint was "right now it is white text so it is terrible to eyes". That is a claim about
/// pixels, and no assertion about control state can answer it, so these render the panel and read
/// the bitmap back.
///
/// They also write the frame to disk, so the panel can be looked at rather than only asserted
/// about. The path is in the test output.
/// </summary>
public class ScriptPanelLooksRightTests
{
    private readonly ITestOutputHelper _out;
    public ScriptPanelLooksRightTests(ITestOutputHelper output) => _out = output;

    private const string Script = """
        globals
            integer udg_HeroCount = 0
            unit array udg_Hero
        endglobals

        // Grants the picked hero its starting ability set.
        function GrantStartingKit takes unit whichHero, integer slot returns nothing
            local integer i = 0
            call UnitAddAbility(whichHero, 'A00R')
            call UnitAddAbility(whichHero, 'A1R6')
            set udg_Hero[slot] = whichHero
            loop
                exitwhen i >= 3
                call SetHeroStr(whichHero, GetHeroStr(whichHero, false) + 2, true)
                set i = i + 1
            endloop
            call BJDebugMsg("kit granted to " + GetUnitName(whichHero))
        endfunction

        function Trig_Pick_Conditions takes nothing returns boolean
            return GetSpellAbilityId() == 'A00R' and udg_HeroCount < 10
        endfunction
        """;

    [AvaloniaFact]
    public void The_editor_renders_dark_with_coloured_syntax_not_white_on_white()
    {
        var view = new ScriptView();
        var window = new Window { Width = 1280, Height = 760, Content = view };
        window.Show();
        window.UpdateLayout();

        var doc = BlankMap.Create();
        doc.AddOrReplaceRawFile("war3map.j", Encoding.UTF8.GetBytes(Script));
        view.ShowMap(new MapSession { Current = doc });
        window.UpdateLayout();

        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var path = Path.Combine(Path.GetTempPath(), "wc3ctl-script-panel.png");
        frame!.Save(path);
        _out.WriteLine($"frame written to {path}");

        var pixels = ReadPixels(frame);
        _out.WriteLine($"{frame.PixelSize.Width}x{frame.PixelSize.Height}, "
                     + $"{pixels.Count:N0} distinct colours");

        // The editor's ground is dark. A light ground behind light text is the literal complaint.
        var ground = MostCommon(pixels);
        _out.WriteLine($"most common colour {ground.rgb:X6} covering {ground.share:P1}");
        Assert.True(Luma(ground.rgb) < 0.35,
            $"the panel's dominant colour is {ground.rgb:X6}, which is not a dark ground");

        // And the text is not one colour. Syntax highlighting means the keyword blue, the string
        // orange, the rawcode gold and the comment green are all on screen at once, so a panel
        // rendering monochrome text has no highlighting whatever the definition claims.
        int inkTones = pixels.Count(p => p.Value > 60 && Luma(p.Key) > 0.35 && IsColourful(p.Key));
        _out.WriteLine($"{inkTones} distinct colourful ink tones");
        Assert.True(inkTones >= 3,
            $"only {inkTones} colourful ink tones, so the source is rendering effectively "
            + "monochrome and the highlighting is not reaching the screen");
    }

    private static Dictionary<int, int> ReadPixels(Bitmap frame)
    {
        var size = frame.PixelSize;
        int stride = size.Width * 4;
        var buffer = new byte[stride * size.Height];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(
            buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(),
                buffer.Length, stride);
        }
        finally { handle.Free(); }

        var counts = new Dictionary<int, int>();
        for (int i = 0; i + 3 < buffer.Length; i += 4)
        {
            // BGRA
            int rgb = (buffer[i + 2] << 16) | (buffer[i + 1] << 8) | buffer[i];
            counts[rgb] = counts.TryGetValue(rgb, out var n) ? n + 1 : 1;
        }
        return counts;
    }

    private static (int rgb, double share) MostCommon(Dictionary<int, int> pixels)
    {
        long total = pixels.Values.Sum(v => (long)v);
        var top = pixels.MaxBy(p => p.Value);
        return (top.Key, total == 0 ? 0 : top.Value / (double)total);
    }

    private static double Luma(int rgb)
    {
        double r = ((rgb >> 16) & 0xFF) / 255.0;
        double g = ((rgb >> 8) & 0xFF) / 255.0;
        double b = (rgb & 0xFF) / 255.0;
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>Whether a colour has real hue, as opposed to a grey or an antialiasing shade.</summary>
    private static bool IsColourful(int rgb)
    {
        int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return max - min >= 40;
    }
}
