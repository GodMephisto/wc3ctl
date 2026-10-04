// src/Wc3.Commands/TargetBuildSettings.cs
using System.Text.Json;

namespace Wc3.Commands;

/// <summary>
/// Where a target setting comes from, for display and debugging.
/// </summary>
public enum TargetSource
{
    /// <summary>An explicit --target flag on the command line.</summary>
    Option,
    /// <summary>The WC3_TARGET_BUILD environment variable.</summary>
    Environment,
    /// <summary>The persisted settings file on disk.</summary>
    Settings,
    /// <summary>No user setting, derived from the installed game build or newest schema.</summary>
    Default,
}

/// <summary>
/// A target build that also reports where it was resolved from.
/// </summary>
public sealed record TargetBuildResult(string Build, TargetSource Source, IReadOnlyList<string>? Sections = null);

/// <summary>
/// Per-build schema metadata, used by target list.
/// </summary>
public sealed record BuildInfo(string Build, IReadOnlyList<string> Sections);

/// <summary>
/// Reads and writes the user's target build setting. The setting controls which
/// game build (schema) the toolkit checks maps against by default.
///
/// The file lives at %APPDATA%/wc3ctl/settings.json on Windows and
/// ~/.config/wc3ctl/settings.json on macOS (ApplicationData maps to ~/.config).
/// An environment variable WC3_TARGET_BUILD overrides the file. An explicit
/// --target overrides both.
/// </summary>
public static class TargetBuildSettings
{
    private const string SettingsKey = "targetBuild";

    private static Lazy<string?> SettingsDir = new(() =>
    {
        var envDir = Environment.GetEnvironmentVariable("WC3CTL_SETTINGS_DIR");
        return string.IsNullOrWhiteSpace(envDir) ? null : envDir;
    });

    /// <summary>Reset the cached settings directory. For testing only.</summary>
    internal static void ResetSettingsDir() => SettingsDir = new(() =>
    {
        var envDir = Environment.GetEnvironmentVariable("WC3CTL_SETTINGS_DIR");
        return string.IsNullOrWhiteSpace(envDir) ? null : envDir;
    });

