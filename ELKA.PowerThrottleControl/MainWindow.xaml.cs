using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
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
    private readonly HttpClient _updateClient = UpdateService.CreateClient();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Version _currentVersion = Assembly.GetExecutingAssembly().GetName().Version!;
    private readonly bool _portable = !UpdateLauncher.IsInstalledCopy();
    private AvailableUpdate? _availableUpdate;
    private bool _updateBusy;
    private readonly ManualApplicationStore _manualStore = new(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    private readonly Dictionary<string, ApplicationEntry> _manualApplications = new(StringComparer.OrdinalIgnoreCase);

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
        _applicationsView.SortDescriptions.Add(new SortDescription(nameof(ApplicationEntry.DisplayName), ListSortDirection.Ascending));
        _applicationsView.SortDescriptions.Add(new SortDescription(nameof(ApplicationEntry.ExecutablePath), ListSortDirection.Ascending));
        VersionText.Text = $"v{_currentVersion.ToString(3)} · {(_portable ? "Portable" : "Installed")}";
        UpdateButton.ToolTip = _portable
            ? "Check GitHub, then download the portable ZIP beside this copy."
            : "Check GitHub, then install the update and reopen this application.";
        var updateResult = Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith("--update-result="));
        StatusText.Text = updateResult switch
        {
            "--update-result=success" => $"Updated successfully to v{_currentVersion.ToString(3)}. Your settings have been kept.",
            "--update-result=cancelled" => "The update was cancelled. You can check for updates and try again whenever you are ready.",
            "--update-result=restart" => "The update is installed. Windows needs a restart to finish replacing files.",
            "--update-result=failed" => "The update could not finish. The application has reopened; you can try again.",
            _ => StatusText.Text
        };
        try
        {
            foreach (var path in _manualStore.LoadPaths())
            {
                var app = ExecutableApplication.TryCreate(path);
                if (app is null) continue;
                _manualApplications[app.ExecutablePath] = app;
                Applications.Add(app);
            }
            if (Applications.Count > 0 && updateResult is null)
                StatusText.Text = "Your added applications are loaded. Refresh power status to check their Windows settings.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not load saved applications: {ex.Message}";
        }
        UpdateCount();
    }

    private void AddApplicationButton_Click(object sender, RoutedEventArgs e)
    {
        AddApplicationPanel.Visibility = AddApplicationPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (AddApplicationPanel.Visibility == Visibility.Visible) ExecutablePathBox.Focus();
    }

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Choose the game or application's executable",
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) == true)
        {
            ExecutablePathBox.Text = picker.FileName;
            AddExecutableButton.Focus();
        }
    }

    private void ExecutablePathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && AddExecutableButton.IsEnabled)
        {
            e.Handled = true;
            AddExecutable_Click(sender, e);
        }
    }

    private void AddExecutable_Click(object sender, RoutedEventArgs e)
    {
        ApplicationEntry app;
        try { app = ExecutableApplication.Create(ExecutablePathBox.Text); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox.Show(this, ex.Message, "Choose an application EXE", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var existing = Applications.FirstOrDefault(item => item.ExecutablePath.Equals(app.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) app = existing;
        else Applications.Add(app);
        app.IsSelected = true;
        _manualApplications[app.ExecutablePath] = app;
        FilterBox.Clear();
        _applicationsView.Refresh();
        UpdateCount();
        ApplicationsGrid.SelectedItem = app;
        ApplicationsGrid.ScrollIntoView(app);
        StatusText.Text = $"{app.DisplayName} is selected. Click Disable power throttling to set it to OFF / never.";
        try { _manualStore.Remember(app.ExecutablePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusText.Text = "Application added for this session, but its path could not be saved.";
            MessageBox.Show(this, ex.Message, "Could not remember application", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _lifetime.Cancel();
        _updateClient.Dispose();
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updateBusy) return;
        _updateBusy = true;
        SetBusy(true);
        try
        {
            var updater = new UpdateService(_updateClient);
            if (_availableUpdate is null)
            {
                UpdateButton.Content = "Checking…";
                StatusText.Text = "Checking GitHub for a newer release…";
                _availableUpdate = await updater.CheckAsync(_currentVersion, _portable, _lifetime.Token);
                StatusText.Text = _availableUpdate is null
                    ? $"You are up to date (v{_currentVersion.ToString(3)})."
                    : _portable
                        ? $"v{_availableUpdate.Version} is available. Click Download ZIP to save it beside this portable copy."
                        : $"v{_availableUpdate.Version} is available. Click Install update to download it, install it, and reopen the app.";
                return;
            }

            var update = _availableUpdate;
            var downloadRoot = _portable
                ? Path.Combine(AppContext.BaseDirectory, "Updates")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElkaSoft", "ELKA.PowerThrottleControl", "Updates");
            var progress = new Progress<int>(percent =>
            {
                if (!_updateBusy || _lifetime.IsCancellationRequested) return;
                UpdateButton.Content = $"Downloading {percent}%";
                StatusText.Text = $"Downloading v{update.Version}… {percent}%";
            });
            UpdateButton.Content = "Downloading…";
            var download = await updater.DownloadAsync(update, downloadRoot, progress, _lifetime.Token);
            if (_portable)
            {
                UpdateLauncher.ShowDownload(download.Path);
                StatusText.Text = $"Portable ZIP verified and saved. Extract it to your USB folder when ready: {download.Path}";
            }
            else
            {
                UpdateButton.Content = "Starting update…";
                StatusText.Text = "Download verified. Preparing to close, install, and reopen…";
                await UpdateLauncher.PrepareInstallerAsync(download, _lifetime.Token);
                Application.Current.Shutdown();
            }
        }
        catch (Exception) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _updateBusy = false;
            _availableUpdate = null;
            var message = ex is OperationCanceledException ? "The update check timed out. Please try again." : ex.Message;
            if (ex is UnauthorizedAccessException && _portable)
                message = "This portable folder is not writable. Move the app to a writable folder on your USB drive and try again.";
            StatusText.Text = "The update could not complete. You can try again.";
            MessageBox.Show(this, message, "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _updateBusy = false;
            UpdateButton.Content = _availableUpdate is null ? "Check for updates"
                : _portable ? $"Download ZIP v{_availableUpdate.Version}" : $"Install update v{_availableUpdate.Version}";
            SetBusy(false);
        }
    }

    private async void SearchApplications_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Searching installed and running applications…");
        try
        {
            var discovered = await Task.Run(_discoveryService.Discover);
            // Re-read remembered paths so a previously disconnected game drive can reappear.
            string? savedPathsError = null;
            try
            {
                foreach (var path in _manualStore.LoadPaths())
                {
                    var app = ExecutableApplication.TryCreate(path);
                    if (app is not null) _manualApplications.TryAdd(app.ExecutablePath, app);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { savedPathsError = ex.Message; }
            var selectedPaths = Applications.Where(app => app.IsSelected).Select(app => app.ExecutablePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var merged = ExecutableApplication.Merge(discovered, _manualApplications.Values.Where(app => File.Exists(app.ExecutablePath)));
            Applications.Clear();
            foreach (var app in merged)
            {
                app.IsThrottlingDisabled = null;
                app.IsSelected = selectedPaths.Contains(app.ExecutablePath);
                Applications.Add(app);
            }
            _applicationsView.Refresh();
            UpdateCount();

            if (merged.Count == 0)
            {
                StatusText.Text = "No applications were found. Use Add application to choose an EXE directly.";
                if (savedPathsError is not null) StatusText.Text += $" Saved applications: {savedPathsError}";
                return;
            }

            StatusText.Text = $"Found {merged.Count:N0} applications. Waiting for Windows to confirm their real power status…";
            await RefreshPowerStatusAsync(keepWindowOpen: false);
            if (savedPathsError is not null) StatusText.Text += $" Saved applications: {savedPathsError}";
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
            MessageBox.Show(this, "Search all applications or add an EXE first, then refresh the Windows status.",
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
        UpdateButton.IsEnabled = !busy;
        AddApplicationButton.IsEnabled = !busy;
        AddApplicationPanel.IsEnabled = !busy;
        if (message is not null) StatusText.Text = message;
    }

    private void UpdateCount()
    {
        if (CountText is null) return;
        CountText.Text = $"{(_applicationsView?.Cast<object>().Count() ?? 0):N0} shown";
    }
}
