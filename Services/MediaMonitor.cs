using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Windows.Media.Control;

namespace Capsyn.Services;

/// <summary>某一刻的媒体播放快照（对外只读，UI 直接绑定）。</summary>
/// <param name="IsAvailable">有没有可用的媒体会话（有播放器在放东西）。</param>
/// <param name="Title">歌名。</param>
/// <param name="Artist">歌手。</param>
/// <param name="Album">专辑。</param>
/// <param name="IsPlaying">是否正在播放。</param>
/// <param name="Position">播放位置（已按经过时间插值）。</param>
/// <param name="Duration">总时长；未知时为 <see cref="TimeSpan.Zero"/>。</param>
/// <param name="CanSkipNext">播放器是否支持「下一首」。</param>
/// <param name="CanSkipPrevious">播放器是否支持「上一首」。</param>
/// <param name="SourceApp">来源应用（汽水音乐 / Spotify …），用于调试显示。</param>
public sealed record MediaSnapshot(
    bool IsAvailable,
    string Title,
    string Artist,
    string Album,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    bool CanSkipNext,
    bool CanSkipPrevious,
    string SourceApp)
{
    public static MediaSnapshot Empty { get; } = new(
        false, string.Empty, string.Empty, string.Empty, false,
        TimeSpan.Zero, TimeSpan.Zero, false, false, string.Empty);

    /// <summary>「歌名 · 歌手」或只有歌名。</summary>
    public string DisplayLine => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Title} · {Artist}";

    /// <summary>0~1 的播放进度；时长未知时返回 0。</summary>
    public double Progress => Duration > TimeSpan.Zero
        ? Math.Clamp(Position.TotalMilliseconds / Duration.TotalMilliseconds, 0, 1)
        : 0;
}

/// <summary>
/// 系统媒体会话（SMTC）监听 + 控制。
///
/// 为什么用它：Windows 上的播放器（汽水音乐 / 网易云 / Spotify / 浏览器…）会把「正在放什么」注册到
/// <see cref="GlobalSystemMediaTransportControlsSessionManager"/>，这里读到的不仅是歌名/歌手/时长，
/// 还能直接做播放 / 暂停 / 下一首 / 上一首 —— **不需要给每个 app 写适配器，也不需要任何权限**。
///
/// 线程模型：SMTC 的事件在任意线程触发，这里统一编组到创建时所在的 <see cref="DispatcherQueue"/>，
/// 所以订阅方（UI）可以直接改控件。
/// </summary>
public sealed class MediaMonitor : IDisposable
{
    /// <summary>位置插值：SMTC 只报告「某一刻的位置」，播放中按经过时间往前推。</summary>
    private static readonly TimeSpan PositionRefreshInterval = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherQueue _dispatcher;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    private MediaSnapshot _current = MediaSnapshot.Empty;
    private DateTimeOffset _timelineUpdatedAt = DateTimeOffset.MinValue;
    private TimeSpan _timelinePosition;
    private string _artworkKey = string.Empty;
    private bool _disposed;

