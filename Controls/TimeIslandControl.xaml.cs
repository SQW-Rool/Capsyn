using System.Numerics;
using Capsyn.Configuration;
using Capsyn.Services;
using Capsyn.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Capsyn.Controls;

/// <summary>
/// 「时间岛」UI：时钟 / 监测区两种内容，外加每秒刷新的时钟定时器。
///
/// 形态由 <see cref="TimeToolsViewModel"/> 的占用状态决定：
///   * 没有占用 → 时钟（HH:mm:ss，宽度 320）；
///   * 有占用 → 监测区：主计时的「模式图标 + mm:ss」（宽度 172）。
/// 第二个计时**不在这里画** —— 它是一颗独立的圆形小岛（<see cref="TimerDotControl"/>），
/// 由 <see cref="IslandShell"/> 摆在本胶囊左边。
///
/// 宽度变化由外壳用弹簧插值（窗口区域跟着长/缩），这里只通过
/// <see cref="PreferredWidthChanged"/> 告诉它「我要多宽」。
/// </summary>
public sealed partial class TimeIslandControl : UserControl
{
    /// <summary>形态切换的淡入淡出 / 位移时长（Fast 档）。</summary>
    private static readonly TimeSpan SwapDuration = MotionTokens.Fast;

    /// <summary>形态切换时的水平位移（DIP）。</summary>
    private const float SwapSlide = 6f;

    private readonly IslandOptions _options = IslandOptions.Default;
    private readonly DispatcherTimer _clockTimer;

    private bool _monitoring;

    /// <summary>切换序号：连着切形态时只让最后一次收尾生效。</summary>
    private int _swapToken;

    public TimeIslandControl()
    {
        // x:Bind 在 InitializeComponent 时求值，这两个必须先准备好。
        // Tools 是共享实例：工具页改了计时，这里立刻能看到。
        ViewModel = new TimeIslandViewModel(_options);
        Tools = TimeToolsViewModel.Shared;

        InitializeComponent();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += OnClockTick;

        Tools.PropertyChanged += OnToolsPropertyChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>时钟的视图模型（可绑定）。</summary>
    public TimeIslandViewModel ViewModel { get; }

    /// <summary>共享的时间工具状态（正计时 / 倒计时 / 闹钟）。</summary>
    public TimeToolsViewModel Tools { get; }

    /// <summary>胶囊想要的宽度（DIP）：320 / 172，由外壳插值到窗口区域。</summary>
    public event Action<double>? PreferredWidthChanged;

    /// <summary>当前目标宽度。</summary>
    public double DesiredWidth { get; private set; } = IslandOptions.Default.Width;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 先立即取一次时间，避免启动后第一秒显示空白。
        ViewModel.Refresh();

        // 启动时按当前占用状态直接摆好（不动画）。
        UpdateMonitor(animate: false);

        _clockTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _clockTimer.Stop();

    private void OnClockTick(object? sender, object e) => ViewModel.Refresh();

    private void OnToolsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => UpdateMonitor(animate: true);

    /// <summary>
    /// 把共享状态投影到胶囊：内容（读数 / 图标 / 配色）、宽度目标、结构（时钟 ↔ 监测区）。
    /// 每次 tick 都会进来（读数要跳），但只有结构真的变了才播过场。
    /// </summary>
    private void UpdateMonitor(bool animate)
    {
        var primary = Tools.PrimaryActivity;
        var monitoring = primary.HasValue;

        if (primary is { } mode)
        {
            ApplyActivity(mode, Tools.MonitorPrimaryText, Tools.MonitorPrimaryRinging);
        }

        var width = monitoring ? _options.MonitoringWidth : _options.Width;

        if (Math.Abs(width - DesiredWidth) > 0.01)
        {
            DesiredWidth = width;
            PreferredWidthChanged?.Invoke(width);
        }

        if (monitoring == _monitoring)
        {
            return;
        }

        _monitoring = monitoring;
        SwitchViews(animate);
    }

    /// <summary>把主计时投影到胶囊上：图标、读数、配色（响铃时红）。</summary>
    private void ApplyActivity(TimeToolMode mode, string text, bool ringing)
    {
        var isCountdown = mode == TimeToolMode.Countdown;

        var textBrush = ringing
            ? Res("CriticalTextBrush")
            : isCountdown ? Res("WarningTextBrush") : Res("AccentTextBrush");

        PrimaryStopwatchIcon.Visibility = isCountdown ? Visibility.Collapsed : Visibility.Visible;
        PrimaryCountdownIcon.Visibility = isCountdown ? Visibility.Visible : Visibility.Collapsed;
        PrimaryStopwatchIcon.Stroke = textBrush;
        PrimaryCountdownIcon.Stroke = textBrush;
        PrimaryReadout.Text = text;
        PrimaryReadout.Foreground = textBrush;

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Root, "计时中 " + text);
    }

