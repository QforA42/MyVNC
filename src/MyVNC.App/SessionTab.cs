using System.ComponentModel;
using System.Runtime.CompilerServices;
using MyVNC.App.Controls;
using MyVNC.Rfb;

namespace MyVNC.App;

/// <summary>One connection within a <see cref="SessionWindow"/> — a persistent
/// <see cref="RemoteFramebufferControl"/> plus the bits of UI state (title, connecting/error
/// status, selected-ness) its tab header and content panel bind to.</summary>
public sealed class SessionTab(RfbConnectionOptions options) : INotifyPropertyChanged
{
    public RfbConnectionOptions Options { get; } = options;
    public RemoteFramebufferControl Framebuffer { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private string _title = string.IsNullOrWhiteSpace(options.DesktopNameOverride) ? options.Host : options.DesktopNameOverride;
    public string Title { get => _title; set => Set(ref _title, value); }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    private bool _isStatusVisible = true;
    public bool IsStatusVisible { get => _isStatusVisible; set => Set(ref _isStatusVisible, value); }

    private bool _isCloseVisible;
    public bool IsCloseVisible { get => _isCloseVisible; set => Set(ref _isCloseVisible, value); }

    private string _statusMessage = string.Empty;
    public string StatusMessage { get => _statusMessage; set => Set(ref _statusMessage, value); }

    /// <summary>Consecutive failed-reconnect count, driving the auto-reconnect backoff delay.
    /// Reset to 0 only once a connection has stayed up for a while (see
    /// <see cref="ConnectedAtUtc"/>), not merely on a completed handshake.</summary>
    public int ReconnectAttempt { get; set; }

    /// <summary>When the current connection completed its handshake, or null while not connected.
    /// A server that accepts the handshake and then drops the connection straight away (seen with
    /// wayvnc on omarchy01) would otherwise reset <see cref="ReconnectAttempt"/> on every cycle and
    /// keep the tab retrying every 2s forever.</summary>
    public DateTime? ConnectedAtUtc { get; set; }

    /// <summary>Cancels a pending auto-reconnect delay when the tab is closed manually.</summary>
    public CancellationTokenSource? ReconnectCts { get; set; }

    /// <summary>True while a ConnectAsync (TCP connect + handshake) is in flight, so a
    /// user-initiated "connect again" from the dashboard can't start a second attempt racing the
    /// first one over the same framebuffer.</summary>
    public bool IsConnecting { get; set; }

    /// <summary>Cancels an in-flight ConnectAsync (TCP connect + handshake) when the tab is closed
    /// manually — without this, closing a tab mid-reconnect only cancelled the *delay* before the
    /// attempt, not the attempt itself, letting DisconnectAsync race the still-running ConnectAsync
    /// over the same Framebuffer._client field.</summary>
    public CancellationTokenSource? ConnectCts { get; set; }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
