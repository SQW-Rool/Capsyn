namespace Capsyn.Configuration;

/// <summary>
/// 「岛」的外观、动画与数据配置。
///
/// 布局约定（都是 DIP）：
///   窗口 = 616 × 438 的一块画布（尺寸由看板决定，含弹簧回弹余量）
///   胶囊（时间岛）固定在窗口顶部居中，看板（看板岛）在胶囊下方，
///   两者之间留 <see cref="ExpandedGap"/> 的缝隙，看板从这条缝向下生长。
///
/// TODO(扩展): 从 exe 同目录的 island.config.json 读取并覆盖下面的默认值。
/// </summary>
public sealed class IslandOptions
{
    /// <summary>全局默认配置。</summary>
    public static IslandOptions Default { get; } = new();

    // ---------------- 收起态：时间岛（胶囊） ----------------

    /// <summary>胶囊宽度（DIP）。</summary>
    public double Width { get; init; } = 320;

    /// <summary>胶囊高度（DIP）。</summary>
    public double Height { get; init; } = 40;

    /// <summary>距主屏顶边的距离（DIP）。</summary>
    public double TopOffset { get; init; } = 10;

    /// <summary>胶囊圆角半径：高度的一半。</summary>
    public double CornerRadius => Height / 2.0;

    // ---------------- 展开态：看板岛 ----------------

    /// <summary>看板宽度（DIP）。</summary>
    public double ExpandedWidth { get; init; } = 550;

    /// <summary>看板高度（DIP）。</summary>
    public double ExpandedHeight { get; init; } = 350;

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

    /// <summary>窗口宽度：取胶囊、看板（含回弹余量）里最宽的那个。</summary>
    public double WindowWidth => Math.Max(Width, ExpandedWidth * BounceMargin);

    /// <summary>窗口高度：胶囊 + 缝隙 + 看板（含回弹余量）。</summary>
    public double WindowHeight
        => Height
           + ExpandedGap
           + (ExpandedHeight * BounceMargin);

    /// <summary>胶囊在窗口内的左边距（水平居中）。</summary>
    public double PillLeft => (WindowWidth - Width) / 2.0;

    /// <summary>看板在窗口内的上边距（正好在胶囊下方，隔一条缝隙）。</summary>
    public double PanelTop => Height + ExpandedGap;

    /// <summary>看板生长动画的起始横向缩放（= 胶囊宽 / 看板宽）。</summary>
    public double CollapsedScaleX => Width / ExpandedWidth;

    /// <summary>看板生长动画的起始纵向缩放：从 0 高度开始向下长。</summary>
    public double CollapsedScaleY => 0.0;

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
