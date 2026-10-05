using System.Globalization;
using Capsyn.Configuration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Capsyn.ViewModels;

/// <summary>
/// 「时间岛」的视图模型：把当前时间做成可绑定属性，并持有胶囊的样式（颜色 / 圆角 / 字体）。
///
/// TODO(扩展): 这里就是以后放通知队列、音乐播放状态、计时器剩余时间的地方，
///             例如新增 INotifyPropertyChanged 属性 Notification、NowPlaying、TimerRemaining，
///             并在对应的 UserControl 里 x:Bind 它们。
/// </summary>
public sealed class TimeIslandViewModel : ObservableObject
{
    private readonly IslandOptions _options;
    private string _currentTime = string.Empty;

    public TimeIslandViewModel(IslandOptions options)
    {
        // 注意：这些 XAML 对象必须在 UI 线程上创建（构造函数由 UserControl 在 UI 线程调用）。
        _options = options;
        PillBrush = new SolidColorBrush(ParseColor(options.PillColor, Microsoft.UI.Colors.Black));
        ForegroundBrush = new SolidColorBrush(ParseColor(options.ForegroundColor, Microsoft.UI.Colors.White));
        FontFamily = new FontFamily(options.FontFamily);
        FontSize = options.FontSize;
        PillCornerRadius = new CornerRadius(options.CornerRadius);
    }

    /// <summary>当前时间，格式 HH:mm:ss（24 小时制）。可绑定。</summary>
    public string CurrentTime
    {
        get => _currentTime;
        private set => SetProperty(ref _currentTime, value);
    }

    /// <summary>胶囊底色（纯黑）。</summary>
    public SolidColorBrush PillBrush { get; }

    /// <summary>时间文字颜色（白）。</summary>
    public SolidColorBrush ForegroundBrush { get; }

    /// <summary>等宽字体。</summary>
    public FontFamily FontFamily { get; }

    /// <summary>字号。</summary>
    public double FontSize { get; }

    /// <summary>超圆角：高度的一半。</summary>
    public CornerRadius PillCornerRadius { get; }

    /// <summary>
    /// 重新读取系统时间并推送绑定。
    /// 每次都重新取 <see cref="DateTime.Now"/>（而不是自己累加 1 秒），避免定时器抖动累积误差。
    /// </summary>
    public void Refresh() => CurrentTime = DateTime.Now.ToString(_options.TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>把 "#RRGGBB" / "#AARRGGBB" 解析成 Color，失败则用兜底色。</summary>
    private static Color ParseColor(string value, Color fallback)
    {
        var text = value.Trim().TrimStart('#');

        if (text.Length == 6)
        {
            text = "FF" + text;
        }

        if (text.Length != 8
            || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            return fallback;
        }

        return Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);
    }
}
