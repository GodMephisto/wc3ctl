using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace Wc3.Studio.Controls;

/// <summary>
/// The landing surface a workspace shows while no map is open. It owns the welcome copy and the
/// clickable map lists, and it hears back through events rather than reaching into the workspace,
/// so the same control is reusable and testable on its own. The recent and discovered lists come
/// in as bindable inputs (<see cref="RecentMaps"/>, <see cref="DiscoveredMaps"/>), so this control
/// never decides which maps matter.
/// </summary>
public partial class WelcomeView : UserControl
{
    /// <summary>The recent map paths to list, newest first.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> RecentMapsProperty =
        AvaloniaProperty.Register<WelcomeView, IReadOnlyList<string>?>(nameof(RecentMaps));

    /// <summary>The map files discovered under the Warcraft III folder to list.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> DiscoveredMapsProperty =
        AvaloniaProperty.Register<WelcomeView, IReadOnlyList<string>?>(nameof(DiscoveredMaps));

    /// <summary>Whether the New Blank Map action is offered. Only the Target workspace shows it.</summary>
    public static readonly StyledProperty<bool> ShowNewBlankMapProperty =
        AvaloniaProperty.Register<WelcomeView, bool>(nameof(ShowNewBlankMap), defaultValue: true);

    /// <summary>The recent map paths to list, newest first.</summary>
    public IReadOnlyList<string>? RecentMaps
    {
        get => GetValue(RecentMapsProperty);
        set => SetValue(RecentMapsProperty, value);
    }

    /// <summary>The map files discovered under the Warcraft III folder to list.</summary>
    public IReadOnlyList<string>? DiscoveredMaps
    {
        get => GetValue(DiscoveredMapsProperty);
        set => SetValue(DiscoveredMapsProperty, value);
    }

    /// <summary>Whether the New Blank Map action is offered.</summary>
    public bool ShowNewBlankMap
    {
        get => GetValue(ShowNewBlankMapProperty);
        set => SetValue(ShowNewBlankMapProperty, value);
    }

    /// <summary>Raised when the user clicks a listed map, with its full path.</summary>
    public event EventHandler<string>? MapActivated;

    /// <summary>Raised when the user asks for the OS map picker.</summary>
    public event EventHandler? OpenMapRequested;

    /// <summary>Raised when the user asks to create a new blank map.</summary>
    public event EventHandler? NewBlankMapRequested;

    /// <summary>Raised when the user asks to connect AI apps.</summary>
    public event EventHandler? ConnectAiAppsRequested;

    public WelcomeView()
    {
        InitializeComponent();
        NewBlankButton.IsVisible = ShowNewBlankMap;
        RebuildList(RecentList, RecentMaps, NoRecentText);
        RebuildList(DiscoveredList, DiscoveredMaps, NoDiscoveredText);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Guarded because a change can be raised before InitializeComponent has named the fields.
        if (change.Property == RecentMapsProperty && RecentList is not null)
            RebuildList(RecentList, RecentMaps, NoRecentText);
        else if (change.Property == DiscoveredMapsProperty && DiscoveredList is not null)
            RebuildList(DiscoveredList, DiscoveredMaps, NoDiscoveredText);
        else if (change.Property == ShowNewBlankMapProperty && NewBlankButton is not null)
            NewBlankButton.IsVisible = ShowNewBlankMap;
    }

    /// <summary>Fills one list host with a one-click button per path, showing the empty note when
    /// there is nothing to list. Shared by the recent and discovered lists so the row is declared
    /// once.</summary>
    private void RebuildList(StackPanel host, IReadOnlyList<string>? paths, TextBlock emptyNote)
    {
        host.Children.Clear();
        var list = paths ?? Array.Empty<string>();
        foreach (var path in list)
        {
            var captured = path;
            var button = new Button
            {
                Content = Path.GetFileName(path),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 4),
            };
            ToolTip.SetTip(button, path);
            button.Click += (_, _) => MapActivated?.Invoke(this, captured);
            host.Children.Add(button);
        }
        emptyNote.IsVisible = list.Count == 0;
    }

    /// <summary>
    /// The .w3x and .w3m files in the given folders and their immediate subfolders, sorted by file
    /// name and capped at <paramref name="max"/>. Only one level deep, so a folder with thousands
    /// of maps cannot stall the UI thread, and an unreadable or missing folder is skipped rather
    /// than thrown so the welcome screen still renders with no Warcraft III install.
    /// </summary>
    public static IReadOnlyList<string> DiscoverMaps(string? mapsDir, int max = 6)
    {
        var found = new List<string>();
        foreach (var root in CandidateMapDirs(mapsDir))
        {
            foreach (var dir in DirsShallow(root))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir))
                        if (IsMapFile(file))
                            found.Add(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // This folder cannot be walked, so it contributes nothing.
                }
            }
        }
        return found
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    private static bool IsMapFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".w3x", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".w3m", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The folders discovery looks in: the user's chosen map folder, then the default
    /// Warcraft III Maps folder under Documents.</summary>
    private static IEnumerable<string> CandidateMapDirs(string? mapsDir)
    {
        if (!string.IsNullOrWhiteSpace(mapsDir))
            yield return mapsDir;
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Warcraft III", "Maps");
    }

    /// <summary>A folder and its immediate subfolders. The subfolder list is materialized up front
    /// so an unreadable root yields nothing instead of throwing mid-iteration.</summary>
    private static IEnumerable<string> DirsShallow(string root)
    {
        yield return root;
        if (!Directory.Exists(root))
            yield break;
        string[] subdirs;
        try
        {
            subdirs = Directory.GetDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var sub in subdirs)
            yield return sub;
    }

    private void OnOpenMapClick(object? sender, RoutedEventArgs e) =>
        OpenMapRequested?.Invoke(this, EventArgs.Empty);

    private void OnNewBlankClick(object? sender, RoutedEventArgs e) =>
        NewBlankMapRequested?.Invoke(this, EventArgs.Empty);

    private void OnConnectAiClick(object? sender, RoutedEventArgs e) =>
        ConnectAiAppsRequested?.Invoke(this, EventArgs.Empty);
}
