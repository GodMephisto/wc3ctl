// tests/Wc3.Studio.Tests/SchemaChangelogPanelTests.cs
using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Wc3.Commands;
using Wc3.Studio;
using Wc3.Studio.Controls;
using Wc3.Studio.Panels;

using Wc3.Tests;

namespace Wc3.Studio.Tests;

/// <summary>
/// The schema changelog panel over temp schema folders. Pins that two chosen builds produce the
/// command's field-level diff in the list, so the panel is a thin view over
/// GamedataChangelogCommand rather than a second diff implementation.
/// </summary>
public class SchemaChangelogPanelTests
{
    private const string OlderBuild = "TFT-v1.30.0.00001";
    private const string NewerBuild = "TFT-v1.35.0.00001";

    private static string TempSchemas()
    {
        var root = Path.Combine(Path.GetTempPath(), "wc3-studio-changelog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // ugol's maximum widens from 100 to 300, a "bounds" change the panel must surface.
        GamedataFixtures.Save(root, OlderBuild, slk: "units/unitmetadata.slk",
            fields: new[] { GamedataFixtures.Field("ugol", "int", "1", "100", true) });
        GamedataFixtures.Save(root, NewerBuild, slk: "units/unitmetadata.slk",
            fields: new[] { GamedataFixtures.Field("ugol", "int", "1", "300", true) });
        return root;
    }

    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;

    [AvaloniaFact]
    public void Compare_shows_the_field_level_diff_between_two_builds()
    {
        var root = TempSchemas();
        try
        {
            var view = new SchemaChangelogView();
            var window = new Window { Width = 1000, Height = 700, Content = view };
            window.Show();
            view.ShowMap(new MapSession { SchemasDir = root });
            window.UpdateLayout();

            // The panel defaults to oldest -> newest, which is the meaningful direction.
            Assert.Equal(OlderBuild, Field<SearchableComboBox>(view, "BuildACombo").SelectedId);
            Assert.Equal(NewerBuild, Field<SearchableComboBox>(view, "BuildBCombo").SelectedId);

            view.Compare();
            window.UpdateLayout();

            var list = Field<ListBox>(view, "ChangeList");
            var rows = (list.ItemsSource as IEnumerable)!.Cast<SchemaChangelogView.ChangeRow>().ToList();
            Assert.Contains(rows, r => r.Display.Contains("ugol") && r.Detail.Contains("max"));
            Assert.Contains("bounds changed", Field<TextBlock>(view, "SummaryText").Text);
        }
        finally { Directory.Delete(root, true); }
    }
}
