using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ELKA.PowerThrottleControl.Models;

public sealed class NetworkApplicationEntry : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string DisplayName { get; init; }
    public required string ExecutablePath { get; init; }
    public required string Architecture { get; init; }
    public bool IsRunning { get; init; }
    public string StatusText => IsRunning ? "Running" : "Installed";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
