namespace Wc3.Studio;

/// <summary>Shared state for the currently open map, passed to every panel.</summary>
public sealed class MapSession
{
    public Wc3.Model.MapDocument? Current { get; set; }
    public string? MapPath { get; set; }
    public string? GameDir { get; set; }
}
