using System.Windows.Input;

namespace EasyConnect.Helpers;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception> _onException;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute, Action<Exception> onException, Func<bool>? canExecute = null)
        : this(_ => execute(), onException, canExecute) { }

    public AsyncRelayCommand(Func<object?, Task> execute, Action<Exception> onException, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(onException);
        _execute = execute;
        _onException = onException;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    // ICommand requires a void entry point. Observe failures here so an asynchronous
    // operation cannot surface as an unhandled exception on WPF's dispatcher.
    public async void Execute(object? parameter)
    {
        try
        {
            await ExecuteAsync(parameter);
        }
        catch (Exception exception)
        {
            _onException(exception);
        }
    }

    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(null))
        {
            return;
        }

        _isExecuting = true;
        try
        {
            NotifyCanExecuteChanged();
            await _execute(parameter);
        }
        finally
        {
            _isExecuting = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
