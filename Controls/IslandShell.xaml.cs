using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Capsyn.Configuration;
using Capsyn.Helpers;
using Capsyn.Interop;
using Capsyn.Services;
using Capsyn.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace Capsyn.Controls;

/// <summary>
/// 「岛」的状态机与外轮廓。
///
/// 形态：时间岛（胶囊，固定）→ 悬停后在它下方长出看板岛。
/// （原来的电源岛已按需求删除；「关闭程序」现在是看板岛右下角的一个图标按钮。）
///
/// 看板有两个视图，高度不一样：
///   * 指标视图 350 —— CPU / 内存 / 网络三张卡；
///   * 时间工具视图 430 —— 分段控件 + 环形读数 + 预设 + 主按钮。
/// 所以外轮廓由**两根**弹簧一起决定：
///   * <see cref="_panelSpring"/>：展开进度 0~1（管宽度和圆角，也管内容缩放）；
///   * <see cref="_heightSpring"/>：看板高度（DIP，350 ↔ 430），切换视图时单独长高/缩矮。
/// 窗口画布按「最高的视图」一次留够（<see cref="IslandOptions.WindowHeight"/> 用最大高度 + 回弹余量），
/// 切视图只改窗口区域，**不用动窗口尺寸**，所以顶部胶囊的位置一秒都不用重算。
///
/// 外轮廓（看得见的形状）由 <see cref="SpringScalar"/> 逐帧算出来喂给窗口区域：
/// WinUI 客户区不透明，只有窗口区域能裁出「不存在」的像素；而窗口区域是 Win32 的，
/// Composition 动画碰不到，所以这一层用与 Composition 侧相同时序参数的弹簧在 C# 侧积分。
/// </summary>
public sealed partial class IslandShell : UserControl
{
    /// <summary>外轮廓同步用的高频定时器（实测约 30Hz，够给弹簧形状用）。</summary>
    private const int ShapeSyncIntervalMs = 8;

    private readonly IslandOptions _options = IslandOptions.Default;
    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _shapeSyncTimer;
    private readonly Stopwatch _shapeClock = new();

    /// <summary>驱动看板「展开进度」的弹簧（0 = 收起，1 = 展开）。</summary>
    private readonly SpringScalar _panelSpring = new();

    /// <summary>驱动看板高度的弹簧（DIP）：指标视图 350 ↔ 时间工具视图 430。</summary>
    private readonly SpringScalar _heightSpring = new();

    /// <summary>驱动胶囊宽度的弹簧（DIP）：时钟 320 ↔ 监测区 172。</summary>
    private readonly SpringScalar _pillWidthSpring = new();

    /// <summary>驱动「第二个计时的小圆岛」出现进度（0 = 没有 / 1 = 完全展开）。</summary>
    private readonly SpringScalar _dotSpring = new();

    /// <summary>胶囊宽度的目标值（弹簧没有 getter，单独记一份用于去重）。</summary>
    private double _pillWidthTarget;

    /// <summary>共享的时间工具状态 + 驱动它 tick 的定时器（正计时/倒计时收起后也要继续走）。</summary>
    private readonly TimeToolsViewModel _timeTools = TimeToolsViewModel.Shared;

    private readonly DispatcherTimer _timeToolsTimer;

    private IslandAnimator? _panelAnimator;
    private bool _expanded;
    private bool _shapeSyncRunning;
    private bool _ready;

    /// <summary>到点提醒正在显示：这期间鼠标移开也不收起（响铃总得让人看见 / 点掉）。</summary>
    private bool _alertActive;

    private double _lastTickSeconds;

    /// <summary>最近一帧算出的胶囊 / 小圆 / 看板尺寸与位置（DIP），悬停判定要用它们。</summary>
    private double _pillLeftDip;
    private double _pillWidthDip;
    private double _dotLeftDip;
    private double _dotProgress;
    private double _panelWidthDip;
    private double _panelHeightDip;

