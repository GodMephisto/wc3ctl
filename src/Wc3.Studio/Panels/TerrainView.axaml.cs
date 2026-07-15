using Avalonia.Controls;

namespace Wc3.Studio.Panels;

public partial class TerrainView : UserControl, IMapPanel
{
    public TerrainView()
    {
        InitializeComponent();
    }

    public void ShowMap(MapSession session)
    {
        TerrainStubText.Text = $"Terrain panel — map: {session.MapPath}";
    }
}
