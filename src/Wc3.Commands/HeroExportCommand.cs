// src/Wc3.Commands/HeroExportCommand.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wc3.Model;

namespace Wc3.Commands;

public sealed record HeroExportResult(
    string Directory,
    HeroDefinition Definition,
    int AssetsWritten,
    long AssetBytes,
    int ObjectsExcluded,
    int FilesExcluded);

/// <summary>
/// Exports a hero out of a map as a reviewable <see cref="HeroDefinition"/> plus its assets,
/// which is the <c>import</c> verb of the format.
///
/// The critical decision here is WHAT NOT TO CARRY. The porter's failure was pulling in whatever
/// a script closure could reach - on one hero that was 61 extra objects and thousands of foreign
/// functions belonging to other heroes - and then trimming the calls it could not resolve, which
/// is what produced uninitialized locals and non-terminating loops. So this exports the hero's
/// REAL dependencies (edges through actual object fields) and REPORTS the closure carry as review
/// notes instead of silently including it. A human decides.
///
/// Being incomplete is acceptable and expected. A definition is reviewed before it is installed,
/// so "found the kit, flagged 61 maybes" is a good result. That was never true of a porter whose
/// output went straight into a map someone then hosted.
/// </summary>
public static class HeroExportCommand
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static HeroExportResult Run(MapDocument doc, string rawcode, string sourceMapPath,
        string outputDirectory, string? gameDir = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        Directory.CreateDirectory(outputDirectory);
        var assetDir = Path.Combine(outputDirectory, "assets");

        var bundle = BundleCommand.ResolveUnit(doc, rawcode, gameDir);

        // The hero's own dependency set, not everything a script could reach.
        var realObjects = BundleStructure.RealObjects(bundle);
        var realFiles = BundleStructure.RealFiles(bundle);

        // Carry each object's MAP-LEVEL fields, so the definition stands alone. Base-game values
        // are deliberately excluded: they belong to the engine, not the hero, and baking them in
        // would make a definition wrong the moment a patch changes a default.
        var objects = new List<DefinitionObject>();
        foreach (var o in bundle.Objects.Where(o => realObjects.Contains(o.Rawcode)))
        {
            var merged = ObjectGetCommand.Execute(doc, o.Kind, o.Rawcode, gameDir);
            var fields = merged.Found
                ? merged.Fields.Where(f => f.Source == "map")
                    .Select(f => new DefinitionField(f.Code, f.Value)).ToList()
                : new List<DefinitionField>();
            objects.Add(new DefinitionObject(o.Rawcode, o.Kind.ToString().ToLowerInvariant(),
                o.Name, merged.BaseRawcode, o.CustomToMap,
                o.Rawcode == bundle.RootRawcode ? "root" : "dependency", fields));
        }

        var assets = new List<DefinitionAsset>();
        long bytes = 0;
        foreach (var f in bundle.Files.Where(f => realFiles.Contains(f.Path)))
        {
            var entry = doc.GetFile(f.Path);
            if (entry is null) continue;
            var payload = entry.OverrideBytes ?? entry.RawBytes;
            var dest = Path.Combine(assetDir, f.Path.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, payload);
            assets.Add(new DefinitionAsset(f.Path, f.Category, payload.Length,
                Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()));
            bytes += payload.Length;
        }

        string? scriptFile = null;
        var entryPoints = new List<string>();
        if (bundle.Functions.Count > 0 && doc.GetFile("war3map.j") is { } js)
        {
            var text = Encoding.UTF8.GetString(js.OverrideBytes ?? js.RawBytes);
            var lines = text.Split('\n');
            var sb = new StringBuilder();
            foreach (var fn in bundle.Functions.OrderBy(f => f.StartLine))
            {
                int start = Math.Max(0, fn.StartLine - 1);
                int end = Math.Min(lines.Length - 1, fn.EndLine - 1);
                if (start > end) continue;
                sb.AppendLine($"// {fn.Name}  ({fn.Reason})");
                for (int i = start; i <= end; i++) sb.AppendLine(lines[i].TrimEnd('\r'));
                sb.AppendLine();
                entryPoints.Add(fn.Name);
            }
            scriptFile = "script.j";
            File.WriteAllText(Path.Combine(outputDirectory, scriptFile), sb.ToString(), new UTF8Encoding(false));
        }

        // What a target must already provide. Stated so an install can refuse rather than produce
        // a map that silently lacks the hero.
        var requires = new List<DefinitionRequirement>
        {
            new("roster-registration",
                "the target must register this rawcode with its own hero roster; find the call with "
                + "'wc3ctl contract <target>' (GGGA's is RPB_AddHero(rawcode, role, portrait))",
                Satisfiable: false),
            new("spell-dispatch",
                "the target must route this hero's ability ids to its cast handlers, or the abilities "
                + "will exist but do nothing",
                Satisfiable: false),
        };

        int excludedObjects = bundle.Objects.Count - objects.Count;
        int excludedFiles = bundle.Files.Count - assets.Count;
        var notes = new List<string>();
        if (excludedObjects > 0)
            notes.Add($"{excludedObjects} object(s) reachable only through the script closure were "
                      + "EXCLUDED; they usually belong to other heroes. Review and add any that are "
                      + "genuinely this hero's.");
        if (excludedFiles > 0)
            notes.Add($"{excludedFiles} file(s) reachable only through the script closure were EXCLUDED.");
        foreach (var d in bundle.Diagnostics) notes.Add(d);

        var def = new HeroDefinition(HeroDefinition.CurrentSchemaVersion, bundle.RootRawcode,
            bundle.RootName, Path.GetFileName(sourceMapPath), "wc3ctl hero export",
            objects, assets, bundle.Strings, scriptFile, entryPoints, requires, notes);

        File.WriteAllText(Path.Combine(outputDirectory, "hero.json"),
            JsonSerializer.Serialize(def, Json), new UTF8Encoding(false));

        return new HeroExportResult(outputDirectory, def, assets.Count, bytes,
            excludedObjects, excludedFiles);
    }
}
