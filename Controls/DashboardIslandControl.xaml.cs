using Capsyn.Configuration;
using Capsyn.Services;
using Capsyn.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Capsyn.Controls;

/// <summary>
/// 「看板岛」UI：CPU / 内存 / 网络上下行，每秒采样一次（采样在线程池上跑）。
/// 右下角的「关闭程序」按钮抛 <see cref="ExitRequested"/>，
/// 由 <see cref="IslandShell"/> 转给 MainWindow 真正退出。
/// </summary>
public sealed partial class DashboardIslandControl : UserControl
{
    private readonly DispatcherTimer _timer;
    private bool _sampling;

    public DashboardIslandControl()
    {
        // x:Bind 在 InitializeComponent 时求值，ViewModel 必须先准备好。
        ViewModel = new DashboardViewModel();

        InitializeComponent();

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

    /// <summary>用户点了右下角的「关闭程序」。</summary>
    public event Action? ExitRequested;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 先立即采一次，看板一出现就有数据。
        await SampleAsync();
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    private async void OnTimerTick(object? sender, object e) => await SampleAsync();

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
