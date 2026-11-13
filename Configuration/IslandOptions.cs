namespace Capsyn.Configuration;

/// <summary>
/// 「岛」的外观、动画与数据配置。
///
/// 布局约定（都是 DIP）：
///   窗口 = 616 × 528 的一块画布（尺寸按「最高/最宽的那个看板」决定，含弹簧回弹余量）
///   胶囊（时间岛）固定在窗口顶部居中，看板（看板岛）在胶囊下方，
///   两者之间留 <see cref="ExpandedGap"/> 的缝隙，看板从这条缝向下生长。
///   看板高度按视图变：指标 350 / 时间工具 430，切换时由弹簧长过去（窗口画布按最大的那个留余量）。
///
/// TODO(扩展): 从 exe 同目录的 island.config.json 读取并覆盖下面的默认值。
/// </summary>
public sealed class IslandOptions
{
    /// <summary>全局默认配置。</summary>
    public static IslandOptions Default { get; } = new();

    // ---------------- 收起态：时间岛（胶囊） ----------------

    /// <summary>胶囊宽度（DIP）：没有计时占用时显示时钟的那个宽度。</summary>
    public double Width { get; init; } = 320;

    /// <summary>
    /// 胶囊宽度（DIP）：被一个计时占用时（**监测区**，时钟消失、只显示计时读数）。
    /// 比时钟窄一截，看起来更像 iOS 的 compact 状态。
    /// </summary>
    public double MonitoringWidth { get; init; } = 172;

    /// <summary>
    /// 「第二个计时」那颗**独立小圆岛**的直径（DIP）。
    /// 两个计时同时跑时，主位留在胶囊里，另一个变成它旁边单独的一个圆（不是塞进胶囊里）。
    /// </summary>
    public double DotSize { get; init; } = 40;

    /// <summary>小圆岛和胶囊之间的缝隙（DIP）。</summary>
    public double DotGap { get; init; } = 10;

    /// <summary>胶囊高度（DIP）。</summary>
    public double Height { get; init; } = 40;

    /// <summary>距主屏顶边的距离（DIP）。</summary>
    public double TopOffset { get; init; } = 10;

    /// <summary>胶囊圆角半径：高度的一半。</summary>
    public double CornerRadius => Height / 2.0;

    // ---------------- 展开态：看板岛 ----------------

    /// <summary>看板宽度（DIP）。</summary>
    public double ExpandedWidth { get; init; } = 550;

    /// <summary>看板高度（DIP）：指标视图。</summary>
    public double ExpandedHeight { get; init; } = 350;

    /// <summary>
    /// 看板高度（DIP）：时间工具视图。
    /// 比指标视图高 80，用来放「分段控件 + 环形读数 + 预设胶囊 + 主按钮」；
    /// 切换视图时窗口区域按弹簧从 <see cref="ExpandedHeight"/> 长到这个高度（不是跳变）。
    /// </summary>
    public double ExpandedToolsHeight { get; init; } = 430;

    /// <summary>看板高度（DIP）：设置视图（铃声板块）。</summary>
    public double ExpandedSettingsHeight { get; init; } = 360;

    /// <summary>看板高度（DIP）：到点提醒视图（倒计时 / 闹钟响了）。</summary>
    public double ExpandedAlertHeight { get; init; } = 180;

    /// <summary>看板高度（DIP）：拖拽解压视图（虚线落点 + 进度 + 结果）。</summary>
    public double ExpandedDropHeight { get; init; } = 230;

    /// <summary>看板高度（DIP）：音乐控制视图（封面 + 歌名/歌手 + 进度 + 播放控制）。</summary>
    public double ExpandedMusicHeight { get; init; } = 250;


    /// <summary>所有视图里最大的看板高度：窗口画布和回弹余量都按它算。</summary>
    public double MaxExpandedHeight => Math.Max(
        Math.Max(ExpandedHeight, ExpandedToolsHeight),
        Math.Max(ExpandedSettingsHeight, Math.Max(ExpandedAlertHeight, Math.Max(ExpandedDropHeight, ExpandedMusicHeight))));

    /// <summary>看板圆角半径（DIP）。</summary>
    public double ExpandedCornerRadius { get; init; } = 28;

    /// <summary>胶囊与看板之间的缝隙（DIP）。想看板紧贴胶囊就改成 0。</summary>
    public double ExpandedGap { get; init; } = 6;

