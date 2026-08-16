using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using ELKA.PowerThrottleControl.Models;
using ELKA.PowerThrottleControl.Services;

namespace ELKA.PowerThrottleControl;

public partial class MainWindow : Window
{
    private readonly ApplicationDiscoveryService _discoveryService = new();
    private readonly AppStateStore _stateStore = new();
    private readonly PowerThrottlingService _powerService = new();
    private readonly ICollectionView _applicationsView;
    private Dictionary<string, bool> _savedStates = new(StringComparer.OrdinalIgnoreCase);
    private ThemePreference _themePreference;

    public ObservableCollection<ApplicationEntry> Applications { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        _themePreference = ThemeService.LoadPreference();
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        Closed += MainWindow_Closed;
        DataContext = this;
        _applicationsView = CollectionViewSource.GetDefaultView(Applications);
        _applicationsView.Filter = FilterApplication;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _savedStates = await _stateStore.LoadAsync();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        ThemeContextMenu.PlacementTarget = ThemeButton;
        ThemeContextMenu.Placement = PlacementMode.Bottom;
        ThemeContextMenu.IsOpen = true;
    }

    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem
            || !Enum.TryParse(menuItem.Tag?.ToString(), ignoreCase: true, out ThemePreference preference))
        {
            return;
        }

        _themePreference = preference;
        ThemeService.SavePreference(preference);
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var isDark = ThemeService.Apply(Resources, _themePreference);
        ThemeButtonText.Text = $"Theme: {_themePreference}";

        foreach (var item in ThemeContextMenu.Items.OfType<MenuItem>())
        {
            item.IsCheckable = true;
            item.IsChecked = string.Equals(
                item.Tag?.ToString(), _themePreference.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        // Help Windows draw the title bar in a complementary light/dark style.
        Background = (System.Windows.Media.Brush)Resources["WindowBackgroundBrush"];
        ThemeButton.ToolTip = _themePreference == ThemePreference.System
            ? $"Following Windows ({(isDark ? "Dark" : "Light")})"
            : $"Current theme: {_themePreference}";
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_themePreference == ThemePreference.System)
        {
            Dispatcher.Invoke(ApplyTheme);
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
    }
    private async void SearchApplications_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Searching registry entries and Start Menu shortcuts…");
        try
        {
            var discovered = await Task.Run(_discoveryService.Discover);
            Applications.Clear();

            foreach (var app in discovered)
            {
                app.IsThrottlingDisabled = _savedStates.GetValueOrDefault(app.ExecutablePath);
                Applications.Add(app);
            }

            _applicationsView.Refresh();
            UpdateCount();
            StatusText.Text = discovered.Count == 0
                ? "No installed applications with usable executable paths were found."
                : $"Found {discovered.Count:N0} applications. Check one or more, then choose an action.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Application search failed.";
            MessageBox.Show(this, ex.Message, "Search failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DisablePowerThrottling_Click(object sender, RoutedEventArgs e) =>
        await ApplyPowerThrottlingAsync(disable: true);

    private async void EnablePowerThrottling_Click(object sender, RoutedEventArgs e) =>
        await ApplyPowerThrottlingAsync(disable: false);

    private async Task ApplyPowerThrottlingAsync(bool disable)
    {
        var selected = Applications.Where(app => app.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Check at least one application first.", "Nothing selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var action = disable ? "disable" : "enable";
        SetBusy(true, $"Waiting for the elevated command window to {action} power throttling…");
        try
        {
            var result = await _powerService.ApplyAsync(selected, disable);
            if (result.WasCancelled)
            {
                StatusText.Text = "Administrator permission was cancelled; no settings were changed.";
                return;
            }

            var successful = 0;
            for (var index = 0; index < selected.Count && index < result.Successes.Count; index++)
            {
                if (!result.Successes[index])
                {
                    continue;
                }

                selected[index].IsThrottlingDisabled = disable;
                _savedStates[selected[index].ExecutablePath] = disable;
                successful++;
            }

            await _stateStore.SaveAsync(_savedStates);
            var failed = selected.Count - successful;
            StatusText.Text = failed == 0
                ? $"Power throttling was {(disable ? "disabled" : "enabled")} for {successful:N0} application(s)."
                : $"Updated {successful:N0}; {failed:N0} command(s) failed or did not complete.";

            if (failed > 0)
            {
                MessageBox.Show(this,
                    result.ErrorMessage ?? "One or more powercfg commands failed. Review the command window output.",
                    "Some commands did not complete", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "The power throttling action failed.";
            MessageBox.Show(this, ex.Message, "Command failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ListPowerThrottling_Click(object sender, RoutedEventArgs e)
    {
        var result = _powerService.OpenAuthoritativeList();
        StatusText.Text = result.WasCancelled
            ? "Administrator permission was cancelled."
            : result.ErrorMessage ?? "Opened the authoritative Windows power throttling list.";

        if (result.ErrorMessage is not null)
        {
            MessageBox.Show(this, result.ErrorMessage, "Unable to list settings",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _applicationsView?.Refresh();
        UpdateCount();
    }

    private bool FilterApplication(object item)
    {
        if (item is not ApplicationEntry app)
        {
            return false;
        }

        var filter = FilterBox?.Text.Trim();
        return string.IsNullOrWhiteSpace(filter)
               || app.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
               || app.ExecutablePath.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var isChecked = SelectAllCheckBox.IsChecked == true;
        foreach (ApplicationEntry app in _applicationsView)
        {
            app.IsSelected = isChecked;
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        SearchApplicationsButton.IsEnabled = !busy;
        DisableButton.IsEnabled = !busy;
        EnableButton.IsEnabled = !busy;
        ListButton.IsEnabled = !busy;
        if (message is not null)
        {
            StatusText.Text = message;
        }
    }

    private void UpdateCount()
    {
        if (CountText is null)
        {
            return;
        }

        var visible = _applicationsView?.Cast<object>().Count() ?? 0;
        CountText.Text = $"{visible:N0} shown";
    }

    private void PowerNav_Click(object sender, RoutedEventArgs e)
    {
        GeneralNetworkWorkspace.Visibility = Visibility.Collapsed;
        NetworkWorkspace.Visibility = Visibility.Collapsed;
        SetActiveNavigation(PowerNavButton);
        StatusText.Visibility = Visibility.Visible;
    }

    private async void NetworkNav_Click(object sender, RoutedEventArgs e)
    {
        GeneralNetworkWorkspace.Visibility = Visibility.Visible;
        NetworkWorkspace.Visibility = Visibility.Collapsed;
        SetActiveNavigation(NetworkNavButton);
        StatusText.Visibility = Visibility.Collapsed;
        await GeneralNetworkWorkspace.EnsureLoadedAsync();
    }

    private async void VbanNav_Click(object sender, RoutedEventArgs e)
    {
        GeneralNetworkWorkspace.Visibility = Visibility.Collapsed;
        NetworkWorkspace.Visibility = Visibility.Visible;
        SetActiveNavigation(VbanNavButton);
        StatusText.Visibility = Visibility.Collapsed;
        await NetworkWorkspace.EnsureLoadedAsync();
    }

    private void SetActiveNavigation(Button active)
    {
        foreach (var button in new[] { PowerNavButton, NetworkNavButton, VbanNavButton })
        {
            button.Background = (System.Windows.Media.Brush)Resources[
                ReferenceEquals(button, active) ? "SelectionBrush" : "SurfaceAltBrush"];
        }
    }
}
