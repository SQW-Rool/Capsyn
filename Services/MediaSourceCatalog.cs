using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Capsyn.Services;

/// <summary>
/// 一个「音源」的静态描述：怎么认它、怎么启动它、怎么在 SMTC 里匹配它的会话。
/// </summary>
public sealed record MediaSourceDefinition(
    string Id,
    string DisplayName,
    string[] ProcessNames,
    string[] ExeNames,
    string[] RegistryKeywords,
    string[] AumidHints,
    string[] CommonPaths);

/// <summary>某个音源在当前机器上的探测结果。</summary>
public sealed class MediaSourceStatus
{
    public required MediaSourceDefinition Definition { get; init; }

    /// <summary>机器上装了（注册表里有卸载项、或进程在跑、或找到了可执行文件）。</summary>
    public bool IsInstalled { get; init; }

    /// <summary>现在有进程在跑。</summary>
    public bool IsRunning { get; init; }

    /// <summary>能找到的可执行文件（用于「选中但没在跑时帮忙启动」；可能为 null）。</summary>
    public string? LaunchPath { get; init; }

    public string Id => Definition.Id;
    public string DisplayName => Definition.DisplayName;
}

/// <summary>
/// 音源目录：内置支持的播放器列表 + 本机安装/运行状态探测。
///
/// 探测顺序（都失败才算没装）：
///   ① 进程列表里有它的进程名；
///   ② 卸载注册表项（HKLM / HKLM\WOW6432Node / HKCU）的 DisplayName 命中关键词；
///   ③ App Paths / 常见安装目录里存在它的 exe。
///
/// 结果缓存 3 秒：打开音源面板时刷新一次即可，不会在每次 tick 里扫注册表。
/// </summary>
public static class MediaSourceCatalog
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(3);

    private static readonly object Gate = new();

    private static readonly MediaSourceDefinition[] Definitions =
    {
        new(
            "soda",
            "汽水音乐",
            new[] { "SodaMusic", "SodaMusicLauncher" },
            new[] { "SodaMusic.exe", "SodaMusicLauncher.exe" },
            new[] { "汽水音乐", "Soda Music", "SodaMusic" },
            new[] { "sodamusic", "汽水" },
            new[]
            {
                @"%LOCALAPPDATA%\Programs\Soda Music\SodaMusic.exe",
                @"%PROGRAMFILES%\Soda Music\SodaMusic.exe",
            }),

        new(
            "qqmusic",
            "QQ音乐",
            new[] { "QQMusic" },
            new[] { "QQMusic.exe" },
            new[] { "QQ音乐", "QQMusic" },
            new[] { "qqmusic", "qq音乐" },
            new[]
            {
                @"%PROGRAMFILES%\Tencent\QQMusic\QQMusic.exe",
                @"%PROGRAMFILES(X86)%\Tencent\QQMusic\QQMusic.exe",
            }),

        new(
            "netease",
            "网易云音乐",
            new[] { "cloudmusic" },
            new[] { "cloudmusic.exe" },
            new[] { "网易云音乐", "NetEase CloudMusic", "CloudMusic" },
            new[] { "cloudmusic", "netease", "网易云" },
            new[]
            {
                @"%PROGRAMFILES%\Netease\CloudMusic\cloudmusic.exe",
                @"%PROGRAMFILES(X86)%\Netease\CloudMusic\cloudmusic.exe",
                @"%LOCALAPPDATA%\Netease\CloudMusic\cloudmusic.exe",
            }),

        new(
            "kugou",
            "酷狗音乐",
            new[] { "KuGou" },
            new[] { "KuGou.exe" },
            new[] { "酷狗音乐", "KuGou", "KGMusic" },
            new[] { "kugou", "酷狗" },
            new[]
            {
                @"%PROGRAMFILES%\KuGou\KuGou.exe",
                @"%PROGRAMFILES(X86)%\KuGou\KuGou.exe",
            }),

        new(
            "kuwo",
            "酷我音乐",
            new[] { "kwmusic", "KwMusic" },
            new[] { "KwMusic.exe", "kwmusic.exe" },
            new[] { "酷我音乐", "KuWo", "KwMusic" },
            new[] { "kuwo", "酷我" },
            new[]
            {
                @"%PROGRAMFILES%\Kuwo\bin\KwMusic.exe",
                @"%PROGRAMFILES(X86)%\Kuwo\bin\KwMusic.exe",
            }),

        new(
            "spotify",
            "Spotify",
            new[] { "Spotify" },
            new[] { "Spotify.exe" },
            new[] { "Spotify" },
            new[] { "spotify" },
            new[]
            {
                @"%APPDATA%\Spotify\Spotify.exe",
                @"%LOCALAPPDATA%\Microsoft\WindowsApps\Spotify.exe",
            }),

        new(
            "windows",
            "Windows 媒体播放器",
            new[] { "MediaPlayer", "Microsoft.Media.Player" },
            new[] { "MediaPlayer.exe" },
            new[] { "Windows Media Player", "媒体播放器" },
            new[] { "mediaplayer", "zunemusic", "media player", "媒体播放器" },
            Array.Empty<string>()),
    };

    private static IReadOnlyList<MediaSourceStatus>? _cache;
    private static DateTime _cachedAt = DateTime.MinValue;

    /// <summary>所有内置音源（顺序固定，UI 按这个顺序排）。</summary>
    public static IReadOnlyList<MediaSourceDefinition> All => Definitions;

    /// <summary>按 id 找音源定义。</summary>
    public static MediaSourceDefinition? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        foreach (var definition in Definitions)
        {
            if (string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return definition;
            }
        }

        return null;
    }

    /// <summary>按 id 找探测结果（缓存）。</summary>
    public static MediaSourceStatus? FindStatus(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        foreach (var status in GetStatuses())
        {
            if (string.Equals(status.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return status;
            }
        }

        return null;
    }

    /// <summary>某一个音源是否就是给定 SMTC 会话的来源。</summary>
    public static bool Matches(MediaSourceDefinition definition, string? sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return false;
        }

        var aumid = sourceAppUserModelId.ToLowerInvariant();

        foreach (var hint in definition.AumidHints)
        {
            if (aumid.Contains(hint, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var processName in definition.ProcessNames)
        {
            if (aumid.Contains(processName.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>探测本机状态（3 秒缓存，<paramref name="forceRefresh"/> 强制重扫）。</summary>
    public static IReadOnlyList<MediaSourceStatus> GetStatuses(bool forceRefresh = false)
    {
        lock (Gate)
        {
            if (!forceRefresh && _cache is not null && DateTime.UtcNow - _cachedAt < CacheLifetime)
            {
                return _cache;
            }

            _cache = Scan();
            _cachedAt = DateTime.UtcNow;
            return _cache;
        }
    }

    /// <summary>选中但没在跑时，尽量把播放器拉起来。成功返回 true。</summary>
    public static bool TryLaunch(MediaSourceStatus status)
    {
        var path = status.LaunchPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path)!,
                UseShellExecute = true,
            });

            Diagnostics.Log($"media source: 启动 {status.DisplayName} → {path}");
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"media source: 启动 {status.DisplayName} 失败：{ex.Message}");
            return false;
        }
    }

    // ---------------- 探测实现 ----------------

    private static IReadOnlyList<MediaSourceStatus> Scan()
    {
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    running.Add(process.ProcessName);
                }
                catch
                {
                    // 进程刚好退出 / 没权限读名字：忽略
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"media source: 枚举进程失败：{ex.Message}");
        }

        var installedApps = ReadInstalledApps();

        var results = new List<MediaSourceStatus>(Definitions.Length);

        foreach (var definition in Definitions)
        {
            var isRunning = definition.ProcessNames.Any(running.Contains);
            var launchPath = ResolveLaunchPath(definition, installedApps);
            var installed = isRunning || launchPath is not null || MatchesInstalled(definition, installedApps);

            results.Add(new MediaSourceStatus
            {
                Definition = definition,
                IsInstalled = installed,
                IsRunning = isRunning,
                LaunchPath = launchPath,
            });
        }

        return results;
    }

    private sealed record InstalledApp(string DisplayName, string? InstallLocation, string? DisplayIcon);

    private static List<InstalledApp> ReadInstalledApps()
    {
        var apps = new List<InstalledApp>();
        var roots = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };

        foreach (var rootPath in roots)
        {
            using var root = Registry.LocalMachine.OpenSubKey(rootPath);
            ReadAppsFrom(root, apps);
        }

        using (var currentUser = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
        {
            ReadAppsFrom(currentUser, apps);
        }

        return apps;
    }

    private static void ReadAppsFrom(RegistryKey? root, List<InstalledApp> apps)
    {
        if (root is null)
        {
            return;
        }

        foreach (var subKeyName in root.GetSubKeyNames())
        {
            try
            {
                using var key = root.OpenSubKey(subKeyName);
                if (key is null)
                {
                    continue;
                }

                var displayName = key.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                apps.Add(new InstalledApp(
                    displayName,
                    (key.GetValue("InstallLocation") as string)?.Trim().Trim('"'),
                    (key.GetValue("DisplayIcon") as string)?.Trim().Trim('"')));
            }
            catch
            {
                // 单个键读失败不影响整体
            }
        }
    }

    private static bool MatchesInstalled(MediaSourceDefinition definition, List<InstalledApp> installedApps)
        => installedApps.Any(app => definition.RegistryKeywords.Any(
            keyword => app.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)));

    private static string? ResolveLaunchPath(MediaSourceDefinition definition, List<InstalledApp> installedApps)
    {
        // 1) 卸载项里的 DisplayIcon / InstallLocation 通常就是最准的安装路径
        foreach (var app in installedApps)
        {
            if (!definition.RegistryKeywords.Any(k => app.DisplayName.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var exe = ExistingExeFromIcon(app.DisplayIcon)
                      ?? ExeInFolder(app.InstallLocation, definition.ExeNames);
            if (exe is not null)
            {
                return exe;
            }
        }

        // 2) App Paths
        foreach (var exeName in definition.ExeNames)
        {
            foreach (var root in new[]
                     {
                         @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\",
                         @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\",
                     })
            {
                var fromMachine = ReadAppPath(Registry.LocalMachine, root + exeName);
                if (fromMachine is not null)
                {
                    return fromMachine;
                }

                var fromUser = ReadAppPath(Registry.CurrentUser, root + exeName);
                if (fromUser is not null)
                {
                    return fromUser;
                }
            }
        }

        // 3) 常见安装目录
        foreach (var template in definition.CommonPaths)
        {
            var path = Environment.ExpandEnvironmentVariables(template);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string? ReadAppPath(RegistryKey hive, string subKeyPath)
    {
        try
        {
            using var key = hive.OpenSubKey(subKeyPath);
            var value = key?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            value = value.Trim().Trim('"');
            return File.Exists(value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExistingExeFromIcon(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        // "C:\xxx\App.exe,0" / "C:\xxx\App.exe" / 带引号
        var icon = displayIcon.Trim().Trim('"');
        var comma = icon.LastIndexOf(',');
        if (comma > 0)
        {
            icon = icon[..comma].Trim('"');
        }

        try
        {
            return File.Exists(icon) && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? icon : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExeInFolder(string? installLocation, string[] exeNames)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
        {
            return null;
        }

        foreach (var exeName in exeNames)
        {
            var candidate = Path.Combine(installLocation, exeName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            // 有些播放器把 exe 放在 bin / 版本号子目录里
            try
            {
                var nested = Directory
                    .EnumerateFiles(installLocation, exeName, SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (nested is not null)
                {
                    return nested;
                }
            }
            catch
            {
                // 目录扫描失败就算了
            }
        }

        return null;
    }
}
