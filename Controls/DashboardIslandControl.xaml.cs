using Capsyn.Configuration;
using Capsyn.Helpers;
using Capsyn.Services;
using Capsyn.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Capsyn.Controls;

/// <summary>
/// 「看板岛」UI：CPU / 内存 / 网络上下行，每秒采样一次（采样在线程池上跑）。
/// 电源按钮是电源岛的悬停触发器：鼠标移到它上面时 <see cref="IslandShell"/> 会在下方展开电源岛。
/// </summary>
public sealed partial class DashboardIslandControl : UserControl
{
    /// <summary>电源岛展开期间按钮的底色（比常态亮一点，给个状态反馈）。</summary>
    private static readonly Windows.UI.Color ActiveColor = ColorHelper.Parse("#2EFFFFFF", Microsoft.UI.Colors.Transparent);

    /// <summary>按钮常态底色。</summary>
    private static readonly Windows.UI.Color IdleColor = ColorHelper.Parse("#1AFFFFFF", Microsoft.UI.Colors.Transparent);

    private readonly DispatcherTimer _timer;
    private readonly SolidColorBrush _powerButtonBrush = new(IdleColor);
    private bool _sampling;

    public DashboardIslandControl()
    {
        // x:Bind 在 InitializeComponent 时求值，ViewModel 必须先准备好。
        ViewModel = new DashboardViewModel();

        InitializeComponent();

        PowerButton.Background = _powerButtonBrush;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(IslandOptions.Default.MetricsIntervalSeconds),
        };
        _timer.Tick += OnTimerTick;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>视图模型（XAML 里通过 x:Bind 绑定）。</summary>
    public DashboardViewModel ViewModel { get; }

    /// <summary>
    /// 电源按钮在窗口客户区里的矩形（DIP）。
    /// IslandShell 用光标位置和它比较来判断「鼠标是否停在电源按钮上」。
    /// </summary>
    public Rect GetPowerButtonBounds()
    {
        var width = PowerButton.ActualWidth;
        var height = PowerButton.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return default;
        }

        return PowerButton.TransformToVisual(null).TransformBounds(new Rect(0, 0, width, height));
    }

    /// <summary>电源岛是否展开：展开时把按钮画亮一点。</summary>
    public void SetPowerButtonActive(bool active)
        => _powerButtonBrush.Color = active ? ActiveColor : IdleColor;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 先立即采一次，看板一出现就有数据。
        await SampleAsync();
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    private async void OnTimerTick(object? sender, object e) => await SampleAsync();

    /// <summary>
    /// 电源按钮本身只做 UI（按需求）：它真正的用途是「鼠标悬停在上面 → 展开电源岛」，
    /// 点击不执行任何动作，只写一条诊断日志。
    /// </summary>
    private void OnPowerClick(object sender, RoutedEventArgs e)
        => Diagnostics.Log("power button clicked (hover trigger for power island, no action wired)");

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
