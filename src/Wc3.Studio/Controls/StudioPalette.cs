using Avalonia.Media;

namespace Wc3.Studio.Controls;

/// <summary>
/// The Studio's semantic text colours, in one place.
///
/// These were declared privately in five panels before this existed. `#8FA3B8` alone appeared in
/// DependencyGraphView, HeroWiringView, PlayerForceView, TriggerView and CatalogCard under four
/// different names (MutedText, DimBrush, DimBrush, DimBrush), which means a palette change had
/// five places to miss and a reader had no way to know the four names meant the same thing.
///
/// Name them for what they MEAN, not what colour they are, so a panel picks a brush by asking
/// what the text is rather than what it should look like.
///
/// Canvas fills with alpha stay local to the panel that draws them. Those are one drawing's
/// design, not a vocabulary shared across panels.
/// </summary>
public static class StudioPalette
{
    /// <summary>Ordinary body text.</summary>
    public static readonly IBrush Normal = new SolidColorBrush(Color.Parse("#C8CDD3"));

    /// <summary>Secondary text. Labels, counts, anything supporting the thing beside it.</summary>
    public static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#8FA3B8"));

    /// <summary>Gold. Custom to this map, matching the object editor's modified-field highlight.
    /// The single most repeated signal in the Studio, so it has to mean exactly one thing.</summary>
    public static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#E8C56A"));

    /// <summary>Something referenced but absent, a file the map does not contain.</summary>
    public static readonly IBrush Missing = new SolidColorBrush(Color.Parse("#D98C8C"));

    /// <summary>A real problem the user should act on. Distinct from Missing, which is a fact
    /// about the data, where this is a fact about the result being wrong or incomplete.</summary>
    public static readonly IBrush Problem = new SolidColorBrush(Color.Parse("#D9756B"));

    /// <summary>Verified good.</summary>
    public static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#6FBE7A"));

    /// <summary>Informational, neither good nor bad. A passive ability, an inert state.</summary>
    public static readonly IBrush Info = new SolidColorBrush(Color.Parse("#5F9FD1"));

    /// <summary>Amber. A caution the user should read but which is not a failure, for example a
    /// refusal to remove something without moving code first.</summary>
    public static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#E8B339"));

    /// <summary>A group heading inside a list, distinct from the rows under it.</summary>
    public static readonly IBrush Header = new SolidColorBrush(Color.Parse("#7FB2E5"));
}
