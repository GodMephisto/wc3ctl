using Wc3.Model;
using War3Net.Build.Info;

namespace Wc3.Commands;

public static class InfoCommand
{
    public static MapInfoResult Execute(MapDocument doc)
    {
        var info = doc.GetFile("war3map.w3i")?.Model as MapInfo;
        var diags = doc.Diagnostics.Select(d => $"{d.Severity}: {d.FileName} — {d.Message}").ToList();
        return new MapInfoResult(
            Name: info?.MapName ?? "(unknown)",
            Author: info?.MapAuthor ?? "(unknown)",
            Players: info?.Players?.Count ?? 0,
            Width: info?.PlayableMapAreaWidth,
            Height: info?.PlayableMapAreaHeight,
            Diagnostics: diags);
    }
}
