using Avalonia;
using Avalonia.Controls;

namespace Wc3.Studio.Controls;

/// <summary>
/// Shared chrome for a "catalog" panel: a titled, scrollable list of code-built cards with
/// an add-form slot on top, a status line at the bottom, and a swappable empty/error hint.
/// The regions, cameras and sounds panels all compose this instead of each re-declaring the
/// same header / scroll / status / empty-state markup - one place owns the layout, the panels
/// only supply their per-entry cards and their add form.
/// </summary>
public partial class CatalogEditorView : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<CatalogEditorView, string>(nameof(Title), "");

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<CatalogEditorView, string?>(nameof(Subtitle));

    public static readonly StyledProperty<object?> AddContentProperty =
        AvaloniaProperty.Register<CatalogEditorView, object?>(nameof(AddContent));

    public CatalogEditorView()
    {
        InitializeComponent();
    }

    /// <summary>Bold header, e.g. "Regions (war3map.w3r)".</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Dim one-line explanation under the title.</summary>
    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>The "add new entry" form shown above the list. Set in code by the owning
    /// panel; hidden when null. (Deliberately NOT the [Content] property - this control
    /// derives from ContentControl, whose own Content holds the shell's visual tree.)</summary>
    public object? AddContent
    {
        get => GetValue(AddContentProperty);
        set => SetValue(AddContentProperty, value);
    }

    /// <summary>Replaces the card list and switches to the content view.</summary>
    public void SetCards(IEnumerable<Control> cards)
    {
        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;
        ItemHost.Children.Clear();
        foreach (var card in cards)
            ItemHost.Children.Add(card);
    }

    /// <summary>Sets the bottom status/last-action line.</summary>
    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>Shows the centered hint (empty state or error) and hides the content.</summary>
    public void ShowHint(string text)
    {
        PlaceholderText.Text = text;
        PlaceholderText.IsVisible = true;
        ContentRoot.IsVisible = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (TitleText is null)
            return; // property set before InitializeComponent wired the fields
        if (change.Property == TitleProperty)
            TitleText.Text = Title;
        else if (change.Property == SubtitleProperty)
        {
            SubtitleText.Text = Subtitle ?? "";
            SubtitleText.IsVisible = !string.IsNullOrEmpty(Subtitle);
        }
        else if (change.Property == AddContentProperty)
        {
            AddHost.Content = AddContent;
            AddBorder.IsVisible = AddContent is not null;
        }
    }
}
