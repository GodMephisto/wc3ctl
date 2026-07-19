using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Panels;

/// <summary>
/// Dependency graph for one object of any Object Editor kind: the panel
/// resolves its full closure (abilities, buffs, items, model, textures,
/// icons, strings) via <see cref="BundleCommand.ResolveObject"/>, then
/// renders it two ways - a layered node-link graph on a canvas and a
/// structured tree + files/strings lists beside it. Two ways in: the
/// workspace pushes the Objects tab's selection through
/// <see cref="ShowObject"/> (auto-resolving immediately), or the user picks
/// manually from the combo, which lists the current kind's map objects.
/// The selection is exposed through <see cref="SelectedRawcode"/> (any kind)
/// and <see cref="SelectedUnitRawcode"/> (units only - the port seam) with
/// <see cref="SelectionChanged"/> notifications.
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

    // Canvas node fills/borders.
    private static readonly IBrush CustomBorder = new SolidColorBrush(Color.Parse("#E8C56A"));
    private static readonly IBrush CustomFill = new SolidColorBrush(Color.Parse("#26E8C56A"));
    private static readonly IBrush BaseBorder = new SolidColorBrush(Color.Parse("#66808893"));
    private static readonly IBrush BaseFill = new SolidColorBrush(Color.Parse("#14808893"));
    private static readonly IBrush FileBorder = new SolidColorBrush(Color.Parse("#5F87B7"));
    private static readonly IBrush FileFill = new SolidColorBrush(Color.Parse("#145F87B7"));
    private static readonly IBrush MissingBorder = new SolidColorBrush(Color.Parse("#C76B6B"));
    private static readonly IBrush MissingFill = new SolidColorBrush(Color.Parse("#14C76B6B"));
    private static readonly IBrush EdgeStroke = new SolidColorBrush(Color.Parse("#55889CB0"));

    // Layered layout constants (device-independent pixels): objects sit in
    // columns by depth from the root, files in a wrapped band underneath.
    private const double Pad = 24;
    private const double ObjW = 180, ObjH = 48;
    private const double ColGap = 130;
    private const double RowGap = 28;
    private const double FileW = 250, FileH = 40;
    private const double FileGapX = 26, FileGapY = 26;
    private const double BandGap = 100;

    private MapSession? _session;
    /// <summary>Stamp that invalidates in-flight object lists / resolves when the target changes.</summary>
    private int _generation;
    /// <summary>At most one ResolveObject runs at a time (they share the MapDocument).</summary>
    private bool _resolveInFlight;
    /// <summary>Selection changed mid-resolve; run one trailing resolve when it lands.</summary>
    private bool _resolveQueued;
    /// <summary>The kind the combo's object list was (or is being) loaded for.</summary>
    private ObjectKind _listKind = ObjectKind.Unit;
    /// <summary>The document the combo's object list was (or is being) loaded from.</summary>
    private MapDocument? _listDoc;
    /// <summary>The combo's loaded options (null while a list load is in flight).</summary>
    private List<SearchableComboBoxItem>? _options;
    /// <summary>Rawcode to select-and-resolve once the in-flight object list lands.</summary>
    private string? _pendingSelect;

    public DependencyGraphView()
    {
        InitializeComponent();
        ObjectCombo.Watermark = "Search name or rawcode…";
        ObjectCombo.SelectionChanged += OnObjectPicked;
    }

    /// <summary>Kind of the object whose closure is shown (tracks the combo's list kind).</summary>
    public ObjectKind SelectedObjectKind { get; private set; } = ObjectKind.Unit;

    /// <summary>Rawcode of the object whose closure is shown, null when none.</summary>
    public string? SelectedRawcode { get; private set; }

    /// <summary>"Name (rawcode)" of the selected object, for button/tooltip text.</summary>
    public string? SelectedDisplay { get; private set; }

    /// <summary>Port seam (porting is unit-rooted): the selected rawcode when it is a
    /// unit, null for any other kind - non-unit graphs render but cannot port.</summary>
    public string? SelectedUnitRawcode =>
        SelectedObjectKind == ObjectKind.Unit ? SelectedRawcode : null;

    /// <summary>Port seam: display of the selected unit; null when a non-unit is shown.</summary>
    public string? SelectedUnitDisplay =>
        SelectedObjectKind == ObjectKind.Unit ? SelectedDisplay : null;

    /// <summary>Raised whenever the selected object changes (including cleared).</summary>
    public event EventHandler? SelectionChanged;

    public void ShowMap(MapSession session)
    {
        _session = session;
        int gen = ++_generation; // drop anything still in flight for the old map
        ClearSelection();
        ClearRendered();
        StatusText.Text = "";
        SummaryText.Text = "";
        _listDoc = null;

        if (session.Current is not { } doc)
        {
            PlaceholderText.IsVisible = true;
            ContentRoot.IsVisible = false;
            return;
        }

        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;
        LoadObjectList(doc, ObjectKind.Unit, gen);
    }

    /// <summary>
    /// Smart entry point: show <paramref name="rawcode"/>'s dependency closure,
    /// resolving immediately (off the UI thread). The workspace calls this when
    /// the Objects tab's selection should drive this panel; re-showing the object
    /// already displayed is a no-op, so tab switches never re-resolve. Fully
    /// initializes the panel - it works even before any <see cref="ShowMap"/>.
    /// </summary>
    public void ShowObject(MapSession session, ObjectKind kind, string rawcode)
    {
        _session = session;
        if (session.Current is not { } doc)
        {
            PlaceholderText.IsVisible = true;
            ContentRoot.IsVisible = false;
            return;
        }

        // Already showing (or loading toward) exactly this object: nothing to do.
        if (ReferenceEquals(doc, _listDoc) && kind == _listKind
            && (rawcode == SelectedRawcode || rawcode == _pendingSelect))
            return;

        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;

        if (!ReferenceEquals(doc, _listDoc) || kind != _listKind)
        {
            // New kind or a fresh map: reload the picker, then select + resolve.
            _pendingSelect = rawcode;
            LoadObjectList(doc, kind, ++_generation);
        }
        else if (_options is not null)
        {
            SelectAndResolve(rawcode);
        }
        else
        {
            _pendingSelect = rawcode; // list still loading for this kind; retarget it
        }
    }

    /// <summary>
    /// Populate the object picker for one kind off the UI thread - the first
    /// game-data query per install opens CASC, which can take seconds. The
    /// generation stamp drops results that land after the target changed.
    /// </summary>
    private void LoadObjectList(MapDocument doc, ObjectKind kind, int gen)
    {
        _listDoc = doc;
        _listKind = kind;
        _options = null;
        KindLabel.Text = KindSingular(kind) + ":";
        ObjectCombo.SetItems(Array.Empty<SearchableComboBoxItem>());
        var kindWord = KindPlural(kind).ToLowerInvariant();
        SummaryText.Text = $"Loading {kindWord}…";
        string? gameDir = _session?.GameDir;
        Task.Run(() =>
        {
            List<SearchableComboBoxItem>? items = null;
            string? error = null;
            try
            {
                items = ObjectListCommand.Execute(doc, kind, gameDir).Items
                    .Select(i => new SearchableComboBoxItem(i.Name ?? "", i.Rawcode))
                    .OrderBy(o => o.Display, StringComparer.OrdinalIgnoreCase)
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
                if (items is null)
                {
                    SummaryText.Text = "";
                    StatusText.Text = $"Failed to list {kindWord}: {error}";
                    return;
                }
                _options = items;
                ObjectCombo.SetItems(items, selectFirstWhenNoMatch: false);
                if (_pendingSelect is { } pending)
                {
                    // An incoming selection is waiting on this list: resolve it now.
                    _pendingSelect = null;
                    SelectAndResolve(pending);
                }
                else
                {
                    // No auto-select: resolving a closure is heavy, wait for a deliberate pick.
                    SummaryText.Text = items.Count == 0
                        ? $"This map has no custom {KindSingular(kind).ToLowerInvariant()} data."
                        : $"{items.Count} {kindWord} - pick one to analyze.";
                }
            });
        });
    }

    /// <summary>Reflect <paramref name="rawcode"/> in the picker, then auto-resolve it.</summary>
    private void SelectAndResolve(string rawcode)
    {
        var option = _options?.FirstOrDefault(o => o.Id == rawcode);
        ObjectCombo.Select(rawcode, raiseEvent: false);
        // Objects pushed from the editor are always in the list; if one ever is
        // not, resolve the bare rawcode anyway - the resolver reports diagnostics.
        UpdateSelection(rawcode, option?.Display ?? rawcode);
        RequestResolve();
    }

    /// <summary>Update the exposed (kind, rawcode, display), notifying on change.</summary>
    private void UpdateSelection(string? rawcode, string? display)
    {
        if (SelectedObjectKind == _listKind && SelectedRawcode == rawcode && SelectedDisplay == display)
            return;
        SelectedObjectKind = _listKind;
        SelectedRawcode = rawcode;
        SelectedDisplay = display;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnObjectPicked(object? sender, SearchableComboBoxItem item)
    {
        UpdateSelection(item.Id, item.Display);
        RequestResolve();
    }

    /// <summary>
    /// Resolve the selected object's closure off the UI thread with at most one
    /// resolve in flight: selections arriving mid-resolve collapse into a
    /// single trailing resolve of whatever is selected by then (mirrors the
    /// object editor's preview render pattern), so rapid switching never
    /// cross-renders and never runs two resolves against the same document.
    /// </summary>
    private void RequestResolve()
    {
        if (_session?.Current is not { } doc || SelectedRawcode is not { } rawcode)
            return;
        if (_resolveInFlight)
        {
            _resolveQueued = true;
            return;
        }
        _resolveInFlight = true;
        int gen = _generation;
        var kind = SelectedObjectKind;
        string? gameDir = _session.GameDir;
        SummaryText.Text = $"Resolving {SelectedDisplay}…";
        GraphHint.IsVisible = true;
        GraphHint.Text = $"Resolving {SelectedDisplay}…";
        Task.Run(() =>
        {
            UnitBundle? bundle = null;
            string? error = null;
            try
            {
                bundle = BundleCommand.ResolveObject(doc, kind, rawcode, gameDir);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            Dispatcher.UIThread.Post(() =>
            {
                _resolveInFlight = false;
                // A newer selection supersedes this result - re-resolve, even if
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
                    GraphHint.Text = "Resolve failed - see the status line below.";
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
            + $", {bundle.Files.Count} files, {bundle.Strings.Count} strings";
        StatusText.Text = bundle.Diagnostics.Count > 0 ? string.Join("; ", bundle.Diagnostics) : "";
        BuildTree(bundle);
        BuildFilesList(bundle);
        BuildStringsList(bundle);
        RenderGraph(bundle);
    }

    /// <summary>
    /// Structured fallback view: root object → object deps grouped by kind, each
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
        var rootKindWord = (rootNode?.Kind ?? SelectedObjectKind).ToString().ToLowerInvariant();
        var rootItem = new TreeViewItem
        {
            Header = MakeTreeLabel(
                $"{bundle.RootName ?? bundle.RootRawcode} ({bundle.RootRawcode}) - root {rootKindWord}",
                rootNode?.CustomToMap, bold: true),
            IsExpanded = true,
        };

        foreach (var group in bundle.Objects
                     .Where(o => o.Rawcode != bundle.RootRawcode)
                     .GroupBy(o => o.Kind))
        {
            var groupItem = new TreeViewItem
            {
                Header = MakeTreeLabel($"{KindPlural(group.Key)} ({group.Count()})", custom: null, bold: true),
                IsExpanded = true,
            };
            foreach (var node in group)
            {
                var via = viaInto.TryGetValue(node.Rawcode, out var v) ? $", via {v}" : "";
                groupItem.Items.Add(new TreeViewItem
                {
                    Header = MakeTreeLabel(
                        $"{node.Rawcode} - {node.Name ?? "(base game)"}"
                        + $", {(node.CustomToMap ? "custom" : "base")}{via}",
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
                    + $" - [{(file.PresentInMap ? "in map" : "not in map")}]",
                FontSize = 11,
                Foreground = file.PresentInMap ? NormalText : MissingText,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ToolTip.SetTip(row, $"{file.Path}\n{file.Category} - "
                + (file.PresentInMap
                    ? "imported in this map (ports with the bundle)"
                    : "not in this map - base-game asset or a missing import"));
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

    private static string KindPlural(ObjectKind kind) => kind switch
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

    private static string KindSingular(ObjectKind kind) => kind switch
    {
        ObjectKind.Destructable => "Destructible",
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

    /// <summary>
    /// Node-link graph with a simple deterministic layered layout: column 0 is
    /// the root unit, each further column the next BFS depth of object deps;
    /// files get their own wrapped band along the bottom. Edges are straight
    /// lines between node anchors, labeled with their field codes (parallel
    /// edges between the same pair coalesce into one labeled line).
    /// </summary>
    private void RenderGraph(UnitBundle bundle)
    {
        GraphCanvas.Children.Clear();
        if (bundle.Objects.Count == 0)
        {
            GraphHint.IsVisible = true;
            GraphHint.Text = $"{bundle.RootRawcode} resolved to nothing - see the status line.";
            return;
        }
        GraphHint.IsVisible = false;

        // --- object depth from the root (BFS over object→object edges) ---
        var byCode = bundle.Objects.ToDictionary(o => o.Rawcode, StringComparer.Ordinal);
        var adjacency = bundle.Edges
            .Where(e => byCode.ContainsKey(e.From) && byCode.ContainsKey(e.To))
            .GroupBy(e => e.From, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.To).Distinct().ToList(),
                StringComparer.Ordinal);
        var depth = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [bundle.RootRawcode] = 0,
        };
        var queue = new Queue<string>();
        queue.Enqueue(bundle.RootRawcode);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current, out var targets))
                continue;
            foreach (var to in targets)
            {
                if (depth.TryAdd(to, depth[current] + 1))
                    queue.Enqueue(to);
            }
        }
        foreach (var node in bundle.Objects) // unreachable nodes still get drawn
            depth.TryAdd(node.Rawcode, 1);

        // --- columns: one per depth, bundle order within a column, all
        //     vertically centered against the tallest column ---
        var columns = bundle.Objects
            .GroupBy(o => depth[o.Rawcode])
            .OrderBy(g => g.Key)
            .Select(g => g.ToList())
            .ToList();
        double maxColHeight = columns.Max(c => c.Count * ObjH + (c.Count - 1) * RowGap);
        var objRects = new Dictionary<string, Rect>(StringComparer.Ordinal);
        for (int ci = 0; ci < columns.Count; ci++)
        {
            double x = Pad + ci * (ObjW + ColGap);
            double colHeight = columns[ci].Count * ObjH + (columns[ci].Count - 1) * RowGap;
            double y = Pad + (maxColHeight - colHeight) / 2;
            foreach (var node in columns[ci])
            {
                objRects[node.Rawcode] = new Rect(x, y, ObjW, ObjH);
                y += ObjH + RowGap;
            }
        }

        // --- files band: wrapped rows under the object area (case-insensitive
        //     keys - WC3 paths compare case-insensitively) ---
        var fileRects = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        double objAreaWidth = columns.Count * (ObjW + ColGap) - ColGap;
        double bandTop = Pad + maxColHeight + BandGap;
        int perRow = Math.Max(3, (int)((objAreaWidth + FileGapX) / (FileW + FileGapX)));
        for (int i = 0; i < bundle.Files.Count; i++)
        {
            fileRects[bundle.Files[i].Path] = new Rect(
                Pad + i % perRow * (FileW + FileGapX),
                bandTop + i / perRow * (FileH + FileGapY),
                FileW, FileH);
        }
        if (bundle.Files.Count > 0)
        {
            int usedPerRow = Math.Min(perRow, bundle.Files.Count);
            var separator = new Border
            {
                Width = usedPerRow * (FileW + FileGapX) - FileGapX,
                Height = 1,
                Background = EdgeStroke,
            };
            Canvas.SetLeft(separator, Pad);
            Canvas.SetTop(separator, bandTop - 40);
            GraphCanvas.Children.Add(separator);
            var bandLabel = new TextBlock
            {
                Text = "Files",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = MutedText,
            };
            Canvas.SetLeft(bandLabel, Pad);
            Canvas.SetTop(bandLabel, bandTop - 34);
            GraphCanvas.Children.Add(bandLabel);
        }

        // --- edges first (nodes draw on top), parallel edges coalesced ---
        foreach (var group in bundle.Edges.GroupBy(e => (e.From, e.To)))
        {
            if (!TryGetRect(group.Key.From, objRects, fileRects, out var from, out _)
                || !TryGetRect(group.Key.To, objRects, fileRects, out var to, out var toIsFile))
            {
                continue; // endpoint we didn't lay out; resolver guarantees make this rare
            }

            Point p1, p2;
            if (toIsFile)
            {
                p1 = new Point(from.Center.X, from.Bottom);   // object → file: drop down
                p2 = new Point(to.Center.X, to.Y);
            }
            else if (to.X > from.X)
            {
                p1 = new Point(from.Right, from.Center.Y);    // deeper column: left→right
                p2 = new Point(to.X, to.Center.Y);
            }
            else if (to.X < from.X)
            {
                p1 = new Point(from.X, from.Center.Y);        // back-edge (cycle)
                p2 = new Point(to.Right, to.Center.Y);
            }
            else
            {
                p1 = from.Center;                             // same column
                p2 = to.Center;
            }

            GraphCanvas.Children.Add(new Line
            {
                StartPoint = p1,
                EndPoint = p2,
                Stroke = EdgeStroke,
                StrokeThickness = 1.25,
            });

            var viaLabel = new TextBlock
            {
                Text = string.Join(", ", group.Select(e => e.Via).Distinct()),
                FontSize = 9,
                Foreground = MutedText,
            };
            Canvas.SetLeft(viaLabel, (p1.X + p2.X) / 2 + 3);
            Canvas.SetTop(viaLabel, (p1.Y + p2.Y) / 2 - 13);
            GraphCanvas.Children.Add(viaLabel);
        }

        // --- nodes on top of the wiring ---
        foreach (var node in bundle.Objects)
        {
            var rect = objRects[node.Rawcode];
            var visual = MakeObjectNode(node, node.Rawcode == bundle.RootRawcode);
            Canvas.SetLeft(visual, rect.X);
            Canvas.SetTop(visual, rect.Y);
            GraphCanvas.Children.Add(visual);
        }
        foreach (var file in bundle.Files)
        {
            var rect = fileRects[file.Path];
            var visual = MakeFileNode(file);
            Canvas.SetLeft(visual, rect.X);
            Canvas.SetTop(visual, rect.Y);
            GraphCanvas.Children.Add(visual);
        }

        // Explicit size so the ScrollViewer can scroll; slack for edge labels.
        double right = objRects.Values.Select(r => r.Right)
            .Concat(fileRects.Values.Select(r => r.Right)).Max();
        double bottom = objRects.Values.Select(r => r.Bottom)
            .Concat(fileRects.Values.Select(r => r.Bottom)).Max();
        GraphCanvas.Width = right + Pad + 40;
        GraphCanvas.Height = bottom + Pad;
    }

    /// <summary>Edge endpoints are rawcodes (case-sensitive) or file paths (not).</summary>
    private static bool TryGetRect(
        string key,
        Dictionary<string, Rect> objRects,
        Dictionary<string, Rect> fileRects,
        out Rect rect,
        out bool isFile)
    {
        if (objRects.TryGetValue(key, out rect))
        {
            isFile = false;
            return true;
        }
        isFile = true;
        return fileRects.TryGetValue(key, out rect);
    }

    private static Border MakeObjectNode(BundleNode node, bool isRoot)
    {
        var title = new TextBlock
        {
            Text = $"{node.Rawcode}, {(node.CustomToMap ? "custom" : "base")}",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = node.CustomToMap ? AccentText : MutedText,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var subtitle = new TextBlock
        {
            Text = $"{node.Kind} - {node.Name ?? "(base game)"}",
            FontSize = 10,
            Foreground = node.CustomToMap ? NormalText : MutedText,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var visual = new Border
        {
            Width = ObjW,
            Height = ObjH,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(isRoot ? 2 : 1),
            BorderBrush = node.CustomToMap ? CustomBorder : BaseBorder,
            Background = node.CustomToMap ? CustomFill : BaseFill,
            Child = new StackPanel
            {
                Margin = new Thickness(8, 5, 8, 5),
                Spacing = 1,
                Children = { title, subtitle },
            },
        };
        ToolTip.SetTip(visual, $"{node.Rawcode} - {node.Name ?? "(unnamed)"}\n{node.Kind}, "
            + (node.CustomToMap
                ? "custom to this map (must port)"
                : "base game (already in any target)"));
        return visual;
    }

    private static Border MakeFileNode(BundleFile file)
    {
        var name = file.Path.Split('\\', '/').Last();
        var title = new TextBlock
        {
            Text = $"{CategoryPrefix(file.Category)} {name}",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = file.PresentInMap ? NormalText : MissingText,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var subtitle = new TextBlock
        {
            Text = file.PresentInMap ? "in map" : "not in map",
            FontSize = 9,
            Foreground = MutedText,
        };
        var visual = new Border
        {
            Width = FileW,
            Height = FileH,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = file.PresentInMap ? FileBorder : MissingBorder,
            Background = file.PresentInMap ? FileFill : MissingFill,
            Child = new StackPanel
            {
                Margin = new Thickness(8, 4, 8, 4),
                Children = { title, subtitle },
            },
        };
        ToolTip.SetTip(visual, $"{file.Path}\n{file.Category} - "
            + (file.PresentInMap
                ? "imported in this map (ports with the bundle)"
                : "not in this map - base-game asset or a missing import"));
        return visual;
    }

    /// <summary>Reset the picker and the exposed selection (notifying the workspace).</summary>
    private void ClearSelection()
    {
        ObjectCombo.SetItems(Array.Empty<SearchableComboBoxItem>());
        _options = null;
        _pendingSelect = null;
        if (SelectedRawcode is not null || SelectedDisplay is not null)
        {
            SelectedRawcode = null;
            SelectedDisplay = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Drop everything rendered for the previous object/map.</summary>
    private void ClearRendered()
    {
        GraphCanvas.Children.Clear();
        GraphCanvas.Width = 0;
        GraphCanvas.Height = 0;
        GraphHint.IsVisible = true;
        GraphHint.Text = "Pick an object to see its dependency graph.";
        DepTree.Items.Clear();
        FilesList.Children.Clear();
        StringsList.Children.Clear();
        FilesExpander.Header = "Files (0)";
        StringsExpander.Header = "Strings (0)";
        FilesExpander.IsExpanded = false;
        StringsExpander.IsExpanded = false;
    }

}
