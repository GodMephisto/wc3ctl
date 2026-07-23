using System.Reflection;
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
        Title = $"wc3ctl Studio · {BuildStamp()}";
        SourceWorkspace.MapChanged += OnWorkspaceMapChanged;
        TargetWorkspace.MapChanged += OnWorkspaceMapChanged;
        SourceWorkspace.PortRequested += OnPortRequested;
        SourceWorkspace.PortPreviewRequested += OnPortPreviewRequested;
        Opened += OnOpenedAutoLoad;
    }

    /// <summary>Dev/QA convenience: <c>Wc3.Studio.exe --open &lt;map&gt; [--open-target &lt;map&gt;]</c>
    /// auto-loads maps on startup so the UI can be driven and screenshotted without the file
    /// dialog. No-op in normal use (no flags). Runs once, after the window is up.</summary>
    private void OnOpenedAutoLoad(object? sender, EventArgs e)
    {
        Opened -= OnOpenedAutoLoad;
        var args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--open", StringComparison.OrdinalIgnoreCase))
                TryOpen(SourceWorkspace, args[i + 1]);
            else if (string.Equals(args[i], "--open-target", StringComparison.OrdinalIgnoreCase))
                TryOpen(TargetWorkspace, args[i + 1]);
        }

        static void TryOpen(MapWorkspaceView ws, string path)
        {
            try { if (System.IO.File.Exists(path)) ws.OpenMap(path); }
            catch { /* dev flag, never crash the app over it */ }
        }
    }

    /// <summary>Git short-hash + UTC build time baked in at compile (see the
    /// StampBuildInfo target). Shown in the title bar so a stale build is obvious at a
    /// glance - no more guessing whether the running binary carries the latest change.</summary>
    private static string BuildStamp()
    {
        var attrs = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>();
        string Meta(string key) => attrs.FirstOrDefault(a => a.Key == key)?.Value ?? "?";
        return $"{Meta("GitHash")} · built {Meta("BuildTimeUtc")}Z";
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
        if (!TargetWorkspace.HasMap || TargetWorkspace.Session.Current is not { } liveTarget)
        { SourceWorkspace.SetStatus("Open or create a Target map first (right pane)."); return; }

        // A saved target reloads from disk (keeps the open session pristine, writes a sibling
        // .ported). A blank/unsaved target has no file, so we port into its live in-memory doc
        // and let the user Save afterward — otherwise porting into a New Blank Map is impossible.
        var targetPath = TargetWorkspace.Session.MapPath;
        var gameDir = SourceWorkspace.Session.GameDir;
        var targetLabel = targetPath is not null ? Path.GetFileName(targetPath) : "the target map";
        SourceWorkspace.SetStatus(dryRun
            ? $"Previewing port of {rawcode} → {targetLabel}…"
            : $"Porting {rawcode} → {targetLabel}…");

        try
        {
            var (result, outPath) = await Task.Run(() =>
            {
                var source = MapDocument.Load(sourcePath);
                // Saved target → a fresh disk copy; blank/unsaved → the live in-memory doc.
                var target = targetPath is not null ? MapDocument.Load(targetPath) : liveTarget;
                var bundle = BundleCommand.ResolveUnit(source, rawcode, gameDir);
                if (dryRun)
                    return (PortCommand.PreviewPort(source, bundle, target), (string?)null);
                var r = PortCommand.PortUnit(source, bundle, target);
                if (targetPath is null)
                    return (r, (string?)null); // ported into the live in-memory target; user Saves
                var dir = Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? ".";
                var stem = Path.GetFileNameWithoutExtension(targetPath);
                // Strip a trailing ".ported" so re-porting accumulates into one file, not a chain.
                if (stem.EndsWith(".ported", StringComparison.OrdinalIgnoreCase))
                    stem = stem[..^".ported".Length];
                string outp = Path.Combine(dir, stem + ".ported" + Path.GetExtension(targetPath));
                target.Save(outp);
                return (r, (string?)outp);
            });

            var summary = $"{result.RootRawcode} → {result.RootPortedTo} "
                + $"({result.Objects.Count} objects, {result.CopiedFiles.Count} files, {result.Remaps.Count} remaps)";
            if (dryRun)
                SourceWorkspace.SetStatus($"Preview: {summary} - nothing written.");
            else if (outPath is not null)
            {
                SourceWorkspace.SetStatus($"Ported {summary} into {Path.GetFileName(outPath)}. Target pane now shows the ported copy.");
                TargetWorkspace.OpenMap(outPath); // reload the written .ported into the Target pane
            }
            else
            {
                SourceWorkspace.SetStatus($"Ported {summary} into the target (unsaved) - click Save to write it to disk.");
                TargetWorkspace.RefreshAfterExternalEdit(); // the live in-memory target was mutated
            }

            await ShowPortReport(result, outPath, dryRun);
        }
        catch (Exception ex)
        {
            SourceWorkspace.SetStatus($"{(dryRun ? "Preview" : "Port")} failed: {ex.Message}");
        }
    }

    /// <summary>Port report dialog: dry-run preview, a saved .ported file, or an in-memory
    /// port into an unsaved target (<paramref name="outPath"/> null but not a dry run).</summary>
    private async Task ShowPortReport(PortResult r, string? outPath, bool dryRun)
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
        sb.AppendLine().AppendLine(dryRun
            ? "DRY RUN - nothing written"
            : outPath is not null
                ? $"Saved: {outPath}"
                : "Ported into the target in memory - not yet saved (use Save to write it).");

        var dialog = new Window
        {
            Title = dryRun ? "Port preview (dry run)" : "Port complete",
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
