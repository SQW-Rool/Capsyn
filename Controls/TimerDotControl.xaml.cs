using Capsyn.Services;
using Capsyn.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Capsyn.Controls;

/// <summary>
/// 「第二个计时」的独立小圆岛（40 × 40）：一圈进度环 + 模式图标，点一下换到主位。
///
/// 它和胶囊是**两块独立的窗口区域**（外壳把它们一起并进 SetWindowRgn），
/// 所以视觉上是"两个岛"，不是一个岛里塞一个圆。数据来自共享的 <see cref="TimeToolsViewModel"/>。
/// </summary>
public sealed partial class TimerDotControl : UserControl
{
    private const double DotSize = 40.0;
    private const double RingThickness = 2.0;

    /// <summary>上次画到哪（进度变化小于 2% 不重画）。</summary>
    private double _drawnProgress = -1;

    public TimerDotControl()
    {
        Tools = TimeToolsViewModel.Shared;

        InitializeComponent();

        Tools.PropertyChanged += OnToolsPropertyChanged;

        Loaded += (_, _) => UpdateAll(force: true);
    }

    /// <summary>共享的时间工具状态。</summary>
    public TimeToolsViewModel Tools { get; }

    private void OnDotClick(object sender, RoutedEventArgs e)
    {
        Tools.SwapActivities();
        Diagnostics.Log("timer dot: swap primary/secondary activity");
    }

    private void OnToolsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => UpdateAll(force: false);

    /// <summary>把共享状态投影到小圆：图标、配色、（可选）重画进度环。</summary>
    private void UpdateAll(bool force)
    {
        var mode = Tools.SecondaryActivity;

        if (mode is not { } value)
        {
            return;
        }

        var isCountdown = value == TimeToolMode.Countdown;
        var ringing = Tools.MonitorSecondaryRinging;

        var textBrush = ringing
            ? Res("CriticalTextBrush")
            : isCountdown ? Res("WarningTextBrush") : Res("AccentTextBrush");

        StopwatchIcon.Visibility = isCountdown ? Visibility.Collapsed : Visibility.Visible;
        CountdownIcon.Visibility = isCountdown ? Visibility.Visible : Visibility.Collapsed;
        StopwatchIcon.Stroke = textBrush;
        CountdownIcon.Stroke = textBrush;
        Ring.Stroke = textBrush;

        var progress = Tools.MonitorSecondaryProgress;

        if (!force && Math.Abs(progress - _drawnProgress) < 0.02 && progress < 1.0)
        {
            return;
        }

        _drawnProgress = progress;
        Ring.Data = BuildArc(progress);

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            DotButton,
            (isCountdown ? "倒计时" : "正计时") + " " + Tools.MonitorSecondaryText);
    }

    /// <summary>12 点方向起、顺时针扫 <c>progress × 360°</c> 的圆弧（半径留出线宽的一半）。</summary>
    private static Geometry? BuildArc(double progress)
    {
        var value = Math.Clamp(progress, 0.0, 1.0);

        if (value <= 0.01)
        {
            return null;
        }

        var sweep = Math.Min(value * 360.0, 359.9);
        var radius = (DotSize - RingThickness) / 2.0;
        var center = DotSize / 2.0;

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
        return geometry;
    }

    private static Point PointOnCircle(double center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(
            center + (radius * Math.Cos(radians)),
            center + (radius * Math.Sin(radians)));
    }

    private static Brush? Res(string key)
        => Application.Current?.Resources is { } resources
           && resources.TryGetValue(key, out var value)
           && value is Brush brush
            ? brush
            : null;
}
