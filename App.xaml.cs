using Microsoft.UI.Xaml;

namespace Capsyn;

/// <summary>
/// 应用入口。WinUI 3 未打包应用由 WindowsAppSDK 自动生成的 Main 启动
/// （WindowsPackageType=None 时会自动 Bootstrap 本机的 Windows App Runtime）。
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 常驻悬浮组件：全程只有一个窗口，位置在启动时算好后固定。
        _window = new MainWindow();
        _window.ShowIsland();
    }
}