    /// <summary>
    /// The file path where settings are stored. Tests can override this through
    /// the WC3CTL_SETTINGS_DIR environment variable at runtime.
    /// </summary>
    internal static string FilePath
    {
        get
        {
            string? baseDir = SettingsDir.Value;
            if (baseDir is not null)
                return Path.Combine(baseDir, "settings.json");
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "wc3ctl", "settings.json");
        }
    }

    /// <summary>
    /// Get the current target build, resolving through the precedence chain:
    /// option (explicit --target) > environment (WC3_TARGET_BUILD) > settings
    /// file > default (installed game build or newest schema).
    /// </summary>
    /// <param name="option">
    /// An explicit --target value, or null to fall through to lower tiers.
    /// </param>
    /// <param name="schemasDir">
    /// Path to a schemas directory, or null to use the default location.
    /// </param>
    public static TargetBuildResult Get(string? option = null, string? schemasDir = null)
    {
        // Tier 1: explicit option.
        if (option is not null && option.Length > 0)
            return new TargetBuildResult(option, TargetSource.Option);

        // Tier 2: environment variable.
        var envBuild = Environment.GetEnvironmentVariable("WC3_TARGET_BUILD");
        if (envBuild is not null && envBuild.Length > 0)
            return new TargetBuildResult(envBuild, TargetSource.Environment);

        // Tier 3: settings file.
        try
        {
            string path = FilePath;
            if (File.Exists(path))
            {
                var content = File.ReadAllText(path);
                var obj = JsonDocument.Parse(content);
                if (obj.RootElement.TryGetProperty(SettingsKey, out JsonElement kb)
                    && kb.GetString() is { } stored && stored.Length > 0)
                {
                    return new TargetBuildResult(stored, TargetSource.Settings);
                }
            }
        }
        catch { /* corrupt or unreadable, fall through to default */ }

        // Tier 4 derives default from installed game or newest schema.
        string defaultBuild = ResolveDefaultBuild(schemasDir);
        return new TargetBuildResult(defaultBuild, TargetSource.Default);
    }

    /// <summary>
    /// Persist the given build as the user's target. Validates it against the
    /// schemas folder before writing.
    /// </summary>
    /// <param name="build">
    /// A build name (e.g. "TFT-v1.30.1.10211") or a schema folder path.
    /// </param>
    /// <param name="schemasDir">
    /// Path to a schemas directory, or null to use the default location.
    /// </param>
    public static void Set(string build, string? schemasDir = null)
    {
        // Validate the build by looking it up in schemas, reusing
        // TargetCompatCommand lookup logic (LoadTargetSchema is private there).
        // lookup logic (LoadTargetSchema is private there, so replicate).
        var resolvedSchemas = ResolveSchemasDir(schemasDir);

        // Try as a path first.
        if (Directory.Exists(build) && File.Exists(Path.Combine(build, "manifest.json")))
            return; // valid path.

        // Try as a build name.
        bool found = false;
        foreach (var subDir in Directory.EnumerateDirectories(resolvedSchemas))
        {
            string manifestPath = Path.Combine(subDir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            var manifest = GameSchemaCommand.Load(subDir);
            if (manifest.Build == build)
            {
                found = true;
                break;
            }
        }

        if (!found)
        {
            // List up to 10 close names.
            var builds = new List<string>();
            foreach (var subDir in Directory.EnumerateDirectories(resolvedSchemas))
            {
                string manifestPath = Path.Combine(subDir, "manifest.json");
                if (!File.Exists(manifestPath)) continue;
                builds.Add(GameSchemaCommand.Load(subDir).Build);
            }
            builds.Sort((a, b) => Wc3.Commands.GameBuildName.Compare(
                Wc3.Commands.GameBuildName.Parse(a),
                Wc3.Commands.GameBuildName.Parse(b)));

            var close = builds.Where(b =>
                b.Contains(build, StringComparison.OrdinalIgnoreCase)
                || build.Contains(b.Split('-').FirstOrDefault() ?? "", StringComparison.OrdinalIgnoreCase)
            ).Take(10).ToList();

            throw new FileNotFoundException(
                "build '" + build + "' not found"
                + (close.Count > 0 ? (". Close names: " + string.Join(", ", close)) : ""));
        }

        // Write settings file.
        string? dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var existing = File.Exists(FilePath)
            ? JsonDocument.Parse(File.ReadAllText(FilePath)).RootElement
            : JsonDocument.Parse("{}").RootElement;

        var doc = JsonDocument.Parse("{}");
        // Reuse what was there and overwrite targetBuild.
        using var writer = new Utf8JsonWriter(
            System.IO.File.Open(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read));
        writer.WriteStartObject();
        foreach (var prop in existing.EnumerateObject())
        {
            if (prop.Name != SettingsKey)
                writer.WritePropertyName(prop.Name);
            prop.WriteTo(writer);
        }
        writer.WriteString(SettingsKey, build);
        writer.WriteEndObject();
        writer.Flush();
    }

    /// <summary>
    /// Clear (delete) the user's target setting, returning to default behavior.
    /// </summary>
    public static void Clear()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }

    /// <summary>
    /// List every build in the schemas index with its sections.
    /// </summary>
    /// <param name="schemasDir">
    /// Path to a schemas directory, or null to use the default location.
    /// </param>
    /// <param name="fullOnly">
    /// When true, skip builds that are scripts-only (no objects).
    /// </param>
    public static IReadOnlyList<BuildInfo> List(string? schemasDir = null, bool fullOnly = false)
    {
        var resolvedSchemas = ResolveSchemasDir(schemasDir);
        var result = new List<BuildInfo>();

        foreach (var subDir in Directory.EnumerateDirectories(resolvedSchemas))
        {
            string manifestPath = Path.Combine(subDir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            var schema = GameSchemaCommand.Load(subDir);

            bool hasObjects = schema.Objects is not null && schema.Objects.Count > 0;
            if (fullOnly && !hasObjects) continue;

            var sections = GameSchemaCommand.Sections(schema);
            result.Add(new BuildInfo(schema.Build, sections));
        }

        result.Sort((a, b) => Wc3.Commands.GameBuildName.Compare(
            Wc3.Commands.GameBuildName.Parse(a.Build),
            Wc3.Commands.GameBuildName.Parse(b.Build)));
        return result;
    }

    /// <summary>
    /// Resolve the schemas directory, matching the same logic as TargetCompatCommand.
    /// </summary>
    private static string ResolveSchemasDir(string? schemasDir)
    {
        if (schemasDir is not null && Directory.Exists(schemasDir))
            return Path.GetFullPath(schemasDir);

        string baseDir = AppContext.BaseDirectory;
        string candidate1 = Path.Combine(baseDir, "schemas");
        if (Directory.Exists(candidate1))
            return Path.GetFullPath(candidate1);

        for (string? dir = baseDir; dir != null; dir = Directory.GetParent(dir)?.FullName)
        {
            string candidate = Path.Combine(dir, "data", "schemas");
            if (Directory.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        throw new DirectoryNotFoundException(
            "could not find schemas directory. Pass --schemas <dir>.");
    }

    /// <summary>
    /// Derive the default build by checking if a schema exists matching the installed
    /// game version, or fall back to the newest schema by version number.
    /// </summary>
    private static string ResolveDefaultBuild(string? schemasDir)
    {
        var resolvedSchemas = ResolveSchemasDir(schemasDir);
        string? defaultBuild = null;
        string? newestBuild = null;
        var newestParsed = default(Wc3.Commands.GameBuildName);

        foreach (var subDir in Directory.EnumerateDirectories(resolvedSchemas))
        {
            string manifestPath = Path.Combine(subDir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            var schema = GameSchemaCommand.Load(subDir);

            // Try to find the installed game's build.
            string? installDir = null;
            try { installDir = Wc3.GameData.GameInstall.Locate(); } catch { /* ignore */ }
            if (installDir is not null)
            {
                string? installBuild = Wc3.GameData.GameInstall.ReadBuildVersion(installDir);
                if (installBuild is not null && installBuild == schema.Build)
                {
                    defaultBuild = schema.Build;
                }
            }

            // Track the newest schema by version.
            var parsed = Wc3.Commands.GameBuildName.Parse(schema.Build);
            if (default(Wc3.Commands.GameBuildName) == newestParsed
                || Wc3.Commands.GameBuildName.Compare(parsed, newestParsed) > 0)
            {
                newestBuild = schema.Build;
                newestParsed = parsed;
            }
        }

        return defaultBuild ?? (newestBuild ?? "unknown");
    }
}
