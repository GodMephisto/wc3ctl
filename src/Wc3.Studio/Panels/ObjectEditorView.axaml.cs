using Avalonia.Controls;

namespace Wc3.Studio.Panels;

public partial class ObjectEditorView : UserControl, IMapPanel
{
    public ObjectEditorView()
    {
        InitializeComponent();
    }

    public void ShowMap(MapSession session)
    {
        ObjectEditorStubText.Text = $"Object editor panel — map: {session.MapPath}";
    }
}
