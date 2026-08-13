using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ELKA.PowerThrottleControl.Models;

public sealed class ApplicationEntry : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isThrottlingDisabled;

    public required string DisplayName { get; init; }
    public required string ExecutablePath { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsThrottlingDisabled
    {
        get => _isThrottlingDisabled;
        set
        {
            if (SetField(ref _isThrottlingDisabled, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText => IsThrottlingDisabled ? "OFF (disabled)" : "ON (enabled/default)";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

