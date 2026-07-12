using Microsoft.Win32;
namespace Wc3.GameData;

public static class GameInstall
{
    public static string? Locate(string? overridePath = null)
    {
        // An explicit override is authoritative: a path that doesn't exist means
        // "not found", never a silent fallback to some other detected install.
        if (!string.IsNullOrWhiteSpace(overridePath))
            return Directory.Exists(overridePath) ? overridePath : null;

        if (OperatingSystem.IsWindows())
        {
            foreach (var (hive, sub, val) in new[]
            {
                (RegistryHive.CurrentUser, @"SOFTWARE\Blizzard Entertainment\Warcraft III", "InstallPath"),
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Warcraft III", "InstallLocation"),
            })
            {
                try
                {
                    using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(sub);
                    if (key?.GetValue(val) is string p && Directory.Exists(p)) return p;
                }
                catch { /* ignore, fall through */ }
            }
        }
        foreach (var p in new[] { @"D:\Warcraft III", @"C:\Program Files (x86)\Warcraft III", @"C:\Program Files\Warcraft III" })
            if (Directory.Exists(p)) return p;
        return null;
    }
}
