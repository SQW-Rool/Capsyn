using System.Diagnostics;
using Capsyn.Configuration;
using Capsyn.Helpers;
using Capsyn.Interop;
using Capsyn.Services;
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

    /// <summary>驱动看板岛外轮廓的弹簧。</summary>
    private readonly SpringScalar _panelSpring = new();

    private IslandAnimator? _panelAnimator;
    private bool _expanded;
    private bool _shapeSyncRunning;
    private bool _ready;
    private double _lastTickSeconds;

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

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>窗口在屏幕上的矩形（物理像素）。由 MainWindow 注入，用来判断光标是否落在岛上。</summary>
    public Func<RectInt32>? WindowRectProvider { get; set; }

    /// <summary>形变进度：(看板 ScaleX, 看板 ScaleY)。MainWindow 收到后同步窗口区域。</summary>
    public event Action<double, double>? ShapeProgress;

    /// <summary>用户点了看板岛右下角的「关闭程序」。</summary>
    public event Action? ExitRequested;

    /// <summary>收起态时看板的缩放（= 胶囊尺寸）。</summary>
    public (double X, double Y) CollapsedPanelScale => (_options.CollapsedScaleX, _options.CollapsedScaleY);

    /// <summary>看板岛是否展开。</summary>
    public bool IsExpanded => _expanded;

    private void ApplyGeometry()
    {
        // 整块底板铺满窗口（窗口本身比岛大，含弹簧回弹余量），形状怎么变底下都是黑的。
        IslandBackdrop.Background = new SolidColorBrush(
            ColorHelper.Parse(_options.PillColor, Microsoft.UI.Colors.Black));

        // 时间岛：顶部居中，一直可见。
        Clock.Width = _options.Width;
        Clock.Height = _options.Height;

        // 看板岛内容：在时间岛下方，隔一条缝隙。
        Dashboard.Width = _options.ExpandedWidth;
        Dashboard.Height = _options.ExpandedHeight;
        Dashboard.Margin = new Thickness(0, _options.PanelTop, 0, 0);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _panelAnimator = new IslandAnimator(
            Dashboard,
            _options.ContentFadeInDuration,
            _options.ContentFadeOutDuration);

        Dashboard.ExitRequested += OnExitRequested;

        _panelSpring.Reset(0);
        _ready = true;

        // 启动即收起态：先把形状同步给窗口区域，再开始监听光标。
        ShapeProgress?.Invoke(_options.CollapsedScaleX, _options.CollapsedScaleY);
        _hoverTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Dashboard.ExitRequested -= OnExitRequested;
        _hoverTimer.Stop();
        _collapseTimer.Stop();
        StopShapeSync();
        _ready = false;
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

        // 离开了：交给宽限计时器（宽限期内移回来就不收）
        if (_expanded && !_collapseTimer.IsEnabled)
        {
            Diagnostics.Log("hover: outside island -> collapse scheduled");
            _collapseTimer.Start();
        }
    }

    private void OnCollapseTick(object? sender, object e)
    {
        _collapseTimer.Stop();

        if (WindowRectProvider is not null
            && NativeMethods.GetCursorPos(out var cursor)
            && IsCursorInside(cursor, BaseIslandRect()))
        {
            // 宽限期内又移回来了，保持展开
            return;
        }

        SetDashboardExpanded(false);
    }

    /// <summary>时间岛 + 看板岛的命中框（展开时取两者的外接矩形，含中间缝隙）。</summary>
    private RectInt32 BaseIslandRect()
    {
        var window = WindowRectProvider!;
        var origin = window();
        var scale = XamlRoot?.RasterizationScale ?? 1.0;

        if (!_expanded)
        {
            return new RectInt32(
                origin.X + (int)Math.Round(_options.PillLeft * scale),
                origin.Y,
                (int)Math.Round(_options.Width * scale),
                (int)Math.Round(_options.Height * scale));
        }

        return new RectInt32(
            origin.X + (int)Math.Round(((_options.WindowWidth - _options.ExpandedWidth) / 2.0) * scale),
            origin.Y,
            (int)Math.Round(_options.ExpandedWidth * scale),
            (int)Math.Round((_options.PanelTop + _options.ExpandedHeight) * scale));
    }

    private static bool IsCursorInside(NativeMethods.POINT cursor, RectInt32 rect)
        => cursor.X >= rect.X && cursor.X <= rect.X + rect.Width
           && cursor.Y >= rect.Y && cursor.Y <= rect.Y + rect.Height;

    // ---------------- 状态切换 ----------------

    private void SetDashboardExpanded(bool expanded)
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
            Dashboard.ResetToMetricsView();

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

    private void OnShapeSyncTick(object? sender, object e)
    {
        var elapsedSeconds = _shapeClock.Elapsed.TotalSeconds;
        var deltaSeconds = elapsedSeconds - _lastTickSeconds;
        _lastTickSeconds = elapsedSeconds;

        _panelSpring.Advance(deltaSeconds);

        var targetPanel = _expanded ? 1.0 : 0.0;
        var panelProgress = Math.Clamp(_panelSpring.Value, 0.0, 1.2);

        // 看板：横向从胶囊宽度长到看板宽度，纵向从 0 高长到看板高度。
        var panelScaleX = _options.CollapsedScaleX + ((1.0 - _options.CollapsedScaleX) * panelProgress);
        var panelScaleY = _options.CollapsedScaleY + ((1.0 - _options.CollapsedScaleY) * panelProgress);
        ShapeProgress?.Invoke(panelScaleX, panelScaleY);

        var elapsedMs = _shapeClock.ElapsedMilliseconds;
        if ((_panelSpring.IsSettled && elapsedMs > 200) || elapsedMs > 2400)
        {
            // 收尾：精确对齐到终态。
            var finalScaleX = targetPanel > 0.5 ? 1.0 : _options.CollapsedScaleX;
            var finalScaleY = targetPanel > 0.5 ? 1.0 : _options.CollapsedScaleY;
            ShapeProgress?.Invoke(finalScaleX, finalScaleY);
            StopShapeSync();
        }
    }
}
