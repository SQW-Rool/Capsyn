using Capsyn.Configuration;
using Capsyn.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Capsyn.Controls;

/// <summary>
/// 「时间岛」UI：只负责显示 + 驱动每秒刷新。
/// 时间本身是 <see cref="TimeIslandViewModel.CurrentTime"/>（可绑定），后续换模块只要换一个 UserControl。
/// </summary>
public sealed partial class TimeIslandControl : UserControl
{
    private readonly DispatcherTimer _timer;

    public TimeIslandControl()
    {
        // x:Bind 是 OneTime/OneWay 求值，ViewModel 必须在 InitializeComponent 之前准备好。
        ViewModel = new TimeIslandViewModel(IslandOptions.Default);

        InitializeComponent();

        // 每 1 秒刷新一次。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // TODO(扩展): 在这里订阅点击/悬停/拖拽手势（PointerPressed / Tapped），
        //             用于展开动画、点击交互、鼠标穿透开关等。
    }

    /// <summary>视图模型（XAML 里通过 x:Bind 绑定）。</summary>
    public TimeIslandViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 先立即取一次时间，避免启动后第一秒显示空白。
        ViewModel.Refresh();

        _timer.Start();

        // TODO(展开动画): 首次出现时的「从中间弹开」动画在这里启动。
        // TODO(通知队列): 这里可以开始消费待显示的通知。
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // 组件被卸载时停表，避免定时器泄漏。
        _timer.Stop();
    }

    private void OnTimerTick(object? sender, object e) => ViewModel.Refresh();
}
