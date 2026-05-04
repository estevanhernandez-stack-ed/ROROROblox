using System.Windows.Input;

namespace ROROROblox.App.ViewModels;

/// <summary>
/// Minimal <see cref="ICommand"/> for binding to MVVM commands without pulling in a full MVVM
/// framework. Async-friendly — the executor can return Task and the UI runs it without awaiting.
/// </summary>
internal sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        : this(p => { execute(p); return Task.CompletedTask; }, canExecute) { }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => { execute(); return Task.CompletedTask; }, canExecute is null ? null : _ => canExecute()) { }

    public RelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute is null || _canExecute(parameter);

    public async void Execute(object? parameter)
    {
        try
        {
            await _execute(parameter).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // RelayCommand swallowing is intentional — VM-level catch is the right place; surfacing
            // here would crash the UI. Concrete commands (Add/Launch/etc.) catch + display modals.
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
