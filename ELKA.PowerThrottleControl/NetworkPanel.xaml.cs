using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ELKA.PowerThrottleControl.Models;
using ELKA.PowerThrottleControl.Services;

namespace ELKA.PowerThrottleControl;

public partial class NetworkPanel : UserControl
{
    private readonly NetworkApplicationDiscoveryService _discovery = new();
    private readonly FirewallService _firewall = new();
    private bool _hasLoaded;

    public ObservableCollection<NetworkApplicationEntry> Applications { get; } = [];

    public NetworkPanel() => InitializeComponent();

    public async Task EnsureLoadedAsync()
    {
        if (_hasLoaded) return;
        _hasLoaded = true;
        await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        SetBusy(true, "Detecting installed and running VB-Audio applications…");
        try
        {
            var discovered = await Task.Run(_discovery.Discover);
            Applications.Clear();
            foreach (var app in discovered) Applications.Add(app);
            var running = discovered.Count(app => app.IsRunning);
            StatusText.Text = discovered.Count == 0
                ? "No supported VoiceMeeter, VBAN, Macro Buttons, Matrix, or Coconut executables were found."
                : $"Found {discovered.Count} VB-Audio executable(s); {running} running instance(s) selected automatically.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "VB-Audio application detection failed.";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Detection failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private async void AllowVban_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(VbanPortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show(Window.GetWindow(this), "Enter a UDP port from 1 through 65535.", "Invalid VBAN port", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await RunAsync((apps, profiles) => _firewall.AllowVbanAsync(apps, port, profiles), $"VBAN UDP port {port}");
    }

    private async void AllowFull_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0) { ShowNothingSelected(); return; }
        var profiles = GetProfiles();
        if (profiles is null) return;
        var answer = MessageBox.Show(Window.GetWindow(this),
            $"This advanced action allows all inbound and outbound protocols for {selected.Count} selected program(s) on the {profiles} profile(s).\n\nUse the narrower VBAN UDP action unless full application access is genuinely required. Continue?",
            "Allow all application traffic?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        await RunPreparedAsync(selected, profiles, _firewall.AllowFullAccessAsync, "full inbound and outbound application traffic");
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0) { ShowNothingSelected(); return; }
        SetBusy(true, "Waiting for administrator permission to remove ELKA firewall rules…");
        try { await ReportAsync(selected, await _firewall.RemoveElkaRulesAsync(selected), "ELKA firewall rules removed"); }
        finally { SetBusy(false); }
    }

    private void List_Click(object sender, RoutedEventArgs e)
    {
        var result = _firewall.OpenElkaRulesList();
        StatusText.Text = result.WasCancelled ? "Administrator permission was cancelled." : result.ErrorMessage ?? "Opened the ELKA firewall rule list.";
        if (result.ErrorMessage is not null) MessageBox.Show(Window.GetWindow(this), result.ErrorMessage, "Unable to list rules", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async Task RunAsync(Func<IReadOnlyList<NetworkApplicationEntry>, string, Task<FirewallActionResult>> action, string description)
    {
        var selected = Selected();
        if (selected.Count == 0) { ShowNothingSelected(); return; }
        var profiles = GetProfiles();
        if (profiles is null) return;
        await RunPreparedAsync(selected, profiles, action, description);
    }

    private async Task RunPreparedAsync(IReadOnlyList<NetworkApplicationEntry> selected, string profiles,
        Func<IReadOnlyList<NetworkApplicationEntry>, string, Task<FirewallActionResult>> action, string description)
    {
        SetBusy(true, $"Waiting for administrator permission to configure {description}…");
        try { await ReportAsync(selected, await action(selected, profiles), description); }
        catch (Exception ex)
        {
            StatusText.Text = "The firewall action failed.";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Firewall command failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private async Task ReportAsync(IReadOnlyList<NetworkApplicationEntry> selected, FirewallActionResult result, string description)
    {
        await Task.Yield();
        if (result.WasCancelled) { StatusText.Text = "Administrator permission was cancelled; no firewall settings were changed."; return; }
        var successful = result.Successes.Count(success => success);
        var failed = selected.Count - successful;
        StatusText.Text = failed == 0 ? $"Configured {description} for {successful} application(s)." : $"Configured {successful}; {failed} application(s) failed or did not complete.";
        if (failed > 0) MessageBox.Show(Window.GetWindow(this), result.ErrorMessage ?? "Review the elevated command window output.", "Some firewall commands failed", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private List<NetworkApplicationEntry> Selected() => Applications.Where(app => app.IsSelected).ToList();

    private string? GetProfiles()
    {
        var profiles = new List<string>();
        if (PrivateProfileCheckBox.IsChecked == true) profiles.Add("private");
        if (DomainProfileCheckBox.IsChecked == true) profiles.Add("domain");
        if (PublicProfileCheckBox.IsChecked == true) profiles.Add("public");
        if (profiles.Count > 0) return string.Join(',', profiles);
        MessageBox.Show(Window.GetWindow(this), "Select at least one Windows Firewall profile.", "No profile selected", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectAllCheckBox.IsChecked == true;
        foreach (var app in Applications) app.IsSelected = selected;
    }

    private void ShowNothingSelected() => MessageBox.Show(Window.GetWindow(this), "Check at least one VB-Audio application first.", "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);

    private void SetBusy(bool busy, string? text = null)
    {
        RefreshButton.IsEnabled = AllowVbanButton.IsEnabled = AllowFullButton.IsEnabled = RemoveButton.IsEnabled = ListButton.IsEnabled = !busy;
        if (text is not null) StatusText.Text = text;
    }
}
