using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Wc3.Commands;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Panels;

/// <summary>
/// Surfaces <see cref="HeroWiringAudit"/> in Studio. Audits every placed hero with no input
/// needed, that is the common case, with a picker to focus on just one as a refinement.
/// Three states matter here, not two. Ok (a castable ability wired end to end),
/// NotCastDispatched (a passive, aura, inventory or spellbook, healthy by design and never
/// a fault), and every other status (a real, actionable problem naming the exact missing
/// link). Composes <see cref="CatalogEditorView"/> for its shell rather than re-declaring
/// the header, scroll and status line markup a fourth time.
/// </summary>
public partial class HeroWiringView : UserControl, IMapPanel
{
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.Parse("#6FBE7A"));
    private static readonly IBrush PassiveBrush = new SolidColorBrush(Color.Parse("#5F9FD1"));
    private static readonly IBrush ProblemBrush = new SolidColorBrush(Color.Parse("#D9756B"));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#8FA3B8"));

    /// <summary>The focus picker's sentinel id for "show every hero" (a real hero rawcode
    /// is always exactly 4 characters, so an empty id never collides with one).</summary>
    private const string AllHeroesId = "";

    private readonly SearchableComboBox _focus = new();
    private readonly Button _rerunButton = new() { Content = "Re-run audit" };

    private MapSession? _session;
    /// <summary>Stamp that drops an in-flight audit result if a newer one (Re-run, or a
    /// fresh map) supersedes it before it lands.</summary>
    private int _generation;
    private IReadOnlyList<HeroWiringResult>? _results;

    public HeroWiringView()
    {
        InitializeComponent();
        _focus.Watermark = "All heroes";
        _focus.SelectionChanged += (_, _) => Render();
        _rerunButton.Padding = new Thickness(10, 2);
        _rerunButton.VerticalAlignment = VerticalAlignment.Center;
        _rerunButton.Click += (_, _) => RunAudit();
        ToolTip.SetTip(_rerunButton,
            "Re-check the map. Fixing a trigger elsewhere does not update this list on its own.");
        Catalog.AddContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _focus, _rerunButton },
        };
    }

    public void ShowMap(MapSession session)
    {
        _session = session;
        RunAudit();
    }

    /// <summary>
    /// Runs the audit off the UI thread, a big multi-hero arena reparses the whole script
    /// once per distinct hero type, and can take a real moment. Disables Re-run while a
    /// pass is in flight rather than queuing a second one, a deliberate click covers it.
    /// </summary>
    private void RunAudit()
    {
        if (_session?.Current is not { } doc)
        {
            Catalog.ShowHint("Hero wiring audit, no map open.");
            return;
        }

        int gen = ++_generation;
        _rerunButton.IsEnabled = false;
        Catalog.SetStatus("Auditing placed heroes…");

        Task.Run(() =>
        {
            IReadOnlyList<HeroWiringResult>? results = null;
            string? error = null;
            try { results = HeroWiringAudit.AuditPlacedHeroes(doc); }
            catch (Exception ex) { error = ex.Message; }

            Dispatcher.UIThread.Post(() =>
            {
                if (gen != _generation)
                    return; // superseded by a newer run
                _rerunButton.IsEnabled = true;
                if (results is null)
                {
                    Catalog.ShowHint($"Hero wiring audit failed, {error}");
                    return;
                }
                _results = results;
                RefreshFocusList();
                Render();
            });
        });
    }

    /// <summary>Rebuilds the focus picker's choices from the latest results, keeping
    /// whatever was selected before if it still exists (a Re-run should not silently
    /// snap the picker back to "All heroes").</summary>
    private void RefreshFocusList()
    {
        var keep = _focus.SelectedId;
        var items = new List<SearchableComboBoxItem> { new("All heroes", AllHeroesId) };
        if (_results is { } results)
            foreach (var r in results)
                items.Add(new SearchableComboBoxItem($"{r.Name ?? r.Hero} (player {r.OwnerId})", r.Hero));
        _focus.SetItems(items, selectId: keep);
    }

    /// <summary>Renders the current results, filtered to the focus picker's choice.</summary>
    private void Render()
    {
        if (_results is not { } results)
            return;
        if (results.Count == 0)
        {
            Catalog.SetCards(Array.Empty<Control>());
            Catalog.SetStatus("No placed heroes found on this map.");
            return;
        }

        var focusId = _focus.SelectedId;
        var shown = string.IsNullOrEmpty(focusId) ? results : results.Where(r => r.Hero == focusId).ToList();
        Catalog.SetCards(shown.Select(HeroCard));

        int problems = results.Sum(r => r.Problems.Count);
        Catalog.SetStatus($"{results.Count} placed hero(es), {problems} problem(s) total"
            + (shown.Count != results.Count ? $", showing {shown.Count}" : ""));
    }

    /// <summary>One hero, the verdict headline first (green when clean, red when not), then
    /// every ability with its own tag and untruncated detail, worst problems already sorted
    /// first by <see cref="HeroWiringAudit.Audit"/>.</summary>
    private static Border HeroCard(HeroWiringResult r)
    {
        var body = new StackPanel { Spacing = 4 };

        body.Children.Add(new TextBlock
        {
            Text = $"{r.Name ?? r.Hero} ({r.Hero}, player {r.OwnerId})",
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        int castable = r.Abilities.Count - r.NotCastable;
        body.Children.Add(new TextBlock
        {
            Text = $"{r.Wired} of {castable} castable abilities wired, {r.NotCastable} passive, "
                + $"{r.Problems.Count} problem{(r.Problems.Count == 1 ? "" : "s")}",
            FontWeight = FontWeight.SemiBold,
            Foreground = r.Problems.Count > 0 ? ProblemBrush : OkBrush,
        });

        foreach (var a in r.Abilities)
            body.Children.Add(AbilityRow(a));

        return new Card { Child = body };
    }

    /// <summary>One ability, a tag (OK/PASSIVE/PROBLEM) plus its rawcode and name on the
    /// head line, the full Detail wrapped underneath rather than trimmed, that detail is
    /// the whole payoff of the audit.</summary>
    private static Control AbilityRow(AbilityWiring a)
    {
        var (tag, brush) = a.Status switch
        {
            WiringStatus.Ok => ("OK", OkBrush),
            WiringStatus.NotCastDispatched => ("PASSIVE", PassiveBrush),
            _ => ("PROBLEM", ProblemBrush),
        };

        var row = new StackPanel { Spacing = 1, Margin = new Thickness(0, 3, 0, 0) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        head.Children.Add(new TextBlock
        {
            Text = tag, FontWeight = FontWeight.Bold, Foreground = brush, Width = 64,
        });
        head.Children.Add(new TextBlock
        {
            Text = $"{a.Ability}  {a.Name ?? "(unnamed)"}",
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        row.Children.Add(head);
        row.Children.Add(new TextBlock
        {
            Text = a.Detail,
            FontSize = 11,
            Foreground = DimBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(70, 0, 0, 0),
        });
        return row;
    }
}
