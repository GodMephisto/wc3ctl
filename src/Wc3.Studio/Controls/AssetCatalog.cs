// src/Wc3.Studio/Controls/AssetCatalog.cs
using System.Collections.Concurrent;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Studio.Controls;

/// <summary>
/// The picker's view of <see cref="AssetListCommand"/>. All of the path logic now lives in the
/// command layer, because a picker is only one consumer of the answer and the CLI and MCP need the
/// same list. The <c>AssetFamily</c> enum moved with it.
///
/// What is left here is the part that is genuinely a UI concern, the shape and the cache. The
/// first base-game enumeration pays one CASC listfile pass that takes seconds, which cannot happen
/// on the UI thread, so it is handed back as a Task and memoised per install and family for the
/// process lifetime. Every later field selection then resolves instantly. A front end that does
/// not mind blocking, like the CLI, calls the command directly and needs none of this.
/// </summary>
public static class AssetCatalog
{
    private static readonly ConcurrentDictionary<(string Dir, AssetFamily Family), Task<IReadOnlyList<string>>>
        GameCache = new();

    /// <summary>The picker family for a metadata type token, or null when the field is not an
    /// asset path.</summary>
    public static AssetFamily? FamilyForFieldType(string type) =>
        AssetListCommand.FamilyForFieldType(type);

    /// <summary>The open map's own entries of the family. Cheap, so it stays synchronous and the
    /// picker can populate from the map before the base-game side arrives.</summary>
    public static IReadOnlyList<string> MapPaths(MapDocument doc, AssetFamily family) =>
        AssetListCommand.MapPaths(doc, family);

    /// <summary>The family's extensions.</summary>
    public static IReadOnlyList<string> FamilyExtensions(AssetFamily family) =>
        AssetListCommand.FamilyExtensions(family);

    /// <summary>
    /// The base game's paths of the family, empty when no install answers, never throwing.
    /// Cached per install and family. Callers marshal back to the UI thread themselves.
    /// </summary>
    public static Task<IReadOnlyList<string>> GamePathsAsync(string? gameDir, AssetFamily family) =>
        GameCache.GetOrAdd((gameDir ?? "", family),
            key => Task.Run(() =>
                AssetListCommand.Execute(null, key.Family, key.Dir.Length == 0 ? null : key.Dir)
                    .GamePaths));
}
