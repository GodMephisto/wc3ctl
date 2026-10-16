using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Wc3.Commands;

namespace Wc3.Studio.Dialogs;

/// <summary>
/// The app Settings dialog: two folder inputs (Warcraft III install and default map folder),
/// each with an OS folder picker via <see cref="Controls.FilePathBox"/>, and a checkbox that
/// adds or removes the Windows Explorer right-click entry through
/// <see cref="FileAssociationCommand"/>. Opened modally with the current values, it returns the
/// edited <see cref="StudioSettings"/> on Save, or null on Cancel, so the caller only persists
/// and applies when the user actually saves.
/// </summary>
public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
    }

    /// <summary>Builds the dialog seeded with the current settings so both boxes show the
    /// values already in effect (empty boxes mean auto-detect / none) and the checkbox reflects
    /// whether the Explorer entry is currently registered.</summary>
    public SettingsDialog(StudioSettings current) : this()
    {
        GameDirBox.Text = current.GameDir;
        MapsDirBox.Text = current.MapsDir;
        AssociateCheck.IsChecked = FileAssociationCommand.IsRegistered();
        // The registry is a Windows feature only, so the toggle is shown disabled elsewhere.
        AssociateCheck.IsEnabled = FileAssociationCommand.IsSupported;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        ApplyAssociation();
        Close(new StudioSettings
        {
            GameDir = Normalize(GameDirBox.Text),
            MapsDir = Normalize(MapsDirBox.Text),
        });
    }

    /// <summary>Registers or removes the Explorer entry when the checkbox disagrees with the
    /// current state. Best effort, a registry that refuses the write leaves the app usable.</summary>
    private void ApplyAssociation()
    {
        if (!FileAssociationCommand.IsSupported)
            return;
        bool wanted = AssociateCheck.IsChecked == true;
        if (wanted == FileAssociationCommand.IsRegistered())
            return;
        if (wanted)
            FileAssociationCommand.Register(StudioExePath);
        else
            FileAssociationCommand.Unregister();
    }

    /// <summary>The running Studio executable, or the one beside this assembly when the process
    /// path is not a normal exe (for example under the test host).</summary>
    private static string StudioExePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Wc3.Studio.exe");

    /// <summary>Empty or whitespace becomes null, so a cleared box means auto-detect / none.</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
