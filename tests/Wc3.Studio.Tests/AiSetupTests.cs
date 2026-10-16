using Wc3.Mcp.Setup;
using Xunit;

namespace Wc3.Studio.Tests;

/// <summary>
/// The Connect AI Apps logic, driven against a temp profile so no real client config is read. The
/// dialog renders this, so the detection and registration reporting is the part worth pinning.
/// </summary>
public class AiSetupTests
{
    private static string TempHome()
    {
        var home = Path.Combine(Path.GetTempPath(), "wc3-studio-aisetup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        return home;
    }

    private static SetupEnvironment TempEnv(string home, Func<string, string?>? find = null) =>
        new(home,
            Path.Combine(home, "AppData", "Roaming"),
            Path.Combine(home, "AppData", "Local"),
            Path.Combine(home, ".codex"),
            find ?? (_ => null),
            (_, _) => (0, ""),
            () => null,
            _ => { });

    [Fact]
    public void Statuses_report_detection_and_registration()
    {
        var home = TempHome();
        try
        {
            var cursorDir = Path.Combine(home, ".cursor");
            Directory.CreateDirectory(cursorDir);
            File.WriteAllText(Path.Combine(cursorDir, "mcp.json"),
                "{\"mcpServers\":{\"wc3ctl\":{\"command\":\"wc3ctl\"}}}");

            var statuses = AiSetup.Statuses(TempEnv(home), "wc3ctl");

            var cursor = statuses.Single(s => s.Id == "cursor");
            Assert.True(cursor.Detected);
            Assert.True(cursor.Registered);
            Assert.Equal("found, set up", cursor.State);

            var zed = statuses.Single(s => s.Id == "zed");
            Assert.False(zed.Detected);
            Assert.Equal("not found", zed.State);
        }
        finally { Directory.Delete(home, true); }
    }

    [Fact]
    public void Statuses_report_a_detected_but_unconfigured_client()
    {
        var home = TempHome();
        try
        {
            Directory.CreateDirectory(Path.Combine(home, ".cursor"));
            var statuses = AiSetup.Statuses(TempEnv(home), "wc3ctl");
            var cursor = statuses.Single(s => s.Id == "cursor");
            Assert.True(cursor.Detected);
            Assert.False(cursor.Registered);
            Assert.Equal("found, not set up", cursor.State);
        }
        finally { Directory.Delete(home, true); }
    }

    [Fact]
    public void ResolveEntry_prefers_the_cli_exe()
    {
        var home = TempHome();
        try
        {
            var env = TempEnv(home, name => name == "wc3ctl" ? @"C:\tools\wc3ctl.exe" : null);
            var entry = AiSetup.ResolveEntry(env);
            Assert.Equal("wc3ctl", entry.Name);
            Assert.Equal(@"C:\tools\wc3ctl.exe", entry.Command);
            Assert.Equal(new[] { "mcp", "serve" }, entry.Args);
        }
        finally { Directory.Delete(home, true); }
    }

    [Fact]
    public void StarterPrompt_names_the_open_map()
    {
        Assert.Contains("C:/maps/a.w3x", AiSetup.StarterPrompt("C:/maps/a.w3x"));
        Assert.Contains("Warcraft III map", AiSetup.StarterPrompt(null));
    }
}
