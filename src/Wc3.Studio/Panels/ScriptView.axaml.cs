using Avalonia.Controls;

namespace Wc3.Studio.Panels;

public partial class ScriptView : UserControl, IMapPanel
{
    public ScriptView()
    {
        InitializeComponent();
    }

    public void ShowMap(MapSession session)
    {
        ScriptStubText.Text = $"Script panel — map: {session.MapPath}";
    }
}
