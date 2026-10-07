using System.Numerics;
using Capsyn.Configuration;
using Capsyn.Services;
using Capsyn.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Capsyn.Controls;

/// <summary>
/// 「看板岛」UI，里面有两个视图，切换只改可见性（面板尺寸与动画都不动）：
///   * 指标视图（默认）：CPU / 内存 / 网络上下行，每秒采样一次（采样在线程池上跑）；
///   * 时间工具视图：点左下「时间工具」进入，显示「正计时 / 倒计时 / 闹钟」三个按钮，
///     左上角「返回」回到指标视图 —— 三个工具按钮按需求**先只做 UI**，不接任何逻辑。
///
/// 两个视图之间走一段 **iOS 风格 push / pop 过场**：旧视图往一侧淡出滑走、新视图从另一侧淡入滑进来
/// （返回时方向相反），旧视图少走一点形成视差；缓动用 cubic-bezier(0.32, 0.72, 0, 1)（iOS 弹层那条曲线）。
/// 整段动画都跑在合成线程上，不参与布局，UI 线程不需要出帧，所以不掉帧。
///
/// 右下角的「关闭程序」按钮抛 <see cref="ExitRequested"/>，
/// 由 <see cref="IslandShell"/> 转给 MainWindow 真正退出。
/// </summary>
public sealed partial class DashboardIslandControl : UserControl
{
    /// <summary>过场：新视图淡入 + 滑入的时长。</summary>
    private static readonly TimeSpan ViewEnterDuration = MotionTokens.Normal;

    /// <summary>过场：旧视图淡出 + 滑走的时长（比入场短，先把位置让出来）。</summary>
    private static readonly TimeSpan ViewExitDuration = MotionTokens.Fast;

    /// <summary>过场：新视图的滑动距离（DIP）。</summary>
    private const float ViewSlideDistance = 48f;

    /// <summary>旧视图滑走的距离系数 —— 比新视图少走一点，形成视差。</summary>
    private const float ViewExitParallax = 0.6f;

    /// <summary>复位时用的「瞬时」动画时长：走一段 1ms 的动画把属性显式推到目标值。</summary>
    private static readonly TimeSpan SnapDuration = MotionTokens.Instant;

    private readonly DispatcherTimer _timer;
    private bool _sampling;
    private bool _timeToolsVisible;

    /// <summary>过场序号：连点「时间工具 / 返回」时，只让最后一次的收尾生效。</summary>
    private int _viewSwitchToken;

    public DashboardIslandControl()
    {
        // x:Bind 在 InitializeComponent 时求值，ViewModel 必须先准备好。
        ViewModel = new DashboardViewModel();

        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(IslandOptions.Default.MetricsIntervalSeconds),
        };
        _timer.Tick += OnTimerTick;

        Loaded += OnLoaded;

