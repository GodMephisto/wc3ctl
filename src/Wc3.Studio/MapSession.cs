namespace Wc3.Studio;

/// <summary>Shared state for the currently open map, passed to every panel.</summary>
public sealed class MapSession
{
    public Wc3.Model.MapDocument? Current { get; set; }
    public string? MapPath { get; set; }
    public string? GameDir { get; set; }

    /// <summary>
    /// The schemas directory the target-aware panels resolve builds from, or null to
    /// auto-resolve the way the command layer does. Tests point this at a temp folder so a
    /// panel can be driven without a game install; the shell leaves it null.
    /// </summary>
    public string? SchemasDir { get; set; }
}
