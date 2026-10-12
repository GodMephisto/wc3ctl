// tests/Wc3.Studio.Tests/MinBuildStatusTests.cs
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Panels;

using Wc3.Tests;

namespace Wc3.Studio.Tests;

/// <summary>
/// The workspace status area's minimum-build line, over temp schema folders. Pins that the line
/// names the earliest build the map runs on, and stays hidden when there is nothing to say.
/// </summary>
public class MinBuildStatusTests
{
    private static string TempSchemas()
    {
        var root = Path.Combine(Path.GetTempPath(), "wc3-studio-minbuild-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        GamedataFixtures.Save(root, "ROC-v1.00", natives: new[] { "OldNative" });
        GamedataFixtures.Save(root, "TFT-v1.31.0.00001", natives: new[] { "OldNative", "NewNative" });
        return root;
    }

    private static MapDocument MapWith(string script)
    {
        var doc = BlankMap.Create();
        doc.AddOrReplaceRawFile("war3map.j", Encoding.UTF8.GetBytes(script));
        return doc;
    }

    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;

    [AvaloniaFact]
    public void The_workspace_shows_the_maps_earliest_build()
    {
        var root = TempSchemas();
        try
        {
            var workspace = new MapWorkspaceView();
            var window = new Window { Width = 1000, Height = 700, Content = workspace };
            window.Show();

            workspace.Session.Current = MapWith("""
                function main takes nothing returns nothing
                    call OldNative()
                endfunction
                """);
            workspace.Session.MapPath = "synthetic.w3x";
            workspace.Session.SchemasDir = root;

            workspace.RefreshMinBuildStatus();

            var text = Field<TextBlock>(workspace, "MinBuildText");
            Assert.True(text.IsVisible);
            Assert.Contains("ROC-v1.00", text.Text);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public void The_line_is_hidden_when_the_map_has_no_script()
    {
        var root = TempSchemas();
        try
        {
            var workspace = new MapWorkspaceView();
            var window = new Window { Width = 1000, Height = 700, Content = workspace };
            window.Show();

            workspace.Session.Current = BlankMap.Create();
            workspace.Session.MapPath = "synthetic.w3x";
            workspace.Session.SchemasDir = root;

            workspace.RefreshMinBuildStatus();

            Assert.False(Field<TextBlock>(workspace, "MinBuildText").IsVisible);
        }
        finally { Directory.Delete(root, true); }
    }
}
