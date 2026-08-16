using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ELKA.PowerThrottleControl.Models;
using ELKA.PowerThrottleControl.Services;

namespace ELKA.PowerThrottleControl;

public partial class GeneralNetworkPanel : UserControl
{
    private readonly ApplicationDiscoveryService _discovery = new();
    private readonly GeneralFirewallService _firewall = new();
    private readonly ICollectionView _view;
    private bool _hasLoaded;

    public ObservableCollection<ApplicationEntry> Applications { get; } = [];

    public GeneralNetworkPanel()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(Applications);
        _view.Filter = FilterApplication;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_hasLoaded) return;
        _hasLoaded = true;
        await DiscoverAsync();
    }

    private async void Discover_Click(object sender, RoutedEventArgs e) => await DiscoverAsync();

    private async Task DiscoverAsync()
    {
        SetBusy(true, "Searching registry entries and Start Menu shortcuts…");
        try
        {
            var discovered = await Task.Run(_discovery.Discover);
            Applications.Clear();
            foreach (var app in discovered) Applications.Add(app);
            _view.Refresh();
            UpdateCount();
            StatusText.Text = discovered.Count == 0
                ? "No installed applications with usable executable paths were found."
                : $"Found {discovered.Count:N0} applications. Use the search box to narrow the list.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Application search failed.";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Search failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private async void AllowPorts_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0) { ShowNothingSelected(); return; }
        if (!GeneralFirewallService.TryNormalizePorts(PortsBox.Text, out var ports, out var error))
        {
            MessageBox.Show(Window.GetWindow(this), error + "\n\nExamples: 80,443   5000-5010   80,443,5000-5010", "Invalid ports", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!TrySettings(out var profiles, out var inbound, out var outbound)) return;
        var protocols = TcpRadio.IsChecked == true ? new[] { "TCP" } : UdpRadio.IsChecked == true ? new[] { "UDP" } : new[] { "TCP", "UDP" };
        SetBusy(true, $"Waiting for administrator permission to allow {string.Join(" and ", protocols)} ports {ports}…");
        try
        {
            await ReportAsync(selected, await _firewall.AllowPortsAsync(selected, ports, protocols, inbound, outbound, profiles),
                $"ports {ports} ({string.Join("/", protocols)})");
        }
        catch (Exception ex) { ShowFailure(ex); }
        finally { SetBusy(false); }
    }

    private async void AllowAll_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0) { ShowNothingSelected(); return; }
        if (!TrySettings(out var profiles, out var inbound, out var outbound)) return;
        var answer = MessageBox.Show(Window.GetWindow(this),
            $"This allows all protocols and ports for {selected.Count} selected application(s), using the chosen direction and {profiles} profile(s).\n\nContinue?",
            "Allow all application traffic?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        SetBusy(true, "Waiting for administrator permission to allow all selected application traffic…");
        try { await ReportAsync(selected, await _firewall.AllowAllTrafficAsync(selected, inbound, outbound, profiles), "all application traffic"); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { SetBusy(false); }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0) { ShowNothingSelected(); return; }
        SetBusy(true, "Waiting for administrator permission to remove general ELKA Network rules…");
        try { await ReportAsync(selected, await _firewall.RemoveRulesAsync(selected), "ELKA Network rules removed"); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { SetBusy(false); }
    }

    private void List_Click(object sender, RoutedEventArgs e)
    {
        var result = _firewall.OpenRulesList();
        StatusText.Text = result.WasCancelled ? "Administrator permission was cancelled." : result.ErrorMessage ?? "Opened the general ELKA Network rule list.";
        if (result.ErrorMessage is not null) MessageBox.Show(Window.GetWindow(this), result.ErrorMessage, "Unable to list rules", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async Task ReportAsync(IReadOnlyList<ApplicationEntry> selected, FirewallActionResult result, string description)
    {
        await Task.Yield();
        if (result.WasCancelled) { StatusText.Text = "Administrator permission was cancelled; no firewall settings were changed."; return; }
        var successful = result.Successes.Count(value => value);
        var failed = selected.Count - successful;
        StatusText.Text = failed == 0 ? $"Configured {description} for {successful} application(s)." : $"Configured {successful}; {failed} application(s) failed or did not complete.";
        if (failed > 0) MessageBox.Show(Window.GetWindow(this), result.ErrorMessage ?? "Review the elevated command window output.", "Some firewall commands failed", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private bool TrySettings(out string profiles, out bool inbound, out bool outbound)
    {
        inbound = InboundCheckBox.IsChecked == true;
        outbound = OutboundCheckBox.IsChecked == true;
        if (!inbound && !outbound)
        {
            profiles = string.Empty;
            MessageBox.Show(Window.GetWindow(this), "Select Inbound, Outbound, or both.", "No direction selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var values = new List<string>();
        if (PrivateProfileCheckBox.IsChecked == true) values.Add("private");
        if (DomainProfileCheckBox.IsChecked == true) values.Add("domain");
        if (PublicProfileCheckBox.IsChecked == true) values.Add("public");
        profiles = string.Join(',', values);
        if (values.Count > 0) return true;
        MessageBox.Show(Window.GetWindow(this), "Select at least one Windows Firewall profile.", "No profile selected", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_view is null) return;
        _view.Refresh();
        UpdateCount();
    }

    private bool FilterApplication(object item)
    {
        if (item is not ApplicationEntry app) return false;
        var text = SearchBox?.Text.Trim();
        return string.IsNullOrWhiteSpace(text) || app.DisplayName.Contains(text, StringComparison.CurrentCultureIgnoreCase)
               || app.ExecutablePath.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectAllCheckBox.IsChecked == true;
        foreach (ApplicationEntry app in _view) app.IsSelected = selected;
    }

    private List<ApplicationEntry> Selected() => Applications.Where(app => app.IsSelected).ToList();
    private void UpdateCount()
    {
        if (_view is null || CountText is null) return;
        CountText.Text = $"{_view.Cast<object>().Count():N0} shown";
    }
    private void ShowNothingSelected() => MessageBox.Show(Window.GetWindow(this), "Check at least one application first.", "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);
    private void ShowFailure(Exception ex) { StatusText.Text = "The firewall action failed."; MessageBox.Show(Window.GetWindow(this), ex.Message, "Firewall command failed", MessageBoxButton.OK, MessageBoxImage.Error); }

    private void SetBusy(bool busy, string? text = null)
    {
        DiscoverButton.IsEnabled = AllowPortsButton.IsEnabled = AllowAllButton.IsEnabled = RemoveButton.IsEnabled = ListButton.IsEnabled = !busy;
        if (text is not null) StatusText.Text = text;
    }
}
