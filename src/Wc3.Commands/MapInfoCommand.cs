// src/Wc3.Commands/MapInfoCommand.cs
using Wc3.Model;
using War3Net.Build.Extensions;
using War3Net.Build.Info;

namespace Wc3.Commands;

/// <summary>Editable snapshot of war3map.w3i. String fields are raw (TRIGSTR_ keys are
/// NOT resolved — resolving would break the reference on write-back); the numeric
/// fields are informational/read-only in the editor.</summary>
public sealed record MapInfoFields(
    string MapName,
    string Author,
    string Description,
    string RecommendedPlayers,
    int PlayableWidth,
    int PlayableHeight,
    int Players);

/// <summary>
/// Read/write access to the map-info file (war3map.w3i). The write side mutates the
/// already-parsed War3Net <see cref="MapInfo"/> model and re-serializes the WHOLE file
/// via War3Net's writer, so every field the parser captured — including the Unk*
/// unknowns — is re-emitted; untouched fields survive byte-for-byte
/// (<c>MapInfoCommandTests.Unchanged_reserialize_is_byte_identical</c> pins this).
/// MapDocument.SerializeEntry has no MapInfo case, so the bytes go back through
/// <see cref="MapDocument.AddOrReplaceRawFile"/> (raw payloads are written verbatim on Save).
/// </summary>
public static class MapInfoCommand
{
    public const string FileName = "war3map.w3i";

    /// <summary>Field names <see cref="Set"/> accepts (matched case-insensitively).
    /// Structural values (dimensions, player list) are read-only.</summary>
    public static readonly IReadOnlyList<string> EditableFields =
        new[] { "MapName", "Author", "Description", "RecommendedPlayers" };

    public static MapInfoFields Read(MapDocument doc) => ToFields(GetInfo(doc));

    /// <summary>Sets one editable field and writes the re-serialized w3i back into the
    /// in-memory document (persisted on the next Save). Returns the updated snapshot.</summary>
    public static MapInfoFields Set(MapDocument doc, string field, string value)
    {
        var info = GetInfo(doc);
        ApplyField(info, field, value);
        var entry = doc.AddOrReplaceRawFile(FileName, Serialize(info));
        // AddOrReplaceRawFile drops the parsed model (raw payload wins on Save); restore
        // it so in-memory readers keep seeing the mutated MapInfo. The two stay
        // consistent — the override bytes were serialized from this very model.
        entry.Model = info;
        return ToFields(info);
    }

    /// <summary>Pure field mapping onto the War3Net model (hermetically testable).</summary>
    public static void ApplyField(MapInfo info, string field, string value)
    {
        if (Eq(field, "MapName")) info.MapName = value;
        else if (Eq(field, "Author")) info.MapAuthor = value;
        else if (Eq(field, "Description")) info.MapDescription = value;
        else if (Eq(field, "RecommendedPlayers")) info.RecommendedPlayers = value;
        else throw new ArgumentException(
            $"Unknown or read-only field '{field}'. Editable fields: {string.Join(", ", EditableFields)}.",
            nameof(field));

        static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Serializes a MapInfo with War3Net's writer (the exact inverse of the
    /// ReadMapInfo parser wired in DefaultParsers).</summary>
    public static byte[] Serialize(MapInfo info)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            writer.Write(info);
        return ms.ToArray();
    }

    private static MapInfo GetInfo(MapDocument doc) =>
        doc.GetFile(FileName)?.Model as MapInfo
        ?? throw new InvalidOperationException(
            $"{FileName} is missing or could not be parsed; map info is not editable.");

    private static MapInfoFields ToFields(MapInfo info) => new(
        MapName: info.MapName ?? string.Empty,
        Author: info.MapAuthor ?? string.Empty,
        Description: info.MapDescription ?? string.Empty,
        RecommendedPlayers: info.RecommendedPlayers ?? string.Empty,
        PlayableWidth: info.PlayableMapAreaWidth,
        PlayableHeight: info.PlayableMapAreaHeight,
        Players: info.Players?.Count ?? 0);
}
