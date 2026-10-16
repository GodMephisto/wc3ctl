using Xunit;

namespace Wc3.Studio.Tests;

/// <summary>
/// The recent-maps list is what the welcome screen shows, so its ordering and de-duplication are
/// pinned here without touching the user's real settings file.
/// </summary>
public class StudioSettingsTests
{
    [Fact]
    public void AddRecentMap_puts_the_newest_first()
    {
        var settings = new StudioSettings();
        settings.AddRecentMap(@"C:\maps\a.w3x");
        settings.AddRecentMap(@"C:\maps\b.w3x");
        Assert.Equal(new[] { @"C:\maps\b.w3x", @"C:\maps\a.w3x" }, settings.RecentMaps);
    }

    [Fact]
    public void AddRecentMap_deduplicates_case_insensitively()
    {
        var settings = new StudioSettings();
        settings.AddRecentMap(@"C:\Maps\A.w3x");
        settings.AddRecentMap(@"c:\maps\a.w3x");
        Assert.Single(settings.RecentMaps);
        Assert.Equal(@"c:\maps\a.w3x", settings.RecentMaps[0]);
    }

    [Fact]
    public void AddRecentMap_keeps_only_the_cap()
    {
        var settings = new StudioSettings();
        for (int i = 0; i < StudioSettings.MaxRecentMaps + 5; i++)
            settings.AddRecentMap($@"C:\maps\m{i}.w3x");
        Assert.Equal(StudioSettings.MaxRecentMaps, settings.RecentMaps.Count);
        Assert.Equal($@"C:\maps\m{StudioSettings.MaxRecentMaps + 4}.w3x", settings.RecentMaps[0]);
    }
}
