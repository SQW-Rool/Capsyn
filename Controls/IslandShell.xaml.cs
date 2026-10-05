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
/// 形态：时间岛（胶囊，固定）→ 悬停后在它下方长出看板岛 → 悬停看板岛的「电源」按钮时，
///       再在看板岛下方长出电源岛。
///
/// 入场顺序：先播电源岛的出场动画，随后（<see cref="IslandOptions.DashboardNudgeDelayMs"/> 之后）
///           让看板岛轻量重播一次入场（卡片错帧「让一下」）。
/// 出场顺序：级联收回 —— 先收电源岛，再收看板岛，与入场对称。
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
    private readonly DispatcherTimer _powerCloseTimer;
    private readonly DispatcherTimer _cascadeTimer;
    private readonly DispatcherTimer _nudgeTimer;
    private readonly DispatcherTimer _shapeSyncTimer;
    private readonly Stopwatch _shapeClock = new();

    /// <summary>驱动看板岛外轮廓的弹簧。</summary>
    private readonly SpringScalar _panelSpring = new();

    /// <summary>驱动电源岛外轮廓的弹簧。</summary>
    private readonly SpringScalar _powerSpring = new();

    private IslandAnimator? _panelAnimator;
    private IslandAnimator? _powerAnimator;
    private bool _expanded;
    private bool _powerExpanded;
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

        _powerCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _powerCloseTimer.Tick += OnPowerCloseTick;

        _cascadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
        _cascadeTimer.Tick += OnCascadeTick;

        _nudgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_options.DashboardNudgeDelayMs) };
        _nudgeTimer.Tick += OnNudgeTick;

        _shapeSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ShapeSyncIntervalMs) };
        _shapeSyncTimer.Tick += OnShapeSyncTick;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>窗口在屏幕上的矩形（物理像素）。由 MainWindow 注入，用来判断光标是否落在岛上。</summary>
    public Func<RectInt32>? WindowRectProvider { get; set; }

    /// <summary>形变进度：(看板 ScaleX, 看板 ScaleY, 电源岛 ScaleY)。MainWindow 收到后同步窗口区域。</summary>
    public event Action<double, double, double>? ShapeProgress;

    /// <summary>用户在电源岛里点了「关闭程序」。</summary>
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

        // 电源岛内容：在看板岛下方，再隔一条缝隙。
        PowerIsland.Width = _options.PowerIslandWidth;
        PowerIsland.Height = _options.PowerIslandHeight;
        PowerIsland.Margin = new Thickness(0, _options.PowerIslandTop, 0, 0);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _panelAnimator = new IslandAnimator(
            Dashboard,
            _options.ContentFadeInDuration,
            _options.ContentFadeOutDuration);

        _powerAnimator = new IslandAnimator(
            PowerIsland,
            fadeInDuration: TimeSpan.FromMilliseconds(180),
            fadeOutDuration: TimeSpan.FromMilliseconds(110));

        PowerIsland.ExitRequested += OnExitRequested;

        _panelSpring.Reset(0);
        _powerSpring.Reset(0);
        _ready = true;

        // 启动即收起态：先把形状同步给窗口区域，再开始监听光标。
        ShapeProgress?.Invoke(_options.CollapsedScaleX, _options.CollapsedScaleY, 0);
        _hoverTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        PowerIsland.ExitRequested -= OnExitRequested;
        _hoverTimer.Stop();
        _collapseTimer.Stop();
        _powerCloseTimer.Stop();
        _cascadeTimer.Stop();
        _nudgeTimer.Stop();
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

        var onPowerButton = _expanded && IsCursorOnPowerButton(cursor);
        var inPowerIsland = _powerExpanded && IsCursorInside(cursor, PowerIslandRect());

        if (onPowerButton || inPowerIsland)
        {
            // 停在电源按钮 / 电源岛里：看板岛与电源岛都保持展开
            _collapseTimer.Stop();
            _powerCloseTimer.Stop();
            _cascadeTimer.Stop();
            SetDashboardExpanded(true);
            SetPowerExpanded(true);
            return;
        }

        if (IsCursorInside(cursor, BaseIslandRect()))
        {
            // 在时间岛 / 看板岛的其它区域：只显示看板岛，电源岛收回
            _collapseTimer.Stop();
            _cascadeTimer.Stop();
            SetDashboardExpanded(true);
            if (_powerExpanded && !_powerCloseTimer.IsEnabled)
            {
                Diagnostics.Log("hover: left power button -> power island closing");
                _powerCloseTimer.Start();
            }

            return;
        }

        // 全都离开了：交给宽限计时器做级联收回
        if ((_expanded || _powerExpanded) && !_collapseTimer.IsEnabled)
        {
            Diagnostics.Log("hover: outside island -> collapse scheduled");
            _collapseTimer.Start();
        }
    }

    private void OnPowerCloseTick(object? sender, object e)
    {
        _powerCloseTimer.Stop();

        if (WindowRectProvider is not null
            && NativeMethods.GetCursorPos(out var cursor)
            && (IsCursorOnPowerButton(cursor) || IsCursorInside(cursor, PowerIslandRect())))
        {
            // 宽限期内又回到按钮 / 电源岛上了
            return;
        }

        SetPowerExpanded(false);
    }

    private void OnCollapseTick(object? sender, object e)
    {
        _collapseTimer.Stop();

        if (WindowRectProvider is not null
            && NativeMethods.GetCursorPos(out var cursor)
            && (IsCursorInside(cursor, BaseIslandRect())
                || IsCursorInside(cursor, PowerIslandRect())
                || IsCursorOnPowerButton(cursor)))
        {
            // 宽限期内又移回来了，保持展开
            return;
        }

        CollapseAll();
    }

    private void OnCascadeTick(object? sender, object e)
    {
        _cascadeTimer.Stop();

        if (WindowRectProvider is not null
            && NativeMethods.GetCursorPos(out var cursor)
            && (IsCursorInside(cursor, BaseIslandRect())
                || IsCursorInside(cursor, PowerIslandRect())
                || IsCursorOnPowerButton(cursor)))
        {
            // 级联途中又回来了：把电源岛重新展开
            SetDashboardExpanded(true);
            SetPowerExpanded(true);
            return;
        }

        SetDashboardExpanded(false);
    }

    /// <summary>级联收回：先电源岛，再看板岛（与入场顺序对称）。</summary>
    private void CollapseAll()
    {
        if (_powerExpanded)
        {
            SetPowerExpanded(false);
            _cascadeTimer.Start();
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

    /// <summary>电源岛的命中框。</summary>
    private RectInt32 PowerIslandRect()
    {
        var origin = WindowRectProvider!();
        var scale = XamlRoot?.RasterizationScale ?? 1.0;

        return new RectInt32(
            origin.X + (int)Math.Round(((_options.WindowWidth - _options.PowerIslandWidth) / 2.0) * scale),
            origin.Y + (int)Math.Round(_options.PowerIslandTop * scale),
            (int)Math.Round(_options.PowerIslandWidth * scale),
            (int)Math.Round(_options.PowerIslandHeight * scale));
    }

    /// <summary>「电源」按钮的命中框（按钮不大，四周放宽 6 DIP 好悬停）。</summary>
    private bool IsCursorOnPowerButton(NativeMethods.POINT cursor)
    {
        if (WindowRectProvider is null)
        {
            return false;
        }

        var bounds = Dashboard.GetPowerButtonBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        var origin = WindowRectProvider();
        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        var tolerance = 6 * scale;

        var left = origin.X + (bounds.X * scale) - tolerance;
        var top = origin.Y + (bounds.Y * scale) - tolerance;
        var right = origin.X + ((bounds.X + bounds.Width) * scale) + tolerance;
        var bottom = origin.Y + ((bounds.Y + bounds.Height) * scale) + tolerance;

        return cursor.X >= left && cursor.X <= right && cursor.Y >= top && cursor.Y <= bottom;
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

    private void SetPowerExpanded(bool expanded)
    {
        if (_powerExpanded == expanded || _powerAnimator is null)
        {
            return;
        }

        _powerExpanded = expanded;
        Dashboard.SetPowerButtonActive(expanded);
        Diagnostics.Log($"SetPowerExpanded({expanded})");

        if (expanded)
        {
            _powerAnimator.Expand();
            _powerSpring.SetTarget(1.0, _options.PowerSpringDampingRatio, _options.PowerSpringPeriod.TotalSeconds);

            // 先电源岛出场，稍后让看板岛轻量重播一次入场。
            _nudgeTimer.Stop();
            _nudgeTimer.Start();
        }
        else
        {
            _nudgeTimer.Stop();
            _powerAnimator.Collapse();
            _powerSpring.SetTarget(0.0, _options.CollapseSpringDampingRatio, _options.PowerSpringPeriod.TotalSeconds);
        }

        StartShapeSync();
    }

    private void OnNudgeTick(object? sender, object e)
    {
        _nudgeTimer.Stop();
        _panelAnimator?.Nudge();
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
        _powerSpring.Advance(deltaSeconds);

        var targetPanel = _expanded ? 1.0 : 0.0;
        var targetPower = _powerExpanded ? 1.0 : 0.0;

        var panelProgress = Math.Clamp(_panelSpring.Value, 0.0, 1.2);
        var powerProgress = Math.Clamp(_powerSpring.Value, 0.0, 1.2);

        // 看板：横向从胶囊宽度长到看板宽度，纵向从 0 高长到看板高度。
        var panelScaleX = _options.CollapsedScaleX + ((1.0 - _options.CollapsedScaleX) * panelProgress);
        var panelScaleY = _options.CollapsedScaleY + ((1.0 - _options.CollapsedScaleY) * panelProgress);
        ShapeProgress?.Invoke(panelScaleX, panelScaleY, powerProgress);

        var elapsedMs = _shapeClock.ElapsedMilliseconds;
        if ((_panelSpring.IsSettled && _powerSpring.IsSettled && elapsedMs > 200) || elapsedMs > 2400)
        {
            // 收尾：精确对齐到终态。
            var finalScaleX = targetPanel > 0.5 ? 1.0 : _options.CollapsedScaleX;
            var finalScaleY = targetPanel > 0.5 ? 1.0 : _options.CollapsedScaleY;
            ShapeProgress?.Invoke(finalScaleX, finalScaleY, targetPower > 0.5 ? 1.0 : 0.0);
            StopShapeSync();
        }
    }
}
