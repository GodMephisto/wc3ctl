using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Wc3.Studio.Controls;

/// <summary>
/// The Studio's standard card: a rounded 1px #33555555 border with 10x8 padding
/// around its Child (Border's own content slot, so both XAML child syntax and
/// code-built <c>new Card { Child = ... }</c> work). Replaces the per-panel Card()
/// helpers. StyleKey stays Border so any Border-targeting styles apply unchanged.
/// </summary>
public class Card : Border
{
    private static readonly IBrush EdgeBrush = new SolidColorBrush(Color.Parse("#33555555"));

    public Card()
    {
        BorderBrush = EdgeBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Padding = new Thickness(10, 8);
    }

    protected override Type StyleKeyOverride => typeof(Border);
}
