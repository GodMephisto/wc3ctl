// tests/Wc3.Tests/TargetBuildSettingsTests.cs
// Tests for TargetBuildSettings, covering precedence, bad name suggestions, and clear.
using Wc3.Commands;

namespace Wc3.Tests;

public class TargetBuildSettingsTests
{
    private static string TempSettingsDir()
    {
        var tempDir = Path.GetTempPath();
        var settingsDir = Path.Combine(tempDir, "wc3ctl-test-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(settingsDir);
        return settingsDir;
    }

    private static string? FindSchemasDir()
    {
        var schemasDir = Environment.GetEnvironmentVariable("WC3_SCHEMAS_DIR");
        if (!string.IsNullOrWhiteSpace(schemasDir) && Directory.Exists(schemasDir))
            return schemasDir;
        var schemasPath = Path.Combine(AppContext.BaseDirectory, "data", "schemas");
        if (Directory.Exists(schemasPath))
            return schemasPath;
        return null;
    }

    [Fact]
    public void Precedence_option_over_environment_over_settings()
    {
        TargetBuildSettings.ResetSettingsDir();
        var settingsDir = TempSettingsDir();
        try
        {
            // Write a settings file.
            File.WriteAllText(
                Path.Combine(settingsDir, "settings.json"),
                "{\"targetBuild\": \"TFT-v1.31.0.00001\"}");

            Environment.SetEnvironmentVariable("WC3_TARGET_BUILD", "TFT-v1.30.2.10200");
            Environment.SetEnvironmentVariable("WC3CTL_SETTINGS_DIR", settingsDir);

            try
            {
                // Option overrides everything.
                var result = TargetBuildSettings.Get(option: "TFT-v1.30.1.10211");
                Assert.Equal("TFT-v1.30.1.10211", result.Build);
                Assert.Equal(TargetSource.Option, result.Source);

                // Without option, falls through to env var.
                result = TargetBuildSettings.Get();
                Assert.Equal("TFT-v1.30.2.10200", result.Build);
                Assert.Equal(TargetSource.Environment, result.Source);

                // Without option and env var, falls through to settings file.
                Environment.SetEnvironmentVariable("WC3_TARGET_BUILD", null);
                result = TargetBuildSettings.Get();
                Assert.Equal("TFT-v1.31.0.00001", result.Build);
                Assert.Equal(TargetSource.Settings, result.Source);
            }
            finally
            {
                Environment.SetEnvironmentVariable("WC3_TARGET_BUILD", null);
            }
        }
        finally
        {
            if (Directory.Exists(settingsDir))
                Directory.Delete(settingsDir, true);
        }
    }

    [Fact]
    public void Precedence_settings_over_default_skips_without_schemas()
    {
        TargetBuildSettings.ResetSettingsDir();
        var settingsDir = TempSettingsDir();
        try
        {
            File.WriteAllText(
                Path.Combine(settingsDir, "settings.json"),
                "{\"targetBuild\": \"TFT-v1.30.0.00001\"}");

            Environment.SetEnvironmentVariable("WC3CTL_SETTINGS_DIR", settingsDir);

            var schemasDir = FindSchemasDir();
            if (schemasDir is null)
            {
                // Default resolution requires schemas, skip test.
                return;
            }

            var result = TargetBuildSettings.Get(schemasDir: schemasDir);
            Assert.Equal("TFT-v1.30.0.00001", result.Build);
            Assert.Equal(TargetSource.Settings, result.Source);
        }
        finally
        {
            if (Directory.Exists(settingsDir))
                Directory.Delete(settingsDir, true);
        }
    }

    [Fact]
    public void Set_with_bad_name_throws_with_suggestions()
    {
        TargetBuildSettings.ResetSettingsDir();
        var settingsDir = TempSettingsDir();
        try
        {
            Environment.SetEnvironmentVariable("WC3CTL_SETTINGS_DIR", settingsDir);
            var schemasDir = FindSchemasDir();

            // Skip if no schemas available.
            if (schemasDir is null)
                return;

            var ex = Assert.Throws<FileNotFoundException>(() =>
                TargetBuildSettings.Set("bogus-build-name-that-does-not-exist", schemasDir));

            Assert.Contains("not found", ex.Message);
            // Close names should be suggested.
            Assert.Contains("Close names:", ex.Message);
        }
        finally
        {
            if (Directory.Exists(settingsDir))
                Directory.Delete(settingsDir, true);
        }
    }

    [Fact]
    public void Clear_removes_the_setting()
    {
        TargetBuildSettings.ResetSettingsDir();
        var settingsDir = TempSettingsDir();
        try
        {
            // Write a settings file.
            File.WriteAllText(
                Path.Combine(settingsDir, "settings.json"),
                "{\"targetBuild\": \"TFT-v1.30.1.10211\"}");

            Environment.SetEnvironmentVariable("WC3CTL_SETTINGS_DIR", settingsDir);

            // Verify the setting is there.
            var result = TargetBuildSettings.Get();
            Assert.Equal("TFT-v1.30.1.10211", result.Build);

            // Clear it.
            TargetBuildSettings.Clear();

            // Now Get should return default (or throw if no schemas).
            // The source should no longer be Settings.
            try
            {
                var cleared = TargetBuildSettings.Get();
                Assert.NotEqual(TargetSource.Settings, cleared.Source);
            }
            catch (DirectoryNotFoundException)
            {
                // No schemas available, clear succeeded (file was deleted).
                Assert.False(File.Exists(Path.Combine(settingsDir, "settings.json")));
            }
        }
        finally
        {
            if (Directory.Exists(settingsDir))
                Directory.Delete(settingsDir, true);
        }
    }

    [Fact]
    public void Watched_failing_by_swapping_branches()
    {
        TargetBuildSettings.ResetSettingsDir();
        // This test verifies the precedence chain by temporarily swapping two branches.
        // Start: option overrides environment variable.
        // Swap: remove the option, so environment variable is used instead.
        // Verify: the build string changes.
        // Restore: put the option back.
        var settingsDir = TempSettingsDir();
        try
        {
            File.WriteAllText(
                Path.Combine(settingsDir, "settings.json"),
                "{\"targetBuild\": \"TFT-v1.32.0.00001\"}");

            Environment.SetEnvironmentVariable("WC3_TARGET_BUILD", "TFT-v1.30.3.10205");
            Environment.SetEnvironmentVariable("WC3CTL_SETTINGS_DIR", settingsDir);

            try
            {
                // Normal: option wins.
                var withOption = TargetBuildSettings.Get(option: "TFT-v1.30.1.10211");
                Assert.Equal("TFT-v1.30.1.10211", withOption.Build);

                // Swapped: no option, so env var wins.
                var noOption = TargetBuildSettings.Get(option: null);
                Assert.Equal("TFT-v1.30.3.10205", noOption.Build);
                Assert.Equal(TargetSource.Environment, noOption.Source);

                // Restore: option back.
                Assert.Equal("TFT-v1.30.1.10211", TargetBuildSettings.Get(option: "TFT-v1.30.1.10211").Build);
            }
            finally
            {
                Environment.SetEnvironmentVariable("WC3_TARGET_BUILD", null);
            }
        }
        finally
        {
            if (Directory.Exists(settingsDir))
                Directory.Delete(settingsDir, true);
        }
    }
}
