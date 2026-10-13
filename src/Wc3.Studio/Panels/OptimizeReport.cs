using System.IO;
using Wc3.Commands;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Panels;

/// <summary>One label and value pair in a report's summary card.</summary>
public sealed record OptimizeFact(string Label, string Value);

/// <summary>One rendered row, tagged and toned for colour.</summary>
public sealed record OptimizeRow(ReportTone Tone, string Tag, string Detail);

/// <summary>
/// One optimise or audit result, reduced to the shape the panel renders. <see cref="Title"/> heads
/// the summary card, <see cref="Status"/> is the bottom line, <see cref="Facts"/> are the headline
/// numbers, and <see cref="Rows"/> are the findings and notes. <see cref="Clean"/> is true when no
/// row is a problem, so the panel can say so without recounting.
/// </summary>
public sealed record OptimizeReport(
    string Title,
    string Status,
    IReadOnlyList<OptimizeFact> Facts,
    IReadOnlyList<OptimizeRow> Rows)
{
    public bool Clean => Rows.All(r => r.Tone != ReportTone.Problem);
}

/// <summary>
/// Turns each optimise and audit command result into one display shape. Pure, no UI, so it is
/// checked on its own, and every number it shows is one the command already computed rather than a
/// second derivation in the view. The panel draws whatever this returns, and nothing else.
/// </summary>
public static class OptimizeReportBuilder
{
    public static OptimizeReport ForModelAudit(ModelAuditResult r)
    {
        var facts = new List<OptimizeFact>
        {
            new("format", r.Format),
            new("bytes", r.Bytes.ToString("N0")),
            new("geosets", r.Geosets.ToString()),
            new("vertices", r.Vertices.ToString("N0")),
            new("faces", r.Faces.ToString("N0")),
            new("textures", r.Textures.ToString()),
            new("sequences", r.Sequences.ToString()),
            new("nodes", r.Nodes.ToString()),
            new("effects", $"{r.Effects} ({r.ParticleEmitters} emitters, {r.ParticleEmitters2} emitters 2, "
                + $"{r.Ribbons} ribbons, {r.Corns} corns)"),
        };

        var rows = new List<OptimizeRow>();
        foreach (var d in r.Diagnostics)
            rows.Add(new OptimizeRow(ReportTone.Warn, "could not read", d));
        foreach (var i in r.Issues)
            rows.Add(new OptimizeRow(ReportTone.Problem, i.Kind.ToString(), i.Detail));
        if (rows.Count == 0)
            rows.Add(new OptimizeRow(ReportTone.Ok, "clean",
                "no duplicate texture, orphan or empty geoset, zero-area face, unused node or dangling emitter"));

        return new OptimizeReport($"Model audit  {Leaf(r.Path)}", Status(r.IssueCount, "issue"), facts, rows);
    }

    public static OptimizeReport ForModelOptimize(ModelOptimizeResult r)
    {
        var facts = new List<OptimizeFact>
        {
            new("output", Leaf(r.Path)),
            new("geosets", r.Geosets.ToString()),
            new("vertices", Delta(r.VerticesBefore, r.VerticesAfter, r.VerticesRemoved)),
            new("faces", Delta(r.FacesBefore, r.FacesAfter, r.FacesRemoved)),
            new("effects removed", r.EffectsRemoved.ToString()),
            new("bytes", $"{r.BytesBefore:N0} -> {r.BytesAfter:N0}"),
        };

        var rows = new List<OptimizeRow>();
        foreach (var d in r.Diagnostics)
            rows.Add(new OptimizeRow(ReportTone.Info, "note", d));
        if (rows.Count == 0)
            rows.Add(new OptimizeRow(ReportTone.Ok, "wrote",
                $"the smaller model to {Leaf(r.Path)}"));

        return new OptimizeReport($"Model optimise  {Leaf(r.Path)}",
            $"wrote {Leaf(r.Path)}, {r.FacesBefore} -> {r.FacesAfter} face(s)", facts, rows);
    }

    public static OptimizeReport ForTextureOptimize(TextureOptimizeResult r)
    {
        var facts = new List<OptimizeFact>
        {
            new("output", Leaf(r.Path)),
            new("format", r.Format),
            new("size", $"{r.WidthBefore} x {r.HeightBefore} -> {r.WidthAfter} x {r.HeightAfter}"),
            new("bytes", $"{r.BytesBefore:N0} -> {r.BytesAfter:N0}"),
        };

        var rows = new List<OptimizeRow>
        {
            new(ReportTone.Ok, "wrote", $"the scaled texture to {Leaf(r.Path)}"),
        };
        return new OptimizeReport($"Texture optimise  {Leaf(r.Path)}",
            $"wrote {Leaf(r.Path)}, {r.WidthBefore} x {r.HeightBefore} -> {r.WidthAfter} x {r.HeightAfter}",
            facts, rows);
    }

