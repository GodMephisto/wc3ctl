using Avalonia.Controls;
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
    private MapSession? _session;
    private bool _suppress;
    /// <summary>Stamp that invalidates in-flight unit lists / resolves when the map changes.</summary>
    private int _generation;

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

    /// <summary>Resolve the selected unit's closure. Fleshed out in the next slice.</summary>
    private void RequestResolve()
    {
        SummaryText.Text = $"Selected {SelectedUnitDisplay} — closure resolution comes next.";
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
