using Avalonia;
using Avalonia.Headless;
using Wc3.Studio.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Wc3.Studio.Tests;

/// <summary>Headless Avalonia app for UI tests — reuses the real Studio App so themes,
/// styles and the custom controls (SearchableComboBox, etc.) load exactly as at runtime.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Wc3.Studio.App>()
            .UseSkia()
            // UseHeadlessDrawing false hands rendering to Skia, so a window can be captured to a
            // real bitmap. That is the only way to check a complaint like "white text so it is
            // terrible to eyes", which no assertion about control state can answer.
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
