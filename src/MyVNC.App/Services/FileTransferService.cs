using System.IO;
using Renci.SshNet;

namespace MyVNC.App.Services;

public sealed record FileTransferResult(bool Success, string FileName, string? RemotePath, string? Error);
public sealed record RemoteFile(string Name, long SizeBytes, DateTime LastModifiedUtc);

/// <summary>Sends and fetches individual files to/from a host over SFTP (SSH, port 22), via a
/// fixed ~/myvnc-shared directory — RFB/VNC itself has no file-transfer capability, and wayvnc
/// doesn't implement any of the vendor extensions (TightVNC/UltraVNC) that add one. Reuses the
/// same username/password already stored for the VNC connection: wayvnc's documented PAM setup
/// means that's the same as the host's Linux login, and this is an in-process SFTP connection
/// (not a spawned process), so unlike SshLauncher there's no command-line exposure risk in
/// passing it directly.</summary>
public static class FileTransferService
{
    private const int SshPort = 22;
    private const string SharedDirectory = "myvnc-shared";

    public static async Task<FileTransferResult> UploadFileAsync(string host, string? username, string? password, string localFilePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(localFilePath);
        try
        {
            return await Task.Run(() =>
            {
                using var client = new SftpClient(host, SshPort, username ?? string.Empty, password ?? string.Empty);
                client.Connect();
                ct.ThrowIfCancellationRequested();

                if (!client.Exists(SharedDirectory))
                    client.CreateDirectory(SharedDirectory);

                var remoteName = fileName;
                var remotePath = $"{SharedDirectory}/{remoteName}";
                var suffix = 1;
                while (client.Exists(remotePath))
                {
                    var stem = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    remoteName = $"{stem} ({suffix++}){ext}";
                    remotePath = $"{SharedDirectory}/{remoteName}";
                }

                using var localStream = File.OpenRead(localFilePath);
                client.UploadFile(localStream, remotePath);

                AppLog.Write($"Uploaded file to {host}: {remotePath}");
                return new FileTransferResult(true, fileName, remotePath, null);
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Write($"File upload to {host} failed for '{fileName}': {ex.Message}");
            return new FileTransferResult(false, fileName, null, ex.Message);
        }
    }

    /// <summary>Lists the files (not subdirectories) currently in ~/myvnc-shared. Returns an
    /// empty list if the directory doesn't exist yet — nothing's been shared either direction.</summary>
    public static async Task<IReadOnlyList<RemoteFile>> ListSharedFilesAsync(string host, string? username, string? password, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            using var client = new SftpClient(host, SshPort, username ?? string.Empty, password ?? string.Empty);
            client.Connect();
            ct.ThrowIfCancellationRequested();

            if (!client.Exists(SharedDirectory)) return (IReadOnlyList<RemoteFile>)[];

            return client.ListDirectory(SharedDirectory)
                .Where(f => f.IsRegularFile)
                .Select(f => new RemoteFile(f.Name, f.Length, f.LastWriteTimeUtc))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, ct).ConfigureAwait(false);
    }

    public static async Task<FileTransferResult> DownloadFileAsync(string host, string? username, string? password, string remoteFileName, string localDestinationDirectory, CancellationToken ct = default)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var client = new SftpClient(host, SshPort, username ?? string.Empty, password ?? string.Empty);
                client.Connect();
                ct.ThrowIfCancellationRequested();

                var remotePath = $"{SharedDirectory}/{remoteFileName}";

                var localName = remoteFileName;
                var localPath = Path.Combine(localDestinationDirectory, localName);
                var suffix = 1;
                while (File.Exists(localPath))
                {
                    var stem = Path.GetFileNameWithoutExtension(remoteFileName);
                    var ext = Path.GetExtension(remoteFileName);
                    localName = $"{stem} ({suffix++}){ext}";
                    localPath = Path.Combine(localDestinationDirectory, localName);
                }

                using var localStream = File.Create(localPath);
                client.DownloadFile(remotePath, localStream);

                AppLog.Write($"Downloaded file from {host}: {remotePath} -> {localPath}");
                return new FileTransferResult(true, remoteFileName, localPath, null);
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Write($"File download from {host} failed for '{remoteFileName}': {ex.Message}");
            return new FileTransferResult(false, remoteFileName, null, ex.Message);
        }
    }
}
