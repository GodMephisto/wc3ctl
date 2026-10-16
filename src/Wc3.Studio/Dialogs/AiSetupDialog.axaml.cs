using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Wc3.Mcp.Setup;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Dialogs;

/// <summary>
/// The one-click Connect AI Apps dialog: lists every supported client with its detection and
/// registration state, registers the server in all detected clients, and copies a starter prompt.
/// The logic lives in <see cref="AiSetup"/>, so this class only renders and relays clicks. The
/// environment is a settable property so a test can drive it against a temp profile.
/// </summary>
public partial class AiSetupDialog : Window
{
    private readonly string? _sourceMapPath;

    public AiSetupDialog() : this(null) { }

    /// <summary>Builds the dialog, seeding the starter prompt with the open Source map path.</summary>
    public AiSetupDialog(string? sourceMapPath)
    {
        InitializeComponent();
        _sourceMapPath = sourceMapPath;
        Refresh();
    }

    /// <summary>The setup environment, defaulting to the real machine. Tests inject a temp one.</summary>
    public SetupEnvironment Env { get; set; } = SetupEnvironment.Current;

    /// <summary>The server name the dialog reports registration for.</summary>
    public string ServerName { get; set; } = SetupProduct.Wc3ctl.DefaultServerName;

    /// <summary>Rebuilds the client rows and the summary line from the current environment.</summary>
    public void Refresh()
    {
        var statuses = AiSetup.Statuses(Env, ServerName);
        ClientList.Children.Clear();
        foreach (var status in statuses)
            ClientList.Children.Add(BuildRow(status));
        int detected = statuses.Count(s => s.Detected);
        int registered = statuses.Count(s => s.Registered);
        ConnectButton.IsEnabled = detected > 0;
        if (string.IsNullOrEmpty(LogText.Text))
            LogText.Text = detected == 0
                ? "No supported AI app was found on this PC."
                : $"{detected} AI app(s) found, {registered} already set up.";
    }

    /// <summary>Runs the shared setup path to register the server in every detected client, off the
    /// UI thread, then reports the log and refreshes the states.</summary>
    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        LogText.Text = "Connecting to the detected AI apps…";
        var (code, log) = await Task.Run(() => AiSetup.ConnectAll(Env, null));
        LogText.Text = string.IsNullOrWhiteSpace(log)
            ? (code == 0 ? "Done." : $"Finished with exit code {code}.")
            : log.Trim();
        Refresh();
    }

    private async void OnCopyPromptClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;
        await clipboard.SetTextAsync(AiSetup.StarterPrompt(_sourceMapPath));
        LogText.Text = "Starter prompt copied to the clipboard.";
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>One client row: name and id on the left, the state on the right, in the standard card.</summary>
    private static Control BuildRow(AiSetup.ClientStatus status)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        var left = new StackPanel { Spacing = 1 };
        left.Children.Add(new TextBlock { Text = status.DisplayName, FontWeight = FontWeight.SemiBold });
        left.Children.Add(new TextBlock { Text = status.Id, FontSize = 11, Foreground = StudioPalette.Muted });
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var state = new TextBlock
        {
            Text = status.State,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = StateBrush(status),
        };
        Grid.SetColumn(state, 1);
        grid.Children.Add(state);

        return new Card { Child = grid };
    }

    private static IBrush StateBrush(AiSetup.ClientStatus status) =>
        !status.Detected ? StudioPalette.Muted
        : status.Registered ? StudioPalette.Ok
        : status.State == "found" ? StudioPalette.Info
        : StudioPalette.Warn;
}
