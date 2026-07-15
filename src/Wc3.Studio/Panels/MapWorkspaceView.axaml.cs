using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Studio.Panels;

/// <summary>
/// A self-contained workspace for ONE map: a role header ("Source"/"Target") with
/// its own open button and status line, its own <see cref="MapSession"/>, and its
/// own panel-tab instances with lazy per-tab loading. Two instances live side by
/// side in MainWindow and must never share mutable map state; a later wave wires
/// porting across them through the public <see cref="Session"/> surface.
/// </summary>
public partial class MapWorkspaceView : UserControl
{
    private readonly HashSet<IMapPanel> _loadedPanels = new();
    private string _role = "Map";

    public MapWorkspaceView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Header label, "Source" or "Target" (set from MainWindow.axaml). The role
    /// also picks the role-specific header affordances: only the Source side
    /// shows the (still disabled) port seam.
    /// </summary>
    public string Role
    {
        get => _role;
        set
        {
            _role = value;
            RoleText.Text = value;
            PortButtonHost.IsVisible = value == "Source";
        }
    }

    /// <summary>This workspace's map state, passed to its panels — never shared.</summary>
    public MapSession Session { get; } = new();

    public bool HasMap => Session.Current is not null;

    /// <summary>Raised after a map is successfully opened into this workspace.</summary>
    public event EventHandler? MapChanged;

    /// <summary>Shows the OS map picker, then loads the chosen map into this workspace.</summary>
    public async Task PickAndOpenMapAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Open Warcraft III map — {_role}",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Warcraft III maps") { Patterns = new[] { "*.w3x", "*.w3m" } },
                FilePickerFileTypes.All,
            },
        });
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
        {
            OpenMap(path);
        }
    }

    public void OpenMap(string path)
    {
        try
        {
            var doc = MapDocument.Load(path);
            Session.Current = doc;
            Session.MapPath = path;
            // GameDir stays null — panels auto-detect the game install.

            var info = InfoCommand.Execute(doc);
            var list = ListCommand.Execute(doc);
            var name = string.IsNullOrEmpty(info.Name) ? "(unnamed)" : info.Name;
            StatusText.Text = info.Diagnostics.Count > 0
                ? $"{path} — {name} — {list.Files.Count} file(s) — {info.Diagnostics.Count} diagnostic(s): {string.Join("; ", info.Diagnostics)}"
                : $"{path} — {name} — {list.Files.Count} file(s)";

            // Lazy loading: only the visible tab refreshes now; the other
            // panels load on first selection (see OnPanelTabsSelectionChanged).
            _loadedPanels.Clear();
            LoadSelectedPanel();

            MapChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error opening {path}: {ex.Message}";
        }
    }

    private async void OnOpenMapClick(object? sender, RoutedEventArgs e) =>
        await PickAndOpenMapAsync();

    private void OnPanelTabsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged bubbles up from selectors inside tab content too.
        if (!ReferenceEquals(e.Source, PanelTabs) || Session.Current is null)
        {
            return;
        }

        try
        {
            LoadSelectedPanel();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading panel: {ex.Message}";
        }
    }

    private void LoadSelectedPanel()
    {
        if (PanelTabs.SelectedItem is TabItem { Content: IMapPanel panel } && _loadedPanels.Add(panel))
        {
            panel.ShowMap(Session);
        }
    }
}
