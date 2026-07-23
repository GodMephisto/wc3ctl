using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Wc3.Commands;

namespace Wc3.Studio.Controls;

/// <summary>
/// A player's color swatch + label, inline: a 12x12 rounded square tinted by
/// <see cref="PlayerColors"/> from <see cref="PlayerId"/> (out-of-range ids fall back
/// to gray there, so any owner id renders), then <see cref="Text"/>. The text trims
/// with an ellipsis when the container is narrower than the label, so rows in a
/// narrow dock truncate instead of overlapping their neighbours.
/// </summary>
public partial class PlayerChip : UserControl
{
    public static readonly StyledProperty<int> PlayerIdProperty =
        AvaloniaProperty.Register<PlayerChip, int>(nameof(PlayerId));

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<PlayerChip, string>(nameof(Text), "");

    public PlayerChip()
    {
        InitializeComponent();
        UpdateVisuals();
    }

    /// <summary>Player slot id (0..27); drives the swatch color.</summary>
    public int PlayerId
    {
        get => GetValue(PlayerIdProperty);
        set => SetValue(PlayerIdProperty, value);
    }

    /// <summary>Label shown next to the swatch, e.g. "Player 1 (Red) · map name".</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlayerIdProperty || change.Property == TextProperty)
            UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (SwatchBorder is null)
            return; // property set before InitializeComponent wired the fields
        var (r, g, b) = PlayerColors.Color(PlayerId);
        SwatchBorder.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        ChipText.Text = Text;
    }
}
