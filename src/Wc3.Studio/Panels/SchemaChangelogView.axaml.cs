// src/Wc3.Studio/Panels/SchemaChangelogView.axaml.cs
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Wc3.Commands;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Panels;

/// <summary>
/// Pick two builds and show the field-level diff of their object data, from
/// <see cref="GamedataChangelogCommand"/>. A thin view, all the work is the command's, this panel
/// only chooses the builds and renders the change list.
/// </summary>
public partial class SchemaChangelogView : UserControl, IMapPanel
{
    private MapSession? _session;

    public SchemaChangelogView()
    {
        InitializeComponent();
        ChangeList.ItemsSource = Array.Empty<ChangeRow>();
    }

    /// <summary>One row of the change list, a file heading or a field change.</summary>
    public sealed record ChangeRow(string Display, string Detail, bool IsHeader)
    {
        public IBrush RowBrush { get; init; } = StudioPalette.Normal;
        public FontWeight RowWeight { get; init; } = FontWeight.Normal;
        public Thickness RowMargin { get; init; } = new(0);
    }

    public void ShowMap(MapSession session)
    {
        _session = session;
        ChangeList.ItemsSource = Array.Empty<ChangeRow>();
        SummaryText.Text = "";
        StatusText.Text = "";
        RefreshBuilds();
    }

    private string? SchemasDir => _session?.SchemasDir;

    private void RefreshBuilds()
    {
        var items = new List<SearchableComboBoxItem>();
        try
        {
            foreach (var build in ObjectTargetFieldsCommand.Builds(SchemasDir))
                items.Add(new SearchableComboBoxItem(build.Build, build.Build));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"No schema builds available. {ex.Message}";
        }

        // Oldest build on the left (the "from" side), newest on the right, so the diff reads in
        // the same direction the versions move.
        var from = items.FirstOrDefault();
        var to = items.Count > 1 ? items[^1] : items.FirstOrDefault();
        BuildACombo.SetItems(items, selectId: from?.Id);
        BuildBCombo.SetItems(items, selectId: to?.Id);

        StatusText.Text = items.Count < 2
            ? "Need at least two schema builds to compare."
            : $"{items.Count} build(s) available.";
    }

    private void OnCompareClick(object? sender, RoutedEventArgs e) => Compare();

    /// <summary>Runs the diff between the two chosen builds and renders it. Public so a test can
    /// drive it after selecting builds without synthesising a click.</summary>
    public void Compare()
    {
        if (BuildACombo.SelectedId is not { Length: > 0 } from
            || BuildBCombo.SelectedId is not { Length: > 0 } to)
        {
            StatusText.Text = "Pick two builds to compare.";
            return;
        }

        GamedataChangelogResult result;
        try
        {
            result = GamedataChangelogCommand.Execute(from, to, SchemasDir);
        }
        catch (Exception ex)
        {
            ChangeList.ItemsSource = Array.Empty<ChangeRow>();
            SummaryText.Text = "";
            StatusText.Text = $"Could not compare {from} and {to}. {ex.Message}";
            return;
        }

        var rows = new List<ChangeRow>();
        foreach (var group in result.Groups)
        {
            rows.Add(new ChangeRow($"{group.Slk} ({group.Changes.Count})", "", true)
            {
                RowBrush = StudioPalette.Header,
                RowWeight = FontWeight.SemiBold,
                RowMargin = new Thickness(0, 8, 0, 2),
            });
            foreach (var change in group.Changes)
                rows.Add(new ChangeRow(change.Code, $"{change.Change}  {change.Detail}".Trim(), false));
        }
        ChangeList.ItemsSource = rows;

        SummaryText.Text = $"{result.Added} added, {result.Removed} removed, "
            + $"{result.Retyped} retyped, {result.BoundsChanged} bounds changed "
            + $"across {result.Groups.Count} file(s)";
        StatusText.Text = result.Groups.Count == 0
            ? (result.NotCompared.Count > 0
                ? "No comparable object fields. " + string.Join("; ", result.NotCompared)
                : "No object field differences between these builds.")
            : result.NotCompared.Count > 0
                ? "Not compared. " + string.Join("; ", result.NotCompared)
                : "";
    }
}
