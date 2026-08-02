// src/Wc3.Commands/HeroDefinition.cs
namespace Wc3.Commands;

/// <summary>
/// One field the source map set on an object. Only map-level fields are carried: a base-game
/// value is not the hero's data, it is the engine's, and re-stating it would make the definition
/// brittle across patches.
/// </summary>
public sealed record DefinitionField(string Code, string Value);

/// <summary>
/// One object the hero needs, with its fields STATED rather than inferred. Carrying the fields is
/// what makes a definition self-contained: without them an install would still need the original
/// map, and the whole point of the format is that it does not.
/// </summary>
public sealed record DefinitionObject(
    string Rawcode,
    string Kind,
    string? Name,
    string? BaseRawcode,
    bool CustomToMap,
    string Origin,
    IReadOnlyList<DefinitionField> Fields);

/// <summary>
/// One asset. <see cref="Sha256"/> makes an install idempotent and lets a collision be judged:
/// same hash means the target already has this exact file and nothing needs writing.
/// </summary>
public sealed record DefinitionAsset(
    string Path,
    string Category,
    long SizeBytes,
    string Sha256);

/// <summary>
/// Something the TARGET map must provide. This is the part the World Editor has no concept of and
/// the reason a hero with perfect object data can still not exist to the player: a map builds its
/// roster from its own script, so a unit absent from that call is simply not a hero there.
/// </summary>
public sealed record DefinitionRequirement(
    string Kind,
    string Detail,
    bool Satisfiable);

/// <summary>
/// A hero as a self-describing artifact, rather than something inferred out of a host map every
/// time it is needed.
///
/// The reason this exists: extracting a hero from a merged 200,000-line arena map is intractable,
/// and every failure in that direction came from INFERENCE - guessing which objects belong to it,
/// which functions are reachable, which calls can be trimmed. A definition replaces inference with
/// declaration. Stated once, reviewed by a human, then installed mechanically.
///
/// It also lets the importer be imperfect, which it never could be before. "Got 80% and flagged
/// the rest" is a good outcome for something a person reviews, and a broken map when it goes
/// straight into a game.
/// </summary>
public sealed record HeroDefinition(
    int SchemaVersion,
    string Id,
    string? Name,
    string SourceMap,
    string ExportedFrom,
    IReadOnlyList<DefinitionObject> Objects,
    IReadOnlyList<DefinitionAsset> Assets,
    IReadOnlyList<string> Strings,
    string? ScriptFile,
    IReadOnlyList<string> ScriptEntryPoints,
    /// <summary>
    /// Global declarations the carried functions read, verbatim. Without these the script
    /// does not compile: carrying functions alone produced 50 'undeclared variable' errors,
    /// which is the same fatal outcome as a missing callee, and an un-compilable war3map.j
    /// means a hosted map shows no player slots.
    /// </summary>
    IReadOnlyList<string> Globals,
    IReadOnlyList<DefinitionRequirement> Requires,
    IReadOnlyList<string> ReviewNotes)
{
    public const int CurrentSchemaVersion = 1;
}
