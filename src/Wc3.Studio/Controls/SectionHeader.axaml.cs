using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Wc3.Studio.Controls;

/// <summary>
/// A visible group/section bar: solid-ish background that reads on both light and
/// dark themes, 1px bottom border, bold BaseHigh text. When <see cref="Collapsible"/>
/// it shows a ▾ (open) / ▸ (<see cref="Collapsed"/>) glyph, gets a hand cursor, and a
/// click raises <see cref="Toggled"/> - the header does NOT flip its own state; the
/// owner decides and rebinds <see cref="Collapsed"/> (the palette rebuilds its list).
/// The click is marked handled so it never bubbles into list-selection machinery.
/// </summary>
public partial class SectionHeader : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SectionHeader, string>(nameof(Text), "");

    public static readonly StyledProperty<bool> CollapsibleProperty =
        AvaloniaProperty.Register<SectionHeader, bool>(nameof(Collapsible));

    public static readonly StyledProperty<bool> CollapsedProperty =
        AvaloniaProperty.Register<SectionHeader, bool>(nameof(Collapsed));

    public SectionHeader()
    {
        InitializeComponent();
        UpdateVisuals();
    }

    /// <summary>Raised when a Collapsible header is clicked; the owner toggles the
    /// underlying state and re-renders.</summary>
    public event EventHandler? Toggled;

    /// <summary>The bar's title text.</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Whether the bar shows a disclosure glyph and raises <see cref="Toggled"/>
    /// on click (default false: a plain section divider).</summary>
    public bool Collapsible
    {
        get => GetValue(CollapsibleProperty);
        set => SetValue(CollapsibleProperty, value);
    }

    /// <summary>Which glyph a Collapsible bar shows: ▸ when true, ▾ when false.</summary>
    public bool Collapsed
    {
        get => GetValue(CollapsedProperty);
        set => SetValue(CollapsedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == CollapsibleProperty
            || change.Property == CollapsedProperty)
            UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (TitleText is null)
            return; // property set before InitializeComponent wired the fields
        TitleText.Text = Text;
        GlyphText.IsVisible = Collapsible;
        GlyphText.Text = Collapsed ? "▸" : "▾";
        Bar.Cursor = Collapsible ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    /// <summary>Click on a Collapsible bar: consume the press (so e.g. a hosting
    /// ListBox never treats it as a selection) and let the owner toggle.</summary>
    private void OnBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!Collapsible)
            return;
        e.Handled = true;
        Toggled?.Invoke(this, EventArgs.Empty);
    }
}
