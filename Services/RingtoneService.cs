using System.IO;
using System.Runtime.InteropServices;
using Capsyn.Configuration;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Capsyn.Services;

/// <summary>
/// 铃声播放。
///
/// 播什么：
///   * 每一类（闹钟 / 倒计时）优先用**自定义文件**（用户在设置里选的 wav / mp3 / m4a …，会复制到本地目录）；
///   * 没有自定义就用**内置音效**（<see cref="RingtoneLibrary"/> 代码合成，首次播放落到本地 wav 缓存）。
///
/// 怎么播：`Windows.Media.Playback.MediaPlayer`（支持 mp3 等格式，能循环、能停、能设音量）。
/// 早先用的是 winmm 的 `PlaySound`，但它只认 wav，加自定义音效后换成了 MediaPlayer；
/// `PlaySound` 只留作兜底（MediaPlayer 起不来时至少还能响一声）。
///
/// 播放策略：
///   * 闹钟：**循环**，直到用户点「停止」或弹窗关掉（另有 2 分钟兜底，防止忘了关一直响）；
///   * 倒计时：循环，最长 30 秒；
///   * 设置页里点某一项 = **试听**：单次播放（最多 3 秒）。
/// </summary>
public sealed class RingtoneService
{
    /// <summary>全局唯一实例。</summary>
    public static RingtoneService Shared { get; } = new();

    private readonly object _gate = new();
    private MediaPlayer? _player;
    private Timer? _autoStop;

    /// <summary>当前在播什么（null = 没在播）。自定义文件的 Index 记 -1。</summary>
    public (RingtoneKind Kind, int Index)? Current { get; private set; }

    /// <summary>闹钟：循环播放，最长 2 分钟。</summary>
    public void PlayAlarm() => Play(RingtoneKind.Alarm, AppSettings.Current.AlarmRingtone, loop: true, TimeSpan.FromMinutes(2));

    /// <summary>倒计时：循环播放，最长 30 秒。</summary>
    public void PlayCountdown() => Play(RingtoneKind.Countdown, AppSettings.Current.CountdownRingtone, loop: true, TimeSpan.FromSeconds(30));

    /// <summary>设置页试听内置音效：单次播放（不循环）。</summary>
    public void Preview(RingtoneKind kind, int index) => Play(kind, index, loop: false, TimeSpan.FromSeconds(3), ignoreCustom: true);

    /// <summary>设置页试听自定义文件：单次播放。</summary>
    public void PreviewCustom(RingtoneKind kind, string path) => PlayFile(kind, path, loop: false, TimeSpan.FromSeconds(3), index: -1);

    /// <summary>停止播放（幂等）。</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _autoStop?.Dispose();
            _autoStop = null;
            Current = null;

            try
            {
                _player?.Pause();
                _player?.Dispose();
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"ringtone: stop failed: {ex.Message}");
            }
            finally
            {
                _player = null;
            }

            // 保险丝：万一 MediaPlayer 没起来、回退到了 PlaySound，这里把它也停掉。
            try
            {
                PlaySound(null, IntPtr.Zero, 0);
            }
            catch (Exception)
            {
                // 忽略
            }

            Diagnostics.Log("ringtone: stopped");
        }
    }

    /// <summary>
    /// 把用户选的文件复制进本地铃声目录（原文件之后被删/移动也不影响），返回最终路径。
    /// 只保留扩展名，文件名固定成 <c>custom-alarm.wav</c> / <c>custom-countdown.mp3</c>，
    /// 这样同一类反复换文件不会越堆越多。
    /// </summary>
    public static string ImportCustomFile(RingtoneKind kind, string sourcePath)
    {
        var directory = AppSettings.RingtoneFolder;
        Directory.CreateDirectory(directory);

        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".wav";
        }

        var destination = Path.Combine(directory, $"custom-{kind.ToString().ToLowerInvariant()}{extension}");
        File.Copy(sourcePath, destination, overwrite: true);

        Diagnostics.Log($"ringtone: imported {sourcePath} -> {destination} ({new FileInfo(destination).Length} B)");
        return destination;
    }

    private void Play(RingtoneKind kind, int index, bool loop, TimeSpan maxDuration, bool ignoreCustom = false)
    {
        var custom = AppSettings.Current.GetCustomFile(kind);

        if (custom is not null && !ignoreCustom)
        {
            PlayFile(kind, custom, loop, maxDuration, index: -1);
            return;
        }

        string path;

        try
        {
            path = EnsureWaveFile(kind, index);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"ringtone: cannot prepare wave file: {ex.Message}");
            return;
        }

        PlayFile(kind, path, loop, maxDuration, index: Math.Clamp(index, 0, RingtoneLibrary.Count - 1));
    }

    private void PlayFile(RingtoneKind kind, string path, bool loop, TimeSpan maxDuration, int index)
    {
        if (!File.Exists(path))
        {
            Diagnostics.Log($"ringtone: file not found: {path}");
            return;
        }

        lock (_gate)
        {
            _autoStop?.Dispose();

            try
            {
                _player?.Pause();
                _player?.Dispose();
            }
            catch (Exception)
            {
                // 换文件时旧 player 关不掉也无所谓
            }

            try
            {
                var player = new MediaPlayer
                {
                    AutoPlay = false,
                    IsLoopingEnabled = loop,
                    Volume = 0.8,
                };

                player.CommandManager.IsEnabled = false;   // 不往系统媒体控制中心里露
                player.Source = MediaSource.CreateFromUri(new Uri(path));
                player.Play();

                _player = player;
                Current = (kind, index);
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"ringtone: MediaPlayer failed ({ex.Message}), fallback to PlaySound");
                FallbackToPlaySound(path, loop);
                return;
            }

            finally
            {
                _autoStop = new Timer(_ => Stop(), null, maxDuration, Timeout.InfiniteTimeSpan);
            }

            var name = index >= 0 ? RingtoneLibrary.For(kind)[index].Name : Path.GetFileName(path);
            Diagnostics.Log(
                $"ringtone: play {kind} #{index} \"{name}\" (loop={loop}, max={maxDuration.TotalSeconds:0}s)");
        }
    }

    /// <summary>MediaPlayer 不可用时的兜底（只认 wav，但至少不是静音）。</summary>
    private void FallbackToPlaySound(string path, bool loop)
    {
        const uint SndAsync = 0x0001;
        const uint SndNoDefault = 0x0002;
        const uint SndLoop = 0x0008;
        const uint SndFileName = 0x20000;

        try
        {
            var flags = SndAsync | SndNoDefault | SndFileName | (loop ? SndLoop : 0u);
            PlaySound(path, IntPtr.Zero, flags);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"ringtone: PlaySound fallback failed: {ex.Message}");
        }
    }

    /// <summary>把合成的内置 WAV 写进 <c>%LOCALAPPDATA%\Capsyn\ringtones</c>（已存在就不重写），返回路径。</summary>
    private static string EnsureWaveFile(RingtoneKind kind, int index)
    {
        var safeIndex = Math.Clamp(index, 0, RingtoneLibrary.Count - 1);
        var directory = AppSettings.RingtoneFolder;

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{kind.ToString().ToLowerInvariant()}-{safeIndex}.wav");
        var info = new FileInfo(path);

        if (!info.Exists || info.Length == 0)
        {
            File.WriteAllBytes(path, RingtoneLibrary.GetWave(kind, safeIndex));
            Diagnostics.Log($"ringtone: generated {path}");
        }

        return path;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);
}
