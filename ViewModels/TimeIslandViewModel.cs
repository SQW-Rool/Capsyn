using System.Globalization;
using Capsyn.Configuration;
using Capsyn.Helpers;
using Microsoft.UI.Xaml.Media;

namespace Capsyn.ViewModels;

/// <summary>
/// 「时间岛」的视图模型：把当前时间做成可绑定属性。
/// 岛体的形状/底色由外层 <see cref="Controls.IslandShell"/> + 窗口区域负责，这里只管文字。
///
/// TODO(扩展): 这里就是以后放通知队列、音乐播放状态、计时器剩余时间的地方。
/// </summary>
public sealed class TimeIslandViewModel : ObservableObject
{
    private readonly IslandOptions _options;
    private string _currentTime = string.Empty;
    private string _clockMain = string.Empty;
    private string _clockSeconds = string.Empty;
    private string _dateLabel = string.Empty;

    public TimeIslandViewModel(IslandOptions options)
    {
        // 注意：这些 XAML 对象必须在 UI 线程上创建（构造函数由 UserControl 在 UI 线程调用）。
        _options = options;
        ForegroundBrush = new SolidColorBrush(ColorHelper.Parse(options.ForegroundColor, Microsoft.UI.Colors.White));
        FontFamily = new FontFamily(options.FontFamily);
        FontSize = options.FontSize;
    }

    /// <summary>当前时间，格式 HH:mm:ss（24 小时制）。可绑定。</summary>
    public string CurrentTime
    {
        get => _currentTime;
        private set => SetProperty(ref _currentTime, value);
    }

    /// <summary>主时间（HH:mm），大字号显示。</summary>
    public string ClockMain
    {
        get => _clockMain;
        private set => SetProperty(ref _clockMain, value);
    }

    /// <summary>秒数（ss），小字号跟在主时间后面。</summary>
    public string ClockSeconds
    {
        get => _clockSeconds;
        private set => SetProperty(ref _clockSeconds, value);
    }

    /// <summary>日期标签（M月d日 周X）。</summary>
    public string DateLabel
    {
        get => _dateLabel;
        private set => SetProperty(ref _dateLabel, value);
    }

    /// <summary>时间文字颜色（白）。</summary>
    public SolidColorBrush ForegroundBrush { get; }

    /// <summary>等宽字体。</summary>
    public FontFamily FontFamily { get; }

    /// <summary>字号。</summary>
    public double FontSize { get; }

    /// <summary>
    /// 重新读取系统时间并推送绑定。
    /// 每次都重新取 <see cref="DateTime.Now"/>（而不是自己累加 1 秒），避免定时器抖动累积误差。
    /// </summary>
    public void Refresh()
    {
        var now = DateTime.Now;

        CurrentTime = now.ToString(_options.TimeFormat, CultureInfo.InvariantCulture);
        ClockMain = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        ClockSeconds = now.ToString("ss", CultureInfo.InvariantCulture);
        DateLabel = now.ToString("M月d日 ddd", CultureInfo.GetCultureInfo("zh-CN"));
    }
}
