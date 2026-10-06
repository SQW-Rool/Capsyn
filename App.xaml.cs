using Capsyn.Services;
using Microsoft.UI.Xaml;

namespace Capsyn;

/// <summary>
/// 应用入口。WinUI 3 未打包应用由 WindowsAppSDK 自动生成的 Main 启动
/// （WindowsPackageType=None 时会自动 Bootstrap 本机的 Windows App Runtime）。
///
/// 这里做了两件「整程序」级别的事：
///   1) 单实例保护 —— 双击两次 / 自启 + 手动打开时，第二个进程直接退出，不会两个岛叠在一起；
///   2) 未处理异常的兜底 —— 写 capsyn-crash.log，并且 UI 线程的异常不让它把进程带走。
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();

        HookUnhandledExceptions();

        if (!SingleInstance.TryAcquire())
        {
            Diagnostics.Log("single instance: 已有实例在运行，本次启动退出");
            Environment.Exit(0);
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 常驻悬浮组件：全程只有一个窗口，位置在启动时算好后固定。
        _window = new MainWindow();
        _window.ShowIsland();
    }

    private void HookUnhandledExceptions()
    {
        // UI 线程上的异常：记下来，并标记已处理 —— 悬浮组件不该因为一次定时器回调出错就整只消失。
        UnhandledException += (_, e) =>
        {
            Diagnostics.LogCrash("Application.UnhandledException", e.Exception);
            e.Handled = true;
        };

        // 后台线程上的异常拦不住进程退出，但至少要留下记录。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception
                ?? new InvalidOperationException(e.ExceptionObject?.ToString() ?? "(无异常对象)");
            Diagnostics.LogCrash("AppDomain.UnhandledException (无法阻止退出)", exception);
        };

        // 被丢弃的 Task 异常默认会静默消失，这里记一笔并标记已观察。
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Diagnostics.LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }
}
