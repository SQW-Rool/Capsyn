namespace Capsyn.Configuration;

/// <summary>
/// 动效 token（C# 侧）—— 与 <c>Themes/Tokens.xaml</c> 里的时长 / 缓动 token <b>一一对应</b>：
/// XAML 用 <c>{ThemeResource DurationFastMs}</c> 之类，C# 用这里的常量，**两边数值必须同步改**。
///
/// 依据：[Motion in practice](https://learn.microsoft.com/en-us/windows/apps/develop/motion/motion-in-practice)
/// —— 统一三档，不再每个控件各写各的：
///   * <see cref="Fast"/>   150ms：轻量反馈（hover / press）、淡出、小范围位移；
///   * <see cref="Normal"/> 300ms：入场 / 展开 / 场景切换；
///   * <see cref="Slow"/>   500ms：大范围或需要强调的过渡。
/// 缓动统一以 EaseOut（减速）为主，见 Tokens.xaml 的 EasingDecelerate / EasingStandard。
///
/// 注意：**弹簧周期（Period）不是"时长三档"** —— 它决定弹性的物理手感，单独保留：
/// SpringPeriodExpand 420ms / SpringPeriodCollapse 220ms，与迁移前手感完全一致。
/// </summary>
internal static class MotionTokens
{
    /// <summary>1ms：把属性显式推到目标值的"瞬时"动画（SnapToRest 用，不是真正的过场）。</summary>
    public static readonly TimeSpan Instant = TimeSpan.FromMilliseconds(1);

    /// <summary>150ms —— 轻量反馈 / 淡出。</summary>
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(150);

    /// <summary>300ms —— 入场 / 展开 / 场景切换。</summary>
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(300);

    /// <summary>500ms —— 大范围或需要强调的过渡。</summary>
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(500);

    // ---------------- 统一缓动（Composition 侧用；XAML 侧对应 Tokens.xaml 的 EasingDecelerate） ----------------
    // 减速曲线（快起慢收），与 Fluent 的"入场/反馈"节奏一致：cubic-bezier(0.1, 0.9, 0.2, 1.0)

    public const float EasingControlPoint1X = 0.1f;
    public const float EasingControlPoint1Y = 0.9f;
    public const float EasingControlPoint2X = 0.2f;
    public const float EasingControlPoint2Y = 1.0f;

    // ---------------- 场景过场曲线（iOS 弹层那条 cubic-bezier(0.32, 0.72, 0, 1)） ----------------
    // 与 8px 网格 / 三档时长无关，是"过场手感"的专用曲线：起手快、收尾柔和。

    public const float TransitionControlPoint1X = 0.32f;
    public const float TransitionControlPoint1Y = 0.72f;
    public const float TransitionControlPoint2X = 0f;
    public const float TransitionControlPoint2Y = 1f;

    // ---------------- 弹簧物理参数（保留原手感，不归三档时长） ----------------

    /// <summary>展开弹簧周期。</summary>
    public static readonly TimeSpan SpringPeriodExpand = TimeSpan.FromMilliseconds(420);

    /// <summary>收起弹簧周期。</summary>
    public static readonly TimeSpan SpringPeriodCollapse = TimeSpan.FromMilliseconds(220);

    /// <summary>展开弹簧阻尼比（越小弹得越明显）。</summary>
    public const double SpringDampingExpand = 0.62;

    /// <summary>收起弹簧阻尼比（收敛快、不弹）。</summary>
    public const double SpringDampingCollapse = 0.9;
}
