using System.Text.RegularExpressions;
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
    // A node the user excluded from the port (left click toggles it) renders greyed out
    // with struck-through text, distinct from every other state above (custom/base/file/
    // missing all still read at a glance, excluded reads as "not part of this port").
    private static readonly IBrush ExcludedBorder = new SolidColorBrush(Color.Parse("#8F6B6B6B"));
    private static readonly IBrush ExcludedFill = new SolidColorBrush(Color.Parse("#146B6B6B"));
    private const double ExcludedOpacity = 0.45;

    // Layered layout constants (device independent pixels). Objects sit in
    // rows by depth from the root, files in a wrapped band underneath.
    private const double Pad = 24;
    private const double ObjW = 180, ObjH = 48;
    private const double SiblingGapX = 130;
    private const double LevelGapY = 28;
    private const double FileW = 250, FileH = 40;
    private const double FileGapX = 26, FileGapY = 26;
    private const double BandGap = 100;

    // Canvas interaction (tldraw-style): the canvas carries scale-then-translate
    // render transforms; wheel zooms about the cursor, dragging empty space pans,
    // dragging a node repositions it (its edges re-anchor live).
    private const double MinScale = 0.2, MaxScale = 3.0;
    private const double WheelZoomStep = 1.1, ButtonZoomStep = 1.25;
    /// <summary>A press that moves less than this many pixels before release is a click
    /// (toggles the node's excluded state), not a drag.</summary>
    private const double ClickMoveThreshold = 3;
    private readonly ScaleTransform _zoomTransform = new();
    private readonly TranslateTransform _panTransform = new();
    /// <summary>Rendered edges keyed by their endpoint visuals, so a node drag can
    /// re-anchor just the lines/labels touching that node.</summary>
    private readonly List<GraphEdge> _edges = new();
    /// <summary>Object node visuals keyed by rawcode, kept across the graph's lifetime so
    /// a click can restyle exactly one node (exclude/include) without a full re-layout,
    /// which would also throw away any manual dragging the user had done.</summary>
    private readonly Dictionary<string, Border> _objVisuals = new(StringComparer.Ordinal);
    /// <summary>File node visuals keyed by path (case-insensitive, WC3 paths compare that
    /// way), same reasoning as <see cref="_objVisuals"/>.</summary>
    private readonly Dictionary<string, Border> _fileVisuals = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Rawcodes/paths the user has excluded from the port for the bundle currently
    /// shown (left click on a node toggles membership). Cleared on every fresh resolve.
    /// The root itself can never be a member, it is the object being ported.</summary>
    private readonly HashSet<string> _excluded = new(StringComparer.Ordinal);
    /// <summary>Node being dragged, null while panning or idle.</summary>
    private Border? _dragNode;
    /// <summary>True once the current press has moved past <see cref="ClickMoveThreshold"/>,
    /// which marks it a drag rather than a toggle-click.</summary>
    private bool _dragMoved;
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

    /// <summary>The last resolved closure, kept so the hide toggle can rebuild the tree and
    /// graph without resolving again. Null when nothing is rendered.</summary>
    private UnitBundle? _lastBundle;

    /// <summary>View only. True hides objects carried only by the script closure from the
    /// tree and the graph. It never affects the bundle or the port exclusion set.</summary>
    private bool _hideCarried = true;

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
        HideCarriedCheck.IsCheckedChanged += (_, _) => OnHideCarriedToggled();
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
        _dragMoved = false;
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
            if (!_dragMoved && delta.X * delta.X + delta.Y * delta.Y > ClickMoveThreshold * ClickMoveThreshold)
                _dragMoved = true;
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

    /// <summary>A press that never moved past the click threshold is a plain left click,
    /// not a drag, toggle that node's excluded state rather than leaving it where it was
    /// (which a drag of zero distance would do anyway, so this only ever adds behaviour).</summary>
    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragNode is { } node && !_dragMoved)
            ToggleExcluded(node);
        _dragNode = null;
        _panning = false;
        if (ReferenceEquals(e.Pointer.Captured, GraphViewport))
            e.Pointer.Capture(null);
    }

    /// <summary>Left click on a node flips whether it is excluded from the port (default
    /// included). The root cannot be excluded, it is the object being ported, clicking it
    /// is a no-op. Restyles just this one node, everything else on the canvas is untouched.</summary>
    private void ToggleExcluded(Border node)
    {
        string? key = node.Tag switch
        {
            BundleNode n => n.Rawcode,
            BundleFile f => f.Path,
            _ => null,
        };
        if (key is null || key == SelectedRawcode)
            return;
        if (!_excluded.Remove(key))
            _excluded.Add(key);
        RestyleNode(node);
        ExclusionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>(Re)applies the excluded-or-not look to one node border, driven entirely by
    /// its Tag (the BundleNode or BundleFile it represents) and current membership in
    /// <see cref="_excluded"/>, so this is idempotent and safe to call any time.</summary>
    private void RestyleNode(Border visual)
    {
        (IBrush border, IBrush fill, string tip, bool excluded) = visual.Tag switch
        {
            BundleNode n when _excluded.Contains(n.Rawcode) =>
                (ExcludedBorder, ExcludedFill,
                    $"{n.Rawcode}, {n.Name ?? "(unnamed)"}\n{n.Kind}, EXCLUDED, left click to include it in the port again",
                    true),
            BundleNode n =>
                (n.CustomToMap ? CustomBorder : BaseBorder, n.CustomToMap ? CustomFill : BaseFill,
                    $"{n.Rawcode}, {n.Name ?? "(unnamed)"}\n{n.Kind}, "
                    + (n.CustomToMap ? "custom to this map (must port)" : "base game (already in any target)")
                    + "\nleft click to exclude it from the port",
                    false),
            BundleFile f when _excluded.Contains(f.Path) =>
                (ExcludedBorder, ExcludedFill,
                    $"{f.Path}\n{f.Category}, EXCLUDED, left click to include it in the port again", true),
            BundleFile f =>
                (f.PresentInMap ? FileBorder : MissingBorder, f.PresentInMap ? FileFill : MissingFill,
                    $"{f.Path}\n{f.Category}, "
                    + (f.PresentInMap
                        ? "imported in this map (ports with the bundle)"
                        : "not in this map, a base game asset or a missing import")
                    + "\nleft click to exclude it from the port",
                    false),
            _ => (BaseBorder, BaseFill, "", false),
        };
        visual.BorderBrush = border;
        visual.Background = fill;
        visual.Opacity = excluded ? ExcludedOpacity : 1.0;
        ToolTip.SetTip(visual, tip);
        if (visual.Child is StackPanel { Children.Count: > 0 } stack && stack.Children[0] is TextBlock title)
            title.TextDecorations = excluded ? TextDecorations.Strikethrough : null;
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

    /// <summary>Rawcodes/file paths the user has excluded from the port via a left click
    /// on this graph's nodes (default is everything included, an empty set). Scoped to
    /// whatever bundle is currently shown, a fresh resolve clears it. The workspace reads
    /// this at port time and narrows the bundle through <c>BundleFilter.Apply</c>.</summary>
    public IReadOnlySet<string> ExcludedKeys => _excluded;

    /// <summary>Raised whenever a node's excluded state toggles.</summary>
    public event EventHandler? ExclusionChanged;

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
        _excluded.Clear(); // a fresh resolve starts with everything included
        _lastBundle = bundle;
        int carriedCount = BundleStructure.CarriedByScriptClosure(bundle).Count;
        HideCarriedCheck.Content = $"Show objects carried by the script closure ({carriedCount})";
        HideCarriedCheck.IsVisible = carriedCount > 0;
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

    /// <summary>Rebuilds the tree and graph for the current toggle state. This is view only,
    /// and never changes the bundle or the port exclusion set.</summary>
    private void OnHideCarriedToggled()
    {
        _hideCarried = HideCarriedCheck.IsChecked != true;
        if (_lastBundle is { } bundle)
        {
            BuildTree(bundle);
            RenderGraph(bundle);
        }
    }

    /// <summary>
    /// Structured fallback view: root object → object deps grouped by kind, each
    /// with a custom/base badge and the field codes ("via") that pull it in. Each
    /// ability additionally nests the trigger functions attributed to it (and
    /// whatever helpers those pulled in), so the script closure reads as "this
    /// ability's own logic" instead of a flat, unrelated function list.
    /// </summary>
    private void BuildTree(UnitBundle bundle)
    {
        DepTree.Items.Clear();

        var viaInto = bundle.Edges
            .GroupBy(e => e.To, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g.Select(e => e.Via).Distinct()),
                StringComparer.Ordinal);

        var (childrenOf, seedsByOwner) = AttributeFunctions(bundle);
        var realAdjacency = BundleStructure.RealAdjacency(bundle);
        var realParents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (from, targets) in realAdjacency)
        {
            foreach (var to in targets)
            {
                if (!realParents.TryGetValue(to, out var parents))
                    realParents[to] = parents = new HashSet<string>(StringComparer.Ordinal);
                parents.Add(from);
            }
        }
        var byCode = bundle.Objects.ToDictionary(o => o.Rawcode, StringComparer.Ordinal);

        void AddAbilityFunctions(TreeViewItem item, BundleNode node)
        {
            if (node.Kind == ObjectKind.Ability && seedsByOwner.TryGetValue(node.Rawcode, out var ownSeeds))
                item.Items.Add(FunctionGroupItem(ownSeeds, childrenOf));
        }

        TreeViewItem CreateObjectItem(BundleNode node)
        {
            var via = viaInto.TryGetValue(node.Rawcode, out var v) ? $", via {v}" : "";
            var item = new TreeViewItem
            {
                Header = MakeTreeLabel(
                    $"{node.Rawcode} - {node.Name ?? "(base game)"}"
                    + $", {(node.CustomToMap ? "custom" : "base")}{via}",
                    node.CustomToMap, bold: false),
            };
            AddAbilityFunctions(item, node);
            return item;
        }

        void AddObjectChildren(
            TreeViewItem parentItem,
            string parentRawcode,
            HashSet<string> visited,
            HashSet<string>? allowedRawcodes)
        {
            if (!realAdjacency.TryGetValue(parentRawcode, out var childCodes))
                return;

            foreach (var childRawcode in childCodes)
            {
                if (allowedRawcodes is not null && !allowedRawcodes.Contains(childRawcode))
                    continue;
                if (!visited.Add(childRawcode))
                    continue;
                if (!byCode.TryGetValue(childRawcode, out var childNode))
                    continue;

                var childItem = CreateObjectItem(childNode);
                parentItem.Items.Add(childItem);
                AddObjectChildren(childItem, childRawcode, visited, allowedRawcodes);
            }
        }

        var rootNode = bundle.Objects.FirstOrDefault(o => o.Rawcode == bundle.RootRawcode);
        var rootKindWord = (rootNode?.Kind ?? SelectedObjectKind).ToString().ToLowerInvariant();
        var rootCustom = rootNode?.CustomToMap ?? false;
        var rootItem = new TreeViewItem
        {
            Header = MakeTreeLabel(
                $"{bundle.RootRawcode} - {bundle.RootName ?? "(base game)"}"
                + $", {(rootCustom ? "custom" : "base")}, root {rootKindWord}",
                rootNode?.CustomToMap, bold: true),
            IsExpanded = true,
        };
        if (rootNode is not null)
            AddAbilityFunctions(rootItem, rootNode);

        var expandedReal = new HashSet<string>(StringComparer.Ordinal) { bundle.RootRawcode };
        AddObjectChildren(rootItem, bundle.RootRawcode, expandedReal, allowedRawcodes: null);

        var carriedRawcodes = BundleStructure.CarriedByScriptClosure(bundle);
        if (carriedRawcodes.Count > 0 && !_hideCarried)
        {
            var carriedItem = new TreeViewItem
            {
                Header = MakeTreeLabel(
                    $"Carried by the script closure ({carriedRawcodes.Count})",
                    custom: null, bold: true),
                IsExpanded = true,
            };

            var carriedRoots = bundle.Objects
                .Where(o => carriedRawcodes.Contains(o.Rawcode)
                    && (!realParents.TryGetValue(o.Rawcode, out var parents)
                        || !parents.Any(carriedRawcodes.Contains)))
                .ToList();
            var expandedCarried = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in carriedRoots)
            {
                if (!expandedCarried.Add(node.Rawcode))
                    continue;

                var nodeItem = CreateObjectItem(node);
                carriedItem.Items.Add(nodeItem);
                AddObjectChildren(nodeItem, node.Rawcode, expandedCarried, carriedRawcodes);
            }
            foreach (var node in bundle.Objects.Where(o => carriedRawcodes.Contains(o.Rawcode)))
            {
                if (!expandedCarried.Add(node.Rawcode))
                    continue;

                var nodeItem = CreateObjectItem(node);
                carriedItem.Items.Add(nodeItem);
                AddObjectChildren(nodeItem, node.Rawcode, expandedCarried, carriedRawcodes);
            }

            rootItem.Items.Add(carriedItem);
        }

        if (seedsByOwner.TryGetValue(OtherOwnerKey, out var otherSeeds))
            rootItem.Items.Add(FunctionGroupItem(otherSeeds, childrenOf, "Other triggers"));

        DepTree.Items.Add(rootItem);
    }

    /// <summary>Sentinel owner key for a seed function that names no ability present in
    /// this bundle (references the hero itself, an out-of-closure rawcode, or nothing),
    /// never a real rawcode (those are always exactly 4 characters).</summary>
    private const string OtherOwnerKey = "";

    private static readonly Regex QuotedRawcode = new(@"'([^']{4})'", RegexOptions.Compiled);

    /// <summary>
    /// Attributes every script-closure function to the one ability it belongs under. A
    /// seed function ("references '...'") is owned by the first quoted rawcode that is an
    /// ability actually in this bundle, or <see cref="OtherOwnerKey"/> when none match. A
    /// discovered function ("called by Name") is not a seed at all, it nests as a CHILD of
    /// whichever function pulled it in (<paramref name="bundle"/>'s BFS already recorded
    /// exactly one discoverer per function), so helpers land under their calling trigger
    /// rather than under an ability directly.
    /// </summary>
    private static (Dictionary<string, List<BundleFunction>> ChildrenOf,
        Dictionary<string, List<BundleFunction>> SeedsByOwner) AttributeFunctions(UnitBundle bundle)
    {
        var abilityRawcodes = bundle.Objects
            .Where(o => o.Kind == ObjectKind.Ability)
            .Select(o => o.Rawcode)
            .ToHashSet(StringComparer.Ordinal);

        var childrenOf = new Dictionary<string, List<BundleFunction>>(StringComparer.Ordinal);
        var seedsByOwner = new Dictionary<string, List<BundleFunction>>(StringComparer.Ordinal);
        foreach (var f in bundle.Functions)
        {
            if (f.Reason.StartsWith("called by ", StringComparison.Ordinal))
            {
                var caller = f.Reason["called by ".Length..];
                if (!childrenOf.TryGetValue(caller, out var kids)) childrenOf[caller] = kids = new();
                kids.Add(f);
                continue;
            }
            string owner = OtherOwnerKey;
            foreach (Match m in QuotedRawcode.Matches(f.Reason))
            {
                if (!abilityRawcodes.Contains(m.Groups[1].Value)) continue;
                owner = m.Groups[1].Value;
                break; // first named ability wins, mirrors the reason's own discovery order
            }
            if (!seedsByOwner.TryGetValue(owner, out var seeds)) seedsByOwner[owner] = seeds = new();
            seeds.Add(f);
        }
        return (childrenOf, seedsByOwner);
    }

    /// <summary>A collapsible "Trigger functions (N)" node holding <paramref name="seeds"/>,
    /// each nested with whatever helpers it pulled in (recursively, via
    /// <paramref name="childrenOf"/>).</summary>
    private static TreeViewItem FunctionGroupItem(
        IReadOnlyList<BundleFunction> seeds, Dictionary<string, List<BundleFunction>> childrenOf,
        string label = "Trigger functions")
    {
        var group = new TreeViewItem
        {
            Header = MakeTreeLabel($"{label} ({seeds.Count})", custom: null, bold: true),
            IsExpanded = false,
        };
        foreach (var seed in seeds.OrderBy(f => f.StartLine))
            group.Items.Add(FunctionItem(seed, childrenOf, new HashSet<string>(StringComparer.Ordinal)));
        return group;
    }

    /// <summary>One function's row, its own "called by" discoveries nested as children.
    /// <paramref name="ancestry"/> guards against a cyclic caller chain (should never
    /// happen given the BFS that assigns Reason, defensive only) turning into infinite
    /// recursion, a foreign/self-referential edge is simply not descended into twice.</summary>
    private static TreeViewItem FunctionItem(
        BundleFunction fn, Dictionary<string, List<BundleFunction>> childrenOf, HashSet<string> ancestry)
    {
        var item = new TreeViewItem
        {
            Header = MakeTreeLabel($"{fn.Name} ({fn.Reason})", custom: null, bold: false),
        };
        if (ancestry.Add(fn.Name))
        {
            if (childrenOf.TryGetValue(fn.Name, out var kids))
                foreach (var kid in kids.OrderBy(k => k.StartLine))
                    item.Items.Add(FunctionItem(kid, childrenOf, ancestry));
            ancestry.Remove(fn.Name);
        }
        return item;
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
    /// Node-link graph with a simple deterministic layered layout. Level 0 is the
    /// root unit at the top, each level below it the next BFS depth of object deps,
    /// so a hero's abilities branch across the row beneath it. Files get their own
    /// wrapped band along the bottom. Edges are straight lines between node anchors,
    /// labeled with their field codes (parallel edges between the same pair coalesce
    /// into one labeled line). The layout is only the starting arrangement, nodes
    /// drag freely afterwards, with <see cref="PositionEdge"/> re-anchoring their
    /// edges live.
    /// </summary>
    private void RenderGraph(UnitBundle bundle)
    {
        GraphCanvas.Children.Clear();
        _edges.Clear();
        _objVisuals.Clear();
        _fileVisuals.Clear();
        _dragNode = null;
        _panning = false;
        ResetView();
        if (bundle.Objects.Count == 0)
        {
            GraphHint.IsVisible = true;
            GraphHint.Text = $"{bundle.RootRawcode} resolved to nothing - see the status line.";
            return;
        }
        GraphHint.IsVisible = false;

        // Hiding is view only, so the layout skips closure carried objects when the toggle
        // is off. Their edges fall away with them, and TryGetVisual skips endpoints that
        // were not laid out.
        var hidden = _hideCarried
            ? BundleStructure.CarriedByScriptClosure(bundle)
            : new HashSet<string>(StringComparer.Ordinal);
        var objects = bundle.Objects.Where(o => !hidden.Contains(o.Rawcode)).ToList();
        bundle = bundle with { Objects = objects };

        // Object depth from the root, BFS over object to object edges.
        var byCode = objects.ToDictionary(o => o.Rawcode, StringComparer.Ordinal);
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
        foreach (var node in bundle.Objects)
            depth.TryAdd(node.Rawcode, 1);

        // Levels, one row per depth, bundle order across a row, each row
        // horizontally centered against the widest row.
        var levels = objects
            .GroupBy(o => depth[o.Rawcode])
            .OrderBy(g => g.Key)
            .Select(g => g.ToList())
            .ToList();
        double maxLevelWidth = levels.Max(level => level.Count * ObjW + (level.Count - 1) * SiblingGapX);
        var objRects = new Dictionary<string, Rect>(StringComparer.Ordinal);
        for (int li = 0; li < levels.Count; li++)
        {
            double y = Pad + li * (ObjH + LevelGapY);
            double levelWidth = levels[li].Count * ObjW + (levels[li].Count - 1) * SiblingGapX;
            double x = Pad + (maxLevelWidth - levelWidth) / 2;
            foreach (var node in levels[li])
            {
                objRects[node.Rawcode] = new Rect(x, y, ObjW, ObjH);
                x += ObjW + SiblingGapX;
            }
        }

        // --- files band: wrapped rows under the object area (case-insensitive
        //     keys - WC3 paths compare case-insensitively) ---
        // Only the object's real dependencies appear as file nodes. The trigger-carried
        // "port assets" are listed separately in the panel and would just swamp the graph.
        var objectDeps = CleanReachableFiles(bundle);
        var depFiles = bundle.Files.Where(f => objectDeps.Contains(f.Path)).ToList();

        var fileRects = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        double objAreaWidth = maxLevelWidth;
        double bandTop = objRects.Values.Max(r => r.Bottom) + BandGap;
        int perRow = Math.Max(1, (int)((objAreaWidth + FileGapX) / (FileW + FileGapX)));
        for (int i = 0; i < depFiles.Count; i++)
        {
            fileRects[depFiles[i].Path] = new Rect(
                Pad + i % perRow * (FileW + FileGapX),
                bandTop + i / perRow * (FileH + FileGapY),
                FileW, FileH);
        }
        if (depFiles.Count > 0)
        {
            var separator = new Border
            {
                Width = Math.Max(objAreaWidth, Math.Min(perRow, depFiles.Count) * (FileW + FileGapX) - FileGapX),
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
        //     them, but added to the canvas after the edges (nodes draw on top).
        //     Tagged with their own BundleNode/BundleFile and restyled through the
        //     current exclusion state, so a click later only ever touches one node. ---
        foreach (var node in bundle.Objects)
        {
            var rect = objRects[node.Rawcode];
            var visual = MakeObjectNode(node, node.Rawcode == bundle.RootRawcode);
            Canvas.SetLeft(visual, rect.X);
            Canvas.SetTop(visual, rect.Y);
            MakeDraggable(visual);
            RestyleNode(visual);
            _objVisuals[node.Rawcode] = visual;
        }
        foreach (var file in depFiles)
        {
            var rect = fileRects[file.Path];
            var visual = MakeFileNode(file);
            Canvas.SetLeft(visual, rect.X);
            Canvas.SetTop(visual, rect.Y);
            MakeDraggable(visual);
            RestyleNode(visual);
            _fileVisuals[file.Path] = visual;
        }

        // --- edges (below the nodes), parallel edges coalesced ---
        foreach (var group in bundle.Edges.GroupBy(e => (e.From, e.To)))
        {
            if (!TryGetVisual(group.Key.From, _objVisuals, _fileVisuals, out var from, out _)
                || !TryGetVisual(group.Key.To, _objVisuals, _fileVisuals, out var to, out var toIsFile))
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
            GraphCanvas.Children.Add(_objVisuals[node.Rawcode]);
        foreach (var file in depFiles)
            GraphCanvas.Children.Add(_fileVisuals[file.Path]);

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
    /// (Re)anchor an edge's line and label to its endpoints' current rects. An
    /// object to file edge drops from the bottom edge. Otherwise the line leaves the
    /// face toward the target (the bottom for a deeper level, the top for a back
    /// edge), falling back to centers on the same level.
    /// </summary>
    private static void PositionEdge(GraphEdge edge)
    {
        var from = VisualRect(edge.From);
        var to = VisualRect(edge.To);

        Point p1, p2;
        if (edge.ToIsFile)
        {
            p1 = new Point(from.Center.X, from.Bottom);   // object to file, drop straight down
            p2 = new Point(to.Center.X, to.Y);
        }
        else if (to.Y > from.Y)
        {
            p1 = new Point(from.Center.X, from.Bottom);   // deeper level sits below, leave the bottom
            p2 = new Point(to.Center.X, to.Y);
        }
        else if (to.Y < from.Y)
        {
            p1 = new Point(from.Center.X, from.Y);        // back edge points up, leave the top
            p2 = new Point(to.Center.X, to.Bottom);
        }
        else
        {
            p1 = from.Center;                             // same level, connect centers
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
            Tag = node, // read back by RestyleNode/ToggleExcluded to key the exclusion set
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
            Tag = file, // read back by RestyleNode/ToggleExcluded to key the exclusion set
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
        _objVisuals.Clear();
        _fileVisuals.Clear();
        _excluded.Clear();
        _lastBundle = null;
        HideCarriedCheck.IsVisible = false;
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
