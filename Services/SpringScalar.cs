namespace Capsyn.Services;

/// <summary>
/// 一个最小的阻尼弹簧积分器（半隐式欧拉，按固定子步长积分保证稳定）。
///
/// 为什么不用 Composition 的弹簧动画来驱动形状：
/// 岛的外轮廓是 Win32 窗口区域（SetWindowRgn），Composition 动画碰不到它，
/// 而 XAML 元素视觉的属性读取只返回基准值（拿不到动画中的当前值），
/// 所以外轮廓这一层只能自己算弹簧。参数含义与 Composition 保持一致：
///   DampingRatio：阻尼比，&lt;1 会回弹；Period：一个振动周期，越小越快。
/// 内容层（卡片淡入/放大/上浮）依然是 Composition 弹簧动画。
/// </summary>
internal sealed class SpringScalar
{
    /// <summary>单个积分子步长（秒）。必须远小于 Period，否则显式积分会发散。</summary>
    private const double MaxStepSeconds = 0.004;

    /// <summary>一次 Advance 最多推进的时间，避免长时间没 tick 后「炸开」。</summary>
    private const double MaxFrameSeconds = 0.2;

    private double _value;
    private double _velocity;
    private double _target = 1;
    private double _dampingRatio = 0.62;
    private double _period = 0.045;

    /// <summary>当前值。</summary>
    public double Value => _value;

    /// <summary>是否已经基本停稳。</summary>
    public bool IsSettled => Math.Abs(_value - _target) < 0.0015 && Math.Abs(_velocity) < 0.02;

    /// <summary>从当前姿态出发，向 <paramref name="target"/> 运动（保留速度，中途切换方向也不跳变）。</summary>
    public void SetTarget(double target, double dampingRatio, double periodSeconds)
    {
        _target = target;
        _dampingRatio = Math.Max(0.05, dampingRatio);
        _period = Math.Max(0.004, periodSeconds);
    }

    /// <summary>直接落到某个值并清零速度（初始化用）。</summary>
    public void Reset(double value)
    {
        _value = value;
        _velocity = 0;
        _target = value;
    }

    /// <summary>推进 <paramref name="deltaSeconds"/> 秒。</summary>
    public void Advance(double deltaSeconds)
    {
        var remaining = Math.Clamp(deltaSeconds, 0, MaxFrameSeconds);

        while (remaining > 0)
        {
            var step = Math.Min(MaxStepSeconds, remaining);
            Integrate(step);
            remaining -= step;
        }
    }

    private void Integrate(double step)
    {
        // ω = 2π / T；临界阻尼系数 c = 2ζω；刚度 k = ω²
        var omega = 2.0 * Math.PI / _period;
        var stiffness = omega * omega;
        var damping = 2.0 * _dampingRatio * omega;

        var acceleration = (stiffness * (_target - _value)) - (damping * _velocity);
        _velocity += acceleration * step;
        _value += _velocity * step;
    }
}
