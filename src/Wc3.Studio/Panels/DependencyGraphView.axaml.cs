using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
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
/// renders it two ways - a layered node-link graph on an interactive canvas
/// (drag empty space to pan, wheel to zoom about the cursor, drag nodes to
/// rearrange - edges follow) and a structured tree + files/strings lists
/// beside it. Two ways in: the
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

    // Canvas interaction (tldraw-style): the canvas carries scale-then-translate
    // render transforms; wheel zooms about the cursor, dragging empty space pans,
    // dragging a node repositions it (its edges re-anchor live).
    private const double MinScale = 0.2, MaxScale = 3.0;
    private const double WheelZoomStep = 1.1, ButtonZoomStep = 1.25;
    private readonly ScaleTransform _zoomTransform = new();
    private readonly TranslateTransform _panTransform = new();
    /// <summary>Rendered edges keyed by their endpoint visuals, so a node drag can
    /// re-anchor just the lines/labels touching that node.</summary>
    private readonly List<GraphEdge> _edges = new();
    /// <summary>Node being dragged, null while panning or idle.</summary>
    private Border? _dragNode;
    /// <summary>True while a press on empty canvas space is panning the view.</summary>
    private bool _panning;
    /// <summary>Pointer position at press, viewport coordinates (shared by pan and drag).</summary>
    private Point _pressPoint;
    /// <summary>Translate (pan) or node Canvas.Left/Top (drag) at press.</summary>
    private Point _pressOrigin;

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

        // Zoom about the top-left so viewport = canvas * scale + translate holds
        // exactly (the default origin re-centres the scale about the middle).
        GraphCanvas.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        GraphCanvas.RenderTransform = new TransformGroup
        {
            Children = { _zoomTransform, _panTransform },
        };
        GraphViewport.PointerPressed += OnViewportPointerPressed;
        GraphViewport.PointerMoved += OnViewportPointerMoved;
        GraphViewport.PointerReleased += OnViewportPointerReleased;
        GraphViewport.PointerCaptureLost += (_, _) => { _dragNode = null; _panning = false; };
        GraphViewport.PointerWheelChanged += OnViewportWheel;
        ZoomInButton.Click += (_, _) => ZoomAt(ViewportCenter(), ButtonZoomStep);
        ZoomOutButton.Click += (_, _) => ZoomAt(ViewportCenter(), 1 / ButtonZoomStep);
        ZoomFitButton.Click += (_, _) => FitView();
        ZoomResetButton.Click += (_, _) => ResetView();
    }

    // --- canvas interaction: pan / zoom / node drag ---

    /// <summary>A press that no node claimed (nodes mark theirs handled): start a pan.</summary>
    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(GraphViewport).Properties.IsLeftButtonPressed)
            return;
        _panning = true;
        _pressPoint = e.GetPosition(GraphViewport);
        _pressOrigin = new Point(_panTransform.X, _panTransform.Y);
        e.Pointer.Capture(GraphViewport);
    }

    /// <summary>A press on a node Border: start dragging that node instead of panning.</summary>
    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border node
            || !e.GetCurrentPoint(GraphViewport).Properties.IsLeftButtonPressed)
        {
            return;
        }
        _dragNode = node;
        _pressPoint = e.GetPosition(GraphViewport);
        _pressOrigin = new Point(Canvas.GetLeft(node), Canvas.GetTop(node));
        // Capture to the viewport so its Moved/Released handlers drive the drag.
        e.Pointer.Capture(GraphViewport);
        e.Handled = true; // don't let the viewport treat this press as a pan
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        var delta = e.GetPosition(GraphViewport) - _pressPoint;
        if (_dragNode is { } node)
        {
            // Viewport delta → canvas delta: divide out the zoom (pan cancels).
            double scale = _zoomTransform.ScaleX;
            Canvas.SetLeft(node, _pressOrigin.X + delta.X / scale);
            Canvas.SetTop(node, _pressOrigin.Y + delta.Y / scale);
            foreach (var edge in _edges)
            {
                if (ReferenceEquals(edge.From, node) || ReferenceEquals(edge.To, node))
                    PositionEdge(edge);
            }
        }
        else if (_panning)
        {
            _panTransform.X = _pressOrigin.X + delta.X;
            _panTransform.Y = _pressOrigin.Y + delta.Y;
        }
    }

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragNode = null;
        _panning = false;
        if (ReferenceEquals(e.Pointer.Captured, GraphViewport))
            e.Pointer.Capture(null);
    }

    /// <summary>Wheel (plain or Ctrl) zooms about the cursor.</summary>
    private void OnViewportWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0)
            return;
        ZoomAt(e.GetPosition(GraphViewport),
            e.Delta.Y > 0 ? WheelZoomStep : 1 / WheelZoomStep);
        e.Handled = true;
    }

    /// <summary>
    /// Scale by <paramref name="factor"/> (clamped) keeping the canvas point under
    /// <paramref name="viewportPoint"/> fixed: with viewport = canvas * s + t, the
    /// new translate is t' = p - (p - t) * s'/s.
    /// </summary>
    private void ZoomAt(Point viewportPoint, double factor)
    {
        double oldScale = _zoomTransform.ScaleX;
        double newScale = Math.Clamp(oldScale * factor, MinScale, MaxScale);
        if (Math.Abs(newScale - oldScale) < 0.0001)
            return;
        double ratio = newScale / oldScale;
        _panTransform.X = viewportPoint.X - (viewportPoint.X - _panTransform.X) * ratio;
        _panTransform.Y = viewportPoint.Y - (viewportPoint.Y - _panTransform.Y) * ratio;
        _zoomTransform.ScaleX = newScale;
        _zoomTransform.ScaleY = newScale;
        UpdateZoomLabel();
    }

    /// <summary>Scale-to-fit the whole graph, centered in the viewport.</summary>
    private void FitView()
    {
        double w = GraphCanvas.Width, h = GraphCanvas.Height;
        var viewport = GraphViewport.Bounds;
        // Positive-form guard: Width/Height are NaN before the first render.
        if (!(w > 0 && h > 0 && viewport.Width > 0 && viewport.Height > 0))
            return;
        double scale = Math.Clamp(
            Math.Min(viewport.Width / w, viewport.Height / h), MinScale, MaxScale);
        _zoomTransform.ScaleX = scale;
        _zoomTransform.ScaleY = scale;
        _panTransform.X = (viewport.Width - w * scale) / 2;
        _panTransform.Y = (viewport.Height - h * scale) / 2;
        UpdateZoomLabel();
    }

    /// <summary>100% zoom, canvas origin back at the viewport's top-left.</summary>
    private void ResetView()
    {
        _zoomTransform.ScaleX = 1;
        _zoomTransform.ScaleY = 1;
        _panTransform.X = 0;
        _panTransform.Y = 0;
        UpdateZoomLabel();
    }

    private void UpdateZoomLabel() =>
        ZoomLabel.Text = $"{_zoomTransform.ScaleX * 100:F0}%";

    private Point ViewportCenter() =>
        new(GraphViewport.Bounds.Width / 2, GraphViewport.Bounds.Height / 2);

    /// <summary>Kind of the object whose closure is shown (tracks the combo's list kind).</summary>
    public ObjectKind SelectedObjectKind { get; private set; } = ObjectKind.Unit;

    /// <summary>Rawcode of the object whose closure is shown, null when none.</summary>
    public string? SelectedRawcode { get; private set; }

    /// <summary>"Name · kind (rawcode)" of the selected object, for button/tooltip text.</summary>
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
        // Rows read "Name · kind (rawcode)" so the picker always says what it lists.
        var kindTag = " · " + KindSingular(kind).ToLowerInvariant();
        Task.Run(() =>
        {
            List<SearchableComboBoxItem>? items = null;
            string? error = null;
            try
            {
                items = ObjectListCommand.Execute(doc, kind, gameDir).Items
                    .Select(i => new SearchableComboBoxItem(
                        (string.IsNullOrWhiteSpace(i.Name) ? i.Rawcode : i.Name) + kindTag,
                        i.Rawcode))
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
        int deps = CleanReachableFiles(bundle).Count;
        int portAssets = bundle.Files.Count - deps;
        SummaryText.Text =
            $"{bundle.Objects.Count} objects ({custom} custom / {bundle.Objects.Count - custom} base)"
            + $", {deps} files"
            + (portAssets > 0 ? $" (+{portAssets} port assets)" : "")
            + $", {bundle.Strings.Count} strings";
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
        PortAssetsList.Children.Clear();

        // Split the bundle's files. A file is a real object dependency when it is reachable from
        // the root WITHOUT crossing a trigger-script edge (object fields, their models, and those
        // models' textures). Files reachable only through "script" edges are the hero's
        // trigger-driven skill effects, they belong to a PORT but are noise when browsing, so they
        // go under a separate, collapsed "Port assets" group instead of burying the real list.
        var objectDeps = CleanReachableFiles(bundle);
        var deps = bundle.Files.Where(f => objectDeps.Contains(f.Path)).ToList();
        var portAssets = bundle.Files.Where(f => !objectDeps.Contains(f.Path)).ToList();

        FilesExpander.Header = $"Files ({deps.Count})";
        FilesExpander.IsExpanded = deps.Count > 0;
        foreach (var file in deps) FilesList.Children.Add(FileRow(file));

        PortAssetsExpander.Header = $"Port assets ({portAssets.Count})";
        PortAssetsExpander.IsExpanded = false;
        PortAssetsExpander.IsVisible = portAssets.Count > 0;
        foreach (var file in portAssets) PortAssetsList.Children.Add(FileRow(file));
    }

    private Control FileRow(BundleFile file)
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
        return row;
    }

    /// <summary>Files reachable from the root without crossing a trigger-script edge, the object's
    /// real field dependencies (models, textures, icons) as opposed to its trigger-carried assets.</summary>
    private static HashSet<string> CleanReachableFiles(UnitBundle bundle)
    {
        var filePaths = bundle.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var adjacency = new Dictionary<string, List<(string To, string Via)>>(StringComparer.Ordinal);
        foreach (var e in bundle.Edges)
        {
            if (!adjacency.TryGetValue(e.From, out var list)) adjacency[e.From] = list = new();
            list.Add((e.To, e.Via));
        }

        var clean = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { bundle.RootRawcode };
        var stack = new Stack<string>();
        stack.Push(bundle.RootRawcode);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!adjacency.TryGetValue(node, out var outs)) continue;
            foreach (var (to, via) in outs)
            {
                if (via == "script") continue; // trigger-carried assets are not field dependencies
                if (seen.Add(to)) stack.Push(to);
                if (filePaths.Contains(to)) clean.Add(to);
            }
        }
        return clean;
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
    /// edges between the same pair coalesce into one labeled line). The layout
    /// is only the starting arrangement - nodes drag freely afterwards, with
    /// <see cref="PositionEdge"/> re-anchoring their edges live.
    /// </summary>
    private void RenderGraph(UnitBundle bundle)
    {
        GraphCanvas.Children.Clear();
        _edges.Clear();
        _dragNode = null;
        _panning = false;
        ResetView(); // a fresh graph starts at 100%, origin top-left
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
        // Only the object's real dependencies appear as file nodes. The trigger-carried
        // "port assets" are listed separately in the panel and would just swamp the graph.
        var objectDeps = CleanReachableFiles(bundle);
        var depFiles = bundle.Files.Where(f => objectDeps.Contains(f.Path)).ToList();

        var fileRects = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        double objAreaWidth = columns.Count * (ObjW + ColGap) - ColGap;
        double bandTop = Pad + maxColHeight + BandGap;
        int perRow = Math.Max(3, (int)((objAreaWidth + FileGapX) / (FileW + FileGapX)));
        for (int i = 0; i < depFiles.Count; i++)
        {
            fileRects[depFiles[i].Path] = new Rect(
                Pad + i % perRow * (FileW + FileGapX),
                bandTop + i / perRow * (FileH + FileGapY),
                FileW, FileH);
        }
        if (depFiles.Count > 0)
        {
            int usedPerRow = Math.Min(perRow, depFiles.Count);
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

        // --- node visuals: created and positioned first so edges can anchor to
        //     them, but added to the canvas after the edges (nodes draw on top) ---
        var objVisuals = new Dictionary<string, Border>(StringComparer.Ordinal);
        foreach (var node in bundle.Objects)
        {
            var rect = objRects[node.Rawcode];
            var visual = MakeObjectNode(node, node.Rawcode == bundle.RootRawcode);
            Canvas.SetLeft(visual, rect.X);
            Canvas.SetTop(visual, rect.Y);
            MakeDraggable(visual);
            objVisuals[node.Rawcode] = visual;
        }
        var fileVisuals = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in depFiles)
        {
            var rect = fileRects[file.Path];
            var visual = MakeFileNode(file);
            Canvas.SetLeft(visual, rect.X);
            Canvas.SetTop(visual, rect.Y);
            MakeDraggable(visual);
            fileVisuals[file.Path] = visual;
        }

        // --- edges (below the nodes), parallel edges coalesced ---
        foreach (var group in bundle.Edges.GroupBy(e => (e.From, e.To)))
        {
            if (!TryGetVisual(group.Key.From, objVisuals, fileVisuals, out var from, out _)
                || !TryGetVisual(group.Key.To, objVisuals, fileVisuals, out var to, out var toIsFile))
            {
                continue; // endpoint we didn't lay out; resolver guarantees make this rare
            }

            var edge = new GraphEdge(
                from, to, toIsFile,
                new Line { Stroke = EdgeStroke, StrokeThickness = 1.25 },
                new TextBlock
                {
                    Text = string.Join(", ", group.Select(e => e.Via).Distinct()),
                    FontSize = 9,
                    Foreground = MutedText,
                });
            PositionEdge(edge);
            _edges.Add(edge);
            GraphCanvas.Children.Add(edge.Line);
            GraphCanvas.Children.Add(edge.Label);
        }

        // --- nodes on top of the wiring ---
        foreach (var node in bundle.Objects)
            GraphCanvas.Children.Add(objVisuals[node.Rawcode]);
        foreach (var file in depFiles)
            GraphCanvas.Children.Add(fileVisuals[file.Path]);

        // Explicit size = the graph's extent, which Fit scales into the viewport;
        // slack for edge labels.
        double right = objRects.Values.Select(r => r.Right)
            .Concat(fileRects.Values.Select(r => r.Right)).Max();
        double bottom = objRects.Values.Select(r => r.Bottom)
            .Concat(fileRects.Values.Select(r => r.Bottom)).Max();
        GraphCanvas.Width = right + Pad + 40;
        GraphCanvas.Height = bottom + Pad;
    }

    /// <summary>One rendered edge: live endpoint visuals plus the line/label drawn
    /// for it, so a node drag re-anchors exactly the edges touching that node.</summary>
    private sealed record GraphEdge(Border From, Border To, bool ToIsFile, Line Line, TextBlock Label);

    /// <summary>Edge endpoints are rawcodes (case-sensitive) or file paths (not).</summary>
    private static bool TryGetVisual(
        string key,
        Dictionary<string, Border> objVisuals,
        Dictionary<string, Border> fileVisuals,
        out Border visual,
        out bool isFile)
    {
        if (objVisuals.TryGetValue(key, out visual!))
        {
            isFile = false;
            return true;
        }
        isFile = true;
        return fileVisuals.TryGetValue(key, out visual!);
    }

    /// <summary>A node's current canvas rect (nodes have explicit sizes).</summary>
    private static Rect VisualRect(Border visual) =>
        new(Canvas.GetLeft(visual), Canvas.GetTop(visual), visual.Width, visual.Height);

    /// <summary>Wire a node visual for repositioning by drag.</summary>
    private void MakeDraggable(Border visual)
    {
        visual.Cursor = new Cursor(StandardCursorType.SizeAll);
        visual.PointerPressed += OnNodePointerPressed;
    }

    /// <summary>
    /// (Re)anchor an edge's line and label to its endpoints' current rects:
    /// object → file drops from the bottom edge; otherwise the line leaves the
    /// side facing the target (falling back to centers in the same column).
    /// </summary>
    private static void PositionEdge(GraphEdge edge)
    {
        var from = VisualRect(edge.From);
        var to = VisualRect(edge.To);

        Point p1, p2;
        if (edge.ToIsFile)
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

        edge.Line.StartPoint = p1;
        edge.Line.EndPoint = p2;
        Canvas.SetLeft(edge.Label, (p1.X + p2.X) / 2 + 3);
        Canvas.SetTop(edge.Label, (p1.Y + p2.Y) / 2 - 13);
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
        _edges.Clear();
        _dragNode = null;
        _panning = false;
        ResetView();
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
