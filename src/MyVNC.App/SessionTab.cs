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
    /// Reset to 0 on a successful connect.</summary>
    public int ReconnectAttempt { get; set; }

    /// <summary>Cancels a pending auto-reconnect delay when the tab is closed manually.</summary>
    public CancellationTokenSource? ReconnectCts { get; set; }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
