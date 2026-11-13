using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CursedPasswordManager.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Changed(name); return true; }
}
public sealed class RelayCommand(Action action, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => action();
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
}
