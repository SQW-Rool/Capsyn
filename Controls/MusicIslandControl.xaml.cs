using System;
using System.Numerics;
using System.Threading.Tasks;
using Capsyn.Services;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

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
    private bool _vinylSpinning;

    /// <summary>从封面提取的强调色（默认 Prism 蓝）。</summary>
    private Color _accent = Color.FromArgb(255, 124, 141, 255);

    public MusicIslandControl()
    {
        InitializeComponent();

        ApplyAccent(_accent);
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
            ApplyAccent(_accent);
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

            if (await ExtractAccentAsync(artwork) is { } accent && generation == _coverGeneration)
            {
                ApplyAccent(accent);
            }
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
        StartVinylSpin();

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
        StopVinylSpin();

        foreach (var bar in new[] { Eq1, Eq2, Eq3 })
        {
            var visual = ElementCompositionPreview.GetElementVisual(bar);
            visual.StopAnimation("Scale.Y");
            visual.Scale = Vector3.One;
        }

        Equalizer.Visibility = Visibility.Collapsed;
    }

    // ---------------- 封面主色 / 黑胶环 ----------------

    /// <summary>从封面图取平均色，提亮 + 提饱和之后作为这个岛的强调色。</summary>
    private static async Task<Color?> ExtractAccentAsync(byte[] artwork)
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

            var decoder = await BitmapDecoder.CreateAsync(stream);
            var transform = new BitmapTransform
            {
                ScaledWidth = 8,
                ScaledHeight = 8,
                InterpolationMode = BitmapInterpolationMode.Linear,
            };

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            var pixels = pixelData.DetachPixelData();
            double r = 0, g = 0, b = 0;
            var count = 0;

            for (var i = 0; i + 3 < pixels.Length; i += 4)
            {
                b += pixels[i];
                g += pixels[i + 1];
                r += pixels[i + 2];
                count++;
            }

            if (count == 0)
            {
                return null;
            }

            return BoostAccent(Color.FromArgb(255, (byte)(r / count), (byte)(g / count), (byte)(b / count)));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把封面平均色提亮 + 提饱和，保证在深色玻璃上够亮、但不过曝。</summary>
    private static Color BoostAccent(Color color)
    {
        double r = color.R, g = color.G, b = color.B;
        var luminance = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
        var average = (r + g + b) / 3.0;

        const double saturation = 1.45;
        r = average + ((r - average) * saturation);
        g = average + ((g - average) * saturation);
        b = average + ((b - average) * saturation);

        const double targetLuminance = 205.0;
        var scale = luminance > 1 ? targetLuminance / luminance : 1.0;
        scale = Math.Clamp(scale, 0.75, 1.8);

        static byte Clamp(double value) => (byte)Math.Clamp(value * 1, 0, 255);

        return Color.FromArgb(255, Clamp(r * scale), Clamp(g * scale), Clamp(b * scale));
    }

    private void ApplyAccent(Color accent)
    {
        _accent = accent;

        var accentBrush = new SolidColorBrush(accent);
        var lightBrush = new SolidColorBrush(Blend(accent, Colors.White, 0.35));

        Progress.Foreground = accentBrush;
        Eq1.Fill = accentBrush;
        Eq2.Fill = accentBrush;
        Eq3.Fill = accentBrush;
        VinylRing.Stroke = accentBrush;

        PlayPauseButton.Background = accentBrush;
        PlayPauseButton.BorderBrush = lightBrush;
        PlayIcon.Fill = new SolidColorBrush(Colors.White);
        PauseIcon.Fill = new SolidColorBrush(Colors.White);
    }

    private static Color Blend(Color from, Color to, double amount)
        => Color.FromArgb(
            255,
            (byte)Math.Clamp(from.R + ((to.R - from.R) * amount), 0, 255),
            (byte)Math.Clamp(from.G + ((to.G - from.G) * amount), 0, 255),
            (byte)Math.Clamp(from.B + ((to.B - from.B) * amount), 0, 255));

    /// <summary>播放时让封面外那圈虚线缓慢转起来（黑胶氛围）。</summary>
    private void StartVinylSpin()
    {
        if (_vinylSpinning)
        {
            return;
        }

        _vinylSpinning = true;

        var visual = ElementCompositionPreview.GetElementVisual(VinylRing);
        visual.CenterPoint = new Vector3(85f, 85f, 0f);

        var spin = visual.Compositor.CreateScalarKeyFrameAnimation();
        spin.InsertKeyFrame(0f, 0f);
        spin.InsertKeyFrame(1f, 360f);
        spin.Duration = TimeSpan.FromSeconds(26);
        spin.IterationBehavior = AnimationIterationBehavior.Forever;

        visual.StartAnimation("RotationAngleInDegrees", spin);
    }

    private void StopVinylSpin()
    {
        if (!_vinylSpinning)
        {
            return;
        }

        _vinylSpinning = false;
        var visual = ElementCompositionPreview.GetElementVisual(VinylRing);
        visual.StopAnimation("RotationAngleInDegrees");
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
