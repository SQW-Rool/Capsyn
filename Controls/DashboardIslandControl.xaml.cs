using System.Diagnostics;
using System.IO;
using System.Numerics;
using Capsyn.Configuration;
using Capsyn.Services;
using Capsyn.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace Capsyn.Controls;

/// <summary>
/// 「看板岛」UI，里面有两个视图，只切可见性：
///   * 指标视图（默认）：CPU / 内存 / 网络上下行，每秒采样一次（采样在线程池上跑）；
///   * 时间工具视图：正计时 / 倒计时 / 闹钟，真的能用的计时逻辑在 <see cref="TimeToolsViewModel"/>。
///
/// 两个视图高度不同（指标 350 / 时间工具 430）。切到时间工具时，
/// 这个控件通过 <see cref="PreferredPanelHeightChanged"/> 告诉 <see cref="IslandShell"/>
/// 「我要 430」，外壳用弹簧把窗口区域长高 —— 窗口画布本身按 430 一次留够，所以不用动窗口尺寸。
/// 右下角那排图标按钮两个视图共用，靠 <see cref="UpdatePanelHeight"/> 跟着看板底边滑（只改 Translation）。
///
/// 两个视图之间走一段 **iOS 风格 push / pop** 过场：旧视图往一侧淡出滑走、新视图从另一侧淡入滑进来
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

    /// <summary>环形进度的直径与线宽（和 XAML 里的 Ellipse 保持一致）。</summary>
    private const double RingSize = 96.0;
    private const double RingThickness = 6.0;

    /// <summary>根 Grid 的内边距（和 XAML 的 Padding="16" 保持一致）。</summary>
    private const double PanelPadding = 16.0;

    /// <summary>页脚（两个圆形按钮）高度 + 与内容之间留的缝，用来算工具视图内容区高度。</summary>
    private const double FooterReserve = 52.0;

    private readonly DispatcherTimer _timer;
    private bool _sampling;
    private DashboardView _view = DashboardView.Metrics;

    /// <summary>面板高度（DIP）：每个视图各一个，由 IslandOptions 给。</summary>
    private double _metricsHeight;
    private double _toolsHeight;
    private double _settingsHeight;
    private double _alertHeight;
    private double _dropHeight;
    private double _musicHeight;

    /// <summary>系统媒体会话服务（由 <see cref="IslandShell"/> 注入）；没注入时音乐视图显示空状态。</summary>
    private MediaMonitor? _media;

    /// <summary>音乐视图可见时，每 250ms 刷一次进度与位置文字（SMTC 自己不会推动画）。</summary>
    private DispatcherTimer? _musicTimer;

    /// <summary>最近一次媒体快照。</summary>
    private MediaSnapshot _mediaSnapshot = MediaSnapshot.Empty;

    /// <summary>上一次解压到的目录（「打开文件夹」用）。</summary>
    private string? _lastExtractFolder;

    /// <summary>落点区已显示、但还没有文件真的落下来（全局拖拽结束时用它决定要不要收掉）。</summary>
    private bool _dropPending;

    /// <summary>当前提醒是闹钟还是倒计时（决定图标、按钮、文案）。</summary>
    private TimeToolMode _alertKind = TimeToolMode.Alarm;

    /// <summary>页脚当前位移（DIP），避免每帧重复写同一个值。</summary>
    private double _footerOffset = double.NaN;

    /// <summary>环形进度上次画到哪，用来丢弃无意义的重画。</summary>
    private double _ringProgress = -1;

    /// <summary>分段控件每一格的宽度（SizeChanged 时更新）。</summary>
    private double _segmentWidth;

    /// <summary>过场序号：连点「时间工具 / 返回」时，只让最后一次的收尾生效。</summary>
    private int _viewSwitchToken;

    public DashboardIslandControl()
    {
        // x:Bind 在 InitializeComponent 时求值，ViewModel 必须先准备好。
        ViewModel = new DashboardViewModel();

        // 时间工具状态是共享实例：收起后顶部胶囊也要显示同一份计时。
        TimeTools = TimeToolsViewModel.Shared;

        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(IslandOptions.Default.MetricsIntervalSeconds),
        };
        _timer.Tick += OnTimerTick;

        // 计时由外壳 IslandShell 统一 tick（收起后也要走），这里只负责把状态画出来。
        TimeTools.PropertyChanged += OnTimeToolsPropertyChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // Phase 2/3：给这几个图标按钮挂统一指针反馈（hover 放大 / 按下缩小，Composition 隐式动画）
        Loaded += (_, _) => InteractiveAnimations.AttachToButtons(TimeToolsButton, ToolsBackButton, SettingsButton, CloseButton);

        // Phase 2：给三张指标卡挂 Fluent 阴影（ThemeShadow 不能在 Style 里资源化，见 CardElevation 注释）
        // 注意：CardBorder 定义在 Themes/DashboardStyles.xaml，合并进的是 App.Resources；
        // 控件自己的 Resources 里没有这个 key，直接 Resources["CardBorder"] 会抛
        // "Cannot find a resource with the given key"（原生 COMException，被 Application.UnhandledException 记进崩溃日志）。
        Loaded += (_, _) => CardElevation.ApplyCardElevation(this, ResolveCardBorderStyle());

        // 页脚 / 分段指示条都靠 Translation 动，先把这条属性打开。
        ElementCompositionPreview.SetIsTranslationEnabled(FooterLayer, true);
        ElementCompositionPreview.SetIsTranslationEnabled(SegmentIndicator, true);
    }

    /// <summary>视图模型（指标，XAML 里通过 x:Bind 绑定）。</summary>
    public DashboardViewModel ViewModel { get; }

    /// <summary>时间工具的视图模型（正计时 / 倒计时 / 闹钟）。</summary>
    public TimeToolsViewModel TimeTools { get; }

    /// <summary>用户点了右下角的「关闭程序」。</summary>
    public event Action? ExitRequested;

    /// <summary>
    /// 当前视图想要多高的看板：(高度 DIP, 是否播弹簧动画)。
    /// 指标视图 350、时间工具视图 430；<see cref="IslandShell"/> 收到后改窗口区域。
    /// </summary>
    public event Action<double, bool>? PreferredPanelHeightChanged;

    /// <summary>
    /// 按 <see cref="IslandOptions"/> 把两个视图的固定尺寸摆好（由 <see cref="IslandShell"/> 在初始化时调用）。
    /// 这里只设"面板高度"，不改窗口 —— 窗口画布由外壳按最大高度一次留够。
    /// </summary>
    public void ApplyGeometry(IslandOptions options)
    {
        _metricsHeight = options.ExpandedHeight;
        _toolsHeight = options.ExpandedToolsHeight;
        _settingsHeight = options.ExpandedSettingsHeight;
        _alertHeight = options.ExpandedAlertHeight;
        _dropHeight = options.ExpandedDropHeight;
        _musicHeight = options.ExpandedMusicHeight;

        // 指标视图：卡片 104 + 横线上方留白 15 之后才是内容区。
        MetricsPanel.Height = _metricsHeight - (2 * PanelPadding);

        // 时间工具视图：底部还要给页脚（两个圆形按钮）留 52。
        ToolsPanel.Height = _toolsHeight - (2 * PanelPadding) - FooterReserve;

        // 设置视图：从顶排到第二块铃声，页脚同样留 52。
        SettingsPanel.Height = _settingsHeight - (2 * PanelPadding) - FooterReserve;

        // 到点提醒视图：按钮在左下，页脚在右下，所以只扣内边距。
        AlertPanel.Height = _alertHeight - (2 * PanelPadding);

        // 拖拽解压视图：虚线落点 + 底部按钮。
        DropPanel.Height = _dropHeight - (2 * PanelPadding);

        UpdatePanelHeight(_metricsHeight);

        // 铃声按钮的文字来自内置音效库（唯一来源，别在 XAML 里再抄一遍名字）。
        FillRingtoneButtons(RingtoneKind.Alarm, AlarmTone0, AlarmTone1, AlarmTone2, AlarmTone3, AlarmTone4);
        FillRingtoneButtons(RingtoneKind.Countdown, CountdownTone0, CountdownTone1, CountdownTone2, CountdownTone3, CountdownTone4);
        ApplyRingtoneChips();

        // 初始状态：指标视图、环按当前进度画一次。
        ApplyModeVisuals();
        ApplyModeRowVisibility();
        UpdateSegmentIndicator(false);
        UpdateRing(TimeTools.Progress, force: true);
    }

    private static void FillRingtoneButtons(RingtoneKind kind, params Button[] buttons)
    {
        var library = RingtoneLibrary.For(kind);

        for (var i = 0; i < buttons.Length && i < library.Count; i++)
        {
            buttons[i].Content = library[i].Name;
        }
    }

    /// <summary>
    /// 看板当前显示多高（DIP）：页脚那排图标始终贴着看板底边，
    /// 所以把它整体上下平移「当前高度 - 工具视图高度」。只改 Translation，不触发重排。
    /// </summary>
    public void UpdatePanelHeight(double panelHeightDip)
    {
        var offset = (float)(panelHeightDip - _toolsHeight);

        if (!double.IsNaN(_footerOffset) && Math.Abs(offset - _footerOffset) < 0.05)
        {
            return;
        }

        _footerOffset = offset;
        FooterLayer.Translation = new Vector3(0f, offset, 0f);
    }

    /// <summary>
    /// 取 "CardBorder" 样式：先找本控件的 Resources，再找 App.Resources（Themes/DashboardStyles.xaml 合并在那里）。
    /// 用 TryGetValue 而不是索引器，key 缺失时返回 null（Cards 不上阴影，但不会抛异常）。
    /// </summary>
    private Style? ResolveCardBorderStyle()
    {
        if (Resources.TryGetValue("CardBorder", out var local) && local is Style localStyle)
        {
            return localStyle;
        }

        if (Application.Current?.Resources is { } appResources &&
            appResources.TryGetValue("CardBorder", out var appScoped) &&
            appScoped is Style appStyle)
        {
            return appStyle;
        }

        return null;
    }

    private static Brush? Res(string key)
        => Application.Current?.Resources is { } resources
           && resources.TryGetValue(key, out var value)
           && value is Brush brush
            ? brush
            : null;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 先立即采一次，看板一出现就有数据。
        await SampleAsync();
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _musicTimer?.Stop();
    }

    private async void OnTimerTick(object? sender, object e) => await SampleAsync();

    // ---------------- 时间工具：把共享状态画出来 ----------------

    private void OnTimeToolsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TimeToolsViewModel.Progress):
                RefreshRing();
                break;

            case nameof(TimeToolsViewModel.IsRinging):
                ApplyModeVisuals();
                RefreshRing(force: true);
                break;

            case nameof(TimeToolsViewModel.Mode):
                ApplyModeVisuals();
                ApplyModeRowVisibility();
                UpdateSegmentIndicator(true);
                RefreshRing(force: true);
                break;

            case nameof(TimeToolsViewModel.DisplayText):
                if (TimeTools.Mode == TimeToolMode.Alarm)
                {
                    RefreshRing();
                }

                break;
        }
    }

    /// <summary>响铃时环画满（表示"时间到"），平时画实际进度。</summary>
    private void RefreshRing(bool force = false)
        => UpdateRing(TimeTools.IsRinging ? 1.0 : TimeTools.Progress, force);

    // ---------------- 时间工具：交互 ----------------

    private void OnSegmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !int.TryParse(tag, out var index))
        {
            return;
        }

        var target = (TimeToolMode)index;

        if (TimeTools.Mode == target)
        {
            return;
        }

        // 只做"指示条滑到目标段"这一下（Fast），内容直接换 —— 不做整块内容的淡入淡出/位移。
        // 之前那版内容过场会偶发停在半透明/偏移状态（看起来像错位 / 被裁掉），按需求撤掉。
        TimeTools.SelectMode(target);

        ApplyModeRowVisibility();
        ApplyModeVisuals();
        UpdateSegmentIndicator(true, index);
        RefreshRing(force: true);

        Diagnostics.Log($"mode: {target}");
    }

    /// <summary>把模式专属的那一行（预设 / 分钟加减 / 记一圈）按 VM 直接摆一遍（双保险，防 x:Bind 漏通知叠在一起）。</summary>
    private void ApplyModeRowVisibility()
    {
        PresetRow.Visibility = TimeTools.PresetsVisibility;
        StepperRow.Visibility = TimeTools.StepperVisibility;
        LapRow.Visibility = TimeTools.StopwatchToolsVisibility;
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out var index))
        {
            TimeTools.SelectPreset(index);
            ApplyModeVisuals();
        }
    }

    private void OnAlarmStepClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out var minutes))
        {
            TimeTools.AdjustAlarmMinutes(minutes);
        }
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        TimeTools.TogglePrimary();
        ApplyModeVisuals();
    }

    private void OnSecondaryClick(object sender, RoutedEventArgs e)
    {
        TimeTools.ResetCurrent();
        ApplyModeVisuals();
    }

    private void OnLapClick(object sender, RoutedEventArgs e) => TimeTools.Lap();

    // ---------------- 时间工具：纯视觉 ----------------

    /// <summary>按当前模式换配色：分段指示条 / 进度环 / 主按钮 / 读数（响铃时转成红色）。</summary>
    private void ApplyModeVisuals()
    {
        var (softKey, strongKey, maxKey, textKey) = TimeTools.Mode switch
        {
            TimeToolMode.Countdown => ("WarningTintSoftBrush", "WarningTintStrongBrush", "WarningTintMaxBrush", "WarningTextBrush"),
            TimeToolMode.Alarm => ("SecondaryAccentTintSoftBrush", "SecondaryAccentTintStrongBrush", "SecondaryAccentTintMaxBrush", "SecondaryAccentTextBrush"),
            _ => ("AccentTintSoftBrush", "AccentTintStrongBrush", "AccentTintMaxBrush", "AccentTextBrush"),
        };

        var soft = Res(softKey);
        var strong = Res(strongKey);
        var max = Res(maxKey);
        var text = Res(textKey);

        SegmentIndicator.Background = soft;
        RingArc.Stroke = TimeTools.IsRinging ? Res("CriticalTextBrush") : text;

        PrimaryButton.Background = strong;
        PrimaryButton.BorderBrush = max;
        PrimaryButton.Foreground = text;

        // 分段文字：选中的用模式色，其余用次要灰。
        SegmentStopwatch.Foreground = TimeTools.IsStopwatch ? text : Res("TextSecondaryBrush");
        SegmentCountdown.Foreground = TimeTools.IsCountdown ? text : Res("TextSecondaryBrush");
        SegmentAlarm.Foreground = TimeTools.IsAlarm ? text : Res("TextSecondaryBrush");

        // 倒计时的预设胶囊：选中的那个高亮成模式色。
        ApplyChipVisual(PresetChip0, TimeTools.IsCountdown && TimeTools.PresetIndex == 0, soft, strong, text);
        ApplyChipVisual(PresetChip1, TimeTools.IsCountdown && TimeTools.PresetIndex == 1, soft, strong, text);
        ApplyChipVisual(PresetChip2, TimeTools.IsCountdown && TimeTools.PresetIndex == 2, soft, strong, text);
        ApplyChipVisual(PresetChip3, TimeTools.IsCountdown && TimeTools.PresetIndex == 3, soft, strong, text);

        // 闹钟加减：跟模式同色（不高亮某一个）。
        foreach (var chip in new[] { AlarmStep0, AlarmStep1, AlarmStep2, AlarmStep3 })
        {
            ApplyChipVisual(chip, false, soft, strong, text);
        }

        // 读数：响铃时红色（"时间到"要有存在感）。
        TimeReadout.Foreground = TimeTools.IsRinging ? Res("CriticalTextBrush") : Res("TextPrimaryBrush");
        TimeCaption.Foreground = TimeTools.IsRinging ? Res("CriticalTextBrush") : Res("TextTertiaryBrush");
    }

    private static void ApplyChipVisual(Button chip, bool selected, Brush? soft, Brush? strong, Brush? text)
    {
        chip.Background = selected ? soft : Res("CardBackgroundBrush");
        chip.BorderBrush = selected ? strong : Res("CardBorderBrush");
        chip.Foreground = selected ? text : Res("TextSecondaryBrush");
    }

    /// <summary>把分段控件里那条指示条移到当前模式那一格；<paramref name="animate"/> 时走一段 Fast 的位移动画。</summary>
    /// <param name="animate">是否播位移动画。</param>
    /// <param name="modeIndex">要移动到的段位（默认 = 当前模式）；切模式时先传目标段位，指示条才能立刻给反馈。</param>
    private void UpdateSegmentIndicator(bool animate, int? modeIndex = null)
    {
        var width = SegmentsRoot.ActualWidth;

        if (width <= 0)
        {
            return;
        }

        _segmentWidth = width / 3.0;

        // 宽度留 4px 的缝，位置 = 格宽 × 序号（左右各 2px 由 Margin 给出）。
        SegmentIndicator.Width = Math.Max(1, _segmentWidth - 4);
        var target = (float)(_segmentWidth * (modeIndex ?? TimeTools.ModeIndex));

        if (!animate)
        {
            SegmentIndicator.Translation = new Vector3(target, 0f, 0f);
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(SegmentIndicator);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(
            new Vector2(MotionTokens.EasingControlPoint1X, MotionTokens.EasingControlPoint1Y),
            new Vector2(MotionTokens.EasingControlPoint2X, MotionTokens.EasingControlPoint2Y));

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(1f, new Vector3(target, 0f, 0f), ease);
        slide.Duration = MotionTokens.Fast;

        visual.StartAnimation("Translation", slide);
    }

    private void OnSegmentsSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSegmentIndicator(false);

    /// <summary>按进度重画半径 96 的进度弧：从 12 点方向顺时针扫 <c>progress × 360°</c>。</summary>
    private void UpdateRing(double progress, bool force = false)
    {
        var value = Math.Clamp(progress, 0.0, 1.0);

        if (!force && Math.Abs(value - _ringProgress) < 0.01)
        {
            return;
        }

        _ringProgress = value;

        var sweep = value * 360.0;

        if (sweep < 0.5)
        {
            RingArc.Data = null;
            return;
        }

        // 360° 整圆用弧线段画会退化，最多画到 359.9°。
        sweep = Math.Min(sweep, 359.9);

        var radius = (RingSize - RingThickness) / 2.0;
        var center = RingSize / 2.0;

        var figure = new PathFigure
        {
            StartPoint = PointOnCircle(center, radius, -90.0),
            IsClosed = false,
            IsFilled = false,
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = PointOnCircle(center, radius, -90.0 + sweep),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweep > 180.0,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        RingArc.Data = geometry;
    }

    private static Windows.Foundation.Point PointOnCircle(double center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Windows.Foundation.Point(
            center + (radius * Math.Cos(radians)),
            center + (radius * Math.Sin(radians)));
    }

    // ---------------- 视图切换 ----------------

    /// <summary>某个视图要多高的看板（DIP）。</summary>
    private double HeightFor(DashboardView view) => view switch
    {
        DashboardView.Tools => _toolsHeight,
        DashboardView.Settings => _settingsHeight,
        DashboardView.Alert => _alertHeight,
        DashboardView.Drop => _dropHeight,
        DashboardView.Music => _musicHeight,
        _ => _metricsHeight,
    };

    /// <summary>某个视图对应哪块面板。</summary>
    private FrameworkElement PanelFor(DashboardView view) => view switch
    {
        DashboardView.Tools => ToolsPanel,
        DashboardView.Settings => SettingsPanel,
        DashboardView.Alert => AlertPanel,
        DashboardView.Drop => DropPanel,
        DashboardView.Music => MusicPanel,
        _ => MetricsPanel,
    };

    private static string NameFor(DashboardView view) => view switch
    {
        DashboardView.Tools => "time tools",
        DashboardView.Settings => "settings",
        DashboardView.Alert => "alert",
        DashboardView.Drop => "drop",
        DashboardView.Music => "music",
        _ => "metrics",
    };

    /// <summary>「时间工具」：push 到工具视图，并让看板长高到 430。</summary>
    private void OnTimeToolsClick(object sender, RoutedEventArgs e)
    {
        Diagnostics.Log("click: time tools");
        SwitchTo(DashboardView.Tools);
    }

    /// <summary>左上角「返回」：pop 回指标视图。</summary>
    private void OnTimeToolsBackClick(object sender, RoutedEventArgs e) => SwitchTo(DashboardView.Metrics);

    /// <summary>⚙「设置」：push 到设置视图（铃声板块）。</summary>
    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        Diagnostics.Log("click: settings");
        SwitchTo(DashboardView.Settings);
    }

    /// <summary>设置页「返回」：回指标视图。</summary>
    private void OnSettingsBackClick(object sender, RoutedEventArgs e) => SwitchTo(DashboardView.Metrics);

    /// <summary>
    /// iOS 风格的过场 + 高度变化：旧视图往左滑出、新视图从右滑进来（往回则相反），
    /// 同时把「这个视图要多高」发出去，看板按弹簧长高 / 缩矮。
    ///
    /// 旧视图得等出场动画放完才能收起来（否则看不到淡出），所以这里延迟收尾；
    /// 用序号保证连点时只有最后一次收尾生效，不会把刚滑进来的视图又收掉。
    /// </summary>
    private async void SwitchTo(DashboardView target, bool animate = true, bool animateHeight = true)
    {
        if (_view == target)
        {
            return;
        }

        var from = _view;
        _view = target;

        UpdateMusicTimer();

        // 页脚（设置 / 关闭程序）只在常规视图里显示：解压岛自己有「打开文件夹 / 完成」，
        // 音乐视图底部是播放控制按钮，这两处都不需要页脚，留着反而挡住内容。
        FooterLayer.Visibility = target is DashboardView.Drop or DashboardView.Music
            ? Visibility.Collapsed
            : Visibility.Visible;

        // 横线只属于「指标 / 时间工具」两页的布局；设置、到点提醒、解压岛里不要它（会横穿内容）。
        // 注意：这两行的可见性必须在**瞬切（!animate）分支之前**设好 ——
        // 瞬切分支会提前 return，放在后面就会出现"解压岛上横着一条线"。
        DividerLine.Visibility = target is DashboardView.Metrics or DashboardView.Tools
            ? Visibility.Visible
            : Visibility.Collapsed;

        var token = ++_viewSwitchToken;
        var incoming = PanelFor(target);
        var outgoing = PanelFor(from);

        // 新视图先显示；旧视图暂时留着，两个视图才能在过场期间交叉滑动。
        incoming.Visibility = Visibility.Visible;

        if (!animate)
        {
            SnapToRest(incoming);
            SnapToRest(outgoing);
            outgoing.Visibility = Visibility.Collapsed;
            PreferredPanelHeightChanged?.Invoke(HeightFor(target), animateHeight);
            Diagnostics.Log($"dashboard view: {NameFor(target)} (panel {HeightFor(target):0} dip, no animation)");
            return;
        }

        // 越往后的视图 = 更"深"的一层：前进从右边进来，返回从左边进来。
        var direction = (int)target > (int)from ? 1f : -1f;

        PlayEnter(incoming, direction);
        PlayExit(outgoing, direction);

        PreferredPanelHeightChanged?.Invoke(HeightFor(target), true);

        Diagnostics.Log($"dashboard view: {NameFor(target)} (panel {HeightFor(target):0} dip)");

        await Task.Delay(ViewExitDuration);

        if (token != _viewSwitchToken)
        {
            return;   // 期间又切了一次，交回给后来那次收尾
        }

        outgoing.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 把视图复位回「指标」，**不播过场**：每次看板从收起态展开之前由 <see cref="IslandShell"/> 调用，
    /// 这样不管上次停在哪、鼠标是怎么移开的，下一次展开看到的总是指标视图。
    /// </summary>
    public void ResetToMetricsView()
    {
        if (_view == DashboardView.Metrics)
        {
            // 视图没变，但高度可能停在 430 / 300（上次在别的视图收起的），让外壳落回 350。
            PreferredPanelHeightChanged?.Invoke(_metricsHeight, false);
            return;
        }

        _view = DashboardView.Metrics;

        // 让还在等收尾的那次过场失效，免得它 200ms 后把刚复位好的指标视图又收起来。
        _viewSwitchToken++;

        MetricsPanel.Visibility = Visibility.Visible;
        ToolsPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        AlertPanel.Visibility = Visibility.Collapsed;
        DropPanel.Visibility = Visibility.Collapsed;
        MusicPanel.Visibility = Visibility.Collapsed;
        DividerLine.Visibility = Visibility.Visible;
        FooterLayer.Visibility = Visibility.Visible;

        // 复位不是过场：把五个视图都推回静止态，免得有半截动画（位移 / 透明度）留在上面。
        SnapToRest(MetricsPanel);
        SnapToRest(ToolsPanel);
        SnapToRest(SettingsPanel);
        SnapToRest(AlertPanel);
        SnapToRest(DropPanel);
        SnapToRest(MusicPanel);

        UpdateMusicTimer();

        PreferredPanelHeightChanged?.Invoke(_metricsHeight, false);

        Diagnostics.Log("dashboard view: metrics (reset)");
    }

    /// <summary>切到音乐视图（调试钩子用；正常入口是指标页的「正在播放」按钮）。</summary>
    public void ShowMusicView(bool animate = true) => SwitchTo(DashboardView.Music, animate);

    // ---------------- 音乐（系统媒体会话） ----------------

    /// <summary>
    /// 把系统媒体会话服务接进看板（由 <see cref="IslandShell"/> 初始化时调用）。
    /// 服务自己会把事件编组到 UI 线程，所以这里可以放心改控件。
    /// </summary>
    public void AttachMedia(MediaMonitor media)
    {
        _media = media;
        media.Changed += OnMediaChanged;
        media.ArtworkChanged += OnMediaArtworkChanged;

        _musicTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _musicTimer.Tick += OnMusicTick;

        // 先按当前状态画一遍：服务可能比看板先初始化好（启动时已经在放歌）。
        OnMediaChanged(media.Current);
    }

    private void OnMediaChanged(MediaSnapshot snapshot)
    {
        _mediaSnapshot = snapshot;

        MusicTitleText.Text = snapshot.IsAvailable ? snapshot.Title : "没有正在播放的媒体";

        MusicArtistText.Text = snapshot.IsAvailable
            ? (string.IsNullOrWhiteSpace(snapshot.Artist) ? snapshot.Album : snapshot.Artist)
            : "打开汽水音乐 / 其他播放器后会自动显示";

        MusicSourceText.Text = snapshot.IsAvailable ? snapshot.SourceApp : string.Empty;

        MusicStateText.Text = snapshot.IsAvailable
            ? (snapshot.IsPlaying ? "正在播放" : "已暂停")
            : "没有正在播放";

        if (snapshot.IsAvailable && snapshot.IsPlaying)
        {
            StartEqualizer();
        }
        else
        {
            StopEqualizer();
        }

        MusicPlayPauseButton.IsEnabled = snapshot.IsAvailable;
        MusicPreviousButton.IsEnabled = snapshot.IsAvailable && snapshot.CanSkipPrevious;
        MusicNextButton.IsEnabled = snapshot.IsAvailable && snapshot.CanSkipNext;

        MusicPlayIcon.Visibility = snapshot.IsPlaying ? Visibility.Collapsed : Visibility.Visible;
        MusicPauseIcon.Visibility = snapshot.IsPlaying ? Visibility.Visible : Visibility.Collapsed;

        // 指标页的「正在播放」入口：没有媒体就整颗按钮收起来。
        MetricsMusicButton.Visibility = snapshot.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
        MetricsMusicText.Text = snapshot.IsAvailable ? snapshot.Title : "正在播放";

        UpdateMusicProgress();
        UpdateMusicTimer();

        Diagnostics.Log(
            $"media: ui available={snapshot.IsAvailable} playing={snapshot.IsPlaying} " +
            $"title=\"{snapshot.Title}\" artist=\"{snapshot.Artist}\" source=\"{snapshot.SourceApp}\"");
    }

    private void OnMediaArtworkChanged(byte[]? artwork)
    {
        if (artwork is null || artwork.Length == 0)
        {
            MusicCoverImage.Source = null;
            MusicCoverImage.Visibility = Visibility.Collapsed;
            MusicCoverPlaceholder.Visibility = Visibility.Visible;
            MusicBackdropImage.Source = null;
            MusicBackdropLayer.Visibility = Visibility.Collapsed;
            return;
        }

        _ = ShowCoverAsync(artwork);
    }

    /// <summary>封面字节 → <see cref="BitmapImage"/>（只能在 UI 线程做）。</summary>
    private async Task ShowCoverAsync(byte[] artwork)
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

            MusicCoverImage.Source = bitmap;
            MusicCoverImage.Visibility = Visibility.Visible;
            MusicCoverPlaceholder.Visibility = Visibility.Collapsed;

            // 同一张封面铺满面板做背景光晕（低透明度 + 黑色渐变遮罩，看起来像毛玻璃）。
            MusicBackdropImage.Source = bitmap;
            MusicBackdropLayer.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"media: 封面显示失败：{ex.Message}");
        }
    }

    private void OnMusicTick(object? sender, object e) => UpdateMusicProgress();

    /// <summary>只在「音乐视图可见且有媒体」时跑 250ms 计时器，其它时候停着不耗电。</summary>
    private void UpdateMusicTimer()
    {
        if (_musicTimer is null)
        {
            return;
        }

        var shouldRun = _view == DashboardView.Music && _mediaSnapshot.IsAvailable;

        if (shouldRun && !_musicTimer.IsEnabled)
        {
            _musicTimer.Start();
        }
        else if (!shouldRun && _musicTimer.IsEnabled)
        {
            _musicTimer.Stop();
        }
    }

    private void UpdateMusicProgress()
    {
        var position = _media?.EstimatePosition() ?? _mediaSnapshot.Position;
        var duration = _mediaSnapshot.Duration;

        MusicProgress.Value = duration > TimeSpan.Zero
            ? Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1)
            : 0;

        MusicPositionText.Text = _mediaSnapshot.IsAvailable ? FormatMediaTime(position) : "--:--";
        MusicDurationText.Text = duration > TimeSpan.Zero ? FormatMediaTime(duration) : "--:--";
    }

    private static string FormatMediaTime(TimeSpan value)
        => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    // ---------------- 均衡器动画（播放时跳、暂停时停） ----------------

    /// <summary>均衡器是否正在跑（避免每帧重复 StartAnimation）。</summary>
    private bool _equalizerRunning;

    private void StartEqualizer()
    {
        if (_equalizerRunning)
        {
            return;
        }

        _equalizerRunning = true;
        MusicEqualizer.Visibility = Visibility.Visible;

        var bars = new[] { MusicEq1, MusicEq2, MusicEq3, MusicEq4 };

        for (var i = 0; i < bars.Length; i++)
        {
            var bar = bars[i];
            var visual = ElementCompositionPreview.GetElementVisual(bar);

            // 以底边中点为缩放锚点，柱子只往上长。
            visual.CenterPoint = new Vector3((float)bar.Width / 2f, (float)bar.Height, 0f);

            var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(0f, 0.25f);
            animation.InsertKeyFrame(0.5f, 1f);
            animation.InsertKeyFrame(1f, 0.45f);
            animation.Duration = TimeSpan.FromMilliseconds(560 + (i * 130));
            animation.IterationBehavior = AnimationIterationBehavior.Forever;

            visual.StartAnimation("Scale.Y", animation);
        }
    }

    private void StopEqualizer()
    {
        if (!_equalizerRunning)
        {
            MusicEqualizer.Visibility = Visibility.Collapsed;
            return;
        }

        _equalizerRunning = false;

        foreach (var bar in new[] { MusicEq1, MusicEq2, MusicEq3, MusicEq4 })
        {
            var visual = ElementCompositionPreview.GetElementVisual(bar);
            visual.StopAnimation("Scale.Y");
            visual.Scale = Vector3.One;
        }

        MusicEqualizer.Visibility = Visibility.Collapsed;
    }

    private void OnMetricsMusicClick(object sender, RoutedEventArgs e)
    {
        Diagnostics.Log("click: music");
        SwitchTo(DashboardView.Music);
    }

    private void OnMusicBackClick(object sender, RoutedEventArgs e) => SwitchTo(DashboardView.Metrics);

    private async void OnMusicPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_media is null)
        {
            return;
        }

        var ok = await _media.TogglePlayPauseAsync();
        Diagnostics.Log($"click: music play/pause ok={ok}");
        UpdateMusicProgress();
    }

    private async void OnMusicPreviousClick(object sender, RoutedEventArgs e)
    {
        if (_media is null)
        {
            return;
        }

        var ok = await _media.SkipPreviousAsync();
        Diagnostics.Log($"click: music previous ok={ok}");
    }

    private async void OnMusicNextClick(object sender, RoutedEventArgs e)
    {
        if (_media is null)
        {
            return;
        }

        var ok = await _media.SkipNextAsync();
        Diagnostics.Log($"click: music next ok={ok}");
    }

    // ---------------- 设置：铃声 ----------------

    private void OnAlarmToneClick(object sender, RoutedEventArgs e) => SelectRingtone(RingtoneKind.Alarm, sender);

    private void OnCountdownToneClick(object sender, RoutedEventArgs e) => SelectRingtone(RingtoneKind.Countdown, sender);

    /// <summary>点某一行里的某个音效：存盘 + 立刻试听 + 刷新高亮。</summary>
    private void SelectRingtone(RingtoneKind kind, object sender)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !int.TryParse(tag, out var index))
        {
            return;
        }

        AppSettings.Current.SetRingtone(kind, index);

        // 选了内置音效 = 这一类的自定义文件取消。
        AppSettings.Current.SetCustomFile(kind, null);
        AppSettings.Current.Save();

        RingtoneService.Shared.Preview(kind, index);
        ApplyRingtoneChips();

        Diagnostics.Log($"settings: {kind} ringtone -> #{index} ({RingtoneLibrary.For(kind)[index].Name})");
    }

    private void OnAlarmCustomClick(object sender, RoutedEventArgs e) => PickCustomRingtoneAsync(RingtoneKind.Alarm);

    private void OnCountdownCustomClick(object sender, RoutedEventArgs e) => PickCustomRingtoneAsync(RingtoneKind.Countdown);

    /// <summary>
    /// 选一个本地音频文件当铃声：拷进本地铃声目录 → 记进设置 → 立刻试听。
    /// 未打包应用用 FileOpenPicker 必须把窗口句柄喂给 picker（InitializeWithWindow）。
    /// </summary>
    private async void PickCustomRingtoneAsync(RingtoneKind kind)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary,
            };

            picker.FileTypeFilter.Add(".wav");
            picker.FileTypeFilter.Add(".mp3");
            picker.FileTypeFilter.Add(".m4a");
            picker.FileTypeFilter.Add(".aac");
            picker.FileTypeFilter.Add(".wma");
            picker.FileTypeFilter.Add(".flac");
            picker.FileTypeFilter.Add(".ogg");

            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                Diagnostics.Log($"settings: custom picker cancelled ({kind})");
                return;
            }

            var stored = RingtoneService.ImportCustomFile(kind, file.Path);

            AppSettings.Current.SetCustomFile(kind, stored);
            AppSettings.Current.Save();

            RingtoneService.Shared.PreviewCustom(kind, stored);
            ApplyRingtoneChips();

            Diagnostics.Log($"settings: {kind} ringtone -> custom \"{Path.GetFileName(stored)}\"");
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"settings: custom picker failed: {ex.Message}");
        }
    }

    /// <summary>把两行铃声按钮的高亮刷成当前选择（闹钟紫、倒计时橙）。</summary>
    private void ApplyRingtoneChips()
    {
        var alarmCustom = AppSettings.Current.HasCustomFile(RingtoneKind.Alarm);
        var countdownCustom = AppSettings.Current.HasCustomFile(RingtoneKind.Countdown);

        ApplyRingtoneRow(
            alarmCustom ? -1 : AppSettings.Current.AlarmRingtone,
            "SecondaryAccentTintStrongBrush",
            "SecondaryAccentTintMaxBrush",
            "SecondaryAccentTextBrush",
            AlarmTone0, AlarmTone1, AlarmTone2, AlarmTone3, AlarmTone4);

        ApplyRingtoneRow(
            countdownCustom ? -1 : AppSettings.Current.CountdownRingtone,
            "WarningTintStrongBrush",
            "WarningTintMaxBrush",
            "WarningTextBrush",
            CountdownTone0, CountdownTone1, CountdownTone2, CountdownTone3, CountdownTone4);

        ApplyCustomButton(
            AlarmCustomButton,
            RingtoneKind.Alarm,
            "闹钟",
            alarmCustom,
            "SecondaryAccentTintStrongBrush",
            "SecondaryAccentTintMaxBrush",
            "SecondaryAccentTextBrush");

        ApplyCustomButton(
            CountdownCustomButton,
            RingtoneKind.Countdown,
            "倒计时",
            countdownCustom,
            "WarningTintStrongBrush",
            "WarningTintMaxBrush",
            "WarningTextBrush");
    }

    private static void ApplyCustomButton(Button button, RingtoneKind kind, string label, bool selected, string selectedBackground, string selectedBorder, string selectedForeground)
    {
        var file = AppSettings.Current.GetCustomFile(kind);
        var name = file is null ? null : Path.GetFileNameWithoutExtension(file);
        if (name is { Length: > 14 })
        {
            name = name[..14] + "…";
        }

        button.Content = name is null ? $"{label}：选择文件…" : $"{label}：{name}";
        button.Background = selected ? Res(selectedBackground) : Res("CardBackgroundBrush");
        button.BorderBrush = selected ? Res(selectedBorder) : Res("CardBorderBrush");
        button.Foreground = selected ? Res(selectedForeground) : Res("TextSecondaryBrush");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, button.Content as string);
    }

    private static void ApplyRingtoneRow(int selectedIndex, string selectedBackground, string selectedBorder, string selectedForeground, params Button[] buttons)
    {
        var background = Res(selectedBackground);
        var border = Res(selectedBorder);
        var foreground = Res(selectedForeground);

        for (var i = 0; i < buttons.Length; i++)
        {
            var selected = i == selectedIndex;
            buttons[i].Background = selected ? background : Res("CardBackgroundBrush");
            buttons[i].BorderBrush = selected ? border : Res("CardBorderBrush");
            buttons[i].Foreground = selected ? foreground : Res("TextSecondaryBrush");
        }
    }

    // ---------------- 到点提醒视图（倒计时 / 闹钟响了） ----------------

    /// <summary>
    /// 显示「到点提醒」视图：倒计时和闹钟共用同一块面板（第二级弹窗，直接挂在胶囊下面，
    /// 不再单开一个屏幕中央的窗口）。由 <see cref="IslandShell"/> 在响铃开始时调用。
    /// </summary>
    public void ShowAlert(TimeToolMode kind)
    {
        _alertKind = kind;

        var isAlarm = kind == TimeToolMode.Alarm;

        AlertBellIcon.Visibility = isAlarm ? Visibility.Visible : Visibility.Collapsed;
        AlertHourglassIcon.Visibility = isAlarm ? Visibility.Collapsed : Visibility.Visible;

        // 图标底框跟着类型换色：闹钟紫、倒计时橙。
        AlertIconBox.Background = Res(isAlarm ? "SecondaryAccentTintSoftBrush" : "WarningTintSoftBrush");
        AlertIconBox.BorderBrush = Res(isAlarm ? "SecondaryAccentTintStrongBrush" : "WarningTintStrongBrush");

        AlertTitle.Text = isAlarm ? "闹钟" : "倒计时";
        AlertTime.Text = isAlarm ? TimeTools.AlarmTimeText : "00:00";
        AlertCaption.Text = isAlarm ? "到时间了" : "时间到";
        AlertCaption.Foreground = Res(isAlarm ? "SecondaryAccentTextBrush" : "WarningTextBrush");

        // 稍后 5 分钟只有闹钟有；倒计时只有「停止」。
        AlertSnoozeButton.Visibility = isAlarm ? Visibility.Visible : Visibility.Collapsed;

        AlertStopButton.Background = Res(isAlarm ? "SecondaryAccentTintStrongBrush" : "WarningTintStrongBrush");
        AlertStopButton.BorderBrush = Res(isAlarm ? "SecondaryAccentTintMaxBrush" : "WarningTintMaxBrush");
        AlertStopButton.Foreground = Res(isAlarm ? "SecondaryAccentTextBrush" : "WarningTextBrush");

        SwitchTo(DashboardView.Alert);
    }

    /// <summary>收起提醒、回到指标视图（由外壳在响铃结束时调用）。</summary>
    public void HideAlert()
    {
        if (_view == DashboardView.Alert)
        {
            SwitchTo(DashboardView.Metrics);
        }
    }

    private void OnAlertStopClick(object sender, RoutedEventArgs e)
    {
        Diagnostics.Log($"alert: stop ({_alertKind})");
        TimeTools.DismissRinging();
    }

    private void OnAlertSnoozeClick(object sender, RoutedEventArgs e)
    {
        Diagnostics.Log("alert: snooze 5 min");
        TimeTools.SnoozeAlarm(TimeSpan.FromMinutes(5));
    }

    // ---------------- 拖拽解压 ----------------

    /// <summary>
    /// 通知外壳：拖拽 / 解压 / 结果显示期间**别把岛收起来**（true = 保持展开）。
    /// 拖拽期间和「解压中 / 结果」期间都靠它兜住，否则鼠标一移开岛就缩回去了。
    /// </summary>
    public event Action<bool>? KeepExpandedChanged;

    /// <summary>落点区显示中、但文件还没真的落下来。</summary>
    public bool IsDropPending => _dropPending;

    /// <summary>有文件被拖进岛：切到「虚线落点」视图（默认瞬切，不播视图过场）。</summary>
    /// <param name="animateHeight">岛已经展开时传 true：让看板高度用弹簧过渡到 230，避免生硬地一帧跳变。</param>
    public void ShowDropZone(bool animateHeight = false)
    {
        _dropPending = true;
        DropArchiveIcon.Visibility = Visibility.Visible;
        SetDropState("松开以解压", ArchiveExtractor.SupportedFormatsText, showProgress: false, showActions: false);

        // 关键：**不走过场**。以前这里是 SwitchTo(Drop)（push/pop 淡入淡出），
        // 拖进来的第一眼会先看到指标视图（旧视图淡出），然后再淡入落点区 ——
        // 看起来就是"先显示看板、再显示解压岛"。拖拽场景要的是立刻出现，所以直接瞬切。
        SwitchTo(DashboardView.Drop, animate: false, animateHeight: animateHeight);

        KeepExpandedChanged?.Invoke(true);
        Diagnostics.Log("drop: zone shown");
    }

    /// <summary>
    /// 拖出岛 / 用户取消：**直接让解压岛消失**（外壳把岛收起），不切回指标视图。
    /// 以前这里会 SwitchTo(Metrics)，于是"移出"的瞬间会先闪一下指标看板、再收起 —— 观感很差。
    /// 现在保留 Drop 视图不动，收起后下次展开时 ResetToMetricsView() 会把它复位成指标。
    /// </summary>
    public void CancelDrop()
    {
        _dropPending = false;
        Diagnostics.Log("drop: cancelled");
        KeepExpandedChanged?.Invoke(false);
    }

    /// <summary>
    /// 真正落盘：校验格式 → 选目标目录（默认 = 压缩包所在目录）→ 后台解压（带进度）→ 结果状态。
    /// 解压本体在 <see cref="ArchiveExtractor"/>，这里只负责状态流转。
    /// </summary>
    public async Task HandleDropAsync(IReadOnlyList<string> paths)
    {
        _dropPending = false;

        var archive = paths.FirstOrDefault(ArchiveExtractor.IsSupported);

        if (archive is null)
        {
            KeepExpandedChanged?.Invoke(true);
            SetDropState("不支持这个格式", $"支持：{ArchiveExtractor.SupportedFormatsText}", showProgress: false, showActions: true, error: true);
            Diagnostics.Log("drop: unsupported file");
            return;
        }

        var archiveDirectory = Path.GetDirectoryName(archive);
        var destination = FolderPicker.PickFolder(
            App.MainWindowHandle,
            archiveDirectory,
            $"把「{Path.GetFileName(archive)}」解压到哪个文件夹？");

        if (destination is null)
        {
            CancelDrop();
            return;
        }

        KeepExpandedChanged?.Invoke(true);
        SetDropState("解压中…", $"{Path.GetFileName(archive)}  →  {destination}", showProgress: true, showActions: false);
        ApplyDropProgress(0);

        try
        {
            var progress = new Progress<double>(value => ApplyDropProgress(value));
            var result = await ArchiveExtractor.ExtractAsync(archive, destination, progress);

            _lastExtractFolder = result.Destination;

            var note = result.EscapedToSubfolder
                ? "（目标里已有同名文件，已自动放进同名子文件夹）"
                : string.Empty;

            SetDropState("解压完成", $"→ {result.Destination} · {result.FileCount} 个文件{note}", showProgress: false, showActions: true, canOpenFolder: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"drop: extract failed: {ex.Message}");
            SetDropState("解压失败", ex.Message, showProgress: false, showActions: true, error: true);
        }
    }

    /// <summary>
    /// 解压进度：0~1 = 确定进度；负值 = 内核给不出进度（tar.exe），切成滚动条。
    /// </summary>
    private void ApplyDropProgress(double value)
    {
        if (value < 0)
        {
            DropProgress.IsIndeterminate = true;
            return;
        }

        DropProgress.IsIndeterminate = false;
        DropProgress.Value = Math.Clamp(value, 0, 1);
    }

    private void SetDropState(string title, string subtitle, bool showProgress, bool showActions, bool canOpenFolder = false, bool error = false)
    {
        DropTitle.Text = title;
        DropTitle.Foreground = Res(error ? "CriticalTextBrush" : "TextPrimaryBrush");

        DropSubtitle.Text = subtitle;
        DropSubtitle.Foreground = Res(error ? "CriticalTextBrush" : "TextTertiaryBrush");

        DropProgress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        DropProgress.IsIndeterminate = false;
        DropActions.Visibility = showActions ? Visibility.Visible : Visibility.Collapsed;
        DropOpenFolderButton.Visibility = canOpenFolder ? Visibility.Visible : Visibility.Collapsed;
        DropCloseButton.Content = canOpenFolder ? "完成" : "关闭";
    }

    private void HideDrop()
    {
        if (_view == DashboardView.Drop)
        {
            // 同样瞬切回指标（接着就收起了，没必要播过场）。
            SwitchTo(DashboardView.Metrics, animate: false);
        }
    }

    private void OnDropOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (_lastExtractFolder is { } folder && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
    }

    private void OnDropCloseClick(object sender, RoutedEventArgs e)
    {
        _dropPending = false;
        _lastExtractFolder = null;
        KeepExpandedChanged?.Invoke(false);
        HideDrop();
    }

    /// <summary>
    /// 调试钩子：跳过文件夹选择器，直接把 <paramref name="archivePath"/> 解压到它所在目录，
    /// 用来在没有真实拖拽的情况下走一遍「落点区 → 进度 → 结果」三个状态。
    /// （真实流程是 <see cref="HandleDropAsync"/>，多了选目录这一步。）
    /// </summary>
    public async void RunDropTest(string archivePath)
    {
        var destination = Path.GetDirectoryName(archivePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        KeepExpandedChanged?.Invoke(true);
        ShowDropZone();
        SetDropState("解压中…", $"{Path.GetFileName(archivePath)}  →  {destination}", showProgress: true, showActions: false);
        ApplyDropProgress(0);

        try
        {
            var progress = new Progress<double>(value => ApplyDropProgress(value));
            var result = await ArchiveExtractor.ExtractAsync(archivePath, destination, progress);
            _lastExtractFolder = result.Destination;
            SetDropState("解压完成", $"→ {result.Destination} · {result.FileCount} 个文件", showProgress: false, showActions: true, canOpenFolder: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"drop test: failed: {ex.Message}");
            SetDropState("解压失败", ex.Message, showProgress: false, showActions: true, error: true);
        }
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

/// <summary>The three dashboard views (visibility switch only; different heights).</summary>
internal enum DashboardView
{
    Metrics = 0,
    Tools = 1,
    Settings = 2,
    Alert = 3,
    Drop = 4,
    Music = 5,
}
