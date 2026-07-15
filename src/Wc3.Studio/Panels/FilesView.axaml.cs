using Avalonia.Controls;

namespace Wc3.Studio.Panels;

public partial class FilesView : UserControl, IMapPanel
{
    public FilesView()
    {
        InitializeComponent();
    }

    public void ShowMap(MapSession session)
    {
        FilesStubText.Text = $"Files panel — map: {session.MapPath}";
    }
}
