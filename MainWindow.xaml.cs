using Capsyn.Configuration;
using Capsyn.Interop;
using Capsyn.Services;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Capsyn;

/// <summary>
/// 只有「壳」：窗口本身 + 无边框/置顶/固定位置的系统行为。
/// 显示什么内容全部交给 <see cref="Controls.TimeIslandControl"/>，
/// 以后加通知/音乐/计时器只要换一个新的 UserControl。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly IslandWindowStyler _styler;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Capsyn";

        var hwnd = WindowNative.GetWindowHandle(this);
        _styler = new IslandWindowStyler(this, hwnd, IslandOptions.Default);

        // 1) 无边框 / 不进任务栏和 Alt+Tab / 置顶 / 圆角外透明。
        _styler.ApplyChrome();

        // 2) 先藏起来，等位置算好再显示，避免在默认位置闪一下。
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);

        Closed += OnClosed;

        // TODO(扩展): 托盘图标、开机自启、退出入口等常驻组件的初始化放在这里。
    }

    /// <summary>
    /// 按主屏算一次位置并固定，然后显示窗口。由 <see cref="App.OnLaunched"/> 调用。
    /// </summary>
    public void ShowIsland()
    {
        _styler.ApplyPillLayout();      // 尺寸 + 主屏顶部居中（只算这一次）
        _styler.ApplyPillShape();       // 把窗口本身裁成胶囊（圆角以外不算窗口，不会有白角）
        Activate();                     // 显示窗口（此时还未禁止激活，保证一定可见）
        _styler.ApplyPostShowPolicy();  // 点击不激活 + 压到最顶层 + 锁死位置
    }

    private void OnClosed(object sender, WindowEventArgs args) => _styler.Detach();
}
