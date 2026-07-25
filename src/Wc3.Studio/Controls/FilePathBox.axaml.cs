using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace Wc3.Studio.Controls;

/// <summary>
/// A path input with a Browse button: a TextBox plus a "Browse…" button that opens the OS file
/// dialog (via <see cref="FilePicker"/>, so it reopens at the last-used folder) and drops the
/// chosen path into the box. Used everywhere a file path is entered, so no panel ships a bare
/// type-the-path box. <see cref="Text"/> is the two-way path value; <see cref="FilterName"/> /
/// <see cref="FilterPatterns"/> scope the dialog (comma-separated globs, e.g. "*.mp3,*.wav").
/// </summary>
public partial class FilePathBox : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<FilePathBox, string?>(
            nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<FilePathBox, string?>(nameof(Watermark));

    public static readonly StyledProperty<string> DialogTitleProperty =
        AvaloniaProperty.Register<FilePathBox, string>(nameof(DialogTitle), "Select a file");

    public static readonly StyledProperty<string> FilterNameProperty =
        AvaloniaProperty.Register<FilePathBox, string>(nameof(FilterName), "Files");

    public static readonly StyledProperty<string> FilterPatternsProperty =
        AvaloniaProperty.Register<FilePathBox, string>(nameof(FilterPatterns), "*.*");

    private bool _syncing;

    public FilePathBox() => InitializeComponent();

    /// <summary>The path shown/entered. Two-way; set by typing or by Browse.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Grey hint shown when the box is empty.</summary>
    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    /// <summary>Title of the Browse dialog.</summary>
    public string DialogTitle
    {
        get => GetValue(DialogTitleProperty);
        set => SetValue(DialogTitleProperty, value);
    }

    /// <summary>Display name of the file-type filter, e.g. "Audio".</summary>
    public string FilterName
    {
        get => GetValue(FilterNameProperty);
        set => SetValue(FilterNameProperty, value);
    }

    /// <summary>Comma-separated glob patterns for the filter, e.g. "*.mp3,*.wav".</summary>
    public string FilterPatterns
    {
        get => GetValue(FilterPatternsProperty);
        set => SetValue(FilterPatternsProperty, value);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        Text = PathBox.Text;
        _syncing = false;
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var patterns = FilterPatterns
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var picked = await FilePicker.PickOpenAsync(this, DialogTitle,
            new FilePicker.Filter(FilterName, patterns));
        if (picked is not null)
            Text = picked;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (PathBox is null) return; // property set before InitializeComponent wired the fields
        if (change.Property == TextProperty && !_syncing)
        {
            _syncing = true;
            PathBox.Text = Text;
            _syncing = false;
        }
        else if (change.Property == WatermarkProperty)
        {
            PathBox.Watermark = Watermark;
        }
    }
}