        // Phase 2/3：给底部这几个图标按钮挂统一指针反馈（hover 放大 / 按下缩小，Composition 隐式动画）
        Loaded += (_, _) => InteractiveAnimations.AttachToButtons(TimeToolsButton, SettingsButton, CloseButton);
        Unloaded += OnUnloaded;
    }

    /// <summary>视图模型（XAML 里通过 x:Bind 绑定）。</summary>
    public DashboardViewModel ViewModel { get; }

    /// <summary>用户点了右下角的「关闭程序」。</summary>
    public event Action? ExitRequested;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 先立即采一次，看板一出现就有数据。
        await SampleAsync();
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    private async void OnTimerTick(object? sender, object e) => await SampleAsync();

    // ---------------- 视图切换 ----------------

    /// <summary>「时间工具」：push 到工具视图。</summary>
    private void OnTimeToolsClick(object sender, RoutedEventArgs e) => SetTimeToolsVisible(true);

    /// <summary>左上角「返回」：pop 回指标视图。</summary>
    private void OnTimeToolsBackClick(object sender, RoutedEventArgs e) => SetTimeToolsVisible(false);

    /// <summary>
    /// iOS 风格的过场：
    ///   * push（进工具视图）：旧视图往左滑出、新视图从右滑进来；
    ///   * pop（返回）：方向相反。
    /// 只切四个元素的可见性，不碰面板尺寸 / 窗口区域 / 外层弹簧。
    ///
    /// 旧视图得等出场动画放完才能收起来（否则看不到淡出），所以这里延迟收尾；
    /// 用序号保证连点时只有最后一次收尾生效，不会把刚滑进来的视图又收掉。
    /// </summary>
    private async void SetTimeToolsVisible(bool visible)
    {
        if (_timeToolsVisible == visible)
        {
            return;
        }

        _timeToolsVisible = visible;

        var token = ++_viewSwitchToken;
        var direction = visible ? 1f : -1f;   // +1 = 新视图从右边进来

        var incomingFirst = visible ? (FrameworkElement)TimeToolsBackButton : MetricsView;
        var incomingSecond = visible ? (FrameworkElement)TimeToolsPanel : TimeToolsButton;
        var outgoingFirst = visible ? (FrameworkElement)MetricsView : TimeToolsBackButton;
        var outgoingSecond = visible ? (FrameworkElement)TimeToolsButton : TimeToolsPanel;

        // 新视图先显示；旧视图暂时留着，两个视图才能在过场期间交叉滑动。
        incomingFirst.Visibility = Visibility.Visible;
        incomingSecond.Visibility = Visibility.Visible;

        PlayEnter(incomingFirst, direction);
        PlayEnter(incomingSecond, direction);
        PlayExit(outgoingFirst, direction);
        PlayExit(outgoingSecond, direction);

        Diagnostics.Log($"dashboard view: {(visible ? "time tools" : "metrics")}");

        await Task.Delay(ViewExitDuration);

        if (token != _viewSwitchToken)
        {
            return;   // 期间又切了一次，交回给后来那次收尾
        }

        outgoingFirst.Visibility = Visibility.Collapsed;
        outgoingSecond.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 把视图复位回「指标」，**不播过场**：每次看板从收起态展开之前由 <see cref="IslandShell"/> 调用，
    /// 这样不管上次停在哪、鼠标是怎么移开的，下一次展开看到的总是指标视图。
    /// </summary>
    public void ResetToMetricsView()
    {
        if (!_timeToolsVisible)
        {
            return;
        }

        _timeToolsVisible = false;

        // 让还在等收尾的那次过场失效，免得它 200ms 后把刚复位好的指标视图又收起来。
        _viewSwitchToken++;

        MetricsView.Visibility = Visibility.Visible;
        TimeToolsButton.Visibility = Visibility.Visible;
        TimeToolsBackButton.Visibility = Visibility.Collapsed;
        TimeToolsPanel.Visibility = Visibility.Collapsed;

        // 复位不是过场：把四个元素都推回静止态，免得有半截动画（位移 / 透明度）留在上面。
        SnapToRest(MetricsView);
        SnapToRest(TimeToolsButton);
        SnapToRest(TimeToolsBackButton);
        SnapToRest(TimeToolsPanel);

        Diagnostics.Log("dashboard view: metrics (reset)");
    }

    /// <summary>
    /// 把元素推回静止态：透明度 1、位移 0。
    ///
    /// 这里**不能**用 <c>StopAnimation</c> —— `Translation` 是
    /// <c>ElementCompositionPreview.SetIsTranslationEnabled()</c> 挂上去的动画属性，
    /// 停掉动画并不保证回到 0，会把上一次出场的 -29px 留在元素上 ——
    /// 表现就是「下次展开时整个看板内容错位」。所以用一段极短动画显式推到目标值。
    /// </summary>
    private static void SnapToRest(FrameworkElement view)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(view, true);

        var visual = ElementCompositionPreview.GetElementVisual(view);
        var compositor = visual.Compositor;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = SnapDuration;

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(1f, Vector3.Zero);
        slide.Duration = SnapDuration;

        visual.StartAnimation(nameof(Visual.Opacity), fade);
        visual.StartAnimation("Translation", slide);
    }

    /// <summary>
    /// 新视图：淡入 + 从 <paramref name="direction"/> 那侧滑进来。
    /// 缓动用 iOS 弹层那条 cubic-bezier(0.32, 0.72, 0, 1) —— 起手快、收尾柔和，看着「丝滑」。
    /// </summary>
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
        fade.Duration = ViewEnterDuration;

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new Vector3(direction * ViewSlideDistance, 0f, 0f));
        slide.InsertKeyFrame(1f, Vector3.Zero, ease);
        slide.Duration = ViewEnterDuration;

        visual.StartAnimation(nameof(Visual.Opacity), fade);
        visual.StartAnimation("Translation", slide);
    }

    /// <summary>旧视图：淡出 + 往相反方向滑走（少走一点，形成视差）。</summary>
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
        fade.Duration = ViewExitDuration;

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, Vector3.Zero);
        slide.InsertKeyFrame(
            1f,
            new Vector3(-direction * ViewSlideDistance * ViewExitParallax, 0f, 0f),
            ease);
        slide.Duration = ViewExitDuration;

        visual.StartAnimation(nameof(Visual.Opacity), fade);
        visual.StartAnimation("Translation", slide);
    }

    /// <summary>「关闭程序」：退出应用的唯一入口。</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Diagnostics.Log("close button clicked -> exit");
        ExitRequested?.Invoke();
    }

    private async Task SampleAsync()
    {
        // 采样在后台线程，避免偶发的慢系统调用把 UI 线程（以及动画的定时器）挤住。
        if (_sampling)
        {
            return;
        }

        _sampling = true;
        try
        {
            await ViewModel.RefreshAsync();
        }
        finally
        {
            _sampling = false;
        }
    }
}
