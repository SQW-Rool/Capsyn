using Capsyn.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Capsyn.Controls;

/// <summary>
/// 「电源岛」UI：关机 / 重启 / 睡眠 / 关闭程序。
///
/// 按需求，只有「关闭程序」真正生效（抛 <see cref="ExitRequested"/>）；
/// 关机 / 重启 / 睡眠只做 UI，点击不执行任何系统操作，只写一条诊断日志。
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

    // 关机 / 重启 / 睡眠：按需求只做 UI，不接系统操作。
    private void OnShutdownClick(object sender, RoutedEventArgs e)
        => Diagnostics.Log("power island: 关机 clicked (UI only, no action wired)");

    private void OnRestartClick(object sender, RoutedEventArgs e)
        => Diagnostics.Log("power island: 重启 clicked (UI only, no action wired)");

    private void OnSleepClick(object sender, RoutedEventArgs e)
        => Diagnostics.Log("power island: 睡眠 clicked (UI only, no action wired)");

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        Diagnostics.Log("power island: 关闭程序 clicked -> exit");
        ExitRequested?.Invoke();
    }
}