    /// <summary>在「时钟」和「监测区」之间切：淡出旧的、淡入新的，带 6px 位移。</summary>
    private void SwitchViews(bool animate)
    {
        var incoming = _monitoring ? (FrameworkElement)MonitorView : ClockView;
        var outgoing = _monitoring ? (FrameworkElement)ClockView : MonitorView;

        var token = ++_swapToken;
        incoming.Visibility = Visibility.Visible;

        if (!animate)
        {
            SnapToRest(incoming);
            SnapToRest(outgoing);
            outgoing.Visibility = Visibility.Collapsed;
            return;
        }

        PlayEnter(incoming, _monitoring ? 1f : -1f);
        PlayExit(outgoing, _monitoring ? 1f : -1f);

        _ = CollapseAfterAsync(outgoing, token);
    }

    private async Task CollapseAfterAsync(FrameworkElement outgoing, int token)
    {
        await Task.Delay(SwapDuration);

        if (token == _swapToken)
        {
            outgoing.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>把元素推回静止态（透明度 1、位移 0）。不能用 StopAnimation：不保证回到 0。</summary>
    private static void SnapToRest(FrameworkElement view)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(view, true);

        var visual = ElementCompositionPreview.GetElementVisual(view);
        var compositor = visual.Compositor;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = MotionTokens.Instant;

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(1f, Vector3.Zero);
        slide.Duration = MotionTokens.Instant;

        visual.StartAnimation(nameof(Visual.Opacity), fade);
        visual.StartAnimation("Translation", slide);
    }

    private static void PlayEnter(FrameworkElement view, float direction)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(view, true);

        var visual = ElementCompositionPreview.GetElementVisual(view);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.32f, 0.72f),
            new Vector2(0f, 1f));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, ease);
        fade.Duration = SwapDuration;

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new Vector3(direction * SwapSlide, 0f, 0f));
        slide.InsertKeyFrame(1f, Vector3.Zero, ease);
        slide.Duration = SwapDuration;

        visual.StartAnimation(nameof(Visual.Opacity), fade);
        visual.StartAnimation("Translation", slide);
    }

    private static void PlayExit(FrameworkElement view, float direction)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(view, true);

        var visual = ElementCompositionPreview.GetElementVisual(view);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.4f, 0f),
            new Vector2(1f, 1f));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 1f);
        fade.InsertKeyFrame(1f, 0f, ease);
        fade.Duration = SwapDuration;

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, Vector3.Zero);
        slide.InsertKeyFrame(1f, new Vector3(-direction * SwapSlide, 0f, 0f), ease);
        slide.Duration = SwapDuration;

        visual.StartAnimation(nameof(Visual.Opacity), fade);
        visual.StartAnimation("Translation", slide);
    }

    private static Brush? Res(string key)
        => Application.Current?.Resources is { } resources
           && resources.TryGetValue(key, out var value)
           && value is Brush brush
            ? brush
            : null;
}
