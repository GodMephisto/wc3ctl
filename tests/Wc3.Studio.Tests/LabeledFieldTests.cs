using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wc3.Studio.Controls;
using Xunit;

namespace Wc3.Studio.Tests;

public class LabeledFieldTests
{
    /// <summary>
    /// LabeledField is a UserControl (whose Content is already its [Content] property). Marking a
    /// SECOND property [Content] broke the control's own template load, so InitializeComponent never
    /// wired up Slot and every row rendered blank, the whole unit/doodad/map-info properties editors
    /// showed labels and editors as empty space. This guards that the template loads and both the
    /// label and the slotted editor are actually in the visual tree.
    /// </summary>
    [AvaloniaFact]
    public void RendersItsLabelAndSlottedEditor()
    {
        var editor = new TextBox();
        var field = new LabeledField { Label = "Hero Level", FieldContent = editor };
        var window = new Window { Content = field, Width = 300, Height = 120 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var labels = field.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Hero Level", labels);
        Assert.Contains(field.GetVisualDescendants().OfType<TextBox>(), tb => ReferenceEquals(tb, editor));
    }
}
