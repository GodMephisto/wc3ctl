using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wc3.Studio.Controls;
using Xunit;

namespace Wc3.Studio.Tests;

/// <summary>
/// The welcome landing surface: it lists what the workspace feeds it and reports clicks through
/// events. These pin the two behaviours the workspace depends on, a clickable recent map and the
/// Target-only blank-map action.
/// </summary>
public class WelcomeViewTests
{
    [AvaloniaFact]
    public void Lists_a_recent_map_and_raises_activation_on_click()
    {
        var view = new WelcomeView { RecentMaps = new[] { @"C:\maps\a.w3x" } };
        var window = new Window { Width = 500, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var activated = new List<string>();
        view.MapActivated += (_, path) => activated.Add(path);

        var button = view.GetVisualDescendants().OfType<Button>()
            .Single(b => (b.Content as string) == "a.w3x");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(new[] { @"C:\maps\a.w3x" }, activated);
    }

    [AvaloniaFact]
    public void Hides_the_blank_map_action_when_not_offered()
    {
        var view = new WelcomeView { ShowNewBlankMap = false };
        var window = new Window { Width = 500, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var blank = view.GetVisualDescendants().OfType<Button>()
            .Single(b => (b.Content as string) == "New Blank Map");
        Assert.False(blank.IsVisible);
    }

    [AvaloniaFact]
    public void Shows_an_empty_note_when_there_is_nothing_to_list()
    {
        var view = new WelcomeView();
        var window = new Window { Width = 500, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var note = view.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == "No recent maps yet.");
        Assert.True(note.IsVisible);
    }
}
