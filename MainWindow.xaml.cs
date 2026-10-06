using Capsyn.Configuration;
using Capsyn.Interop;
using Capsyn.Services;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Capsyn;

/// <summary>
/// 只有「壳」：窗口本身 + 无边框/置顶/固定位置的系统行为。
/// 显示什么、怎么展开，全交给 <see cref="Controls.IslandShell"/>。
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

        // 1) 无边框 / 不进任务栏和 Alt+Tab / 置顶 / 不画系统边框。
        _styler.ApplyChrome();

        // 2) 先藏起来，等位置和形状都算好再显示，避免在默认位置闪一下。
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);

        // 岛壳需要两样东西：窗口矩形（判断悬停）和形变进度（同步窗口区域）。
        Island.WindowRectProvider = () => _styler.WindowRect;
        Island.ShapeProgress += OnIslandShapeProgress;
        Island.ExitRequested += OnExitRequested;

        Closed += OnClosed;

        // TODO(扩展): 托盘图标、开机自启、退出入口等常驻组件的初始化放在这里。
    }

    /// <summary>
    /// 按主屏算一次窗口位置并固定，先裁成胶囊再显示。由 <see cref="App.OnLaunched"/> 调用。
    /// </summary>
    public void ShowIsland()
    {
        _styler.ApplyIslandLayout();                 // 整块看板画布，主屏顶部居中（只算这一次）

        var (scaleX, scaleY) = Island.CollapsedPanelScale;
        _styler.UpdateIslandShape(scaleX, scaleY);      // 收起态：窗口只露出时间岛胶囊

        Activate();                                  // 显示窗口（此时还未禁止激活，保证一定可见）
        _styler.ApplyPostShowPolicy();               // 不抢焦点 + 压到最顶层 + 锁死位置
    }

    private void OnIslandShapeProgress(double panelScaleX, double panelScaleY)
        => _styler.UpdateIslandShape(panelScaleX, panelScaleY);

    /// <summary>看板岛右下角的「关闭程序」：关掉窗口并退出应用。</summary>
    private void OnExitRequested()
    {
        Diagnostics.Log("exit requested -> closing window and exiting");
        Close();
        Application.Current.Exit();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Island.ShapeProgress -= OnIslandShapeProgress;
        Island.ExitRequested -= OnExitRequested;
        _styler.Detach();
    }
}
