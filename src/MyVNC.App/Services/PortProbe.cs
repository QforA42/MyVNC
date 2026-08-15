using System.Net.Sockets;

namespace MyVNC.App.Services;

/// <summary>Best-effort TCP reachability check, shared by the form's "Testa anslutning" button
/// and the dashboard's background SSH-availability check.</summary>
public static class PortProbe
{
    public static async Task<bool> IsOpenAsync(string host, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask || !client.Connected)
                return false;
            await connectTask;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
