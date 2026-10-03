using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Wc3.Studio.Dialogs;

/// <summary>
/// The app Settings dialog: two folder inputs (Warcraft III install and default map folder),
/// each with an OS folder picker via <see cref="Controls.FilePathBox"/>. Opened modally with
/// the current values, it returns the edited <see cref="StudioSettings"/> on Save, or null on
/// Cancel, so the caller only persists and applies when the user actually saves.
/// </summary>
public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
    }

    /// <summary>Builds the dialog seeded with the current settings so both boxes show the
    /// values already in effect (empty boxes mean auto-detect / none).</summary>
    public SettingsDialog(StudioSettings current) : this()
    {
        GameDirBox.Text = current.GameDir;
        MapsDirBox.Text = current.MapsDir;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnSave(object? sender, RoutedEventArgs e) => Close(new StudioSettings
    {
        GameDir = Normalize(GameDirBox.Text),
        MapsDir = Normalize(MapsDirBox.Text),
    });

    /// <summary>Empty or whitespace becomes null, so a cleared box means auto-detect / none.</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
