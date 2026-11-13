using System;
using System.Numerics;
using System.Threading.Tasks;
using Capsyn.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Capsyn.Controls;

/// <summary>
/// 「音乐副岛」：音乐播放时出现在看板右侧的独立小岛。
///
/// 只负责把 <see cref="MediaMonitor"/> 的快照画出来 + 把三个按钮的点击转成控制调用；
/// 位置和窗口区域由 <see cref="IslandShell"/> 统一摆（它才是管形状的那一层）。
/// </summary>
public sealed partial class MusicIslandControl : UserControl
{
    private MediaMonitor? _media;
    private MediaSnapshot _snapshot = MediaSnapshot.Empty;
    private DispatcherTimer? _timer;
    private int _coverGeneration;
    private bool _equalizerRunning;

    public MusicIslandControl()
    {
        InitializeComponent();

        Loaded += (_, _) => StartTimer();
        Unloaded += (_, _) => _timer?.Stop();
    }

    /// <summary>接上媒体服务（由 <see cref="IslandShell"/> 调用）。</summary>
    public void AttachMedia(MediaMonitor media)
    {
        _media = media;
        media.Changed += OnMediaChanged;
        media.ArtworkChanged += OnArtworkChanged;
        OnMediaChanged(media.Current);
    }

    /// <summary>快照变化（也可能来自播放器自己切歌）。</summary>
    public event Action<MediaSnapshot>? SnapshotChanged;

    private void StartTimer()
    {
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick -= OnTimerTick;
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, object e) => UpdateProgress();

    private void OnMediaChanged(MediaSnapshot snapshot)
    {
        _snapshot = snapshot;

        TitleText.Text = snapshot.IsAvailable ? snapshot.Title : "没有正在播放的媒体";
        ArtistText.Text = snapshot.IsAvailable
            ? (string.IsNullOrWhiteSpace(snapshot.Artist) ? snapshot.Album : snapshot.Artist)
            : "打开播放器后自动显示";

        SourceText.Text = snapshot.IsAvailable ? snapshot.SourceApp : string.Empty;

        StateText.Text = snapshot.IsAvailable
            ? (snapshot.IsPlaying ? "正在播放" : "已暂停")
            : "没有正在播放";

        PlayPauseButton.IsEnabled = snapshot.IsAvailable;
        PreviousButton.IsEnabled = snapshot.IsAvailable && snapshot.CanSkipPrevious;
        NextButton.IsEnabled = snapshot.IsAvailable && snapshot.CanSkipNext;

        PlayIcon.Visibility = snapshot.IsPlaying ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = snapshot.IsPlaying ? Visibility.Visible : Visibility.Collapsed;

        if (snapshot.IsAvailable && snapshot.IsPlaying)
        {
            StartEqualizer();
        }
        else
        {
            StopEqualizer();
        }

        UpdateProgress();
        SnapshotChanged?.Invoke(snapshot);
    }

    private void UpdateProgress()
    {
        var position = _media?.EstimatePosition() ?? _snapshot.Position;
        var duration = _snapshot.Duration;

        Progress.Value = duration > TimeSpan.Zero
            ? Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1)
            : 0;

        PositionText.Text = _snapshot.IsAvailable ? FormatTime(position) : "--:--";
        DurationText.Text = duration > TimeSpan.Zero ? FormatTime(duration) : "--:--";
    }

    private static string FormatTime(TimeSpan value)
        => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    // ---------------- 封面 ----------------

    private static string ArtworkKeyOf(MediaSnapshot snapshot) => $"{snapshot.Title}|{snapshot.Artist}";

    private void OnArtworkChanged(byte[]? artwork, string key)
    {
        if (!string.Equals(key, ArtworkKeyOf(_snapshot), StringComparison.Ordinal))
        {
            return;
        }

        if (artwork is null || artwork.Length == 0)
        {
            CoverImage.Source = null;
            CoverImage.Visibility = Visibility.Collapsed;
            CoverPlaceholder.Visibility = Visibility.Visible;
            BackdropImage.Source = null;
            BackdropLayer.Visibility = Visibility.Collapsed;
            return;
        }

        _ = ShowCoverAsync(artwork, ++_coverGeneration);
    }

    private async Task ShowCoverAsync(byte[] artwork, int generation)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();

            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(artwork);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);

            if (generation != _coverGeneration)
            {
                return;
            }

            CoverImage.Source = bitmap;
            CoverImage.Visibility = Visibility.Visible;
            CoverPlaceholder.Visibility = Visibility.Collapsed;

            BackdropImage.Source = bitmap;
            BackdropLayer.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"music island: 封面显示失败：{ex.Message}");
        }
    }

    // ---------------- 均衡器 ----------------

    private void StartEqualizer()
    {
        if (_equalizerRunning)
        {
            return;
        }

        _equalizerRunning = true;
        Equalizer.Visibility = Visibility.Visible;

        var bars = new[] { Eq1, Eq2, Eq3 };

        for (var i = 0; i < bars.Length; i++)
        {
            var bar = bars[i];
            var visual = ElementCompositionPreview.GetElementVisual(bar);
            visual.CenterPoint = new Vector3((float)bar.Width / 2f, (float)bar.Height, 0f);

            var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(0f, 0.25f);
            animation.InsertKeyFrame(0.5f, 1f);
            animation.InsertKeyFrame(1f, 0.45f);
            animation.Duration = TimeSpan.FromMilliseconds(560 + (i * 150));
            animation.IterationBehavior = AnimationIterationBehavior.Forever;

            visual.StartAnimation("Scale.Y", animation);
        }
    }

    private void StopEqualizer()
    {
        if (!_equalizerRunning)
        {
            Equalizer.Visibility = Visibility.Collapsed;
            return;
        }

        _equalizerRunning = false;

        foreach (var bar in new[] { Eq1, Eq2, Eq3 })
        {
            var visual = ElementCompositionPreview.GetElementVisual(bar);
            visual.StopAnimation("Scale.Y");
            visual.Scale = Vector3.One;
        }

        Equalizer.Visibility = Visibility.Collapsed;
    }

    // ---------------- 控制 ----------------

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_media is null)
        {
            return;
        }

        var ok = await _media.TogglePlayPauseAsync();
        Diagnostics.Log($"music island: play/pause ok={ok}");
        UpdateProgress();
    }

    private async void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        if (_media is null)
        {
            return;
        }

        var ok = await _media.SkipPreviousAsync();
        Diagnostics.Log($"music island: previous ok={ok}");
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_media is null)
        {
            return;
        }

        var ok = await _media.SkipNextAsync();
        Diagnostics.Log($"music island: next ok={ok}");
    }
}
