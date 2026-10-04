// src/Wc3.Commands/TargetCompatCommand.cs
using System.Text;
using War3Net.Common.Extensions;
using Wc3.GameData;
using Wc3.Model;

namespace Wc3.Commands;

using Gbn = Wc3.Commands.GameBuildName;

/// <summary>One finding from checking a map against a target build.</summary>
public sealed record CompatFinding(string Severity, string Kind, string Name, string Where, string Detail);

/// <summary>
/// Result of checking a map against a target build. IdentifiersChecked and FieldsChecked
/// tell a reader how much was actually examined, so "checked and fine" is visible
/// rather than just "empty result."
/// </summary>
public sealed record CompatResult(
    string Map,
    string TargetBuild,
    IReadOnlyList<CompatFinding> Findings,
    IReadOnlyList<string> NotChecked,
    int IdentifiersChecked,
    int FieldsChecked);

/// <summary>
/// Check a map against a target game build (a schema folder or build name).
/// Reports compile-time incompatibilities (missing natives, globals, types, functions)
/// and display-time incompatibilities (modified fields on object data that the target
/// build no longer defines).
/// </summary>
/// <param name="mapPath">Path to a .w3x/.w3m map file.</param>
/// <param name="target">
/// Either a path to a schema folder, or a build name looked up in schemasDir.
/// </param>
/// <param name="schemasDir">
/// Directory containing schema folders (each holding manifest.json). When null,
/// search <AppContext.BaseDirectory>/schemas first, then walk up from the base
/// directory to find a data/schemas folder.
/// </param>
public static class TargetCompatCommand
{
    /// <summary>
    /// Execute compatibility check. target is a schema folder path or a build name
    /// looked up under schemasDir. When schemasDir is null, look in
    /// <AppContext.BaseDirectory>/schemas, then walk up from the base directory
    /// to a folder holding data/schemas.
    /// </summary>
    public static CompatResult Execute(string mapPath, string target, string? schemasDir = null)
    {
        if (!File.Exists(mapPath))
            throw new FileNotFoundException("map not found: " + mapPath, mapPath);

        var findings = new List<CompatFinding>();
        var notChecked = new List<string>();

        // Resolve schemasDir.
        var resolvedSchemas = ResolveSchemasDir(schemasDir);

        // Build the "any schema" union for this schemas directory.
        var allNatives = new Dictionary<string, JassNativeDecl>(StringComparer.Ordinal);
        var allGlobals = new Dictionary<string, JassGlobalDecl>(StringComparer.Ordinal);
        var allTypes = new Dictionary<string, JassTypeDecl>(StringComparer.Ordinal);
        var allFunctions = new Dictionary<string, JassFunctionDecl>(StringComparer.Ordinal);
        var newestBuildForNative = new Dictionary<string, string>(StringComparer.Ordinal);
        var newestBuildForGlobal = new Dictionary<string, string>(StringComparer.Ordinal);
        var newestBuildForType = new Dictionary<string, string>(StringComparer.Ordinal);
        var newestBuildForFunction = new Dictionary<string, string>(StringComparer.Ordinal);

        var earliestBuildForNative = new Dictionary<string, string>(StringComparer.Ordinal);
        var earliestBuildForGlobal = new Dictionary<string, string>(StringComparer.Ordinal);
        var earliestBuildForType = new Dictionary<string, string>(StringComparer.Ordinal);
        var earliestBuildForFunction = new Dictionary<string, string>(StringComparer.Ordinal);

        BuildUnionIndex(resolvedSchemas, allNatives, allGlobals, allTypes, allFunctions,
            newestBuildForNative, newestBuildForGlobal, newestBuildForType, newestBuildForFunction,
            earliestBuildForNative, earliestBuildForGlobal, earliestBuildForType, earliestBuildForFunction);

        // Load the map.
        MapDocument doc = MapDocument.Load(mapPath);

        // 1. Script check (Compile).
        CheckScript(doc, target, resolvedSchemas, allNatives, allGlobals, allTypes, allFunctions,
            newestBuildForNative, newestBuildForGlobal, newestBuildForType, newestBuildForFunction,
            earliestBuildForNative, earliestBuildForGlobal, earliestBuildForType, earliestBuildForFunction,
            findings, notChecked);

        // 2. Display check (object fields).
        CheckObjects(doc, target, resolvedSchemas, findings, notChecked);

        // Find the target schema (for the result).
        GameSchema targetSchema = LoadTargetSchema(target, resolvedSchemas);

        return new CompatResult(
            Path.GetFullPath(mapPath),
            targetSchema.Build,
            findings.OrderBy(f => f.Severity).ThenBy(f => f.Kind).ThenBy(f => f.Name).ToList(),
            notChecked.OrderBy(n => n).ToList(),
            GetIdentifiersChecked(doc, targetSchema),
            GetFieldsChecked(doc, targetSchema));
    }

