using Avalonia;
using Avalonia.Headless;
using Xunit;
using Wc3.Studio.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

// Avalonia headless runs one application on one UI thread, and xUnit parallelises across test
// CLASSES by default. With 15 Avalonia test classes in this project they raced, and the symptom was
// a layout measure failing with "Unable to locate 'Avalonia.Platform.IFontManagerImpl'" in one or
// two arbitrary cases per run, passing on the next run untouched.
//
// It only appeared once this project grew past a handful of Avalonia classes, which is why nothing
// had needed this before. Two wrong guesses preceded the fix, the theory data source and a missing
// font registration, and neither changed the failure rate. The tell was that WHICH case failed
// moved between runs.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Wc3.Studio.Tests;

/// <summary>Headless Avalonia app for UI tests — reuses the real Studio App so themes,
/// styles and the custom controls (SearchableComboBox, etc.) load exactly as at runtime.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Wc3.Studio.App>()
            .UseSkia()
            // The embedded font the real Studio ships, and it is not optional here. With Skia
            // drawing enabled, laying out any TextBlock asks for a font manager, and without a
            // registered collection that resolution depends on whatever the host happens to
            // provide. It failed intermittently, one case in seven, with "Unable to locate
            // 'Avalonia.Platform.IFontManagerImpl'" thrown from inside a text measure, and passed
            // on the next run untouched. A flaky test that occasionally accuses the product is
            // worse than no test. Registering the same font the app registers removes the
            // dependency on the host entirely.
            .WithInterFont()
            // UseHeadlessDrawing false hands rendering to Skia, so a window can be captured to a
            // real bitmap. That is the only way to check a complaint like "white text so it is
            // terrible to eyes", which no assertion about control state can answer.
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
