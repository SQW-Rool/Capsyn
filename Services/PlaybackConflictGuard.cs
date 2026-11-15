using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Capsyn.Configuration;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Capsyn.Services;

/// <summary>播放冲突的处理策略。</summary>
public enum PlaybackConflictPolicy
{
    /// <summary>独占播放：新播放器开始时，自动暂停其它还在播放的。</summary>
    Exclusive = 0,

    /// <summary>仅提醒：不动作，在音乐岛里给一个「暂停它」的按钮。</summary>
    Prompt = 1,

    /// <summary>关闭：完全不处理。</summary>
    Off = 2,
}

/// <summary>
/// 播放冲突监测：Windows 混音器不管「谁在放」，两个播放器同时 Playing 就会一起出声。
///
/// 做法：每 2 秒读一次所有 SMTC 会话的播放状态，记录每个会话「开始播放的时间」；
/// 检测到 ≥2 个会话在播放时：
///   * 独占模式（默认）→ 保留最新开始播放的那个（用户刚点的），
///     通过 <c>TryPauseAsync</c> 暂停其它更早开始播放的（失败用 toggle 兜底），并抛 <see cref="ConflictResolved"/>；
///   * 仅提醒 → 不动作，抛 <see cref="ConflictDetected"/>，由音乐岛显示「暂停它」按钮；
///   * 关闭 → 什么都不做。
///
/// 防抖：
///   * 被暂停的 AUMID 进入 10 秒冷却，避免播放器自己恢复后反复抢；
///   * 用户手动恢复被暂停的播放器时，它变成「最新开始播放」→ 反过来让另一个暂停（符合「谁最后点谁出声」）；
///   * 不自动恢复之前被暂停的会话（否则立刻又冲突）。
/// </summary>
public sealed class PlaybackConflictGuard : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PauseCooldown = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PromptCooldown = TimeSpan.FromSeconds(20);

    private readonly MediaMonitor _media;
    private readonly DispatcherQueue _dispatcher;

    private readonly Dictionary<string, DateTime> _playingSince = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _pauseCooldownUntil = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _promptCooldownUntil = new(StringComparer.Ordinal);

    private DispatcherTimer? _timer;
    private bool _disposed;

    public PlaybackConflictGuard(MediaMonitor media, DispatcherQueue dispatcher)
    {
        _media = media;
        _dispatcher = dispatcher;
    }

    /// <summary>独占模式处理了冲突：参数是（保留的播放器名, 被暂停的播放器名）。</summary>
    public event Action<string, string>? ConflictResolved;

    /// <summary>仅提醒模式发现了冲突：参数是（还在播放的播放器名, 正在播放的播放器名, 冲突 AUMID）。</summary>
    public event Action<string, string, string>? ConflictDetected;

    public void Start()
    {
        if (_timer is not null || _disposed)
        {
            return;
        }

        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += OnTick;
        _timer.Start();

        Diagnostics.Log("conflict guard: 已启动");
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>「仅提醒」模式下用户点了「暂停它」。</summary>
    public async Task<bool> PauseAsync(string aumid)
    {
        var ok = await _media.PauseSessionAsync(aumid);
        if (ok)
        {
            _pauseCooldownUntil[aumid] = DateTime.UtcNow + PauseCooldown;
        }

        return ok;
    }

    private async void OnTick(object? sender, object e)
    {
        if (_disposed)
        {
            return;
        }

        var policy = (PlaybackConflictPolicy)AppSettings.Current.PlaybackConflictPolicy;
        if (policy == PlaybackConflictPolicy.Off)
        {
            return;
        }

        IReadOnlyList<MediaSessionState> states;
        try
        {
            states = _media.GetSessionStates();
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"conflict guard: 读状态失败：{ex.Message}");
            return;
        }

        var now = DateTime.UtcNow;

        // 1) 更新「开始播放时间」
        var playing = states.Where(s => s.IsPlaying).ToList();
        foreach (var state in playing)
        {
            if (!_playingSince.ContainsKey(state.Aumid))
            {
                _playingSince[state.Aumid] = now;
            }
        }

        // 已经不播的会话清掉记录（下次开始播重新计时）
        foreach (var key in _playingSince.Keys.ToList())
        {
            if (states.All(s => s.Aumid != key || !s.IsPlaying))
            {
                _playingSince.Remove(key);
            }
        }

        if (playing.Count < 2)
        {
            return;
        }

        // 2) 挑出「最新开始播放」的那个当赢家；同时开始的取系统当前会话，再不行取列表第一个
        var winner = playing
            .OrderByDescending(s => _playingSince.TryGetValue(s.Aumid, out var t) ? t : DateTime.MinValue)
            .First();

        var losers = playing.Where(s => s.Aumid != winner.Aumid).ToList();

        if (policy == PlaybackConflictPolicy.Prompt)
        {
            foreach (var loser in losers)
            {
                if (_promptCooldownUntil.TryGetValue(loser.Aumid, out var until) && now < until)
                {
                    continue;
                }

                _promptCooldownUntil[loser.Aumid] = now + PromptCooldown;
                Diagnostics.Log($"conflict guard: 发现冲突（{loser.DisplayName} 仍在播放，当前是 {winner.DisplayName}）");
                ConflictDetected?.Invoke(loser.DisplayName, winner.DisplayName, loser.Aumid);
            }

            return;
        }

        // 3) 独占模式：暂停所有比赢家更早开始播放的
        foreach (var loser in losers)
        {
            if (_pauseCooldownUntil.TryGetValue(loser.Aumid, out var until) && now < until)
            {
                continue;
            }

            // 更晚开始播放的（理论上不会有，除非同时）就留给下一轮再判
            var startedLoser = _playingSince.TryGetValue(loser.Aumid, out var tLoser) ? tLoser : DateTime.MinValue;
            var startedWinner = _playingSince.TryGetValue(winner.Aumid, out var tWinner) ? tWinner : DateTime.MinValue;
            if (startedLoser > startedWinner)
            {
                continue;
            }

            var ok = await _media.PauseSessionAsync(loser.Aumid);
            if (!ok)
            {
                continue;
            }

            _pauseCooldownUntil[loser.Aumid] = DateTime.UtcNow + PauseCooldown;
            _playingSince.Remove(loser.Aumid);

            Diagnostics.Log($"conflict guard: 独占 → 保留 {winner.DisplayName}，暂停 {loser.DisplayName}");
            ConflictResolved?.Invoke(winner.DisplayName, loser.DisplayName);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