    /// <summary>
    /// 回弹余量：窗口做得比看板略大，弹簧过冲时外轮廓才不会被窗口边界切掉。
    /// 阻尼比 0.65 的实际过冲约 7%，这里留 12% 的空间。
    /// </summary>
    public double BounceMargin { get; init; } = 1.12;

    // ---------------- 电源岛已按需求删除 ----------------
    // 原来挂在看板岛下方的那块 240 × 68 电源岛（关机 / 重启 / 睡眠 / 关闭程序）连同它的
    // 悬停触发器、弹簧、级联收回逻辑一起删掉了；「关闭程序」现在是看板岛右下角的一个图标按钮。

    // ---------------- 由上面派生出的窗口内部布局 ----------------

    /// <summary>窗口宽度：取最大的胶囊宽度、看板（含回弹余量）里最宽的那个。</summary>
    public double WindowWidth => Math.Max(MaxPillWidth, ExpandedWidth * BounceMargin);

    /// <summary>胶囊可能出现的最大宽度（时钟 320 / 监测 172）。</summary>
    public double MaxPillWidth => Math.Max(Width, MonitoringWidth);

    /// <summary>窗口高度：胶囊 + 缝隙 + 最高的那个看板（含回弹余量）。</summary>
    public double WindowHeight
        => Height
           + ExpandedGap
           + (MaxExpandedHeight * BounceMargin);

    /// <summary>某个宽度的胶囊在窗口内的左边距（始终水平居中）。</summary>
    public double PillLeftFor(double pillWidth) => (WindowWidth - pillWidth) / 2.0;

    /// <summary>
    /// 有第二个计时时，胶囊要让出来的一半宽度 —— 小圆岛挂在胶囊**左侧**，
    /// 整组（圆 + 缝 + 胶囊）仍然居中，所以胶囊往右挪 <c>(DotSize + DotGap) / 2</c>。
    /// </summary>
    public double PillShiftWithDot => (DotSize + DotGap) / 2.0;

    /// <summary>看板在窗口内的上边距（正好在胶囊下方，隔一条缝隙）。</summary>
    public double PanelTop => Height + ExpandedGap;

    // ---------------- 时间 ----------------

    public string TimeFormat { get; init; } = "HH:mm:ss";
    public double FontSize { get; init; } = 17;

    /// <summary>时间字体，等宽。</summary>
    public string FontFamily { get; init; } = "Cascadia Code";

    /// <summary>岛体底色。</summary>
    public string PillColor { get; init; } = "#000000";

    /// <summary>主要文字颜色。</summary>
    public string ForegroundColor { get; init; } = "#FFFFFF";

    // ---------------- 动画（弹簧 + 内容淡入淡出） ----------------

    /// <summary>展开弹簧阻尼比（越小弹得越明显）。</summary>
    public float SpringDampingRatio { get; init; } = 0.62f;

    /// <summary>展开弹簧周期：越小越快。420ms 大约 300ms 长完，末尾有一点点回弹。</summary>
    public TimeSpan SpringPeriod { get; init; } = MotionTokens.SpringPeriodExpand;

    /// <summary>收起弹簧阻尼比（收敛快、不弹）。</summary>
    public float CollapseSpringDampingRatio { get; init; } = 0.9f;

    public TimeSpan CollapseSpringPeriod { get; init; } = MotionTokens.SpringPeriodCollapse;

    /// <summary>看板内容淡入时长。</summary>
    public TimeSpan ContentFadeInDuration { get; init; } = MotionTokens.Normal;

    /// <summary>看板内容淡出时长。</summary>
    public TimeSpan ContentFadeOutDuration { get; init; } = MotionTokens.Fast;

    // 注：内容整体放大 + 卡片/按钮错帧「生长动画」已按需求删除，内容只做透明度过渡。
    // 需要恢复时可以从 git 历史里找回 IslandAnimator 的旧实现（删除前的最后一个版本是 tag v0.2.2；
    // 电源岛整块删除在 v0.2.3-Power-Modified）。

    // ---------------- 交互 ----------------

    /// <summary>鼠标移出后延迟多久收起（避免边缘抖动）。</summary>
    public int CollapseDelayMs { get; init; } = 160;

    /// <summary>光标位置轮询间隔（毫秒）。</summary>
    public int HoverPollMs { get; init; } = 80;

    // ---------------- 数据 ----------------

    /// <summary>CPU / 内存 / 网络的采样间隔（秒）。</summary>
    public double MetricsIntervalSeconds { get; init; } = 1.0;
}
