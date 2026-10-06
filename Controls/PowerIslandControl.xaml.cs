using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Capsyn.Controls;

/// <summary>
/// 「电源岛」UI：关机 / 重启 / 睡眠 / 关闭程序。
///
/// 按需求，只有「关闭程序」真正生效（抛 <see cref="ExitRequested"/>），但它也不写诊断日志；
/// 关机 / 重启 / 睡眠只有 UI：和看板岛的「设置」按钮一样连 <c>Click</c> 都不接。
/// 也就是这四个按钮里没有任何一个会写日志。
/// 出入场动画由 <see cref="IslandShell"/> + <see cref="IslandAnimator"/> 负责。
/// </summary>
public sealed partial class PowerIslandControl : UserControl
{
    public PowerIslandControl()
    {
        InitializeComponent();
    }

    /// <summary>用户点了「关闭程序」。</summary>
    public event Action? ExitRequested;

    // 按需求这里只退出，不写诊断日志。
    private void OnExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
}
