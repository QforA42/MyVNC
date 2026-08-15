using System.Diagnostics;
using System.Windows;
using System.Windows.Shell;
using MyVNC.App.Models;

namespace MyVNC.App.Services;

/// <summary>
/// Builds the Windows taskbar right-click jump list: up to 5 pinned connections, plus up to 5
/// most-recently-used ones not already pinned. Each entry launches a fresh instance of the app
/// with "--connect &lt;id&gt;", which App.xaml.cs resolves back into a profile and connects
/// straight away (see App.OnStartup).
/// </summary>
public static class JumpListBuilder
{
    public static void Rebuild(IEnumerable<ConnectionProfile> profiles)
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        var all = profiles.ToList();
        var pinned = all.Where(p => p.IsPinned).Take(5).ToList();
        var recent = all.Where(p => !p.IsPinned && p.LastConnected.HasValue)
            .OrderByDescending(p => p.LastConnected)
            .Take(5)
            .ToList();

        var jumpList = new JumpList { ShowRecentCategory = false, ShowFrequentCategory = false };

        foreach (var p in pinned)
            jumpList.JumpItems.Add(CreateTask(p, exePath, Loc.T("JumpList.Pinned")));
        foreach (var p in recent)
            jumpList.JumpItems.Add(CreateTask(p, exePath, Loc.T("JumpList.Recent")));

        JumpList.SetJumpList(Application.Current, jumpList);
        jumpList.Apply();
    }

    private static JumpTask CreateTask(ConnectionProfile profile, string exePath, string category) => new()
    {
        Title = profile.DisplayTitle,
        Description = profile.Subtitle,
        ApplicationPath = exePath,
        Arguments = $"--connect {profile.Id}",
        IconResourcePath = exePath,
        IconResourceIndex = 0,
        CustomCategory = category,
    };
}
