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
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