    /// <summary>
    /// Resolve the schemas directory from the argument, or walk up from AppContext.BaseDirectory.
    /// </summary>
    private static string ResolveSchemasDir(string? schemasDir)
    {
        if (schemasDir is not null && Directory.Exists(schemasDir))
            return Path.GetFullPath(schemasDir);

        // Try base directory /schemas.
        string baseDir = AppContext.BaseDirectory;
        string candidate1 = Path.Combine(baseDir, "schemas");
        if (Directory.Exists(candidate1))
            return Path.GetFullPath(candidate1);

        // Walk up from base directory looking for a data/schemas folder.
        for (string? dir = baseDir; dir != null; dir = Directory.GetParent(dir)?.FullName)
        {
            string candidate = Path.Combine(dir, "data", "schemas");
            if (Directory.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        throw new DirectoryNotFoundException(
            "could not find schemas directory. Pass --schemas <dir>, or place schemas under "
            + "<AppContext.BaseDirectory>/schemas or walk up from the base directory to data/schemas.");
    }

    /// <summary>Build a union index across all schema folders, keyed by newest and earliest build.</summary>
    private static void BuildUnionIndex(
        string schemasDir,
        Dictionary<string, JassNativeDecl> allNatives,
        Dictionary<string, JassGlobalDecl> allGlobals,
        Dictionary<string, JassTypeDecl> allTypes,
        Dictionary<string, JassFunctionDecl> allFunctions,
        Dictionary<string, string> newestBuildForNative,
        Dictionary<string, string> newestBuildForGlobal,
        Dictionary<string, string> newestBuildForType,
        Dictionary<string, string> newestBuildForFunction,
        Dictionary<string, string> earliestBuildForNative,
        Dictionary<string, string> earliestBuildForGlobal,
        Dictionary<string, string> earliestBuildForType,
        Dictionary<string, string> earliestBuildForFunction)
    {
        foreach (var subDir in Directory.EnumerateDirectories(schemasDir))
        {
            string manifestPath = Path.Combine(subDir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            GameSchema schema = GameSchemaCommand.Load(subDir);
            string build = schema.Build;

            if (schema.Script is null) continue;

            foreach (var kv in schema.Script)
            {
                // Only common.j and blizzard.j for the compile check. Skip AI scripts.
                string scriptKey = kv.Key;
                if (scriptKey != "scripts/common.j" && scriptKey != "scripts/blizzard.j")
                    continue;

                var api = kv.Value;

                foreach (var nat in api.Natives)
                {
                    if (!allNatives.ContainsKey(nat.Name))
                    {
                        allNatives[nat.Name] = nat;
                        newestBuildForNative[nat.Name] = build;
                        earliestBuildForNative[nat.Name] = build;
                    }
                    else if (Gbn.Compare(Gbn.Parse(build), Gbn.Parse(newestBuildForNative[nat.Name])) > 0)
                    {
                        newestBuildForNative[nat.Name] = build;
                    }
                    else if (earliestBuildForNative.TryGetValue(nat.Name, out var earliestNativeBuild)
                        && (Gbn.Compare(Gbn.Parse(build), Gbn.Parse(earliestNativeBuild)) < 0
                            || (Gbn.Compare(Gbn.Parse(build), Gbn.Parse(earliestNativeBuild)) == 0
                                && Gbn.Parse(build).IsTest && !Gbn.Parse(earliestNativeBuild).IsTest)))
                    {
                        if (Gbn.Parse(build).Prefix != null)
                        {
                            earliestBuildForNative[nat.Name] = build;
                        }
                    }
                }

                foreach (var gl in api.Globals)
                {
                    if (!allGlobals.ContainsKey(gl.Name))
                    {
                        allGlobals[gl.Name] = gl;
                        newestBuildForGlobal[gl.Name] = build;
                        if (Gbn.Parse(build).Prefix != null)
                        {
                            earliestBuildForGlobal[gl.Name] = build;
                        }
                    }
                    else if (Gbn.Compare(Gbn.Parse(build), Gbn.Parse(newestBuildForGlobal[gl.Name])) > 0)
                    {
                        newestBuildForGlobal[gl.Name] = build;
                    }
                    else if (earliestBuildForGlobal.TryGetValue(gl.Name, out var earliestGlobalBuild)
                        && (Gbn.Compare(Gbn.Parse(build), Gbn.Parse(earliestGlobalBuild)) < 0
                            || (Gbn.Compare(Gbn.Parse(build), Gbn.Parse(earliestGlobalBuild)) == 0
                                && Gbn.Parse(build).IsTest && !Gbn.Parse(earliestGlobalBuild).IsTest)))
                    {
                        if (Gbn.Parse(build).Prefix != null)
                        {
                            earliestBuildForGlobal[gl.Name] = build;
                        }
                    }
                }

                foreach (var tp in api.Types)
                {
                    if (!allTypes.ContainsKey(tp.Name))
                    {
                        allTypes[tp.Name] = tp;
                        newestBuildForType[tp.Name] = build;
                        if (Gbn.Parse(build).Prefix != null)
                        {
                            earliestBuildForType[tp.Name] = build;
                        }
                    }
                    else if (Gbn.Compare(Gbn.Parse(build), Gbn.Parse(newestBuildForType[tp.Name])) > 0)
                    {
                        newestBuildForType[tp.Name] = build;
                    }
                    else if (earliestBuildForType.TryGetValue(tp.Name, out var earliestTypeBuild)
                        && (Gbn.Compare(Gbn.Parse(build), Gbn.Parse(earliestTypeBuild)) < 0
                            || (Gbn.Compare(Gbn.Parse(build), Gbn.Parse(earliestTypeBuild)) == 0
                                && Gbn.Parse(build).IsTest && !Gbn.Parse(earliestTypeBuild).IsTest)))
                    {
                        if (Gbn.Parse(build).Prefix != null)
                        {
                            earliestBuildForType[tp.Name] = build;
                        }
                    }
                }

                foreach (var fn in api.Functions)
                {
                    if (!allFunctions.ContainsKey(fn.Name))
                    {
                        allFunctions[fn.Name] = fn;
                        newestBuildForFunction[fn.Name] = build;
                        if (Gbn.Parse(build).Prefix != null)
                        {
                            earliestBuildForFunction[fn.Name] = build;
                        }
                    }
                    else if (string.Compare(build, newestBuildForFunction[fn.Name], StringComparison.Ordinal) > 0)
                    {
                        newestBuildForFunction[fn.Name] = build;
                    }
                    // Track earliest by parsed version. If this build's version is less than
                    // the tracked earliest, replace it. On a version tie, prefer release
                    // over -w3t/-w3b test builds, since release sorts "later" in our scheme.
                    else if (earliestBuildForFunction.TryGetValue(fn.Name, out var earliestFunctionBuild)
                        && (Gbn.Compare(Gbn.Parse(build), Gbn.Parse(earliestFunctionBuild)) < 0
                            || (Gbn.Compare(Gbn.Parse(build), Gbn.Parse(earliestFunctionBuild)) == 0
                                && Gbn.Parse(build).IsTest && !Gbn.Parse(earliestFunctionBuild).IsTest)))
                    {
                        if (Gbn.Parse(build).Prefix != null)
                        {
                            earliestBuildForFunction[fn.Name] = build;
                        }
                    }
                }
            }
        }
    }

    /// <summary>Load the target schema by name or path.</summary>
    private static GameSchema LoadTargetSchema(string target, string schemasDir)
    {
        // Try as a path first.
        if (Directory.Exists(target) && File.Exists(Path.Combine(target, "manifest.json")))
            return GameSchemaCommand.Load(target);

        // Try as a build name.
        foreach (var subDir in Directory.EnumerateDirectories(schemasDir))
        {
            string manifestPath = Path.Combine(subDir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            var manifest = GameSchemaCommand.Load(subDir);
            if (manifest.Build == target)
                return manifest;
        }

        // Not found. List up to 10 close build names.
        var builds = new List<string>();
        foreach (var subDir in Directory.EnumerateDirectories(schemasDir))
        {
            string manifestPath = Path.Combine(subDir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            builds.Add(GameSchemaCommand.Load(subDir).Build);
        }
        builds.Sort((a, b) => Gbn.Compare(Gbn.Parse(a), Gbn.Parse(b)));

        // Find close matches (simple substring / prefix).
        var close = builds.Where(b =>
            b.Contains(target, StringComparison.OrdinalIgnoreCase)
            || target.Contains(b.Split('-').FirstOrDefault() ?? "", StringComparison.OrdinalIgnoreCase)
        ).Take(10).ToList();

        throw new FileNotFoundException(
            "build '" + target + "' not found"
            + (close.Count > 0 ? (". Close names: " + string.Join(", ", close)) : "")
            + ". Pass --schemas <dir> to override the schemas location.");
    }

    /// <summary>
    /// Check the map's script against the target build.
    /// Declared identifiers in the map's own script are skipped (declared = used by the map author).
    /// Every OTHER identifier that is a native, global, type or function in ANY schema but
    /// absent from the target's scripts/common.j and scripts/blizzard.j is a finding.
    /// </summary>
    private static void CheckScript(
        MapDocument doc,
        string target,
        string schemasDir,
        Dictionary<string, JassNativeDecl> allNatives,
        Dictionary<string, JassGlobalDecl> allGlobals,
        Dictionary<string, JassTypeDecl> allTypes,
        Dictionary<string, JassFunctionDecl> allFunctions,
        Dictionary<string, string> newestBuildForNative,
        Dictionary<string, string> newestBuildForGlobal,
        Dictionary<string, string> newestBuildForType,
        Dictionary<string, string> newestBuildForFunction,
        Dictionary<string, string> earliestBuildForNative,
        Dictionary<string, string> earliestBuildForGlobal,
        Dictionary<string, string> earliestBuildForType,
        Dictionary<string, string> earliestBuildForFunction,
        List<CompatFinding> findings,
        List<string> notChecked)
    {
        // Find the script entry.
        MapFileEntry? scriptEntry = doc.GetFile("war3map.j") ?? doc.GetFile(@"scripts\war3map.j");

        if (scriptEntry is null)
        {
            // Map has no script section (neither war3map.j nor scripts\war3map.j).
            // Put "script" in NotChecked and skip.
            if (!notChecked.Contains("script"))
                notChecked.Add("script");
            return;
        }

        string source = ScriptCommand.ScriptEncoding.GetString(scriptEntry.CurrentBytes);

        // Parse the target schema to know what the target declares.
        GameSchema targetSchema = LoadTargetSchema(target, schemasDir);

        // Build a set of what the target defines in common.j and blizzard.j.
        var targetNatives = new HashSet<string>(StringComparer.Ordinal);
        var targetGlobals = new HashSet<string>(StringComparer.Ordinal);
        var targetTypes = new HashSet<string>(StringComparer.Ordinal);
        var targetFunctions = new HashSet<string>(StringComparer.Ordinal);

        if (targetSchema.Script is not null)
        {
            foreach (var scriptFile in targetSchema.Script)
            {
                string shortName = scriptFile.Key;
                // Only common.j and blizzard.j for the compile check.
                if (shortName != "scripts/common.j" && shortName != "scripts/blizzard.j") continue;

                var api = scriptFile.Value;
                foreach (var nat in api.Natives) targetNatives.Add(nat.Name);
                foreach (var gl in api.Globals) targetGlobals.Add(gl.Name);
                foreach (var tp in api.Types) targetTypes.Add(tp.Name);
                foreach (var fn in api.Functions) targetFunctions.Add(fn.Name);
            }
        }
        else
        {
            if (!notChecked.Contains("script"))
                notChecked.Add("script");
        }

        // Parse the map's script.
        JassApi mapApi = JassApiIndex.Parse(source);

        // Build set of identifiers declared IN the map (skip these).
        var declaredInMap = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nat in mapApi.Natives) declaredInMap.Add(nat.Name);
        foreach (var gl in mapApi.Globals) declaredInMap.Add(gl.Name);
        foreach (var tp in mapApi.Types) declaredInMap.Add(tp.Name);
        foreach (var fn in mapApi.Functions) declaredInMap.Add(fn.Name);

        // Collect ALL identifiers (one per unique name, with occurrence count).
        var identifiers = CollectAllIdentifiers(source, mapApi);

        // Check each unique identifier against the target and the union index.
        // One finding per identifier (first line + occurrence count in detail).

        foreach (var (name, line, count) in identifiers)
        {
            // Skip identifiers declared in the map itself.
            if (declaredInMap.Contains(name)) continue;

            // Only identifiers that SOME schema declares can be findings.
            // Unknown identifiers (map globals, keywords caught by mistake) are never a finding.
            if (!allNatives.ContainsKey(name) && !allGlobals.ContainsKey(name)
                && !allTypes.ContainsKey(name) && !allFunctions.ContainsKey(name))
                continue;

            (string kind, bool inTarget) = (name) switch
            {
                _ => (InferKind(name, allNatives, allGlobals, allTypes, allFunctions),
                    CheckInTarget(name, targetNatives, targetGlobals, targetTypes, targetFunctions))
            };

            if (!inTarget)
            {
                (string? earliestRelease, string? earliestTest) = FindEarliestBuilds(name, targetSchema.Build,
                    earliestBuildForNative, earliestBuildForGlobal,
                    earliestBuildForType, earliestBuildForFunction,
                    newestBuildForNative, newestBuildForGlobal,
                    newestBuildForType, newestBuildForFunction);
                string countText = ", used " + count + " time" + (count > 1 ? "s" : "");
                string detail;
                // If the earliest release is a bare build (no prefix), skip the "added in"
                // line. The bare build cannot be substituted without per-schema feature
                // data, so we report "not in <target>" instead of reporting the wrong build.
                if (earliestRelease is not null && Gbn.Parse(earliestRelease).Prefix != null)
                {
                    if (earliestTest != null && earliestTest != earliestRelease)
                    {
                        detail = "added in " + earliestRelease
                            + " (first seen in " + earliestTest + ")"
                            + ", not in " + targetSchema.Build + countText;
                    }
                    else
                    {
                        detail = "added in " + earliestRelease + ", not in " + targetSchema.Build + countText;
                    }
                }
                else if (earliestTest != null)
                {
                    detail = "added in " + earliestTest + ", not in " + targetSchema.Build + countText;
                }
                else
                {
                    detail = "not in " + targetSchema.Build + countText;
                }
                findings.Add(new CompatFinding(
                    "error",
                    kind,
                    name,
                    "line " + line,
                    detail));
            }
        }
    }

    /// <summary>
    /// Tokenize identifiers from the map script outside of string literals and comments.
    /// Returns one entry per unique identifier with (name, firstLine, occurrenceCount).
    /// Arrays like udg_Name[16] yield name udg_Name (strip the bracketed suffix).
    /// </summary>
    private static List<(string Name, int FirstLine, int Count)> CollectAllIdentifiers(
        string source, JassApi mapApi)
    {
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);

        // Collect declared names (these are NOT "used" in the compat sense).
        foreach (var nat in mapApi.Natives) declaredNames.Add(nat.Name);
        foreach (var gl in mapApi.Globals) declaredNames.Add(gl.Name);
        foreach (var tp in mapApi.Types) declaredNames.Add(tp.Name);
        foreach (var fn in mapApi.Functions) declaredNames.Add(fn.Name);

        // Parse line by line, stripping comments and string literals.
        var lines = source.Split('\n');
        var found = new Dictionary<string, (int FirstLine, int Count)>(StringComparer.Ordinal);

        for (int lineNum = 0; lineNum < lines.Length; lineNum++)
        {
            string line = lines[lineNum];
            string cleaned = StripCommentAndStrings(line);
            var tokens = cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            foreach (string token in tokens)
            {
                // Strip trailing non-identifier characters (parentheses, dots) so
                // ExtraNative matches the schema entry ExtraNative.
                string name = StripTrailingPunct(token);

                // Strip any bracketed suffix from array access.
                // udg_Name[16] yields udg_Name.
                int bracketIdx = name.IndexOf('[');
                if (bracketIdx > 0)
                {
                    name = name[..bracketIdx];
                }

                // Skip JASS keywords.
                if (IsKeyword(name)) continue;

                // Skip numeric literals.
                if (IsNumeric(token)) continue;

                // Skip empty strings from punctuation-only tokens.
                if (name.Length == 0) continue;

                // Skip identifiers declared in the map.
                if (declaredNames.Contains(name)) continue;

                if (!found.TryGetValue(name, out var entry))
                {
                    found[name] = (lineNum + 1, 1);
                }
                else
                {
                    found[name] = (entry.FirstLine, entry.Count + 1);
                }
            }
        }

        return found.Select(kv => (kv.Key, kv.Value.FirstLine, kv.Value.Count)).ToList();
    }

    /// <summary>Strip comments (// style) and string literals from a single line.</summary>
    private static string StripCommentAndStrings(string line)
    {
        var result = new StringBuilder();
        bool inString = false;
        bool inComment = false;

        for (int i = 0; i < line.Length; i++)
        {
            if (inComment)
            {
                if (line[i] == '\n')
                {
                    inComment = false;
                    result.AppendLine();
                }
                continue;
            }

            if (inString)
            {
                if (line[i] == '"')
                {
                    inString = false;
                    result.Append('"');
                }
                else if (line[i] == '\\')
                {
                    // Skip escaped character.
                    i++;
                }
                continue;
            }

            // Check for string start.
            if (line[i] == '"')
            {
                inString = true;
                result.Append('"');
                continue;
            }

            // Check for comment start.
            if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/')
            {
                break;
            }

            result.Append(line[i]);
        }

        return result.ToString();
    }

    /// <summary>Strip trailing non-identifier characters from a token.
    /// "ExtraNative()" becomes "ExtraNative", "foo.bar" becomes "foo".</summary>
    private static string StripTrailingPunct(string token)
    {
        int i = token.Length - 1;
        while (i >= 0 && !char.IsLetterOrDigit(token[i]) && token[i] != '_')
            i--;
        return i < 0 ? string.Empty : token[..(i + 1)];
    }

    /// <summary>JASS keywords that should not be treated as identifiers.</summary>
    private static bool IsKeyword(string token)
    {
        switch (token)
        {
            case "native": case "constant": case "function": case "globals": case "endglobals":
            case "endfunction": case "type": case "extends": case "endtype":
            case "takes": case "returns": case "local": case "integer": case "real":
            case "boolean": case "string": case "handle": case "code": case "unit":
            case "widget": case "trigger": case "event": case "condition": case "action":
            case "call": case "exitwhen": case "if": case "then": case "else":
            case "endif": case "do": case "loop": case "endloop":
            case "return": case "null": case "true": case "false":
            case "array": case "private": case "static": case "public":
            case "interface": case "implements": case "thisexport": case "export":
            case "debug": case "scope": case "endscope": case "declaration":
            case "variable": case "nothing": case "library": case "requires":
            case "optional": case "initorder": case "onDestroy":
                return true;
            default:
                return false;
        }
    }

    /// <summary>Check if a token looks like a numeric literal.</summary>
    private static bool IsNumeric(string token)
    {
        // Real numbers and integers.
        if (double.TryParse(token, out _)) return true;
        // Hex literals (0x...).
        if (token.StartsWith("0x", StringComparison.Ordinal) || token.StartsWith("0X", StringComparison.Ordinal)) return true;
        // Binary (0b...).
        if (token.StartsWith("0b", StringComparison.Ordinal) || token.StartsWith("0B", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// Infer the kind of an identifier by looking it up in the union index.
    /// Priority: native > global > type > function (a name can be in multiple).
    /// </summary>
    private static string InferKind(string name,
        Dictionary<string, JassNativeDecl> allNatives,
        Dictionary<string, JassGlobalDecl> allGlobals,
        Dictionary<string, JassTypeDecl> allTypes,
        Dictionary<string, JassFunctionDecl> allFunctions)
    {
        if (allNatives.ContainsKey(name)) return "native";
        if (allGlobals.ContainsKey(name)) return "global";
        if (allTypes.ContainsKey(name)) return "type";
        if (allFunctions.ContainsKey(name)) return "function";
        return "native";
    }

    /// <summary>Check if a name exists in the target's scripts/common.j and scripts/blizzard.j.</summary>
    private static bool CheckInTarget(string name,
        HashSet<string> targetNatives,
        HashSet<string> targetGlobals,
        HashSet<string> targetTypes,
        HashSet<string> targetFunctions)
    {
        string kind = name switch
        {
            _ when targetNatives.Contains(name) => "native",
            _ when targetGlobals.Contains(name) => "global",
            _ when targetTypes.Contains(name) => "type",
            _ when targetFunctions.Contains(name) => "function",
            _ => "identifier",
        };

        return kind switch
        {
            "native" => targetNatives.Contains(name),
            "global" => targetGlobals.Contains(name),
            "type" => targetTypes.Contains(name),
            "function" => targetFunctions.Contains(name),
            _ => false,
        };
    }

    /// <summary>Find the newest build that declares an identifier, using GameBuildName ordering.</summary>
    private static string? FindNewestBuild(string name,
        Dictionary<string, string> newestBuildForNative,
        Dictionary<string, string> newestBuildForGlobal,
        Dictionary<string, string> newestBuildForType,
        Dictionary<string, string> newestBuildForFunction)
    {
        // Collect all build names across all categories where this identifier appears.
        List<string> builds = new();
        if (newestBuildForNative.TryGetValue(name, out var nb)) builds.Add(nb);
        if (newestBuildForGlobal.TryGetValue(name, out var gb)) builds.Add(gb);
        if (newestBuildForType.TryGetValue(name, out var tb)) builds.Add(tb);
        if (newestBuildForFunction.TryGetValue(name, out var fb)) builds.Add(fb);
        if (builds.Count == 0) return null;

        builds.Sort((a, b) => Gbn.Compare(Gbn.Parse(a), Gbn.Parse(b)));
        return builds[^1];
    }

    /// <summary>
    /// Find the earliest release build and the earliest test/beta build that declare
    /// an identifier and are newer than the target. Uses GameBuildName for ordering.
    /// Returns (null, null) when no build newer than the target declares the identifier.
    /// Ignores builds of a different locale than the target when a same-locale build exists.
    /// </summary>
    private static (string? earliestRelease, string? earliestTest) FindEarliestBuilds(
        string name, string targetBuild,
        Dictionary<string, string> earliestBuildForNative,
        Dictionary<string, string> earliestBuildForGlobal,
        Dictionary<string, string> earliestBuildForType,
        Dictionary<string, string> earliestBuildForFunction,
        Dictionary<string, string> newestBuildForNative,
        Dictionary<string, string> newestBuildForGlobal,
        Dictionary<string, string> newestBuildForType,
        Dictionary<string, string> newestBuildForFunction)
    {
        List<string> builds = new();
        if (earliestBuildForNative.TryGetValue(name, out var nb)) builds.Add(nb);
        if (earliestBuildForGlobal.TryGetValue(name, out var gb)) builds.Add(gb);
        if (earliestBuildForType.TryGetValue(name, out var tb)) builds.Add(tb);
        if (earliestBuildForFunction.TryGetValue(name, out var fb)) builds.Add(fb);
        // Also include the newest builds (release versions) for identifiers where
        // the earliest is a test build but a later release also declares it.
        if (newestBuildForNative.TryGetValue(name, out var nbn)) builds.Add(nbn);
        if (newestBuildForGlobal.TryGetValue(name, out var nbg)) builds.Add(nbg);
        if (newestBuildForType.TryGetValue(name, out var nbt)) builds.Add(nbt);
        if (newestBuildForFunction.TryGetValue(name, out var nbf)) builds.Add(nbf);
        if (builds.Count == 0) return (null, null);

        Gbn targetParsed = Gbn.Parse(targetBuild);
        string? targetLocale = targetParsed.Locale;

        // Filter to builds strictly newer than the target.
        builds.RemoveAll(b => Gbn.Compare(Gbn.Parse(b), targetParsed) <= 0);
        if (builds.Count == 0) return (null, null);

        // When target has a locale, prefer same-locale builds.
        // If a same-locale build exists for the same version category, use it.
        var localeFilter = builds.Where(b =>
            targetLocale is null
            || Gbn.Parse(b).Locale == targetLocale);
        var candidate = localeFilter.Any() ? localeFilter : builds;

        var allParsed = candidate.Select(b => (Raw: b, Parsed: Gbn.Parse(b))).ToList();
        allParsed.Sort((a, b) => Gbn.Compare(a.Parsed, b.Parsed));

        // Find earliest RELEASE build (non-test, non-beta).
        string? earliestRelease = null;
        string? earliestTest = null;
        foreach ((string raw, Gbn parsed) in allParsed)
        {
            if (parsed.IsTest || parsed.IsBeta)
            {
                if (earliestTest is null) earliestTest = raw;
            }
            else
            {
                if (earliestRelease is null) earliestRelease = raw;
            }
        }

        return (earliestRelease, earliestTest);
    }

    /// <summary>
    /// Check object data fields that were modified in the map but do not exist
    /// in the target build.
    /// </summary>
    private static void CheckObjects(
        MapDocument doc,
        string target,
        string schemasDir,
        List<CompatFinding> findings,
        List<string> notChecked)
    {
        GameSchema targetSchema = LoadTargetSchema(target, schemasDir);

        if (targetSchema.Objects is null)
        {
            if (!notChecked.Contains("objects"))
                notChecked.Add("objects");
            return;
        }

        // Build a lookup from object kind to the field codes defined in that kind's
        // schema file (the objects section of the schema).
        var validFieldsByKind = new Dictionary<ObjectKind, HashSet<string>>(ObjectKindEqualityComparer.Instance);
        foreach (var (schemaFile, fields) in targetSchema.Objects!)
        {
            string? kindName = SchemaKindFromPath(schemaFile);
            if (!Enum.TryParse<ObjectKind>(kindName, out var kind)) continue;
            var fieldSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in fields)
                fieldSet.Add(field.Code);
            validFieldsByKind[kind] = fieldSet;
        }

        // Read map object modifications for each kind using the same method as
        // object list/get. ObjectKinds.MergedEntries reads war3map.* and
        // war3mapSkin.* overlays, yielding per-object field mods.
        foreach (ObjectKind kind in ObjectKinds.All)
        {
            if (!validFieldsByKind.TryGetValue(kind, out var validFields))
                continue;

            var info = ObjectKinds.Info(kind);
            var entries = ObjectKinds.MergedEntries(doc, info);

            foreach (var entry in entries)
            {
                foreach (var mod in entry.Mods)
                {
                    // Stripped field code. For leveled/variation kinds the key
                    // is "code:N", extract the bare code for schema lookup.
                    string bareCode = mod.Key;
                    int colon = bareCode.IndexOf(':');
                    if (colon > 0) bareCode = bareCode[..colon];

                    if (!validFields.Contains(bareCode))
                    {
                        findings.Add(new CompatFinding(
                            "warning",
                            "field",
                            entry.Id.ToRawcode() + "." + bareCode,
                            entry.Id.ToRawcode(),
                            "field '" + bareCode + "' not defined in target build"));
                    }
                }
            }
        }
    }

    /// <summary>Map a schema file path to an object kind name for the kind-lookup table.</summary>
    private static string? SchemaKindFromPath(string schemaPath)
    {
        string lower = schemaPath.ToLowerInvariant();
        if (lower.Contains("unitmetadata")) return "unit";
        if (lower.Contains("itemmetadata")) return "item";
        if (lower.Contains("abilitymetadata")) return "ability";
        if (lower.Contains("destructablemetadata")) return "destructable";
        if (lower.Contains("doodadmetadata")) return "doodad";
        if (lower.Contains("abilitybuffmetadata")) return "buff";
        if (lower.Contains("upgrademetadata")) return "upgrade";
        if (lower.Contains("upgradeeffectmetadata")) return "upgrade";
        if (lower.Contains("miscmetadata")) return null;
        if (lower.Contains("skinmetadata")) return null;
        return null;
    }

    /// <summary>ObjectKind equality for use as a dictionary key.</summary>
    private sealed class ObjectKindEqualityComparer : IEqualityComparer<ObjectKind>
    {
        public static readonly ObjectKindEqualityComparer Instance = new();
        public bool Equals(ObjectKind a, ObjectKind b) => a == b;
        public int GetHashCode(ObjectKind obj) => obj.GetHashCode();
    }

    /// <summary>Count identifiers examined by parsing the script and the target.</summary>
    private static int GetIdentifiersChecked(MapDocument doc, GameSchema targetSchema)
    {
        MapFileEntry? scriptEntry = doc.GetFile("war3map.j") ?? doc.GetFile(@"scripts\war3map.j");
        if (scriptEntry is null || scriptEntry.CurrentBytes.Length == 0)
            return 0;

        string source = ScriptCommand.ScriptEncoding.GetString(scriptEntry.CurrentBytes);
        JassApi api = JassApiIndex.Parse(source);

        // Count identifiers called in the script but not declared by the map.
        // This includes natives/globals/types/functions from the target (which are "checked"
        // because the command examined them against the target).
        var declaredInMap = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nat in api.Natives) declaredInMap.Add(nat.Name);
        foreach (var gl in api.Globals) declaredInMap.Add(gl.Name);
        foreach (var tp in api.Types) declaredInMap.Add(tp.Name);
        foreach (var fn in api.Functions) declaredInMap.Add(fn.Name);

        var lines = source.Split('\n');
        var allUsed = new HashSet<string>(StringComparer.Ordinal);

        foreach (string line in lines)
        {
            string cleaned = StripCommentAndStrings(line);
            var tokens = cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            foreach (string token in tokens)
            {
                if (IsKeyword(token) || IsNumeric(token)) continue;
                string name = StripTrailingPunct(token);
                int bracketIdx = name.IndexOf('[');
                if (bracketIdx > 0) name = name[..bracketIdx];
                if (name.Length == 0) continue;
                if (!declaredInMap.Contains(name))
                    allUsed.Add(name);
            }
        }

        return allUsed.Count;
    }

    /// <summary>Count object-data fields examined.</summary>
    private static int GetFieldsChecked(MapDocument doc, GameSchema targetSchema)
    {
        if (targetSchema.Objects is null) return 0;

        int totalFields = 0;

        foreach (ObjectKind kind in ObjectKinds.All)
        {
            var info = ObjectKinds.Info(kind);
            var entries = ObjectKinds.MergedEntries(doc, info);

            foreach (var entry in entries)
            {
                totalFields += entry.Mods.Count;
            }
        }

        return totalFields;
    }
}
