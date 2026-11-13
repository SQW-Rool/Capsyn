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
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;

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

    /// <summary>
    /// 音乐副岛滑出进度 0~1：0 = 藏在看板后面，1 = 完全滑到右侧。
    /// 展开时先等看板长好（progress &gt; 0.8）再滑出；鼠标离开立刻往回收。
    /// </summary>
    private readonly SpringScalar _musicSlideSpring = new();

    private double _musicSlide;
    private double _musicDashLeftDip;
    private double _musicSideHeightDip;
    private double _musicSideLeftDip;

    /// <summary>胶囊宽度的目标值（弹簧没有 getter，单独记一份用于去重）。</summary>
    private double _pillWidthTarget;

    /// <summary>共享的时间工具状态 + 驱动它 tick 的定时器（正计时/倒计时收起后也要继续走）。</summary>
    private readonly TimeToolsViewModel _timeTools = TimeToolsViewModel.Shared;

    private readonly DispatcherTimer _timeToolsTimer;

    private IslandAnimator? _panelAnimator;

    /// <summary>系统媒体会话（SMTC）监听：音乐副岛 + 胶囊内容的数据来源。</summary>
    private MediaMonitor? _media;

    /// <summary>当前有没有可用的媒体会话（决定胶囊内容 & 右侧副岛是否出现）。</summary>
    private bool _musicAvailable;
    private bool _expanded;
    private bool _shapeSyncRunning;
    private bool _ready;

    /// <summary>到点提醒正在显示：这期间鼠标移开也不收起（响铃总得让人看见 / 点掉）。</summary>
    private bool _alertActive;

    /// <summary>正在往岛上拖东西（虚线落点已显示）。</summary>
    private bool _dragActive;

    /// <summary>拖拽离开后有 350ms 宽限：从落点区挪到窗口别处时不至于闪一下。</summary>
    private readonly DispatcherTimer _dragLeaveTimer;

    /// <summary>落点区 / 解压中 / 结果显示期间，看板请求「保持展开」。</summary>
    private bool _keepExpanded;

    /// <summary>调试钩子 <c>CAPSYN_DROP_TEST</c> 用的一次性计时器。</summary>
    private DispatcherTimer? _dropTestTimer;

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

        // 拖拽离开的宽限计时器（从落点区挪到窗口别处时别闪）。
        _dragLeaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _dragLeaveTimer.Tick += OnDragLeaveTick;

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

        // 音乐：拿到系统媒体会话后接给右侧副岛，同时驱动收起态胶囊的内容。
        _media = new MediaMonitor(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        MusicIsland.AttachMedia(_media);
        _media.Changed += OnMediaChanged;
        _ = _media.InitializeAsync();

        Dashboard.ExitRequested += OnExitRequested;
        Dashboard.PreferredPanelHeightChanged += OnPreferredPanelHeightChanged;
        Dashboard.KeepExpandedChanged += OnDashboardKeepExpandedChanged;

        _panelSpring.Reset(0);
        _heightSpring.Reset(_options.ExpandedHeight);
        _pillWidthSpring.Reset(_options.Width);
        _dotSpring.Reset(0);
        _musicSlideSpring.Reset(0);
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
        ElementCompositionPreview.SetIsTranslationEnabled(Dashboard, true);
        ElementCompositionPreview.SetIsTranslationEnabled(MusicIsland, true);
        ElementCompositionPreview.SetIsTranslationEnabled(MusicPill, true);

        ApplyMusicSlide(0, _panelWidthDip, _panelHeightDip);

        // 如果启动时已经有计时在跑（比如秒表跨了一次 Unloaded），把 tick 接上。
        SyncTimeToolsTimer();

        // 启动即收起态：先把形状同步给窗口区域，再开始监听光标。
        ShapeProgress?.Invoke(new IslandShapeSnapshot(
            _pillLeftDip, _options.Width, _dotLeftDip, _options.DotSize, 0,
            (_options.WindowWidth - _options.Width) / 2.0, _options.Width, 0,
            0, 0, 0, 0, 0));
        _hoverTimer.Start();


        RunDropDebugHooks();
    }

    /// <summary>
    /// 调试钩子（只在设了环境变量时执行）：
    ///   * <c>CAPSYN_DROP_TEST=&lt;zip 路径&gt;</c>：跳过拖拽和选目录，直接走一遍「落点区 → 进度 → 结果」；
    ///   * <c>CAPSYN_PICK_TEST=1</c>：只创建文件夹选择器并应用默认目录（不 Show），验证 COM 互操作。
    /// 真实拖拽没法用脚本模拟（OLE 拖放需要外部进程发起），所以用它做自动验证。
    /// </summary>
    private void RunDropDebugHooks()
    {
        var dropTestPath = Environment.GetEnvironmentVariable("CAPSYN_DROP_TEST");

        if (!string.IsNullOrWhiteSpace(dropTestPath) && System.IO.File.Exists(dropTestPath))
        {
            Diagnostics.Log($"drop test: {dropTestPath}");

            // 等视觉树"活"过来再展开：在 Loaded 里立刻起合成动画，动画可能不被提交，
            // 看板会停在 opacity 0（面板看不见）。延迟一拍，行为就接近真实拖拽了。
            _dropTestTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _dropTestTimer.Tick += (_, _) =>
            {
                _dropTestTimer?.Stop();
                SetDashboardExpanded(true, resetView: false);
                Dashboard.RunDropTest(dropTestPath);
            };
            _dropTestTimer.Start();
        }

        // CAPSYN_MUSIC_TEST=1：只把看板展开（音乐副岛会随会话自动出现，用于截图 / 自动验证）。
        if (Environment.GetEnvironmentVariable("CAPSYN_MUSIC_TEST") == "1")
        {
            var musicTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _dropTestTimer = musicTimer;
            musicTimer.Tick += (_, _) =>
            {
                musicTimer.Stop();
                SetDashboardExpanded(true, resetView: false);
                Diagnostics.Log("music test: 已展开看板");
            };
            musicTimer.Start();
        }

        if (Environment.GetEnvironmentVariable("CAPSYN_PICK_TEST") == "1")
        {
            FolderPicker.SmokeTest(@"C:\Windows");
        }

        // CAPSYN_DROP_ORDER_TEST=1：模拟"拖进来"的**顺序**（先切落点视图 + 定高，再展开岛），
        // 用来确认展开过程不会先按指标视图（350）长出来、也不会先闪一下指标卡片。
        // CAPSYN_DROP_ORDER_TEST=cancel：再模拟"拖出去"（取消），确认岛直接收起、不闪看板。
        var orderTest = Environment.GetEnvironmentVariable("CAPSYN_DROP_ORDER_TEST");

        if (orderTest is "1" or "cancel")
        {
            var orderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _dropTestTimer = orderTimer;
            orderTimer.Tick += (_, _) =>
            {
                orderTimer.Stop();
                Diagnostics.Log("order test: 模拟拖入 → ShowDropZone(false) 然后 SetDashboardExpanded(true)");
                Dashboard.ShowDropZone(animateHeight: false);
                SetDashboardExpanded(true, resetView: false);

                if (orderTest != "cancel")
                {
                    return;
                }

                var cancelTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                orderTimer = cancelTimer;
                _dropTestTimer = cancelTimer;   // 必须被字段持有，否则会被 GC（踩过两次）
                cancelTimer.Tick += (_, _) =>
                {
                    cancelTimer.Stop();

                    // 模拟**真实的拖出路径**：和 OnDragLeaveTick 一样，只清状态 + 通知收起，
                    // 收起交给 KeepExpandedChanged → 收起计时器（不直接调 SetDashboardExpanded）。
                    Diagnostics.Log("order test: 模拟拖出（光标在岛外）→ 走真实 CancelDrop 路径");
                    _dragActive = false;
                    Dashboard.CancelDrop();
                };
                cancelTimer.Start();
            };
            orderTimer.Start();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Dashboard.ExitRequested -= OnExitRequested;
        Dashboard.PreferredPanelHeightChanged -= OnPreferredPanelHeightChanged;
        Dashboard.KeepExpandedChanged -= OnDashboardKeepExpandedChanged;
        Clock.PreferredWidthChanged -= OnClockPreferredWidthChanged;
        _dragLeaveTimer.Stop();

        if (_media is not null)
        {
            _media.Changed -= OnMediaChanged;
            _media.Dispose();
            _media = null;
        }

        _dropTestTimer?.Stop();
        _dropTestTimer = null;
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

    // ---------------- 拖拽解压（把文件拖到岛上才弹落点区） ----------------
    //
    // 不做全局探测（鼠标钩子那套已经撤掉）：只有文件真的被拖到岛窗口上、
    // OLE 的 DragOver 打到我们时，才展开岛并显示虚线落点区。
    // 好处是绝不会误弹 —— 拖窗口、拖别的文件、在别处拖来拖去都没有任何反应。

    /// <summary>文件被拖到岛上：展开岛并切到「虚线落点」视图。</summary>
    private void OnShellDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;

        if (e.DragUIOverride is { } dragUi)
        {
            dragUi.Caption = "解压到…";
            dragUi.IsCaptionVisible = true;
        }

        _dragLeaveTimer.Stop();

        // 到点提醒正在显示时，别让拖拽把提醒面板顶掉。
        if (_alertActive || _dragActive)
        {
            return;
        }

        _dragActive = true;
        _collapseTimer.Stop();

        // 顺序很重要：先把视图切成落点区、并把看板高度定成 230（都是瞬切），
        // 再展开岛 —— 否则会先按指标视图的高度（350）长出来、再缩回落点区，
        // 而且第一眼看到的是指标卡片而不是解压岛。
        Dashboard.ShowDropZone();
        SetDashboardExpanded(true, resetView: false);

        Diagnostics.Log("drag: files entered island");
    }

    private void OnShellDragLeave(object sender, DragEventArgs e)
    {
        if (_dragActive)
        {
            _dragLeaveTimer.Start();
        }
    }

    private void OnDragLeaveTick(object? sender, object e)
    {
        _dragLeaveTimer.Stop();

        if (!_dragActive)
        {
            return;
        }

        _dragActive = false;
        Dashboard.CancelDrop();

        // 光标还在岛里（比如拖到边上晃了一下又回来）：复位成指标视图，
        // 别让"松开以解压"的旧状态留在展开的看板上。
        // 光标已经在岛外的情况什么都不做 —— 让岛直接收起，不要先闪一下看板。
        if (IsCursorInsideIsland())
        {
            Dashboard.ResetToMetricsView();
        }

        Diagnostics.Log("drag: left island");
    }

    private void OnShellDrop(object sender, DragEventArgs e)
    {
        _dragLeaveTimer.Stop();
        _dragActive = false;

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            Dashboard.CancelDrop();
            return;
        }

        e.Handled = true;
        _ = HandleDropAsync(e);
    }

    /// <summary>Drop 事件里拿文件路径（<c>DataView</c> 只在事件回调期间有效，所以立刻取）。</summary>
    private async Task HandleDropAsync(DragEventArgs e)
    {
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items
                .OfType<StorageFile>()
                .Select(file => file.Path)
                .Where(path => !string.IsNullOrEmpty(path))
                .ToList();

            Diagnostics.Log($"drag: dropped {paths.Count} item(s)");

            await Dashboard.HandleDropAsync(paths);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"drag: drop handling failed: {ex.Message}");
            Dashboard.CancelDrop();
        }
    }
    /// <summary>落点区 / 解压中 / 结果显示期间，看板请求「先别收起」。</summary>
    private void OnDashboardKeepExpandedChanged(bool keep)
    {
        _keepExpanded = keep;

        if (keep)
        {
            _collapseTimer.Stop();
            return;
        }

        // 结束之后按正常逻辑走：光标不在岛上就收起。
        if (!IsCursorInsideIsland())
        {
            _collapseTimer.Start();
        }
    }

    // ---------------- 时间工具的 tick ----------------

    private void OnTimeToolsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TimeToolsViewModel.NeedsClock):
                SyncTimeToolsTimer();
                UpdatePillContent();
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

    /// <summary>左键是否按着（0x8000 = 当前按下）。拖拽期间为 true。</summary>
    private static bool IsLeftButtonDown()
        => (NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0;

    private void OnHoverTick(object? sender, object e)
    {
        if (!_ready || WindowRectProvider is null || XamlRoot is null)
        {
            return;
        }

        // 左键按着 = 用户正在拖东西（拖文件 / 拖窗口）。
        // 这时**不响应悬停**：否则拖着一个 zip 划过胶囊的瞬间，这里会先把指标看板（350 高）展开，
        // 紧接着 OLE 拖放事件到了又切到解压岛（230 高）—— 看起来就是"先显示看板，再显示解压岛"。
        // 交给拖放那套逻辑（DragOver / Drop）去展开，岛就能一次性长成解压岛。
        //
        // 注意：这里**不能**顺手 `_collapseTimer.Stop()`。拖拽中每 50ms 进来一次，
        // 一停就意味着"移出岛之后"永远收不起来（落点区会一直挂着）。收起该不该发生由
        // OnCollapseTick 里的 _dragActive / _keepExpanded 判断，交给它就好。
        if (IsLeftButtonDown())
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
        if (_expanded && !_alertActive && !_dragActive && !_keepExpanded && !_collapseTimer.IsEnabled)
        {
            Diagnostics.Log("hover: outside island -> collapse scheduled");
            _collapseTimer.Start();
        }
    }

    private void OnCollapseTick(object? sender, object e)
    {
        _collapseTimer.Stop();

        if (_alertActive || _dragActive || _keepExpanded)
        {
            return;   // 提醒没关 / 正在拖拽 / 解压结果还没处理完，都别把岛收掉
        }

        if (WindowRectProvider is not null
            && NativeMethods.GetCursorPos(out var cursor)
            && IsCursorInside(cursor, BaseIslandRect()))
        {
            // 宽限期内又移回来了，保持展开
            return;
        }

        // 副岛还在外面：先让它滑回去（弹簧归零），再收起看板，避免副岛悬空。
        if (_musicSlide > 0.05)
        {
            _musicSlideSpring.SetTarget(0, _options.CollapseSpringDampingRatio, _options.CollapseSpringPeriod.TotalSeconds);

            // 形状同步可能在动画停稳后已经停了：重新开起来，让滑出弹簧继续推进。
            StartShapeSync();
            _collapseTimer.Start();
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
        // 有音乐副岛时命中框要扩到「看板 + 副岛」当前的整组位置，否则鼠标移到副岛上会被判成岛外。
        var height = Math.Max(_panelHeightDip, _options.ExpandedHeight);

        var leftDip = _musicDashLeftDip;
        var rightDip = _musicDashLeftDip + _options.ExpandedWidth;

        if (_musicSlide > 0.02)
        {
            height = Math.Max(height, _musicSideHeightDip);
            rightDip = Math.Max(rightDip, _musicSideLeftDip + _options.MusicSideWidth);
        }

        var widthDip = Math.Max(_options.ExpandedWidth, rightDip - leftDip);

        return new RectInt32(
            origin.X + (int)Math.Round(leftDip * scale),
            origin.Y,
            (int)Math.Round(widthDip * scale),
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

    // ---------------- 音乐：胶囊内容 + 右侧副岛 ----------------

    /// <summary>媒体会话变化：切胶囊内容（时间 → 音乐），并决定副岛出不出现。</summary>
    private void OnMediaChanged(MediaSnapshot snapshot)
    {
        var wasAvailable = _musicAvailable;
        _musicAvailable = snapshot.IsAvailable;

        MusicPillTitle.Text = snapshot.IsAvailable ? snapshot.Title : string.Empty;
        MusicPillState.Text = snapshot.IsAvailable && !snapshot.IsPlaying ? "已暂停" : string.Empty;

        // 收起态胶囊：计时/闹钟在跑时优先显示计时，不被音乐顶掉。
        UpdatePillContent();

        if (wasAvailable != _musicAvailable)
        {
            StartShapeSync();
            Diagnostics.Log($"music island: {( _musicAvailable ? "出现" : "收起")}");
        }
    }

    /// <summary>
    /// 收起态胶囊显示什么：计时优先 > 音乐 > 时钟。
    /// 这里看「占用」(<see cref="TimeToolsViewModel.HasMonitorActivity"/>) 而不是「要不要刷新」
    /// (<see cref="TimeToolsViewModel.NeedsClock"/>)：暂停的计时仍然是胶囊内容，不能被音乐顶掉。
    /// </summary>
    private void UpdatePillContent()
    {
        var showMusic = _musicAvailable && !_timeTools.HasMonitorActivity;

        Clock.Visibility = showMusic ? Visibility.Collapsed : Visibility.Visible;
        MusicPill.Visibility = showMusic ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 按滑出进度摆两块岛。左边距始终 = 「当前宽度居中 − 滑出偏移」，
    /// 这样收起（面板宽度缩回胶囊）时也是对称往中间收，不会停在偏移位置。
    /// <paramref name="slide"/> 0 = 看板居中、副岛藏在看板后面；1 = 看板左移、副岛滑到右侧且与看板等高。
    /// </summary>
    private void ApplyMusicSlide(double slide, double panelWidth, double panelHeight)
    {
        slide = Math.Clamp(slide, 0.0, 1.0);

        var shift = ((_options.MusicSideWidth + _options.MusicSideGap) / 2.0) * slide;

        // 看板：始终以「当前宽度」居中，再减去滑出偏移
        _musicDashLeftDip = ((_options.WindowWidth - panelWidth) / 2.0) - shift;

        // 副岛：贴着看板右边缘 + 缝隙；slide=0 时整体藏到看板后面
        var gap = _options.MusicSideGap;
        var sideWidth = _options.MusicSideWidth;
        var finalSideLeft = _musicDashLeftDip + panelWidth + gap;
        _musicSideLeftDip = finalSideLeft - ((sideWidth + gap) * (1.0 - slide));

        // 副岛与看板等高（跟着当前看板高度走，展开/收起都对齐）
        _musicSideHeightDip = Math.Max(panelHeight, 0);

        Dashboard.HorizontalAlignment = HorizontalAlignment.Left;
        Dashboard.Margin = new Thickness(_musicDashLeftDip, _options.PanelTop, 0, 0);
        Dashboard.UpdatePanelHeight(panelHeight);

        MusicIsland.Width = sideWidth;
        MusicIsland.Height = _musicSideHeightDip;
        MusicIsland.Margin = new Thickness(_musicSideLeftDip, _options.PanelTop, 0, 0);
        MusicIsland.Visibility = slide > 0.01 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>把形状发给窗口区域（区域 = 胶囊 ∪ 小圆 ∪ 看板 ∪ 音乐副岛）。</summary>
    private void PublishShape(
        double pillLeft,
        double pillWidth,
        double dotLeft,
        double dotProgress,
        double panelWidth,
        double panelHeight,
        double progress)
    {
        // 音乐副岛：位置由滑出弹簧驱动（ApplyMusicSlide 每帧算好），这里直接用。
        var musicSide = _musicSlide > 0.02 && progress > 0.4;

        ShapeProgress?.Invoke(new IslandShapeSnapshot(
            pillLeft, pillWidth, dotLeft, _options.DotSize, dotProgress,
            _musicDashLeftDip, panelWidth, panelHeight,
            _musicSideLeftDip, musicSide ? _options.MusicSideWidth : 0, _musicSideHeightDip, musicSide ? _musicSlide : 0.0,
            progress));
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

        // 音乐副岛：看板先展开（progress > 0.8），再滑出；鼠标离开 / 收起都会让它往回收。
        var cursorInsideForMusic = IsCursorInsideIsland();
        var slideTarget = _expanded && _musicAvailable && progress > 0.8 && cursorInsideForMusic ? 1.0 : 0.0;
        _musicSlideSpring.SetTarget(
            slideTarget,
            _options.SpringDampingRatio,
            _options.SpringPeriod.TotalSeconds);
        _musicSlideSpring.Advance(deltaSeconds);
        _musicSlide = Math.Clamp(_musicSlideSpring.Value, 0.0, 1.0);
        ApplyMusicSlide(_musicSlide, panelWidth, panelHeight);

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

        PublishShape(pillLeft, pillWidth, dotLeft, dotProgress, panelWidth, panelHeight, progress);

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

            PublishShape(finalPillLeft, finalPillWidth, finalDotLeft, finalDotProgress, finalWidth, finalHeight, finalProgress);
            StopShapeSync();
        }
    }
}
