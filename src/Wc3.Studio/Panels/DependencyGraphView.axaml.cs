using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Studio.Panels;

/// <summary>
/// Dependency graph for one unit: pick a map unit and the panel resolves its
/// full closure (abilities, buffs, items, model, textures, icons, strings)
/// via <see cref="BundleCommand"/>, then renders it two ways — a layered
/// node-link graph on a canvas and a structured tree + files/strings lists
/// beside it. Read-only this wave; the selected unit is exposed through
/// <see cref="SelectedUnitRawcode"/>/<see cref="SelectionChanged"/> as the
/// seam a later wave's port button hangs off.
/// </summary>
public partial class DependencyGraphView : UserControl, IMapPanel
{
    // Palette shared by the canvas and the detail lists: gold accent = custom
    // to this map (matches the object editor's modified-field highlight),
    // grey = base game, steel blue = file assets, red tint = not in the map.
    private static readonly IBrush AccentText = new SolidColorBrush(Color.Parse("#E8C56A"));
    private static readonly IBrush NormalText = new SolidColorBrush(Color.Parse("#C8CDD3"));
    private static readonly IBrush MutedText = new SolidColorBrush(Color.Parse("#8FA3B8"));
    private static readonly IBrush MissingText = new SolidColorBrush(Color.Parse("#D98C8C"));

    private MapSession? _session;
    private bool _suppress;
    /// <summary>Stamp that invalidates in-flight unit lists / resolves when the map changes.</summary>
    private int _generation;
    /// <summary>At most one ResolveUnit runs at a time (they share the MapDocument).</summary>
    private bool _resolveInFlight;
    /// <summary>Selection changed mid-resolve; run one trailing resolve when it lands.</summary>
    private bool _resolveQueued;

    public DependencyGraphView()
    {
        InitializeComponent();
    }

    /// <summary>Port seam: rawcode of the unit whose closure is shown, null when none.</summary>
    public string? SelectedUnitRawcode { get; private set; }

    /// <summary>Port seam: "Name (rawcode)" of the selected unit, for button/tooltip text.</summary>
    public string? SelectedUnitDisplay { get; private set; }

    /// <summary>Port seam: raised whenever the selected unit changes (including cleared).</summary>
    public event EventHandler? SelectionChanged;

    public void ShowMap(MapSession session)
    {
        _session = session;
        int gen = ++_generation; // drop anything still in flight for the old map
        ClearSelection();
        ClearRendered();
        StatusText.Text = "";
        SummaryText.Text = "";

        if (session.Current is not { } doc)
        {
            PlaceholderText.IsVisible = true;
            ContentRoot.IsVisible = false;
            return;
        }

        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;
        LoadUnitList(doc, session.GameDir, gen);
    }

    /// <summary>
    /// Populate the unit picker off the UI thread — the first game-data query
    /// per install opens CASC, which can take seconds. The generation stamp
    /// drops results that land after another map was shown.
    /// </summary>
    private void LoadUnitList(MapDocument doc, string? gameDir, int gen)
    {
        SummaryText.Text = "Loading units…";
        Task.Run(() =>
        {
            List<UnitOption>? units = null;
            string? error = null;
            try
            {
                units = ObjectListCommand.Execute(doc, ObjectKind.Unit, gameDir).Items
                    .Select(i => new UnitOption(
                        i.Rawcode, i.Name is null ? i.Rawcode : $"{i.Name} ({i.Rawcode})"))
                    .OrderBy(u => u.Display, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (gen != _generation)
                    return;
                if (units is null)
                {
                    SummaryText.Text = "";
                    StatusText.Text = $"Failed to list units: {error}";
                    return;
                }
                _suppress = true;
                UnitCombo.ItemsSource = units;
                _suppress = false;
                // No auto-select: resolving a closure is heavy, wait for a deliberate pick.
                SummaryText.Text = units.Count == 0
                    ? "This map has no custom unit data."
                    : $"{units.Count} unit(s) — pick one to analyze.";
            });
        });
    }

