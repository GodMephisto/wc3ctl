using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Wc3.Commands;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Panels;

/// <summary>The optimise or audit action the panel runs.</summary>
public enum OptimizeKind
{
    ModelAudit,
    ModelOptimize,
    TextureOptimize,
    SoundOptimize,
    TerrainAudit,
    TerrainOptimize,
}

/// <summary>
/// The optimise and audit workbench. One panel drives the six commands the CLI and the MCP server
/// already expose, model audit, model optimise, texture optimise, sound optimise, terrain audit and
/// terrain optimise, so the Studio reaches the same passes the other front-ends do. Every action
/// calls the shared command directly and renders whatever it returns, it re-derives nothing.
///
/// <para>The shell is <see cref="CatalogEditorView"/>, the options form sits in its add slot and the
/// report rows sit in its card list, so this panel declares no header, scroll, status or empty-state
/// markup of its own. The file actions read and write the paths the user picks and need no map open.
/// The two terrain actions run against the open map and, when compacting, mark it edited so the
/// workspace Save button lights up, since the command mutates the in-memory terrain rather than
/// writing a second archive.</para>
/// </summary>
public partial class OptimizeView : UserControl, IMapPanel
{
    private readonly SearchableComboBox _kind = new() { MinWidth = 280 };
    private readonly FilePathBox _input = new();
    private readonly FilePathBox _output = new();
    private readonly TextBlock _mapNote = new()
    {
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly NumericUpDown _ratio = new()
    {
        Minimum = 0, Maximum = 1, Increment = 0.05m, Value = 0.5m, FormatString = "0.00", Width = 120,
    };
    private readonly CheckBox _strip = new() { Content = "Drop particle emitters, ribbons and corns" };
    private readonly NumericUpDown _maxSide = new()
    {
        Minimum = 1, Maximum = 8192, Increment = 8, Value = 256, Width = 120,
    };
    private readonly NumericUpDown _rate = new()
    {
        Minimum = 8000, Maximum = 48000, Increment = 50, Value = SoundOptimizeCommand.DefaultRate, Width = 120,
    };
    private readonly CheckBox _mono = new() { Content = "Downmix to a single channel" };
    private readonly Button _run = new() { Content = "Run", Padding = new Thickness(14, 3) };

    private readonly LabeledField _kindRow;
    private readonly LabeledField _inputRow;
    private readonly LabeledField _mapRow;
    private readonly LabeledField _outputRow;
    private readonly LabeledField _ratioRow;
    private readonly LabeledField _stripRow;
    private readonly LabeledField _maxRow;
    private readonly LabeledField _rateRow;
    private readonly LabeledField _monoRow;

    private MapSession? _session;
    private OptimizeReport? _lastReport;
    /// <summary>Stamp that drops an in-flight background result if a newer run supersedes it.</summary>
    private int _generation;

    /// <summary>Raised when a terrain optimise mutated the open map, so the workspace enables Save.</summary>
    public event EventHandler? MapEdited;

    public OptimizeView()
    {
        InitializeComponent();

        _kindRow = new LabeledField { Label = "Action", FieldContent = _kind };
        _inputRow = new LabeledField { Label = "Input", FieldContent = _input };
        _mapRow = new LabeledField { Label = "Input", FieldContent = _mapNote };
        _outputRow = new LabeledField
        {
            Label = "Output",
            FieldContent = _output,
            Hint = "Blank derives one beside the input, the input itself is never modified.",
        };
        _ratioRow = new LabeledField
        {
            Label = "Ratio",
            FieldContent = _ratio,
            Hint = "Target fraction of faces to keep, between 0 and 1.",
        };
        _stripRow = new LabeledField
        {
            Label = "Effects",
            FieldContent = _strip,
            Hint = "Stripping changes how the model looks, so it is off by default.",
        };
        _maxRow = new LabeledField
        {
            Label = "Max side",
            FieldContent = _maxSide,
            Hint = "A texture larger than this on either side is scaled down.",
        };
        _rateRow = new LabeledField
        {
            Label = "Rate",
            FieldContent = _rate,
            Hint = "Target sample rate in Hz.",
        };
        _monoRow = new LabeledField { Label = "Channels", FieldContent = _mono };

        Catalog.AddContent = BuildForm();
        _kind.SetItems(new[]
        {
            new SearchableComboBoxItem("Audit a model", Id(OptimizeKind.ModelAudit)),
            new SearchableComboBoxItem("Optimise a model", Id(OptimizeKind.ModelOptimize)),
            new SearchableComboBoxItem("Optimise a texture", Id(OptimizeKind.TextureOptimize)),
            new SearchableComboBoxItem("Optimise a sound", Id(OptimizeKind.SoundOptimize)),
            new SearchableComboBoxItem("Audit the map terrain", Id(OptimizeKind.TerrainAudit)),
            new SearchableComboBoxItem("Optimise the map terrain", Id(OptimizeKind.TerrainOptimize)),
        });
        _kind.SelectionChanged += (_, _) => ApplyKind();
        _run.Click += (_, _) => Run();
        ApplyKind();
    }

    /// <summary>The action currently selected in the picker.</summary>
    public OptimizeKind SelectedKind => KindFromId(_kind.SelectedId);

    /// <summary>The last rendered report, or null before the first Run. Public for tests.</summary>
    public OptimizeReport? LastReport => _lastReport;

    public void ShowMap(MapSession session)
    {
        _session = session;
        UpdateMapNote();
        // A terrain report belongs to the map that produced it, so it is dropped when the map changes.
        if (IsTerrain(SelectedKind))
            ResetReport(Describe(SelectedKind));
    }

    /// <summary>Selects an action as a user would, refreshing the form. Public for tests.</summary>
    public void SelectKind(OptimizeKind kind)
    {
        _kind.Select(Id(kind));
        ApplyKind();
    }

    /// <summary>Sets the input file path. Public for tests, the browse button does it at runtime.</summary>
    public void SetInput(string path) => _input.Text = path;

    /// <summary>Sets the output file path. Public for tests, the browse button does it at runtime.</summary>
    public void SetOutput(string path) => _output.Text = path;

    private Control BuildForm()
    {
        var form = new StackPanel { Spacing = 2 };
        form.Children.Add(_kindRow);
        form.Children.Add(_inputRow);
        form.Children.Add(_mapRow);
        form.Children.Add(_outputRow);
        form.Children.Add(_ratioRow);
        form.Children.Add(_stripRow);
        form.Children.Add(_maxRow);
        form.Children.Add(_rateRow);
        form.Children.Add(_monoRow);
        form.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { _run },
        });
        return form;
    }

    /// <summary>Shows only the rows and file filter the current action needs.</summary>
    private void ApplyKind()
    {
        var kind = SelectedKind;
        bool terrain = IsTerrain(kind);
        bool writes = kind is OptimizeKind.ModelOptimize or OptimizeKind.TextureOptimize
            or OptimizeKind.SoundOptimize or OptimizeKind.TerrainOptimize;

        _inputRow.IsVisible = !terrain;
        _mapRow.IsVisible = terrain;
        _outputRow.IsVisible = writes;
        _ratioRow.IsVisible = kind == OptimizeKind.ModelOptimize;
        _stripRow.IsVisible = kind == OptimizeKind.ModelOptimize;
        _maxRow.IsVisible = kind == OptimizeKind.TextureOptimize;
        _rateRow.IsVisible = kind == OptimizeKind.SoundOptimize;
        _monoRow.IsVisible = kind == OptimizeKind.SoundOptimize;

        switch (kind)
        {
            case OptimizeKind.ModelAudit:
            case OptimizeKind.ModelOptimize:
                ConfigureInput("Model files", "*.mdx,*.mdl", "Path to a .mdx or .mdl model");
                break;
            case OptimizeKind.TextureOptimize:
                ConfigureInput("Image files", "*.blp,*.dds,*.png,*.jpg,*.jpeg,*.bmp,*.tga,*.gif",
                    "Path to a .blp, .dds or image file");
                break;
            case OptimizeKind.SoundOptimize:
                ConfigureInput("Audio files", "*.wav,*.mp3", "Path to a .wav or .mp3 sound");
                break;
        }

        UpdateMapNote();
        ResetReport(Describe(kind));
    }

    private void ConfigureInput(string filterName, string patterns, string watermark)
    {
        _input.FilterName = filterName;
        _input.FilterPatterns = patterns;
        _input.Watermark = watermark;
        _input.DialogTitle = "Choose " + filterName.ToLowerInvariant();
    }

    private void UpdateMapNote()
    {
        _mapNote.Text = _session?.Current is not null
            ? $"Uses the open map  {Path.GetFileName(_session.MapPath ?? "open map")}"
            : "Open a map to audit or optimise its terrain";
    }

    private void ResetReport(string prompt)
    {
        _lastReport = null;
        Catalog.SetEmpty("Nothing run yet", prompt);
    }

    /// <summary>
    /// Runs the selected action. The file actions touch only their own input and output, so they run
    /// off the UI thread. The terrain actions read or mutate the open map's in-memory model, so they
    /// stay on it.
    /// </summary>
    public void Run()
    {
        var kind = SelectedKind;

        if (IsTerrain(kind))
        {
            if (_session?.Current is null)
            {
                Catalog.ShowHint("Open a map first to audit or optimise its terrain.");
                return;
            }
            RunTerrain(kind);
            return;
        }

        int gen = ++_generation;
        _run.IsEnabled = false;
        Catalog.SetStatus("Running…");

        string input = _input.Text?.Trim() ?? "";
        string output = _output.Text?.Trim() ?? "";
        float ratio = (float)(_ratio.Value ?? 0.5m);
        bool strip = _strip.IsChecked == true;
        int max = (int)(_maxSide.Value ?? 256m);
        int rate = (int)(_rate.Value ?? SoundOptimizeCommand.DefaultRate);
        bool mono = _mono.IsChecked == true;

        Task.Run(() =>
        {
            OptimizeReport? report = null;
            string? error = null;
            try { report = ExecuteFile(kind, input, output, ratio, strip, max, rate, mono); }
            catch (Exception ex) { error = ex.Message; }

            Dispatcher.UIThread.Post(() =>
            {
                if (gen != _generation)
                    return; // superseded by a newer run
                _run.IsEnabled = true;
                if (report is null)
                {
                    Catalog.ShowHint($"Run failed. {error}");
                    return;
                }
                Render(report);
            });
        });
    }

    private void RunTerrain(OptimizeKind kind)
    {
        try
        {
            var (report, changed) = ExecuteTerrain(kind);
            Render(report);
            if (changed)
                MapEdited?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Catalog.ShowHint($"Run failed. {ex.Message}");
        }
    }

    private static OptimizeReport ExecuteFile(OptimizeKind kind, string input, string output,
        float ratio, bool strip, int max, int rate, bool mono)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new InvalidOperationException("Pick an input file first.");

        return kind switch
        {
            OptimizeKind.ModelAudit =>
                OptimizeReportBuilder.ForModelAudit(ModelAuditCommand.ExecutePath(input)),
            OptimizeKind.ModelOptimize =>
                OptimizeReportBuilder.ForModelOptimize(
                    ModelOptimizeCommand.ExecutePath(input, ratio, DefaultOutput(output, input), strip)),
            OptimizeKind.TextureOptimize =>
                OptimizeReportBuilder.ForTextureOptimize(
                    TextureOptimizeCommand.ExecutePath(input, max, DefaultOutput(output, input))),
            _ =>
                OptimizeReportBuilder.ForSoundOptimize(
                    SoundOptimizeCommand.ExecutePath(input, rate, mono, DefaultOutput(output, input, ".wav"))),
        };
    }

    private (OptimizeReport Report, bool Changed) ExecuteTerrain(OptimizeKind kind)
    {
        var doc = _session!.Current!;
        string name = _session.MapPath ?? "open map";

        if (kind == OptimizeKind.TerrainAudit)
            return (OptimizeReportBuilder.ForTerrainAudit(TerrainAuditCommand.Execute(doc, name)), false);

        var r = TerrainOptimizeCommand.Execute(doc, name);
        return (OptimizeReportBuilder.ForTerrainOptimize(r), r.Ok && r.Changed);
    }

    /// <summary>Writes a default output beside the input when the user left the output blank.</summary>
    private static string DefaultOutput(string output, string input, string? forceExtension = null)
    {
        if (!string.IsNullOrWhiteSpace(output))
            return output;
        string dir = Path.GetDirectoryName(input) ?? "";
        string name = Path.GetFileNameWithoutExtension(input);
        string ext = forceExtension ?? Path.GetExtension(input);
        return Path.Combine(dir, name + ".optimized" + ext);
    }

    private void Render(OptimizeReport report)
    {
        _lastReport = report;

        var cards = new List<Control>
        {
            CatalogCard.Build(report.Title, report.Facts.Select(f => (f.Label, f.Value))),
        };
        foreach (var row in report.Rows)
            cards.Add(ReportRowCard.Build(row.Tone, row.Tag, row.Detail));

        Catalog.SetCards(cards);
        Catalog.SetStatus(report.Status);
    }

    private static bool IsTerrain(OptimizeKind kind) =>
        kind is OptimizeKind.TerrainAudit or OptimizeKind.TerrainOptimize;

    private static string Describe(OptimizeKind kind) => kind switch
    {
        OptimizeKind.ModelAudit => "Pick a model, then Run to audit it.",
        OptimizeKind.ModelOptimize => "Pick a model and an output, then Run to shrink it.",
        OptimizeKind.TextureOptimize => "Pick a texture and an output, then Run to scale it down.",
        OptimizeKind.SoundOptimize => "Pick a sound and an output, then Run to resample it.",
        OptimizeKind.TerrainAudit => "Open a map, then Run to audit its terrain.",
        _ => "Open a map, then Run to compact its terrain.",
    };

    private static string Id(OptimizeKind kind) => kind switch
    {
        OptimizeKind.ModelAudit => "model-audit",
        OptimizeKind.ModelOptimize => "model-optimize",
        OptimizeKind.TextureOptimize => "texture-optimize",
        OptimizeKind.SoundOptimize => "sound-optimize",
        OptimizeKind.TerrainAudit => "terrain-audit",
        _ => "terrain-optimize",
    };

    private static OptimizeKind KindFromId(string? id) => id switch
    {
        "model-optimize" => OptimizeKind.ModelOptimize,
        "texture-optimize" => OptimizeKind.TextureOptimize,
        "sound-optimize" => OptimizeKind.SoundOptimize,
        "terrain-audit" => OptimizeKind.TerrainAudit,
        "terrain-optimize" => OptimizeKind.TerrainOptimize,
        _ => OptimizeKind.ModelAudit,
    };
}
