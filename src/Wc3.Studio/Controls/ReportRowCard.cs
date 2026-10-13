using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Wc3.Studio.Controls;

/// <summary>How a reported row reads, which decides its tag colour.</summary>
public enum ReportTone
{
    /// <summary>Verified good, nothing to act on.</summary>
    Ok,
    /// <summary>Informational, neither good nor bad.</summary>
    Info,
    /// <summary>A caution to read, not a failure.</summary>
    Warn,
    /// <summary>A real problem the user should act on.</summary>
    Problem,
}

/// <summary>
/// Builds one report row as a <see cref="Card"/>, a coloured severity tag above an untruncated
/// detail line. Any panel that shows audit findings or command diagnostics renders them through
/// this, so the tone vocabulary and its colours live in one place rather than being re-declared at
/// every row site. This mirrors <see cref="CatalogCard"/>, which does the same job for catalog
/// entries, and it is deliberately a builder rather than a bindable control because a report row
/// has no inputs to bind and is rebuilt whenever a new report arrives.
/// </summary>
public static class ReportRowCard
{
    public static Border Build(ReportTone tone, string tag, string detail)
    {
        var brush = tone switch
        {
            ReportTone.Ok => StudioPalette.Ok,
            ReportTone.Info => StudioPalette.Info,
            ReportTone.Warn => StudioPalette.Warn,
            _ => StudioPalette.Problem,
        };

        var body = new StackPanel { Spacing = 2 };
        body.Children.Add(new TextBlock
        {
            Text = tag,
            FontWeight = FontWeight.Bold,
            FontSize = 11,
            Foreground = brush,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 12,
            Foreground = StudioPalette.Normal,
            TextWrapping = TextWrapping.Wrap,
        });
        return new Card { Child = body };
    }
}