    private void OnUnitChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress)
            return;
        var selected = UnitCombo.SelectedItem as UnitOption;
        SelectedUnitRawcode = selected?.Rawcode;
        SelectedUnitDisplay = selected?.Display;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        if (selected is null)
            return;
        RequestResolve();
    }

    /// <summary>
    /// Resolve the selected unit's closure off the UI thread with at most one
    /// resolve in flight: selections arriving mid-resolve collapse into a
    /// single trailing resolve of whatever is selected by then (mirrors the
    /// object editor's preview render pattern), so rapid switching never
    /// cross-renders and never runs two resolves against the same document.
    /// </summary>
    private void RequestResolve()
    {
        if (_session?.Current is not { } doc || SelectedUnitRawcode is not { } rawcode)
            return;
        if (_resolveInFlight)
        {
            _resolveQueued = true;
            return;
        }
        _resolveInFlight = true;
        int gen = _generation;
        string? gameDir = _session.GameDir;
        SummaryText.Text = $"Resolving {SelectedUnitDisplay}…";
        GraphHint.IsVisible = true;
        GraphHint.Text = $"Resolving {SelectedUnitDisplay}…";
        Task.Run(() =>
        {
            UnitBundle? bundle = null;
            string? error = null;
            try
            {
                bundle = BundleCommand.ResolveUnit(doc, rawcode, gameDir);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            Dispatcher.UIThread.Post(() =>
            {
                _resolveInFlight = false;
                // A newer selection supersedes this result — re-resolve, even if
                // the map changed underneath (RequestResolve re-reads everything).
                if (_resolveQueued)
                {
                    _resolveQueued = false;
                    RequestResolve();
                    return;
                }
                if (gen != _generation)
                    return;
                if (bundle is null)
                {
                    SummaryText.Text = "";
                    GraphHint.Text = "Resolve failed — see the status line below.";
                    StatusText.Text = $"Failed to resolve {rawcode}: {error}";
                    return;
                }
                RenderBundle(bundle);
            });
        });
    }

    /// <summary>Render a freshly resolved closure into every view of this panel.</summary>
    private void RenderBundle(UnitBundle bundle)
    {
        int custom = bundle.Objects.Count(o => o.CustomToMap);
        SummaryText.Text =
            $"{bundle.Objects.Count} objects ({custom} custom / {bundle.Objects.Count - custom} base)"
            + $" · {bundle.Files.Count} files · {bundle.Strings.Count} strings";
        StatusText.Text = bundle.Diagnostics.Count > 0 ? string.Join("; ", bundle.Diagnostics) : "";
        BuildTree(bundle);
        BuildFilesList(bundle);
        BuildStringsList(bundle);
        RenderGraph(bundle);
    }

    /// <summary>
    /// Structured fallback view: root unit → object deps grouped by kind, each
    /// with a custom/base badge and the field codes ("via") that pull it in.
    /// </summary>
    private void BuildTree(UnitBundle bundle)
    {
        DepTree.Items.Clear();

        // Field codes referencing each node, e.g. "via uhab" on an ability.
        var viaInto = bundle.Edges
            .GroupBy(e => e.To, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g.Select(e => e.Via).Distinct()),
                StringComparer.Ordinal);

        var rootNode = bundle.Objects.FirstOrDefault(o => o.Rawcode == bundle.RootRawcode);
        var rootItem = new TreeViewItem
        {
            Header = MakeTreeLabel(
                $"{bundle.RootName ?? bundle.RootRawcode} ({bundle.RootRawcode}) — root unit",
                rootNode?.CustomToMap, bold: true),
            IsExpanded = true,
        };

        foreach (var group in bundle.Objects
                     .Where(o => o.Rawcode != bundle.RootRawcode)
                     .GroupBy(o => o.Kind))
        {
            var groupItem = new TreeViewItem
            {
                Header = MakeTreeLabel($"{KindLabel(group.Key)} ({group.Count()})", custom: null, bold: true),
                IsExpanded = true,
            };
            foreach (var node in group)
            {
                var via = viaInto.TryGetValue(node.Rawcode, out var v) ? $" · via {v}" : "";
                groupItem.Items.Add(new TreeViewItem
                {
                    Header = MakeTreeLabel(
                        $"{node.Rawcode} — {node.Name ?? "(base game)"}"
                        + $" · {(node.CustomToMap ? "custom" : "base")}{via}",
                        node.CustomToMap, bold: false),
                });
            }
            rootItem.Items.Add(groupItem);
        }

        DepTree.Items.Add(rootItem);
    }

    private void BuildFilesList(UnitBundle bundle)
    {
        FilesList.Children.Clear();
        FilesExpander.Header = $"Files ({bundle.Files.Count})";
        FilesExpander.IsExpanded = bundle.Files.Count > 0;
        foreach (var file in bundle.Files)
        {
            var row = new TextBlock
            {
                Text = $"{CategoryPrefix(file.Category)} {file.Path}"
                    + $" — [{(file.PresentInMap ? "in map" : "not in map")}]",
                FontSize = 11,
                Foreground = file.PresentInMap ? NormalText : MissingText,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ToolTip.SetTip(row, $"{file.Path}\n{file.Category} — "
                + (file.PresentInMap
                    ? "imported in this map (ports with the unit)"
                    : "not in this map — base-game asset or a missing import"));
            FilesList.Children.Add(row);
        }
    }

    private void BuildStringsList(UnitBundle bundle)
    {
        StringsList.Children.Clear();
        StringsExpander.Header = $"Strings ({bundle.Strings.Count})";
        StringsExpander.IsExpanded = false; // usually the longest list; opt-in
        foreach (var s in bundle.Strings)
        {
            var row = new TextBlock
            {
                Text = s,
                FontSize = 11,
                Foreground = NormalText,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ToolTip.SetTip(row, s);
            StringsList.Children.Add(row);
        }
    }

    private static TextBlock MakeTreeLabel(string text, bool? custom, bool bold)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            Foreground = custom switch
            {
                true => AccentText,
                false => MutedText,
                null => NormalText,
            },
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTip.SetTip(label, text);
        return label;
    }

    private static string KindLabel(ObjectKind kind) => kind switch
    {
        ObjectKind.Unit => "Units",
        ObjectKind.Item => "Items",
        ObjectKind.Ability => "Abilities",
        ObjectKind.Destructable => "Destructibles",
        ObjectKind.Doodad => "Doodads",
        ObjectKind.Buff => "Buffs",
        ObjectKind.Upgrade => "Upgrades",
        _ => kind.ToString(),
    };

    private static string CategoryPrefix(string category) => category switch
    {
        "model" => "[model]",
        "texture" => "[tex]",
        "icon" => "[icon]",
        "sound" => "[snd]",
        _ => "[file]",
    };

    private void RenderGraph(UnitBundle bundle)
    {
        GraphCanvas.Children.Clear();
    }

    /// <summary>Reset the picker and the exposed selection (notifying the workspace).</summary>
    private void ClearSelection()
    {
        _suppress = true;
        UnitCombo.ItemsSource = null;
        _suppress = false;
        if (SelectedUnitRawcode is not null || SelectedUnitDisplay is not null)
        {
            SelectedUnitRawcode = null;
            SelectedUnitDisplay = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Drop everything rendered for the previous unit/map.</summary>
    private void ClearRendered()
    {
        GraphCanvas.Children.Clear();
        GraphCanvas.Width = 0;
        GraphCanvas.Height = 0;
        GraphHint.IsVisible = true;
        GraphHint.Text = "Pick a unit to see its dependency graph.";
        DepTree.Items.Clear();
        FilesList.Children.Clear();
        StringsList.Children.Clear();
        FilesExpander.Header = "Files (0)";
        StringsExpander.Header = "Strings (0)";
        FilesExpander.IsExpanded = false;
        StringsExpander.IsExpanded = false;
    }

    /// <summary>Unit picker entry; ComboBox renders ToString.</summary>
    private sealed record UnitOption(string Rawcode, string Display)
    {
        public override string ToString() => Display;
    }
}
