namespace Wc3.Studio;

/// <summary>Contract for a Studio panel that displays the currently open map.</summary>
public interface IMapPanel
{
    void ShowMap(MapSession session);
}
