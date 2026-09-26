using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MyVNC.App.Models;
using MyVNC.App.Services;
using MyVNC.Rfb;

namespace MyVNC.App;

/// <summary>
/// The connection launcher: a compact saved-connections list. The new/edit-connection form,
/// settings, help and about all live in full-page views that swap in over the dashboard (opened
/// via the top-bar icons, closed via each page's back arrow) rather than floating dialogs, so
/// every screen shares the same surface as the host list. Stays open so the user can start any
/// number of simultaneous sessions — each "Anslut" opens its own <see cref="SessionWindow"/>
/// rather than taking over this window.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>The live dashboard window, so activation requests from a second app launch
    /// (see <see cref="Services.SingleInstance"/>) always reach whichever instance is actually
    /// current — a language change replaces this window entirely (see OnLanguageChanged).</summary>
    public static MainWindow? Current { get; private set; }

    private readonly ConnectionStore _store = new();
    private readonly ObservableCollection<ConnectionProfile> _profiles = [];
    private ConnectionProfile? _editingProfile;
    private bool _suppressSettingsEvents;

    public MainWindow() : this(openSettings: false) { }

    public MainWindow(bool openSettings, string? autoConnectProfileId = null)
    {
        InitializeComponent();
        Interop.DarkTitleBar.Apply(this);
        Icon = Interop.AppIconFactory.Create();

        Current = this;
        Closed += (_, _) => { if (Current == this) Current = null; };

        ConnectionsList.ItemsSource = _profiles;
        foreach (var p in _store.Load())
            _profiles.Add(p);
        UpdateEmptyState();
        JumpListBuilder.Rebuild(_profiles);

        BuildLanguageList();
        BuildAddressPicker();
        BuildThemePicker();
        BuildSshTerminalPicker();
        LoadSettingsIntoUi();

        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;

        // Host reachability changes over time (a machine can suspend, or SSH can get opened in
        // its firewall after the fact) — re-check whenever the dashboard is loaded or regains
        // focus, rather than polling continuously.
        RefreshSshAvailability();
        Activated += (_, _) => RefreshSshAvailability();

        // Same idea for session-active state: cheap to recompute (no I/O, just checks in-memory
        // open tabs), so re-check whenever the dashboard regains focus. Activation alone isn't
        // enough, though — a connect can fail (or an auto-reconnect give up and close the tab)
        // while the dashboard already has focus, and then no activation follows to clear the
        // card's "connected" state, leaving Connect and its address dropdown disabled.
        RefreshSessionActiveState();
        Activated += (_, _) => RefreshSessionActiveState();
        SessionWindow.SessionsChanged += RefreshSessionActiveState;
        Closed += (_, _) => SessionWindow.SessionsChanged -= RefreshSessionActiveState;

        if (openSettings) ShowPage(SettingsPage);

        if (autoConnectProfileId is not null)
        {
            var profile = _profiles.FirstOrDefault(p => p.Id == autoConnectProfileId);
            // Taskbar jump list launch: replay whichever address was actually used last time,
            // not necessarily the profile's configured default.
            if (profile is not null) ConnectToProfile(profile, profile.LastUsedAddress);
        }
    }

    /// <summary>Called when a second app launch (desktop icon while already running, a taskbar
    /// jump-list shortcut, Start menu) was forwarded here by <see cref="Services.SingleInstance"/>
    /// instead of starting its own disconnected process. A "--connect &lt;id&gt;" arg opens that
    /// session (as a tab or window per Settings, since it's this same running instance); either
    /// way, bring the dashboard to the foreground so the activation is visible.</summary>
    public void HandleActivation(string[] args)
    {
        var connectFlagIndex = Array.IndexOf(args, "--connect");
        if (connectFlagIndex >= 0 && connectFlagIndex + 1 < args.Length)
        {
            var profile = _profiles.FirstOrDefault(p => p.Id == args[connectFlagIndex + 1]);
            if (profile is not null) ConnectToProfile(profile, profile.LastUsedAddress);
        }

        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Rebuilds the whole window on a fresh language, since our XAML resolves display
    /// text once (via the loc: markup extension) rather than through live bindings.</summary>
    private void OnLanguageChanged()
    {
        var wasSettingsOpen = SettingsPage.Visibility == Visibility.Visible;
        new MainWindow(wasSettingsOpen).Show();
        Close();
    }

    /// <summary>Swaps the dashboard out for one full-page view (settings, help, about, the
    /// new/edit-connection form) — mutually exclusive with the dashboard and every other page.</summary>
    private void ShowPage(UIElement page)
    {
        DashboardView.Visibility = Visibility.Collapsed;
        FormPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        HelpPage.Visibility = Visibility.Collapsed;
        AboutPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
    }

    private void ShowDashboard()
    {
        FormPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        HelpPage.Visibility = Visibility.Collapsed;
        AboutPage.Visibility = Visibility.Collapsed;
        DashboardView.Visibility = Visibility.Visible;
    }

    private void UpdateEmptyState()
        => EmptyStateText.Visibility = _profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ----- Search -----

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        SearchPlaceholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var view = CollectionViewSource.GetDefaultView(ConnectionsList.ItemsSource);
        if (query.Length == 0)
        {
            view.Filter = null;
        }
        else
        {
            view.Filter = item => item is ConnectionProfile p &&
                (p.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 p.Host.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 p.Fqdn.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 p.TailscaleIp.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 p.TailscaleFqdn.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ----- Settings page -----

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(SettingsPage);
        OpenLogButton.IsEnabled = AppLog.LogFileExists(); // re-check each time, not just at startup
    }

    // ----- About page -----

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var shortVersion = version?.Split('+')[0] ?? "?";
        AboutVersionText.Text = Loc.T("About.Version", shortVersion);

        ShowPage(AboutPage);
    }

    private void CloseAbout_Click(object sender, RoutedEventArgs e) => ShowDashboard();

    private void AboutLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
        e.Handled = true;
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e) => ShowPage(HelpPage);

    private void CloseHelp_Click(object sender, RoutedEventArgs e) => ShowDashboard();

    private void CloseSettings_Click(object sender, RoutedEventArgs e) => ShowDashboard();

    private void BuildLanguageList()
    {
        foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
            LanguageComboBox.Items.Add(new ComboBoxItem { Content = language.NativeName(), Tag = language });
    }

    private void BuildAddressPicker()
    {
        foreach (AddressKind kind in Enum.GetValues<AddressKind>())
            DefaultAddressComboBox.Items.Add(new ComboBoxItem { Content = Loc.T(kind.LocKey()), Tag = kind });
        DefaultAddressComboBox.SelectedIndex = 0;
    }

    private void BuildThemePicker()
    {
        foreach (ThemePreference preference in Enum.GetValues<ThemePreference>())
            ThemeComboBox.Items.Add(new ComboBoxItem { Content = Loc.T(preference.LocKey()), Tag = preference });
    }

    private void BuildSshTerminalPicker()
    {
        foreach (SshTerminalChoice choice in Enum.GetValues<SshTerminalChoice>())
            SshTerminalComboBox.Items.Add(new ComboBoxItem { Content = Loc.T(choice.LocKey()), Tag = choice });
    }

    private void LoadSettingsIntoUi()
    {
        _suppressSettingsEvents = true;

        foreach (ComboBoxItem item in LanguageComboBox.Items)
            if ((AppLanguage)item.Tag == App.Settings.Language)
                LanguageComboBox.SelectedItem = item;

        foreach (ComboBoxItem item in ThemeComboBox.Items)
            if ((ThemePreference)item.Tag == App.Settings.Theme)
                ThemeComboBox.SelectedItem = item;

        SessionModeToggle.IsChecked = App.Settings.SessionOpenMode == SessionOpenMode.Tab;
        UpdateSessionModeLabel();

        AutoReconnectToggle.IsChecked = App.Settings.AutoReconnect;
        UpdateAutoReconnectLabel();

        foreach (ComboBoxItem item in SshTerminalComboBox.Items)
            if ((SshTerminalChoice)item.Tag == App.Settings.SshTerminal)
                SshTerminalComboBox.SelectedItem = item;

        DebugLoggingToggle.IsChecked = App.Settings.DebugLogging;
        UpdateDebugLoggingLabel();
        OpenLogButton.IsEnabled = AppLog.LogFileExists();

        _suppressSettingsEvents = false;
    }

    private void UpdateSessionModeLabel()
        => SessionModeLabel.Text = Loc.T(App.Settings.SessionOpenMode == SessionOpenMode.Tab ? "Settings.SessionModeTab" : "Settings.SessionModeWindow");

    private void UpdateDebugLoggingLabel()
        => DebugLoggingLabel.Text = Loc.T(App.Settings.DebugLogging ? "Settings.DebugLoggingOn" : "Settings.DebugLoggingOff");

    private void DebugLoggingToggle_Changed(object sender, RoutedEventArgs e)
    {
        UpdateDebugLoggingLabel();
        if (_suppressSettingsEvents) return;
        App.Settings.DebugLogging = DebugLoggingToggle.IsChecked == true;
        App.SettingsStore.Save(App.Settings);
        AppLog.Enabled = App.Settings.DebugLogging;
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AppLog.LogFileExists()) return;
        try { Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{AppLog.FilePath}\"", UseShellExecute = true }); }
        catch { /* best-effort — nothing more useful to do if even Notepad won't launch */ }
    }

    private void UpdateAutoReconnectLabel()
        => AutoReconnectLabel.Text = Loc.T(App.Settings.AutoReconnect ? "Settings.AutoReconnectOn" : "Settings.AutoReconnectOff");

    private void AutoReconnectToggle_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAutoReconnectLabel();
        if (_suppressSettingsEvents) return;
        App.Settings.AutoReconnect = AutoReconnectToggle.IsChecked == true;
        App.SettingsStore.Save(App.Settings);
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsEvents || LanguageComboBox.SelectedItem is not ComboBoxItem item) return;
        var language = (AppLanguage)item.Tag;
        if (language == App.Settings.Language) return;

        App.Settings.Language = language;
        App.SettingsStore.Save(App.Settings);
        Loc.SetLanguage(language); // triggers OnLanguageChanged -> window rebuild
    }

    private void SessionModeToggle_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSessionModeLabel();
        if (_suppressSettingsEvents) return;
        App.Settings.SessionOpenMode = SessionModeToggle.IsChecked == true ? SessionOpenMode.Tab : SessionOpenMode.Window;
        App.SettingsStore.Save(App.Settings);
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsEvents || ThemeComboBox.SelectedItem is not ComboBoxItem item) return;
        var preference = (ThemePreference)item.Tag;
        if (preference == App.Settings.Theme) return;

        App.Settings.Theme = preference;
        App.SettingsStore.Save(App.Settings);
        ThemeManager.Reapply(); // repaints every open window in place — no restart, no rebuild
    }

    private void SshTerminalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsEvents || SshTerminalComboBox.SelectedItem is not ComboBoxItem item) return;
        App.Settings.SshTerminal = (SshTerminalChoice)item.Tag;
        App.SettingsStore.Save(App.Settings);
    }

    // ----- SSH -----

    /// <summary>Kicks off a background reachability check (port 22) for every saved profile —
    /// fire-and-forget, each one updates its own card once it resolves. Best-effort only: never
    /// blocks the UI, and a profile with no reachable SSH port just never shows the icon.</summary>
    private void RefreshSshAvailability()
    {
        foreach (var profile in _profiles)
            _ = CheckSshAvailabilityAsync(profile);
    }

    private async Task CheckSshAvailabilityAsync(ConnectionProfile profile)
    {
        var address = profile.ResolveAddress(profile.DefaultAddress);
        var available = await PortProbe.IsOpenAsync(address, 22, 800).ConfigureAwait(true);
        if (profile.IsSshAvailable == available) return;

        profile.IsSshAvailable = available;
        var index = _profiles.IndexOf(profile);
        if (index >= 0) _profiles[index] = profile; // force the card to re-read IsSshAvailable (no INotifyPropertyChanged on the model)
        ConnectionsList.Items.Refresh();
    }

    private void OpenSshTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ConnectionProfile profile) return;
        var address = profile.ResolveAddress(profile.DefaultAddress);
        if (!SshLauncher.Launch(address, profile.Username, App.Settings.SshTerminal))
            MessageBox.Show(this, Loc.T("Ssh.InvalidTarget"), Loc.T("Card.OpenSsh"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Cheap, synchronous (no I/O — just checks already-open tabs), so unlike SSH
    /// reachability this can just be recomputed outright rather than diffed per-profile.</summary>
    private void RefreshSessionActiveState()
    {
        var changed = false;
        for (var i = 0; i < _profiles.Count; i++)
        {
            var profile = _profiles[i];
            var addresses = profile.AvailableAddresses().Select(a => a.Value);
            var active = SessionWindow.HasConnectedSession(addresses, profile.Port);
            if (profile.IsSessionActive == active) continue;

            profile.IsSessionActive = active;
            _profiles[i] = profile; // force the card to re-read IsSessionActive (no INotifyPropertyChanged on the model)
            changed = true;
        }
        if (changed) ConnectionsList.Items.Refresh();
    }

    // ----- Forget SSH host key -----

    private void ForgetSshHostKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var addresses = new[] { HostBox.Text.Trim(), FqdnBox.Text.Trim(), TailscaleIpBox.Text.Trim(), TailscaleFqdnBox.Text.Trim() }
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToArray();

        ForgetSshHostKeyResultText.Visibility = Visibility.Visible;
        if (addresses.Length == 0)
        {
            ForgetSshHostKeyResultText.Foreground = (Brush)FindResource("DangerBrush");
            ForgetSshHostKeyResultText.Text = Loc.T("Form.ErrorHost");
            return;
        }

        ForgetSshHostKeyButton.IsEnabled = false;
        var cleared = SshLauncher.ForgetHostKeys(addresses);
        var pinsRemoved = HostTrust.Forget(addresses);
        ForgetSshHostKeyButton.IsEnabled = true;

        ForgetSshHostKeyResultText.Foreground = (Brush)FindResource("SuccessBrush");
        ForgetSshHostKeyResultText.Text = Loc.T("Form.ForgetSshHostKeyOk", cleared, pinsRemoved);
    }

    // ----- New/edit connection page -----

    private void NewConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        ExitEditMode();
        ShowPage(FormPage);
        HostBox.Focus();
    }

    private void HideOverlay()
    {
        ShowDashboard();
        ExitEditMode();
    }

    // ----- Test connection -----

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        var host = HostBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            ShowStartError(Loc.T("Form.ErrorHost"));
            return;
        }
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is <= 0 or > 65535)
        {
            ShowStartError(Loc.T("Form.ErrorPort"));
            return;
        }

        var defaultAddress = DefaultAddressComboBox.SelectedItem is ComboBoxItem selected ? (AddressKind)selected.Tag : AddressKind.HostIp;
        var address = ResolveAddress(host, FqdnBox.Text.Trim(), TailscaleIpBox.Text.Trim(), TailscaleFqdnBox.Text.Trim(), defaultAddress);

        TestConnectionButton.IsEnabled = false;
        TestConnectionResultText.Visibility = Visibility.Visible;
        TestConnectionResultText.Foreground = (Brush)FindResource("TextSecondaryBrush");
        TestConnectionResultText.Text = Loc.T("Form.TestConnection") + "...";

        var sw = Stopwatch.StartNew();
        if (await PortProbe.IsOpenAsync(address, port, 3000))
        {
            TestConnectionResultText.Foreground = (Brush)FindResource("SuccessBrush");
            TestConnectionResultText.Text = Loc.T("Form.TestConnectionOk", sw.ElapsedMilliseconds);
        }
        else
        {
            TestConnectionResultText.Foreground = (Brush)FindResource("DangerBrush");
            TestConnectionResultText.Text = Loc.T("Form.TestConnectionFail", address, port);
        }
        TestConnectionButton.IsEnabled = true;
    }

    // ----- Form -----

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        StartErrorText.Visibility = Visibility.Collapsed;

        var host = HostBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            ShowStartError(Loc.T("Form.ErrorHost"));
            return;
        }
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is <= 0 or > 65535)
        {
            ShowStartError(Loc.T("Form.ErrorPort"));
            return;
        }

        var name = NameBox.Text.Trim();
        var fqdn = FqdnBox.Text.Trim();
        var tailscaleIp = TailscaleIpBox.Text.Trim();
        var tailscaleFqdn = TailscaleFqdnBox.Text.Trim();
        var defaultAddress = DefaultAddressComboBox.SelectedItem is ComboBoxItem selected ? (AddressKind)selected.Tag : AddressKind.HostIp;
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        var viewOnly = ViewOnlyCheck.IsChecked == true;
        var actualSize = ActualSizeCheck.IsChecked == true;
        var receiveClipboard = ReceiveClipboardCheck.IsChecked == true;
        var sendClipboard = SendClipboardCheck.IsChecked == true;

        // Two profiles sharing an address (e.g. one saved by LAN IP, another by Tailscale FQDN
        // for the same physical host) defeats SessionWindow.FindActiveSession's duplicate-connect
        // guard, since that only dedups against a *single* profile's own candidate addresses —
        // letting the same server accumulate multiple live client connections and, per the
        // gray-screen investigation, plausibly serve a confused framebuffer. Block it at save time.
        if (RememberCheck.IsChecked == true || _editingProfile is not null)
        {
            var duplicate = FindDuplicateAddressProfile(host, fqdn, tailscaleIp, tailscaleFqdn, excluding: _editingProfile);
            if (duplicate is not null)
            {
                ShowStartError(Loc.T("Form.ErrorDuplicateAddress", duplicate.Value.Profile.Name, duplicate.Value.Address));
                return;
            }
        }

        if (_editingProfile is not null)
        {
            var updated = new ConnectionProfile
            {
                Id = _editingProfile.Id,
                Name = name,
                Host = host,
                Fqdn = fqdn,
                TailscaleIp = tailscaleIp,
                TailscaleFqdn = tailscaleFqdn,
                DefaultAddress = defaultAddress,
                LastUsedAddress = _editingProfile.LastUsedAddress,
                Port = port,
                Username = username,
                ProtectedPassword = string.IsNullOrEmpty(password) ? _editingProfile.ProtectedPassword : SecureStringCodec.Protect(password),
                LastConnected = _editingProfile.LastConnected,
                IsPinned = _editingProfile.IsPinned,
                ViewOnly = viewOnly,
                ActualSize = actualSize,
                ReceiveClipboard = receiveClipboard,
                SendClipboard = sendClipboard,
                // Both [JsonIgnore]/runtime-only — carry them over so editing a profile doesn't
                // make its SSH icon and "already connected" Connect-button state flicker off
                // until the next natural refresh (e.g. the window regaining focus) sets them again.
                IsSshAvailable = _editingProfile.IsSshAvailable,
                IsSessionActive = _editingProfile.IsSessionActive,
            };
            var index = _profiles.IndexOf(_editingProfile);
            if (index >= 0) _profiles[index] = updated;
            PersistProfiles();
            HideOverlay();
            // The carried-over IsSshAvailable/IsSessionActive above are only a stopgap against the
            // icon/button flickering off — if an address actually changed they could now be stale,
            // so re-check immediately rather than waiting for the window to next regain focus.
            RefreshSshAvailability();
            RefreshSessionActiveState();
            return;
        }

        if (RememberCheck.IsChecked == true)
        {
            var profile = new ConnectionProfile
            {
                Name = name,
                Host = host,
                Fqdn = fqdn,
                TailscaleIp = tailscaleIp,
                TailscaleFqdn = tailscaleFqdn,
                DefaultAddress = defaultAddress,
                LastUsedAddress = defaultAddress,
                Port = port,
                Username = username,
                ProtectedPassword = string.IsNullOrEmpty(password) ? null : SecureStringCodec.Protect(password),
                ViewOnly = viewOnly,
                ActualSize = actualSize,
                ReceiveClipboard = receiveClipboard,
                SendClipboard = sendClipboard,
            };
            _profiles.Insert(0, profile);
            PersistProfiles();
        }

        OpenSession(ResolveAddress(host, fqdn, tailscaleIp, tailscaleFqdn, defaultAddress), port, username, password, name,
            viewOnly, receiveClipboard, sendClipboard, actualSize, [host, fqdn, tailscaleIp, tailscaleFqdn]);
        HideOverlay();
    }

    /// <summary>Checks the given candidate addresses against every other saved profile's own
    /// addresses (case-insensitive) and returns the first collision, or null if none. <paramref
    /// name="excluding"/> is the profile being edited (skip it — comparing it against itself would
    /// always "collide").</summary>
    private (ConnectionProfile Profile, string Address)? FindDuplicateAddressProfile(
        string host, string fqdn, string tailscaleIp, string tailscaleFqdn, ConnectionProfile? excluding)
    {
        var candidates = new[] { host, fqdn, tailscaleIp, tailscaleFqdn }
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToList();
        if (candidates.Count == 0) return null;

        foreach (var other in _profiles)
        {
            if (ReferenceEquals(other, excluding)) continue;
            foreach (var (_, otherAddress) in other.AvailableAddresses())
            {
                foreach (var candidate in candidates)
                {
                    if (string.Equals(candidate, otherAddress, StringComparison.OrdinalIgnoreCase))
                        return (other, otherAddress);
                }
            }
        }
        return null;
    }

    private static string ResolveAddress(string host, string fqdn, string tailscaleIp, string tailscaleFqdn, AddressKind kind)
    {
        var address = kind switch
        {
            AddressKind.Fqdn => fqdn,
            AddressKind.TailscaleIp => tailscaleIp,
            AddressKind.TailscaleFqdn => tailscaleFqdn,
            _ => host,
        };
        return string.IsNullOrWhiteSpace(address) ? host : address;
    }

    private void ConnectionCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ConnectionProfile profile) return;
        ConnectToProfile(profile, profile.DefaultAddress);
    }

    private void ConnectCardButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ConnectionProfile profile) return;
        ConnectToProfile(profile, profile.DefaultAddress);
    }

    /// <summary>The small dropdown arrow next to "Anslut" — lets you connect via a specific
    /// address instead of the profile's configured default, for this one connection.</summary>
    private void ConnectDropdown_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ConnectionProfile profile) return;
        var available = profile.AvailableAddresses().ToList();
        if (available.Count == 0) return;

        var menu = new ContextMenu();
        foreach (var (kind, value) in available)
        {
            var item = new MenuItem { Header = $"{Loc.T(kind.LocKey())}: {value}" };
            item.Click += (_, _) => ConnectToProfile(profile, kind);
            menu.Items.Add(item);
        }
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    /// <summary>Connects using a specific address and remembers it as the one to replay from
    /// the taskbar jump list — a plain "Anslut" click passes the profile's own DefaultAddress,
    /// the dropdown passes whichever entry was picked, and either way that's what gets reused
    /// next time this connection is launched from the taskbar.</summary>
    private void ConnectToProfile(ConnectionProfile profile, AddressKind addressKind)
    {
        var address = profile.ResolveAddress(addressKind);
        var password = SecureStringCodec.Unprotect(profile.ProtectedPassword) ?? string.Empty;
        profile.LastConnected = DateTimeOffset.Now;
        profile.LastUsedAddress = addressKind;
        PersistProfiles();
        OpenSession(address, profile.Port, profile.Username, password, profile.DisplayTitle,
            profile.ViewOnly, profile.ReceiveClipboard, profile.SendClipboard, profile.ActualSize,
            profile.AvailableAddresses().Select(a => a.Value));
    }

    private void EditConnection_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ConnectionProfile profile) return;

        _editingProfile = profile;
        NameBox.Text = profile.Name;
        HostBox.Text = profile.Host;
        FqdnBox.Text = profile.Fqdn;
        TailscaleIpBox.Text = profile.TailscaleIp;
        TailscaleFqdnBox.Text = profile.TailscaleFqdn;
        foreach (ComboBoxItem item in DefaultAddressComboBox.Items)
            if ((AddressKind)item.Tag == profile.DefaultAddress)
                DefaultAddressComboBox.SelectedItem = item;
        PortBox.Text = profile.Port.ToString();
        UsernameBox.Text = profile.Username;
        PasswordBox.Password = SecureStringCodec.Unprotect(profile.ProtectedPassword) ?? string.Empty;
        RememberCheck.IsChecked = true;
        ViewOnlyCheck.IsChecked = profile.ViewOnly;
        ActualSizeCheck.IsChecked = profile.ActualSize;
        ReceiveClipboardCheck.IsChecked = profile.ReceiveClipboard;
        SendClipboardCheck.IsChecked = profile.SendClipboard;

        FormTitleText.Text = Loc.T("Form.EditConnection");
        ConnectButton.Content = Loc.T("Form.SaveChanges");
        StartErrorText.Visibility = Visibility.Collapsed;
        TestConnectionResultText.Visibility = Visibility.Collapsed;
        ForgetSshHostKeyResultText.Visibility = Visibility.Collapsed;

        ShowPage(FormPage);
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e) => HideOverlay();

    private void ExitEditMode()
    {
        _editingProfile = null;
        NameBox.Clear();
        HostBox.Clear();
        FqdnBox.Clear();
        TailscaleIpBox.Clear();
        TailscaleFqdnBox.Clear();
        DefaultAddressComboBox.SelectedIndex = 0;
        PortBox.Text = "5900";
        UsernameBox.Clear();
        PasswordBox.Clear();
        RememberCheck.IsChecked = true;
        ViewOnlyCheck.IsChecked = false;
        ActualSizeCheck.IsChecked = false;
        ReceiveClipboardCheck.IsChecked = true;
        SendClipboardCheck.IsChecked = true;
        StartErrorText.Visibility = Visibility.Collapsed;
        TestConnectionResultText.Visibility = Visibility.Collapsed;
        ForgetSshHostKeyResultText.Visibility = Visibility.Collapsed;

        FormTitleText.Text = Loc.T("Form.NewConnection");
        ConnectButton.Content = Loc.T("Form.Connect");
    }

    private void DeleteConnection_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ConnectionProfile profile) return;
        _profiles.Remove(profile);
        PersistProfiles();
        UpdateEmptyState();
        if (_editingProfile == profile) HideOverlay();
    }

    private void PersistProfiles()
    {
        _store.Save(_profiles);
        JumpListBuilder.Rebuild(_profiles);
    }

    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ConnectionProfile profile) return;
        profile.IsPinned = !profile.IsPinned;
        var index = _profiles.IndexOf(profile);
        if (index >= 0) _profiles[index] = profile; // force the card to re-read IsPinned (no INotifyPropertyChanged on the model)
        ConnectionsList.Items.Refresh();
        PersistProfiles();
    }

    private void ShowStartError(string message)
    {
        StartErrorText.Text = message;
        StartErrorText.Visibility = Visibility.Visible;
    }

    /// <summary>Opens a new session — either as a fresh top-level window, or as a tab in the
    /// already-open session window, depending on Settings.</summary>
    private void OpenSession(string host, int port, string username, string password, string displayName,
        bool viewOnly, bool receiveClipboard, bool sendClipboard, bool actualSize, IEnumerable<string> knownAddresses)
    {
        // Only one connection to a given host at a time, no matter which of its known addresses
        // (Host IP, FQDN, Tailscale IP, Tailscale FQDN) is used — two simultaneous clients against
        // the same wayvnc server were directly implicated in a real resource-contention incident
        // (each one's mouse-move traffic driving the other's framebuffer-update load), and a real
        // follow-up incident showed it happening via two *different* addresses for the same
        // machine, which a single-address comparison couldn't catch. Focus the existing session
        // instead of opening a second one.
        var candidates = knownAddresses.Where(a => !string.IsNullOrWhiteSpace(a)).Append(host);
        if (SessionWindow.FindActiveSession(candidates, port) is { } existing)
        {
            AppLog.Write($"Duplicate connect blocked for {host}:{port} — focusing existing session instead");
            SessionWindow.FocusExistingSession(existing);
            // The tab that blocked this may not be a *live* session — it can be sitting on a failed
            // connect, waiting out an auto-reconnect backoff, or (with auto-reconnect off) parked on
            // a "Disconnected" overlay forever. Clicking Connect on its card means "try again now",
            // so kick an immediate attempt rather than just raising a window that's stuck. No-op for
            // a genuinely connected session, which only gets focused.
            SessionWindow.RetryConnect(existing);
            return;
        }

        AppLog.Write($"Opening session: {host}:{port} (mode={App.Settings.SessionOpenMode}, viewOnly={viewOnly})");
        var options = new RfbConnectionOptions
        {
            Host = host,
            Port = port,
            Username = username,
            Password = password,
            DesktopNameOverride = displayName,
            ViewOnly = viewOnly,
            ReceiveClipboard = receiveClipboard,
            SendClipboard = sendClipboard,
            ActualSize = actualSize,
            VerifyServerIdentity = HostTrust.VerifyVncServerAsync,
        };

        if (App.Settings.SessionOpenMode == SessionOpenMode.Tab && SessionWindow.Shared is { } shared)
        {
            shared.AddTab(options);
            if (shared.WindowState == WindowState.Minimized) shared.WindowState = WindowState.Normal;
            shared.Activate();
            return;
        }

        new SessionWindow(options).Show();
    }
}
