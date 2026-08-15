using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
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
/// The connection launcher: a compact saved-connections list. The new/edit-connection form and
/// the settings page both live in dismissable overlays (opened via the + / gear buttons, closed
/// by clicking outside them) rather than taking up permanent screen space. Stays open so the
/// user can start any number of simultaneous sessions — each "Anslut" opens its own
/// <see cref="SessionWindow"/> rather than taking over this window.
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
        BuildSshTerminalPicker();
        LoadSettingsIntoUi();

        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;

        // Host reachability changes over time (a machine can suspend, or SSH can get opened in
        // its firewall after the fact) — re-check whenever the dashboard is loaded or regains
        // focus, rather than polling continuously.
        RefreshSshAvailability();
        Activated += (_, _) => RefreshSshAvailability();

        if (openSettings) ShowSettingsOverlay();

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
        var wasSettingsOpen = SettingsOverlay.Visibility == Visibility.Visible;
        new MainWindow(wasSettingsOpen).Show();
        Close();
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

    // ----- Settings overlay -----

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettingsOverlay();

    private void ShowSettingsOverlay()
    {
        FormOverlay.Visibility = Visibility.Collapsed;
        HelpOverlay.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Visible;
        OpenLogButton.IsEnabled = AppLog.LogFileExists(); // re-check each time, not just at startup
    }

    private void SettingsOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => SettingsOverlay.Visibility = Visibility.Collapsed;

    private void HelpButton_Click(object sender, RoutedEventArgs e) => ShowHelpOverlay();

    private void ShowHelpOverlay()
    {
        FormOverlay.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Collapsed;
        HelpOverlay.Visibility = Visibility.Visible;
    }

    private void HelpOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => HelpOverlay.Visibility = Visibility.Collapsed;

    private void CloseHelp_Click(object sender, RoutedEventArgs e)
        => HelpOverlay.Visibility = Visibility.Collapsed;

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
        => SettingsOverlay.Visibility = Visibility.Collapsed;

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
        SshLauncher.Launch(address, profile.Username, App.Settings.SshTerminal);
    }

    // ----- New/edit connection overlay -----

    private void NewConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
        HelpOverlay.Visibility = Visibility.Collapsed;
        ExitEditMode();
        ShowOverlay();
        HostBox.Focus();
    }

    private void FormOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => HideOverlay();

    /// <summary>Swallows clicks on the form/settings card itself so they don't bubble to the
    /// backdrop and dismiss the overlay the user is actively interacting with.</summary>
    private void FormCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void ShowOverlay() => FormOverlay.Visibility = Visibility.Visible;

    private void HideOverlay()
    {
        FormOverlay.Visibility = Visibility.Collapsed;
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
            };
            var index = _profiles.IndexOf(_editingProfile);
            if (index >= 0) _profiles[index] = updated;
            PersistProfiles();
            HideOverlay();
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
            viewOnly, receiveClipboard, sendClipboard, actualSize);
        HideOverlay();
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
            profile.ViewOnly, profile.ReceiveClipboard, profile.SendClipboard, profile.ActualSize);
    }

    private void EditConnection_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ConnectionProfile profile) return;

        SettingsOverlay.Visibility = Visibility.Collapsed;
        HelpOverlay.Visibility = Visibility.Collapsed;
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

        ShowOverlay();
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
        bool viewOnly, bool receiveClipboard, bool sendClipboard, bool actualSize)
    {
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
