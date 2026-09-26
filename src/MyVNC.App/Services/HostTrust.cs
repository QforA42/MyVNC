using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using MyVNC.Rfb;

namespace MyVNC.App.Services;

/// <summary>
/// Trust-on-first-use pinning of server identities, for both the VNC connection's TLS
/// certificate (VeNCrypt X509Plain/TLSPlain) and the SFTP side channel's SSH host key.
/// wayvnc ships a self-signed certificate, so a CA chain can't vouch for it; instead the first
/// fingerprint seen for an address is shown to the user and, once accepted, pinned in
/// %APPDATA%\MyVNC\known_hosts.json. A later mismatch is a strong warning (default: refuse), and
/// a pinned TLS host that suddenly offers no TLS at all is refused outright as a downgrade.
/// </summary>
public static class HostTrust
{
    private static readonly Lock _lock = new();
    private static readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyVNC", "known_hosts.json");

    private static string TlsKey(string host, int port) => $"tls:{host.Trim().ToLowerInvariant()}:{port}";
    private static string SshKey(string host, int port) => $"ssh:{host.Trim().ToLowerInvariant()}:{port}";

    /// <summary>Plugged into <see cref="RfbConnectionOptions.VerifyServerIdentity"/>. Runs on a
    /// background thread; any prompt is marshalled to the UI thread.</summary>
    public static async Task<bool> VerifyVncServerAsync(RfbServerIdentity identity, CancellationToken ct)
    {
        var key = TlsKey(identity.Host, identity.Port);
        var pinned = Get(key);
        var endpoint = $"{identity.Host}:{identity.Port}";

        if (!identity.IsEncrypted)
        {
            if (pinned is null) return true; // this server has never used TLS with us — nothing to protect
            AppLog.WriteAlways($"TLS downgrade refused for {endpoint}: pinned certificate, but the server offered {identity.Security}");
            await ShowAsync(Loc.T("Trust.TlsDowngrade", endpoint, identity.Security), MessageBoxButton.OK, MessageBoxImage.Error).ConfigureAwait(false);
            return false;
        }

        var fingerprint = identity.CertificateSha256!;
        if (identity.CertificateChainValid || string.Equals(pinned, fingerprint, StringComparison.OrdinalIgnoreCase))
            return true;

        var message = pinned is null
            ? Loc.T("Trust.TlsNew", endpoint, fingerprint)
            : Loc.T("Trust.TlsChanged", endpoint, pinned, fingerprint);
        var accepted = await ShowAsync(message, MessageBoxButton.YesNo,
            pinned is null ? MessageBoxImage.Question : MessageBoxImage.Warning).ConfigureAwait(false) == MessageBoxResult.Yes;

        AppLog.WriteAlways($"TLS certificate for {endpoint} {(pinned is null ? "first seen" : "CHANGED")}: {fingerprint} — {(accepted ? "trusted by user" : "rejected")}");
        if (accepted) Set(key, fingerprint);
        return accepted;
    }

    /// <summary>For SSH.NET's HostKeyReceived event (synchronous, on the SFTP worker thread).
    /// <paramref name="fingerprint"/> is "algorithm SHA256:base64".</summary>
    public static bool VerifySshHostKey(string host, int port, string fingerprint)
    {
        var key = SshKey(host, port);
        var pinned = Get(key);
        if (string.Equals(pinned, fingerprint, StringComparison.Ordinal)) return true;

        var endpoint = port == 22 ? host : $"{host}:{port}";
        var message = pinned is null
            ? Loc.T("Trust.SshNew", endpoint, fingerprint)
            : Loc.T("Trust.SshChanged", endpoint, pinned, fingerprint);
        var accepted = Show(message, MessageBoxButton.YesNo,
            pinned is null ? MessageBoxImage.Question : MessageBoxImage.Warning) == MessageBoxResult.Yes;

        AppLog.WriteAlways($"SSH host key for {endpoint} {(pinned is null ? "first seen" : "CHANGED")}: {fingerprint} — {(accepted ? "trusted by user" : "rejected")}");
        if (accepted) Set(key, fingerprint);
        return accepted;
    }

    /// <summary>Removes every pinned identity (TLS and SSH, any port) for the given addresses.
    /// Returns how many entries were removed.</summary>
    public static int Forget(IEnumerable<string> addresses)
    {
        var hosts = addresses.Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim().ToLowerInvariant()).ToHashSet();
        lock (_lock)
        {
            var map = Load();
            var removed = map.Keys.Where(k => hosts.Contains(HostOf(k))).ToList();
            foreach (var k in removed) map.Remove(k);
            if (removed.Count > 0) Save(map);
            return removed.Count;
        }
    }

    // "tls:host:port" / "ssh:host:port" — the host itself may contain colons (IPv6), so split
    // off the prefix and the trailing port rather than splitting on every colon.
    private static string HostOf(string key)
    {
        var rest = key[(key.IndexOf(':') + 1)..];
        var lastColon = rest.LastIndexOf(':');
        return lastColon < 0 ? rest : rest[..lastColon];
    }

    private static string? Get(string key)
    {
        lock (_lock) return Load().GetValueOrDefault(key);
    }

    private static void Set(string key, string value)
    {
        lock (_lock)
        {
            var map = Load();
            map[key] = value;
            Save(map);
        }
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(_filePath))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_filePath)) ?? [];
        }
        catch (Exception ex)
        {
            // A corrupt store must not silently turn into "trust everything": every host then
            // counts as unknown and gets prompted again, which is the safe direction.
            AppLog.WriteAlways($"known_hosts.json unreadable, treating all hosts as unknown: {ex.Message}");
        }
        return [];
    }

    private static void Save(Dictionary<string, string> map)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        // Relaxed escaping keeps base64 fingerprints ('+', '/') readable for anyone comparing them by hand.
        File.WriteAllText(_filePath, JsonSerializer.Serialize(map, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
    }

    private static Task<MessageBoxResult> ShowAsync(string message, MessageBoxButton buttons, MessageBoxImage icon)
        => Application.Current.Dispatcher.InvokeAsync(() => ShowOnUiThread(message, buttons, icon)).Task;

    private static MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage icon)
        => Application.Current.Dispatcher.Invoke(() => ShowOnUiThread(message, buttons, icon));

    private static MessageBoxResult ShowOnUiThread(string message, MessageBoxButton buttons, MessageBoxImage icon)
    {
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
        // Default to the safe answer: Enter on a changed-key warning must never mean "trust it".
        var safeDefault = buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK;
        return owner is not null
            ? MessageBox.Show(owner, message, Loc.T("Trust.Title"), buttons, icon, safeDefault)
            : MessageBox.Show(message, Loc.T("Trust.Title"), buttons, icon, safeDefault);
    }
}
