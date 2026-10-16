using System.Windows.Input;

namespace Wc3.Studio.Controls;

/// <summary>
/// A tiny <see cref="ICommand"/> over a delegate, for wiring key gestures and menu accelerators in
/// XAML without a view model. Avalonia key bindings need an ICommand, and a one-line action does
/// not deserve a class of its own per command, so this is the one shared implementation.
/// </summary>
public sealed class ActionCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public ActionCommand(Action execute) : this(_ => execute()) { }

    public ActionCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>Never raised, since a command built over a static action does not change its
    /// availability on its own. Declared because ICommand requires it.</summary>
#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);
}
