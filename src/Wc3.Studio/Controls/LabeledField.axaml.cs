using Avalonia;
using Avalonia.Controls;

namespace Wc3.Studio.Controls;

/// <summary>
/// One labeled editor row for a properties panel: the label in a fixed-width column,
/// the slotted editor control next to it, and an optional dim hint underneath. The editor
/// goes in <see cref="FieldContent"/>, set with explicit
/// <c>&lt;c:LabeledField.FieldContent&gt;</c> element syntax.
///
/// <para>FieldContent is deliberately NOT marked <c>[Content]</c>. A UserControl already has
/// <c>Content</c> as its <c>[Content]</c> property (it holds this control's own template), and a
/// SECOND <c>[Content]</c> on the same control breaks the template load, InitializeComponent never
/// wires up <c>Slot</c>, so every row renders blank. Sized to fit a ~360px dock without overflow.</para>
/// </summary>
public partial class LabeledField : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<LabeledField, string>(nameof(Label), "");

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<LabeledField, string?>(nameof(Hint));

    public static readonly StyledProperty<object?> FieldContentProperty =
        AvaloniaProperty.Register<LabeledField, object?>(nameof(FieldContent));

    public LabeledField()
    {
        InitializeComponent();
    }

    /// <summary>Row label shown in the fixed-width left column, e.g. "Hero Level".</summary>
    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Optional dim helper text under the editor, e.g. "0..100, -1 = default".
    /// Null/empty hides the hint line entirely.</summary>
    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>The editor control (TextBox, picker, ...) shown next to the label. Set via explicit
    /// <c>&lt;c:LabeledField.FieldContent&gt;</c> syntax, NOT marked [Content] (see the type remarks).</summary>
    public object? FieldContent
    {
        get => GetValue(FieldContentProperty);
        set => SetValue(FieldContentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (LabelText is null)
            return; // property set before InitializeComponent wired the fields
        if (change.Property == LabelProperty)
        {
            LabelText.Text = Label;
        }
        else if (change.Property == HintProperty)
        {
            HintText.Text = Hint ?? "";
            HintText.IsVisible = !string.IsNullOrEmpty(Hint);
        }
        else if (change.Property == FieldContentProperty)
        {
            Slot.Content = FieldContent;
        }
    }
}