    public static OptimizeReport ForSoundOptimize(SoundOptimizeResult r)
    {
        var facts = new List<OptimizeFact>
        {
            new("output", Leaf(r.Path)),
            new("format", r.BitrateKbps > 0 ? $"{r.Format}  {r.BitrateKbps} kbps" : r.Format),
            new("rate", $"{r.SampleRateBefore} Hz -> {r.SampleRateAfter} Hz"),
            new("channels", $"{r.ChannelsBefore} -> {r.ChannelsAfter}"),
            new("bytes", $"{r.BytesBefore:N0} -> {r.BytesAfter:N0}"),
        };

        var rows = new List<OptimizeRow>
        {
            new(ReportTone.Ok, "wrote", $"the smaller {r.Format} to {Leaf(r.Path)}"),
        };
        if (!string.IsNullOrWhiteSpace(r.Note))
            rows.Add(new OptimizeRow(ReportTone.Warn, "fallback", r.Note));
        return new OptimizeReport($"Sound optimise  {Leaf(r.Path)}",
            $"wrote {Leaf(r.Path)}, {r.BytesBefore:N0} -> {r.BytesAfter:N0} byte(s)", facts, rows);
    }

    public static OptimizeReport ForTerrainAudit(TerrainAuditResult r)
    {
        var facts = new List<OptimizeFact>
        {
            new("grid", $"{r.GridWidth} x {r.GridHeight} corners"),
            new("tiles", r.TileCount.ToString("N0")),
            new("w3e bytes", r.TerrainFileBytes.ToString("N0")),
            new("ground tiles", $"{r.GroundTileTypes} declared, {r.DistinctGroundTextures} in use"),
            new("cliff tiles", $"{r.CliffTileTypes} declared, {r.DistinctCliffTextures} in use"),
            new("cells", $"cliff {r.CliffCells}  ramp {r.RampCells}  water {r.WaterCells}  "
                + $"blight {r.BlightCells}  boundary {r.BoundaryCells}  edge {r.EdgeCells}"),
            new("height", $"{r.MinHeight:0.###}..{r.MaxHeight:0.###} (mean {r.MeanHeight:0.###})"),
            new("cliff level", $"{r.MinCliff}..{r.MaxCliff}"),
        };

        var rows = new List<OptimizeRow>();
        foreach (var d in r.Diagnostics)
            rows.Add(new OptimizeRow(ReportTone.Warn, "not evaluated", d));
        foreach (var i in r.Issues)
            rows.Add(new OptimizeRow(ReportTone.Problem, i.Kind.ToString(), i.Detail));
        if (rows.Count == 0)
            rows.Add(new OptimizeRow(ReportTone.Ok, "clean", "no measured terrain waste or structural fault"));

        return new OptimizeReport($"Terrain audit  {Leaf(r.Path)}", Status(r.IssueCount, "finding"), facts, rows);
    }

    public static OptimizeReport ForTerrainOptimize(TerrainOptimizeResult r)
    {
        var facts = new List<OptimizeFact>
        {
            new("ground tiles", Delta(r.GroundTypesBefore, r.GroundTypesAfter, r.GroundTypesRemoved)),
            new("cliff tiles", Delta(r.CliffTypesBefore, r.CliffTypesAfter, r.CliffTypesRemoved)),
            new("w3e bytes", $"{r.BytesBefore:N0} -> {r.BytesAfter:N0} (-{r.BytesSaved:N0})"),
            new("corners remapped", r.CellsRemapped.ToString("N0")),
        };

        var rows = new List<OptimizeRow>();
        foreach (var d in r.Diagnostics)
            rows.Add(new OptimizeRow(ReportTone.Info, "scope", d));
        rows.Add(r switch
        {
            { Ok: false } => new OptimizeRow(ReportTone.Problem, "refused", r.Message),
            { Changed: true } => new OptimizeRow(ReportTone.Ok, "compacted",
                $"dropped {r.GroundTypesRemoved} ground and {r.CliffTypesRemoved} cliff declared tile(s)"),
            _ => new OptimizeRow(ReportTone.Ok, "already compact", r.Message),
        });

        return new OptimizeReport($"Terrain optimise  {Leaf(r.Path)}", r.Message, facts, rows);
    }

    private static string Status(int problems, string noun) => problems == 0
        ? "no problems found"
        : $"{problems} {noun}{(problems == 1 ? "" : "s")} found";

    private static string Delta(int before, int after, int removed) => $"{before} -> {after}  (-{removed})";

    private static string Leaf(string path) => Path.GetFileName(path);
}
