using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;  // ICommand lives in the BCL under this namespace; it is not a WPF type.
using Avalonia.Threading;

namespace LiveClaude.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _running;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        : this(p => { execute(p); return Task.CompletedTask; }, canExecute)
    {
    }

    public RelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>
    /// Avalonia has no CommandManager, and therefore no ambient "something changed, re-query every
    /// command" signal. Each command raises its own instead, which is both cheaper and more
    /// predictable than the WPF behaviour it replaces.
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() =>
        Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));

    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        _running = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(parameter);
        }
        catch (Exception ex)
        {
            // Fire and forget: a command handler cannot await, and a failed command should surface
            // without blocking the one that raised it.
            _ = Dialogs.ShowWarningAsync(ex.Message);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }
}