    public MediaMonitor(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>快照变化（切歌 / 播放状态变化 / 进度校正）。</summary>
    public event Action<MediaSnapshot>? Changed;

    /// <summary>封面图变化；参数是原始字节（<c>null</c> 表示没有封面）。由 UI 自己转成图片。</summary>
    public event Action<byte[]?>? ArtworkChanged;

    /// <summary>当前快照。</summary>
    public MediaSnapshot Current => _current;

    /// <summary>插值后的当前位置（播放中会随真实时间走）。</summary>
    public TimeSpan EstimatePosition()
    {
        if (!_current.IsAvailable)
        {
            return TimeSpan.Zero;
        }

        if (!_current.IsPlaying)
        {
            return _timelinePosition;
        }

        var elapsed = DateTimeOffset.UtcNow - _timelineUpdatedAt;
        var position = _timelinePosition + elapsed;

        return _current.Duration > TimeSpan.Zero && position > _current.Duration
            ? _current.Duration
            : position;
    }

    /// <summary>初始化：拿会话管理器、订阅全局事件、挂上当前会话。</summary>
    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += OnSessionsChanged;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;

            Diagnostics.Log($"media: 已连接（当前会话数 {_manager.GetSessions().Count}）");
            AttachSession(_manager.GetCurrentSession());
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"media: 初始化失败：{ex.Message}");
        }
    }

    // ---------------- 控制 ----------------

    public async Task<bool> TogglePlayPauseAsync() => await SafeCallAsync(s => s.TryTogglePlayPauseAsync());

    public async Task<bool> SkipNextAsync() => await SafeCallAsync(s => s.TrySkipNextAsync());

    public async Task<bool> SkipPreviousAsync() => await SafeCallAsync(s => s.TrySkipPreviousAsync());

    private async Task<bool> SafeCallAsync(
        Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> call)
    {
        var session = _session;
        if (session is null)
        {
            return false;
        }

        try
        {
            return await call(session);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"media: 控制失败：{ex.Message}");
            await ReinitializeAsync();
            return false;
        }
    }

    // ---------------- 会话挂载 / 解挂 ----------------

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        DetachSession();

        _session = session;

        if (session is null)
        {
            Publish(MediaSnapshot.Empty, artwork: null, resetArtwork: true);
            return;
        }

        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        Diagnostics.Log($"media: 挂上会话 {session.SourceAppUserModelId}");
        _ = RefreshAsync();
    }

    private void DetachSession()
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"media: 解挂会话失败（忽略）：{ex.Message}");
        }

        _session = null;
    }

    private async Task ReinitializeAsync()
    {
        DetachSession();

        try
        {
            if (_manager is not null)
            {
                _manager.SessionsChanged -= OnSessionsChanged;
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _manager = null;
            }

            await InitializeAsync();
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"media: 重连失败：{ex.Message}");
        }
    }

    // ---------------- 事件 ----------------

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        => _ = RefreshAsync();

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        try
        {
            AttachSession(sender.GetCurrentSession());
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"media: 切换当前会话失败：{ex.Message}");
            _ = ReinitializeAsync();
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        => _ = RefreshAsync();

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        => _ = RefreshAsync();

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        => _ = RefreshAsync();

    // ---------------- 抓快照 ----------------

    private async Task RefreshAsync()
    {
        var session = _session;

        if (session is null)
        {
            Publish(MediaSnapshot.Empty, artwork: null, resetArtwork: true);
            return;
        }

        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            var isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var controls = playback?.Controls;

            var title = properties?.Title ?? string.Empty;
            var artist = properties?.Artist ?? string.Empty;
            var album = properties?.AlbumTitle ?? string.Empty;

            _timelinePosition = timeline?.Position ?? TimeSpan.Zero;
            _timelineUpdatedAt = timeline?.LastUpdatedTime ?? DateTimeOffset.UtcNow;

            var duration = timeline is null
                ? TimeSpan.Zero
                : timeline.EndTime - timeline.StartTime;

            var snapshot = new MediaSnapshot(
                IsAvailable: !string.IsNullOrWhiteSpace(title),
                Title: title,
                Artist: artist,
                Album: album,
                IsPlaying: isPlaying,
                Position: _timelinePosition,
                Duration: duration,
                CanSkipNext: controls?.IsNextEnabled ?? false,
                CanSkipPrevious: controls?.IsPreviousEnabled ?? false,
                SourceApp: session.SourceAppUserModelId ?? string.Empty);

            // 封面：换歌才重新读，避免每次播放状态变化都拉一遍图片。
            var artworkKey = $"{title}|{artist}";
            byte[]? artwork = null;
            var artworkChanged = false;

            if (!string.Equals(artworkKey, _artworkKey, StringComparison.Ordinal))
            {
                _artworkKey = artworkKey;
                artwork = await TryReadThumbnailAsync(properties);
                artworkChanged = true;
            }

            Publish(snapshot, artwork, resetArtwork: artworkChanged);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"media: 读取会话失败：{ex.Message}");
            await ReinitializeAsync();
        }
    }

    private static async Task<byte[]?> TryReadThumbnailAsync(GlobalSystemMediaTransportControlsSessionMediaProperties? properties)
    {
        try
        {
            if (properties?.Thumbnail is null)
            {
                return null;
            }

            using var stream = await properties.Thumbnail.OpenReadAsync();
            if (stream is null || stream.Size == 0)
            {
                return null;
            }

            var bytes = new byte[stream.Size];
            using var reader = new Windows.Storage.Streams.DataReader(stream);
            await reader.LoadAsync((uint)bytes.Length);
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"media: 读封面失败（忽略）：{ex.Message}");
            return null;
        }
    }

    /// <summary>把快照编组到 UI 线程后发布。</summary>
    private void Publish(MediaSnapshot snapshot, byte[]? artwork, bool resetArtwork)
    {
        _current = snapshot;

        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed)
            {
                return;
            }

            Changed?.Invoke(snapshot);

            if (resetArtwork)
            {
                ArtworkChanged?.Invoke(artwork);
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachSession();

        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager = null;
        }
    }
}