    public IslandShell()
    {
        InitializeComponent();
        ApplyGeometry();

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_options.HoverPollMs) };
        _hoverTimer.Tick += OnHoverTick;

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_options.CollapseDelayMs) };
        _collapseTimer.Tick += OnCollapseTick;

        _shapeSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ShapeSyncIntervalMs) };
        _shapeSyncTimer.Tick += OnShapeSyncTick;

        // 时间工具的 tick 放在外壳里：它比看板 / 胶囊都活得久，收起后计时也照走。
        _timeToolsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timeToolsTimer.Tick += (_, _) => _timeTools.Tick();
        _timeTools.PropertyChanged += OnTimeToolsPropertyChanged;
        _timeTools.RingingChanged += OnTimeToolsRingingChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>窗口在屏幕上的矩形（物理像素）。由 MainWindow 注入，用来判断光标是否落在岛上。</summary>
    public Func<RectInt32>? WindowRectProvider { get; set; }

    /// <summary>
    /// 形变进度：一帧完整的轮廓参数（胶囊位置/宽度 + 小圆位置/进度 + 看板尺寸 + 展开进度）。
    /// MainWindow 收到后同步窗口区域。
    /// </summary>
    public event Action<IslandShapeSnapshot>? ShapeProgress;

    /// <summary>用户点了看板岛右下角的「关闭程序」。</summary>
    public event Action? ExitRequested;

    /// <summary>收起态时的形状（= 胶囊宽 × 0 高）。MainWindow 启动时按它先裁一次形状。</summary>
    public (double Width, double Height) CollapsedPanelSize => (_options.Width, 0);

    /// <summary>看板岛是否展开。</summary>
    public bool IsExpanded => _expanded;

    /// <summary>
    /// 【Phase 0 实验用】把岛体底色改成本颜色的半透明版本，用来观察窗口背景材质（Mica/Acrylic）是否生效。
    /// 只在设置窗口材质实验里被调用（<see cref="Services.IslandBackdrop"/>），默认路径不会调用它。
    /// </summary>
    public void SetBodyOpacity(double opacity)
    {
        var brush = new SolidColorBrush(ColorHelper.Parse(_options.PillColor, Microsoft.UI.Colors.Black));
        brush.Opacity = Math.Clamp(opacity, 0.0, 1.0);
        IslandBackdrop.Background = brush;
    }

    private void ApplyGeometry()
    {
        // 整块底板铺满窗口（窗口本身比岛大，含弹簧回弹余量），形状怎么变底下都是黑的。
        IslandBackdrop.Background = new SolidColorBrush(
            ColorHelper.Parse(_options.PillColor, Microsoft.UI.Colors.Black));

        // 时间岛：顶部居中，一直可见。
        Clock.Width = _options.Width;
        Clock.Height = _options.Height;

        // 第二个计时的小圆岛：尺寸固定，位置靠 Translation 摆（避免逐帧布局）。
        TimerDot.Width = _options.DotSize;
        TimerDot.Height = _options.DotSize;
        TimerDot.Opacity = 0;

        // 看板岛内容：在时间岛下方，隔一条缝隙。
        // 高度按「最高的视图」固定，切视图只改窗口区域（内容自己按视图排版，不重排）。
        Dashboard.Width = _options.ExpandedWidth;
        Dashboard.Height = _options.MaxExpandedHeight;
        Dashboard.Margin = new Thickness(0, _options.PanelTop, 0, 0);
        Dashboard.ApplyGeometry(_options);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _panelAnimator = new IslandAnimator(
            Dashboard,
            _options.ContentFadeInDuration,
            _options.ContentFadeOutDuration);

        Dashboard.ExitRequested += OnExitRequested;
        Dashboard.PreferredPanelHeightChanged += OnPreferredPanelHeightChanged;

        _panelSpring.Reset(0);
        _heightSpring.Reset(_options.ExpandedHeight);
        _pillWidthSpring.Reset(_options.Width);
        _dotSpring.Reset(0);
        _pillWidthTarget = _options.Width;
        _pillWidthDip = _options.Width;
        _pillLeftDip = _options.PillLeftFor(_options.Width);
        _dotProgress = 0;
        _dotLeftDip = _pillLeftDip - _options.DotGap - _options.DotSize;
        _panelWidthDip = _options.Width;
        _panelHeightDip = 0;
        Dashboard.UpdatePanelHeight(_options.ExpandedHeight);
        _ready = true;

        // 胶囊形态（时钟 / 监测区）变化时，弹簧去追新的宽度。
        Clock.PreferredWidthChanged += OnClockPreferredWidthChanged;

        // 胶囊和"第二个计时的小圆"都是靠 Translation 摆位置的（不触发布局）。
        ElementCompositionPreview.SetIsTranslationEnabled(Clock, true);
        ElementCompositionPreview.SetIsTranslationEnabled(TimerDot, true);

        // 如果启动时已经有计时在跑（比如秒表跨了一次 Unloaded），把 tick 接上。
        SyncTimeToolsTimer();

        // 启动即收起态：先把形状同步给窗口区域，再开始监听光标。
        ShapeProgress?.Invoke(new IslandShapeSnapshot(
            _pillLeftDip, _options.Width, _dotLeftDip, _options.DotSize, 0, _options.Width, 0, 0));
        _hoverTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Dashboard.ExitRequested -= OnExitRequested;
        Dashboard.PreferredPanelHeightChanged -= OnPreferredPanelHeightChanged;
        Clock.PreferredWidthChanged -= OnClockPreferredWidthChanged;
        _timeTools.RingingChanged -= OnTimeToolsRingingChanged;
        _hoverTimer.Stop();
        _collapseTimer.Stop();
        _timeToolsTimer.Stop();
        StopShapeSync();
        _ready = false;
    }

    // ---------------- 到点提醒（倒计时 / 闹钟响了） ----------------

    /// <summary>
    /// 响铃开始：先保证岛是展开的（不再单开窗口 —— 提醒就是岛的第二级面板），再切到提醒视图；
    /// 响铃结束：鼠标还在岛上就切回指标，否则直接收起（下次展开会自动复位成指标）。
    /// </summary>
    private void OnTimeToolsRingingChanged(RingtoneKind kind, bool ringing)
    {
        if (ringing)
        {
            _alertActive = true;

            // resetView: false —— 展开时会默认复位成指标视图，这里不能让它把提醒冲掉。
            SetDashboardExpanded(true, resetView: false);
            Dashboard.ShowAlert(kind == RingtoneKind.Alarm ? TimeToolMode.Alarm : TimeToolMode.Countdown);

            Diagnostics.Log($"alert: show {kind} (expanded={_expanded})");
            return;
        }

        _alertActive = false;

        if (IsCursorInsideIsland())
        {
            Dashboard.HideAlert();
        }
        else
        {
            SetDashboardExpanded(false);
        }

        Diagnostics.Log($"alert: dismiss {kind}");
    }

    /// <summary>光标是否还在岛上（提醒关掉后决定"切回指标"还是"直接收起"）。</summary>
    private bool IsCursorInsideIsland()
    {
        if (WindowRectProvider is null)
        {
            return false;
        }

        return NativeMethods.GetCursorPos(out var cursor) && IsCursorInside(cursor, BaseIslandRect());
    }

    // ---------------- 时间工具的 tick ----------------

    private void OnTimeToolsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TimeToolsViewModel.NeedsClock):
                SyncTimeToolsTimer();
                break;

            case nameof(TimeToolsViewModel.SecondaryActivity):
                // 第二个计时出现 / 消失：小圆岛淡入或缩回去，胶囊往右让半个圆。
                var showDot = _timeTools.SecondaryActivity.HasValue;
                _dotSpring.SetTarget(showDot ? 1.0 : 0.0, 0.8, 0.28);
                StartShapeSync();
                break;
        }
    }

    /// <summary>按共享状态开/关 tick：正计时 50ms（十分之一秒要动），其余 100ms 足够。</summary>
    private void SyncTimeToolsTimer()
    {
        if (!_timeTools.NeedsClock)
        {
            _timeToolsTimer.Stop();
            return;
        }

        var interval = _timeTools.PrimaryActivity == TimeToolMode.Stopwatch
            || _timeTools.Mode == TimeToolMode.Stopwatch
            ? TimeSpan.FromMilliseconds(50)
            : TimeSpan.FromMilliseconds(100);

        if (_timeToolsTimer.Interval != interval)
        {
            _timeToolsTimer.Interval = interval;
        }

        if (!_timeToolsTimer.IsEnabled)
        {
            _timeToolsTimer.Start();
        }
    }

    /// <summary>胶囊要换宽度（时钟 ↔ 监测区 ↔ 监测区+小圆）：弹簧追过去，窗口区域跟着逐帧变。</summary>
    private void OnClockPreferredWidthChanged(double widthDip)
    {
        if (Math.Abs(widthDip - _pillWidthTarget) < 0.01)
        {
            return;
        }

        _pillWidthTarget = widthDip;
        // 胶囊变宽/变窄是"轻量反馈"级别，用比面板展开更硬一点、更快的弹簧。
        _pillWidthSpring.SetTarget(widthDip, 0.82, 0.30);
        StartShapeSync();
    }

    private void OnExitRequested() => ExitRequested?.Invoke();

    // ---------------- 悬停判定 ----------------

    private void OnHoverTick(object? sender, object e)
    {
        if (!_ready || WindowRectProvider is null || XamlRoot is null)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        if (IsCursorInside(cursor, BaseIslandRect()))
        {
            // 在时间岛 / 看板岛里：保持展开
            _collapseTimer.Stop();
            SetDashboardExpanded(true);
            return;
        }

        // 离开了：交给宽限计时器（宽限期内移回来就不收）；到点提醒显示期间不收。
        if (_expanded && !_alertActive && !_collapseTimer.IsEnabled)
        {
            Diagnostics.Log("hover: outside island -> collapse scheduled");
            _collapseTimer.Start();
        }
    }

    private void OnCollapseTick(object? sender, object e)
    {
        _collapseTimer.Stop();

        if (_alertActive)
        {
            return;   // 提醒还没关，别把岛收掉
        }

        if (WindowRectProvider is not null
            && NativeMethods.GetCursorPos(out var cursor)
            && IsCursorInside(cursor, BaseIslandRect()))
        {
            // 宽限期内又移回来了，保持展开
            return;
        }

        SetDashboardExpanded(false);
    }

    /// <summary>
    /// 时间岛 + 小圆岛 + 看板岛的命中框。
    /// 收起态取「胶囊 ∪ 小圆」的外接矩形（含中间那条 10px 缝隙，无所谓），
    /// 展开态取看板矩形（它本来就比上面那些宽）。
    /// </summary>
    private RectInt32 BaseIslandRect()
    {
        var window = WindowRectProvider!;
        var origin = window();
        var scale = XamlRoot?.RasterizationScale ?? 1.0;

        if (!_expanded)
        {
            var left = _dotProgress > 0.02 ? Math.Min(_pillLeftDip, _dotLeftDip) : _pillLeftDip;
            var right = _pillLeftDip + _pillWidthDip;

            return new RectInt32(
                origin.X + (int)Math.Round(left * scale),
                origin.Y,
                (int)Math.Round(Math.Max(2, right - left) * scale),
                (int)Math.Round(_options.Height * scale));
        }

        // 展开态：高度取**当前**看板高度（时间工具视图比指标视图高 80），
        // 否则切到工具视图后底部那 80px 会被判成「岛外」，鼠标一移下去就收起。
        var height = Math.Max(_panelHeightDip, _options.ExpandedHeight);

        return new RectInt32(
            origin.X + (int)Math.Round(((_options.WindowWidth - _options.ExpandedWidth) / 2.0) * scale),
            origin.Y,
            (int)Math.Round(_options.ExpandedWidth * scale),
            (int)Math.Round((_options.PanelTop + height) * scale));
    }

    private static bool IsCursorInside(NativeMethods.POINT cursor, RectInt32 rect)
        => cursor.X >= rect.X && cursor.X <= rect.X + rect.Width
           && cursor.Y >= rect.Y && cursor.Y <= rect.Y + rect.Height;

    // ---------------- 状态切换 ----------------

    private void SetDashboardExpanded(bool expanded, bool resetView = true)
    {
        if (_expanded == expanded || _panelAnimator is null)
        {
            return;
        }

        _expanded = expanded;
        Diagnostics.Log($"SetDashboardExpanded({expanded})");

        if (expanded)
        {
            // 每次从收起态展开都从「指标」视图开始：上次停在时间工具视图（且没点返回）也复位掉。
            // 只在真的「收起 → 展开」时走到这里，宽限期内移回来的情况会提前 return，不会打断交互。
            // resetView: false 给"到点提醒"用 —— 它展开后马上要切到提醒视图，不能被这里冲掉。
            if (resetView)
            {
                Dashboard.ResetToMetricsView();
            }

            _panelAnimator.Expand();
            _panelSpring.SetTarget(1.0, _options.SpringDampingRatio, _options.SpringPeriod.TotalSeconds);
        }
        else
        {
            _panelAnimator.Collapse();
            _panelSpring.SetTarget(0.0, _options.CollapseSpringDampingRatio, _options.CollapseSpringPeriod.TotalSeconds);
        }

        StartShapeSync();
    }

    /// <summary>
    /// 看板说「这个视图要多高」：时间工具视图 430、指标视图 350。
    /// 第一次（复位）是直接落位，之后切换视图走弹簧长高 / 缩矮。
    /// </summary>
    private void OnPreferredPanelHeightChanged(double heightDip, bool animate)
    {
        if (!animate)
        {
            _heightSpring.Reset(heightDip);
            Dashboard.UpdatePanelHeight(heightDip);
            return;
        }

        if (Math.Abs(_heightSpring.Value - heightDip) < 0.01)
        {
            return;
        }

        _heightSpring.SetTarget(heightDip, _options.SpringDampingRatio, _options.SpringPeriod.TotalSeconds);
        StartShapeSync();
    }

    // ---------------- 外轮廓逐帧同步 ----------------

    private void StartShapeSync()
    {
        _shapeClock.Restart();
        _lastTickSeconds = 0;

        if (_shapeSyncRunning)
        {
            return;
        }

        _shapeSyncRunning = true;
        _shapeSyncTimer.Start();
    }

    private void StopShapeSync()
    {
        if (!_shapeSyncRunning)
        {
            return;
        }

        _shapeSyncRunning = false;
        _shapeSyncTimer.Stop();
    }

    /// <summary>收起态的内容缩放：跟着弹簧从 0.94 长到 1.0（等比缩放，避免文字被拉伸）。</summary>
    private const double ContentScaleFrom = 0.94;

    /// <summary>内容根元素的 Composition Visual（首次用时取）。</summary>
    private Visual? _contentVisual;

    /// <summary>把内容缩放到指定倍数；缩放中心固定在内容顶部中央，视觉上是"从上方长出来"。</summary>
    private void ApplyContentSpringScale(double scale)
    {
        _contentVisual ??= ElementCompositionPreview.GetElementVisual(Dashboard);
        _contentVisual.CenterPoint = new Vector3((float)(Dashboard.ActualWidth / 2.0), 0f, 0f);
        _contentVisual.Scale = new Vector3((float)scale, (float)scale, 1f);
    }

    private void OnShapeSyncTick(object? sender, object e)
    {
        var elapsedSeconds = _shapeClock.Elapsed.TotalSeconds;
        var deltaSeconds = elapsedSeconds - _lastTickSeconds;
        _lastTickSeconds = elapsedSeconds;

        _panelSpring.Advance(deltaSeconds);
        _heightSpring.Advance(deltaSeconds);
        _pillWidthSpring.Advance(deltaSeconds);
        _dotSpring.Advance(deltaSeconds);

        // 展开进度：管宽度、圆角、内容缩放（上限 1，避免横向过冲超出画布）。
        var progress = Math.Clamp(_panelSpring.Value, 0.0, 1.0);

        // 高度：展开进度 × 当前视图高度。展开过程本身有回弹（最多 ~1.12 倍，画布已留余量）。
        var panelHeight = Math.Max(0, _heightSpring.Value) * Math.Clamp(_panelSpring.Value, 0.0, 1.2);

        // 胶囊宽度：时钟 / 监测区，由胶囊自己定目标、弹簧插值。
        var pillWidth = Math.Clamp(_pillWidthSpring.Value, 120, _options.MaxPillWidth);

        // 小圆岛出现进度：胶囊往右让半个圆（(圆+缝)/2），整组仍然居中。
        var dotProgress = Math.Clamp(_dotSpring.Value, 0.0, 1.0);
        var pillLeft = _options.PillLeftFor(pillWidth) + (_options.PillShiftWithDot * dotProgress);
        var dotLeft = pillLeft - _options.DotGap - _options.DotSize;

        // 看板：横向从胶囊宽度长到看板宽度，纵向从 0 长到当前视图高度。
        var panelWidth = pillWidth + ((_options.ExpandedWidth - pillWidth) * progress);

        _pillLeftDip = pillLeft;
        _pillWidthDip = pillWidth;
        _dotLeftDip = dotLeft;
        _dotProgress = dotProgress;
        _panelWidthDip = panelWidth;
        _panelHeightDip = panelHeight;

        // 胶囊本体也跟着变宽、往右让位；小圆淡入 + 轻微放大（都只动合成属性，不触发布局）。
        Clock.Width = pillWidth;
        Clock.Translation = new Vector3((float)(_options.PillShiftWithDot * dotProgress), 0f, 0f);
        TimerDot.Opacity = dotProgress;
        TimerDot.Translation = new Vector3((float)dotLeft, 0f, 0f);
        var dotScale = 0.86f + (0.14f * (float)dotProgress);
        ElementCompositionPreview.GetElementVisual(TimerDot).Scale = new Vector3(dotScale, dotScale, 1f);

        // 内容跟着看板弹簧一起长：用**同一个弹簧进度**驱动内容等比缩放（从顶部中心展开），
        // 于是指标卡 / 按钮不是"先摆好再淡入"，而是随着面板一起长出来。
        var contentScale = ContentScaleFrom + ((1.0 - ContentScaleFrom) * progress);
        ApplyContentSpringScale(contentScale);

        // 右下角那排图标按钮始终贴着看板底边：看板长高 / 缩矮时跟着走（只改 Translation，不重排）。
        Dashboard.UpdatePanelHeight(panelHeight);

        ShapeProgress?.Invoke(new IslandShapeSnapshot(
            pillLeft, pillWidth, dotLeft, _options.DotSize, dotProgress, panelWidth, panelHeight, progress));

        var elapsedMs = _shapeClock.ElapsedMilliseconds;
        if ((_panelSpring.IsSettled && _heightSpring.IsSettled && _pillWidthSpring.IsSettled && _dotSpring.IsSettled && elapsedMs > 200)
            || elapsedMs > 2400)
        {
            // 收尾：精确对齐到终态。
            var finalPillWidth = Math.Clamp(_pillWidthSpring.Value, 120, _options.MaxPillWidth);
            var finalDotProgress = Math.Clamp(_dotSpring.Value, 0.0, 1.0);
            var finalPillLeft = _options.PillLeftFor(finalPillWidth) + (_options.PillShiftWithDot * finalDotProgress);
            var finalDotLeft = finalPillLeft - _options.DotGap - _options.DotSize;
            var finalWidth = _expanded ? _options.ExpandedWidth : finalPillWidth;
            var finalHeight = _expanded
                ? Math.Clamp(_heightSpring.Value, 0, _options.ExpandedToolsHeight)
                : 0;
            var finalProgress = _expanded ? 1.0 : 0.0;

            _pillLeftDip = finalPillLeft;
            _pillWidthDip = finalPillWidth;
            _dotLeftDip = finalDotLeft;
            _dotProgress = finalDotProgress;
            _panelWidthDip = finalWidth;
            _panelHeightDip = finalHeight;

            Clock.Width = finalPillWidth;
            Clock.Translation = new Vector3((float)(_options.PillShiftWithDot * finalDotProgress), 0f, 0f);
            TimerDot.Opacity = finalDotProgress;
            TimerDot.Translation = new Vector3((float)finalDotLeft, 0f, 0f);
            Dashboard.UpdatePanelHeight(finalHeight);

            ShapeProgress?.Invoke(new IslandShapeSnapshot(
                finalPillLeft, finalPillWidth, finalDotLeft, _options.DotSize,
                finalDotProgress, finalWidth, finalHeight, finalProgress));
            StopShapeSync();
        }
    }
}
