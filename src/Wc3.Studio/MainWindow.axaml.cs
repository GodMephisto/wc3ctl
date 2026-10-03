using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio.Controls;
using Wc3.Studio.Dialogs;
using Wc3.Studio.Panels;

namespace Wc3.Studio;

public partial class MainWindow : Window
{
    /// <summary>Persisted install/map folders, loaded once at startup and applied to both
    /// workspaces and the shared file picker (see <see cref="ApplySettings"/>).</summary>
    private StudioSettings _settings = StudioSettings.Load();

    public MainWindow()
    {
        InitializeComponent();
        Title = $"wc3ctl Studio · {BuildStamp()}";
        SourceWorkspace.MapChanged += OnWorkspaceMapChanged;
        TargetWorkspace.MapChanged += OnWorkspaceMapChanged;
        SourceWorkspace.PortRequested += OnPortRequested;
        SourceWorkspace.PortPreviewRequested += OnPortPreviewRequested;
        ApplySettings();
        Opened += OnOpenedAutoLoad;
    }

    /// <summary>Pushes the saved folders into the running app: the Warcraft III install goes to
    /// both map sessions (null keeps auto-detect), and the map folder seeds the shared picker so
    /// Open Map starts there. Called at startup and after the Settings dialog saves.</summary>
    private void ApplySettings()
    {
        SourceWorkspace.Session.GameDir = _settings.GameDir;
        TargetWorkspace.Session.GameDir = _settings.GameDir;
        FilePicker.SeedLastDirectory(_settings.MapsDir);
        // Open the base game data now, off the UI thread, rather than making whichever panel
        // needs it first block for a second and a half. See GameDataWarmup for the measurement.
        GameDataWarmup.Begin(_settings.GameDir);
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var updated = await new SettingsDialog(_settings).ShowDialog<StudioSettings?>(this);
        if (updated is null)
            return; // cancelled, nothing changes

        _settings = updated;
        _settings.Save();
        ApplySettings();
        StatusText.Text = "Settings saved. The Warcraft III install and map folder are remembered for next time.";
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
            {
                RevealTargetPane();
                TryOpen(TargetWorkspace, args[i + 1]);
            }
        }

        // After the maps load, optionally select a Source tab / dock sub-tab so a panel
        // can be screenshotted directly (--tab Triggers, --dock Unit).
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--tab", StringComparison.OrdinalIgnoreCase))
                SourceWorkspace.SelectTab(args[i + 1]);
            else if (string.Equals(args[i], "--dock", StringComparison.OrdinalIgnoreCase))
                SourceWorkspace.SelectDock(args[i + 1]);
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

    private async void OnOpenTargetMapClick(object? sender, RoutedEventArgs e)
    {
        RevealTargetPane();
        await TargetWorkspace.PickAndOpenMapAsync();
    }

    /// <summary>Reveals the Target pane (and the splitter) the first time it is needed - opening
    /// a target map or porting. The window starts as a single Source pane so one map fills it;
    /// pressing Port or Open Target brings the second pane in as a half-width split.</summary>
    private void RevealTargetPane()
    {
        if (TargetWorkspace.IsVisible)
            return;
        PanesGrid.ColumnDefinitions[1].Width = new GridLength(4);
        PanesGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
        PaneSplitter.IsVisible = true;
        TargetWorkspace.IsVisible = true;
    }

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
    private async void OnPortRequested(object? sender, MapWorkspaceView.PortRequest request)
    {
        RevealTargetPane(); // pressing Port brings in the Target pane so a map can be set up there
        await RunPort(request, dryRun: false);
    }

    /// <summary>
    /// Dry-run preview: computes the identical report through PortCommand.PreviewPort
    /// (same code path as the real port) and shows it — nothing is written anywhere.
    /// </summary>
    private async void OnPortPreviewRequested(object? sender, MapWorkspaceView.PortRequest request)
    {
        RevealTargetPane();
        await RunPort(request, dryRun: true);
    }

    private async Task RunPort(MapWorkspaceView.PortRequest request, bool dryRun)
    {
        var (rawcode, excludedKeys) = request;
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
        var exclusionNote = excludedKeys.Count > 0 ? $" ({excludedKeys.Count} node(s) excluded)" : "";
        SourceWorkspace.SetStatus(dryRun
            ? $"Previewing port of {rawcode} → {targetLabel}{exclusionNote}…"
            : $"Porting {rawcode} → {targetLabel}{exclusionNote}…");

        try
        {
            var (result, outPath) = await Task.Run(() =>
            {
                var source = MapDocument.Load(sourcePath);
                // Saved target → a fresh disk copy; blank/unsaved → the live in-memory doc.
                var target = targetPath is not null ? MapDocument.Load(targetPath) : liveTarget;
                var bundle = BundleCommand.ResolveUnit(source, rawcode, gameDir);
                // Left-click exclusions from the Dependencies graph narrow the bundle before
                // anything is ported, everything else about the pipeline is unaware of them.
                if (excludedKeys.Count > 0)
                    bundle = BundleFilter.Apply(bundle, excludedKeys);
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
        // One shared report body, PortReport in Wc3.Commands. This used to be a second copy of the
        // CLI's renderer, and it drifted, so Studio went on listing every object the script closure
        // carries as if the ported unit owned them long after the CLI stopped. Studio only owns the
        // footer below, because only Studio can port into an unsaved target.
        var sb = new StringBuilder(PortReport.Body(r));
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
