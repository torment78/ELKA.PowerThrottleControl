using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Microsoft.Win32;
using ELKA.PowerThrottleControl.Models;
using ELKA.PowerThrottleControl.Services;

namespace ELKA.PowerThrottleControl;

public partial class MainWindow : Window
{
    private readonly ApplicationDiscoveryService _discoveryService = new();
    private readonly PowerThrottlingService _powerService = new();
    private readonly ICollectionView _applicationsView;
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
            || !Enum.TryParse(menuItem.Tag?.ToString(), true, out ThemePreference preference)) return;
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
            item.IsChecked = string.Equals(item.Tag?.ToString(), _themePreference.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        Background = (System.Windows.Media.Brush)Resources["WindowBackgroundBrush"];
        ThemeButton.ToolTip = _themePreference == ThemePreference.System
            ? $"Following Windows ({(isDark ? "Dark" : "Light")})"
            : $"Current theme: {_themePreference}";
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_themePreference == ThemePreference.System) Dispatcher.Invoke(ApplyTheme);
    }

    private void MainWindow_Closed(object? sender, EventArgs e) =>
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;

    private async void SearchApplications_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Searching installed applications…");
        try
        {
            var discovered = await Task.Run(_discoveryService.Discover);
            Applications.Clear();
            foreach (var app in discovered)
            {
                app.IsThrottlingDisabled = null;
                Applications.Add(app);
            }
            _applicationsView.Refresh();
            UpdateCount();

            if (discovered.Count == 0)
            {
                StatusText.Text = "No installed applications with usable executable paths were found.";
                return;
            }

            StatusText.Text = $"Found {discovered.Count:N0} applications. Waiting for Windows to confirm their real power status…";
            await RefreshPowerStatusAsync(keepWindowOpen: false);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Application search failed.";
            MessageBox.Show(this, ex.Message, "Search failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
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
            MessageBox.Show(this, "Check at least one application first.", "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetBusy(true, $"Waiting for the elevated command window to {(disable ? "disable" : "enable")} power throttling…");
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
                if (!result.Successes[index]) continue;
                selected[index].IsThrottlingDisabled = disable;
                successful++;
            }

            var failed = selected.Count - successful;
            StatusText.Text = failed == 0
                ? $"Windows {(disable ? "disabled" : "enabled")} power throttling for {successful:N0} application(s)."
                : $"Updated {successful:N0}; {failed:N0} command(s) failed or did not complete.";
            if (failed > 0)
            {
                MessageBox.Show(this, result.ErrorMessage ?? "Review the command window output.",
                    "Some commands did not complete", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "The power throttling action failed.";
            MessageBox.Show(this, ex.Message, "Command failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private async void ListPowerThrottling_Click(object sender, RoutedEventArgs e)
    {
        if (Applications.Count == 0)
        {
            MessageBox.Show(this, "Search all applications first, then refresh the Windows status.",
                "No application list", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SetBusy(true, "Waiting for Windows' authoritative power throttling list…");
        try { await RefreshPowerStatusAsync(keepWindowOpen: true); }
        finally { SetBusy(false); }
    }

    private async Task RefreshPowerStatusAsync(bool keepWindowOpen)
    {
        var result = await _powerService.GetAuthoritativeListAsync(keepWindowOpen);
        if (result.WasCancelled)
        {
            foreach (var app in Applications) app.IsThrottlingDisabled = null;
            StatusText.Text = "Administrator permission was cancelled. Status remains unknown rather than showing an incorrect red value.";
            return;
        }
        if (result.ErrorMessage is not null)
        {
            foreach (var app in Applications) app.IsThrottlingDisabled = null;
            StatusText.Text = "Windows could not confirm the power throttling list.";
            MessageBox.Show(this, result.ErrorMessage, "Unable to read Windows status", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        foreach (var app in Applications)
        {
            app.IsThrottlingDisabled = result.DisabledPaths.Contains(NormalizePath(app.ExecutablePath));
        }
        var matched = Applications.Count(app => app.IsThrottlingDisabled == true);
        StatusText.Text = $"Windows confirmed the status: {matched:N0} discovered application(s) are set to never throttle.";
    }

    private static string NormalizePath(string path)
    {
        try { return System.IO.Path.GetFullPath(path); }
        catch { return path; }
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _applicationsView?.Refresh();
        UpdateCount();
    }

    private bool FilterApplication(object item)
    {
        if (item is not ApplicationEntry app) return false;
        var filter = FilterBox?.Text.Trim();
        return string.IsNullOrWhiteSpace(filter)
               || app.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
               || app.ExecutablePath.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var isChecked = SelectAllCheckBox.IsChecked == true;
        foreach (ApplicationEntry app in _applicationsView) app.IsSelected = isChecked;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        SearchApplicationsButton.IsEnabled = !busy;
        DisableButton.IsEnabled = !busy;
        EnableButton.IsEnabled = !busy;
        ListButton.IsEnabled = !busy;
        if (message is not null) StatusText.Text = message;
    }

    private void UpdateCount()
    {
        if (CountText is null) return;
        CountText.Text = $"{(_applicationsView?.Cast<object>().Count() ?? 0):N0} shown";
    }
}