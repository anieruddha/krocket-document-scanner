using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace KRocketDocumentScanner.App.ViewModels.Mvvm;

/// <summary>Minimal INotifyPropertyChanged base — no external MVVM toolkit, to keep this
/// project's only real dependency being KRocketDocumentScanner.Core (see the .csproj comment).</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>A no-argument command with an optional CanExecute check, re-evaluated on demand.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>An async command that tracks IsRunning and refuses re-entrancy while already running
/// — prevents e.g. double-clicking "Open" from spawning two file dialogs at once.</summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool IsRunning => _isRunning;
    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _isRunning = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            // Deliberately NOT ConfigureAwait(false): Execute() is always invoked by a UI
            // control (Button, MenuItem) on the UI thread, which captures that thread's
            // SynchronizationContext at this await. Without capturing it, if the awaited
            // work resumes on a background thread (as it does here — our scanner calls all
            // route through SerialExecutor's own dedicated thread), the CanExecuteChanged
            // raise below would fire from that background thread, and any control reacting
            // to it touches Avalonia-owned state off the UI thread — a real crash this
            // project hit ("Call from invalid thread"), not a hypothetical one.
            await _execute();
        }
        finally
        {
            _isRunning = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Direct awaitable invocation for tests — bypasses the ICommand interface so a
    /// test can await completion instead of racing the fire-and-forget `async void` above.</summary>
    public Task ExecuteAsync() => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
