using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio.Panels;

namespace Wc3.Studio;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceWorkspace.MapChanged += OnWorkspaceMapChanged;
        TargetWorkspace.MapChanged += OnWorkspaceMapChanged;
        SourceWorkspace.PortRequested += OnPortRequested;
        SourceWorkspace.PortPreviewRequested += OnPortPreviewRequested;
    }

    /// <summary>
    /// Both panes, for the porting wave to wire across (read source selection,
    /// write into the target session). Each workspace owns its map exclusively.
    /// </summary>
    public (MapWorkspaceView Source, MapWorkspaceView Target) Workspaces =>
        (SourceWorkspace, TargetWorkspace);

    private async void OnOpenSourceMapClick(object? sender, RoutedEventArgs e) =>
        await SourceWorkspace.PickAndOpenMapAsync();

    private async void OnOpenTargetMapClick(object? sender, RoutedEventArgs e) =>
        await TargetWorkspace.PickAndOpenMapAsync();

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "About wc3ctl Studio",
            Width = 380,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Text = "wc3ctl Studio\n\nA CLI-first Warcraft III map editor.",
                Margin = new Avalonia.Thickness(16),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// The one-button port: resolve the selected unit's closure in the Source map and
    /// inject it into the Target map, auto-remapping rawcode collisions - no config. Runs
    /// off the UI thread (a real map is large); reports the outcome in a dialog. The port
    /// works on a fresh reload of each map on disk, so the open sessions stay untouched
    /// and the result is written to a sibling &lt;target&gt;.ported.&lt;ext&gt; (never clobbers).
    /// </summary>
    private async void OnPortRequested(object? sender, string rawcode) =>
        await RunPort(rawcode, dryRun: false);

    /// <summary>
    /// Dry-run preview: computes the identical report through PortCommand.PreviewPort
    /// (same code path as the real port) and shows it — nothing is written anywhere.
    /// </summary>
    private async void OnPortPreviewRequested(object? sender, string rawcode) =>
        await RunPort(rawcode, dryRun: true);

    private async Task RunPort(string rawcode, bool dryRun)
    {
        if (!SourceWorkspace.HasMap || SourceWorkspace.Session.MapPath is not { } sourcePath)
        { SourceWorkspace.SetStatus("Open a Source map first."); return; }
        if (!TargetWorkspace.HasMap || TargetWorkspace.Session.MapPath is not { } targetPath)
        { SourceWorkspace.SetStatus("Open a Target map first (right pane)."); return; }

        var gameDir = SourceWorkspace.Session.GameDir;
        SourceWorkspace.SetStatus(dryRun
            ? $"Previewing port of {rawcode} → {Path.GetFileName(targetPath)}…"
            : $"Porting {rawcode} → {Path.GetFileName(targetPath)}…");

        try
        {
            var (result, outPath) = await Task.Run(() =>
            {
                var source = MapDocument.Load(sourcePath);
                var target = MapDocument.Load(targetPath);
                var bundle = BundleCommand.ResolveUnit(source, rawcode, gameDir);
                if (dryRun)
                    return (PortCommand.PreviewPort(source, bundle, target), (string?)null);
                var r = PortCommand.PortUnit(source, bundle, target);
                string outp = Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? ".",
                    Path.GetFileNameWithoutExtension(targetPath) + ".ported" + Path.GetExtension(targetPath));
                target.Save(outp);
                return (r, (string?)outp);
            });

            SourceWorkspace.SetStatus(outPath is null
                ? $"Preview: {result.RootRawcode} → {result.RootPortedTo} ({result.Objects.Count} objects, "
                  + $"{result.CopiedFiles.Count} files, {result.Remaps.Count} remaps) - nothing written."
                : $"Ported {result.RootRawcode} → {result.RootPortedTo} into {Path.GetFileName(outPath)} "
                  + $"({result.Objects.Count} objects, {result.CopiedFiles.Count} files, {result.Remaps.Count} remaps).");
            await ShowPortReport(result, outPath);
        }
        catch (Exception ex)
        {
            SourceWorkspace.SetStatus($"{(dryRun ? "Preview" : "Port")} failed: {ex.Message}");
        }
    }

    /// <summary>Port (or dry-run, when <paramref name="outPath"/> is null) report dialog.</summary>
    private async Task ShowPortReport(PortResult r, string? outPath)
    {
        var sb = new StringBuilder();
        string root = r.RootPortedTo == r.RootRawcode ? r.RootRawcode : $"{r.RootRawcode} → {r.RootPortedTo}";
        sb.AppendLine($"Ported {root}{(r.RootName is null ? "" : $"  \"{r.RootName}\"")}");
        sb.AppendLine($"{r.Objects.Count} object(s), {r.CopiedFiles.Count} file(s) copied, "
                      + $"{r.InlinedStrings} string(s) inlined, {r.Remaps.Count} rawcode(s) remapped");
        sb.AppendLine();
        if (r.Remaps.Count > 0)
        {
            sb.AppendLine("Rawcode remaps (collisions):");
            foreach (var m in r.Remaps)
                sb.AppendLine($"  {m.Kind.ToString().ToLowerInvariant()} {m.From} → {m.To}");
            sb.AppendLine();
        }
        sb.AppendLine("Objects:");
        foreach (var o in r.Objects)
            sb.AppendLine($"  {o.Kind.ToString().ToLowerInvariant()} {o.Rawcode}"
                          + $"{(o.Name is null ? "" : $"  \"{o.Name}\"")}"
                          + $"{(o.ModifiesStandard ? "  (modifies standard object)" : "")}");
        if (r.Script is { } s)
        {
            sb.AppendLine().AppendLine(
                $"Script (best-effort): {s.Functions} function(s), {s.Globals} global(s) carried, "
                + $"{s.Renamed} renamed, init {(s.InitHooked ? "wired" : "NOT wired")}.");
            foreach (var n in s.Notes) sb.AppendLine($"  - {n}");
        }
        if (r.Warnings.Count > 0)
        {
            sb.AppendLine().AppendLine("Warnings:");
            foreach (var w in r.Warnings) sb.AppendLine($"  ! {w}");
        }
        foreach (var d in r.Diagnostics) sb.AppendLine($"note: {d}");
        sb.AppendLine().AppendLine(outPath is null ? "DRY RUN - nothing written" : $"Saved: {outPath}");

        var dialog = new Window
        {
            Title = outPath is null ? "Port preview (dry run)" : "Port complete",
            Width = 620,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                Padding = new Avalonia.Thickness(16),
                Content = new SelectableTextBlock
                {
                    Text = sb.ToString(),
                    FontFamily = new Avalonia.Media.FontFamily("Consolas, Menlo, monospace"),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
            },
        };
        await dialog.ShowDialog(this);
    }

    /// <summary>Global status: which map sits in each pane (details live per pane).</summary>
    private void OnWorkspaceMapChanged(object? sender, EventArgs e) =>
        StatusText.Text = $"Source: {Describe(SourceWorkspace)}   |   Target: {Describe(TargetWorkspace)}";

    private static string Describe(MapWorkspaceView workspace) =>
        workspace.HasMap
            ? Path.GetFileName(workspace.Session.MapPath) ?? "(unnamed)"
            : "no map";
}
