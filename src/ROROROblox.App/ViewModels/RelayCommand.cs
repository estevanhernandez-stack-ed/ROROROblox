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

    /// <summary>
    /// Last-chance reporter for exceptions Execute swallows. Wired once at startup
    /// (App.WireGlobalExceptionHandlers) to log at Warning — not every command body
    /// catches everything, and a failure with no trace contradicts the "we just have
    /// evidence afterward" contract of the global nets. Null (e.g. before startup
    /// wiring) keeps the original swallow-quietly behavior.
    /// </summary>
    internal static Action<Exception>? OnExceptionSwallowed { get; set; }

    public bool CanExecute(object? parameter) => _canExecute is null || _canExecute(parameter);

    public async void Execute(object? parameter)
    {
        try
        {
            await _execute(parameter).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Swallowing is intentional — async void + WPF means a rethrow crashes the app,
            // and concrete commands (Add/Launch/etc.) catch + display modals for their own
            // failure modes. But swallowed must not mean invisible: report for the log.
            try { OnExceptionSwallowed?.Invoke(ex); } catch { }
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <summary>Forces a CanExecute requery on the WPF UI thread.</summary>
    public static void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
