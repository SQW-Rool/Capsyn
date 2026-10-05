using System.Runtime.InteropServices;
using Capsyn.Configuration;
using Capsyn.Interop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Capsyn.Services;

/// <summary>
/// 悬浮胶囊窗口的「系统行为」都收在这里，方便以后替换/扩展：
///   * 无边框、无标题栏、不可缩放
///   * 不出现在任务栏、不出现在 Alt+Tab（IsShownInSwitchers = false + WS_EX_TOOLWINDOW）
///   * 始终置顶（OverlappedPresenter.IsAlwaysOnTop = true → WS_EX_TOPMOST）
///   * 窗口本身不画任何背景：圆角以外直接透出桌面
///     （透明页面背景 + DWM 帧扩展保留 per-pixel alpha + 窗口区域裁成胶囊）
///   * 启动时在主屏顶部居中算一次位置，然后用窗口子类化把位置焊死
/// </summary>
internal sealed class IslandWindowStyler
{
    /// <summary>子类化时用的 id（同一次子类化必须保持一致）。</summary>
    private static readonly IntPtr SubclassId = new(1);

    private readonly AppWindow _appWindow;
    private readonly IslandOptions _options;
    private readonly IntPtr _hwnd;

    /// <summary>必须保存委托实例，否则会被 GC 回收导致回调崩溃。</summary>
    private readonly NativeMethods.SubclassProc _subclassProc;

    private bool _subclassInstalled;
    private bool _positionLocked;
    private bool _disposed;

    // 启动时算好的「钉子」，之后窗口只能停在这里。
    private int _x;
    private int _y;
    private int _width;
    private int _height;

    public IslandWindowStyler(Window window, IntPtr hwnd, IslandOptions options)
    {
        _hwnd = hwnd;
        _options = options;
        _appWindow = window.AppWindow;
        _subclassProc = OnSubclassMessage;
    }

    /// <summary>
    /// 显示之前的窗口装饰：无边框 + 不进切换器 + 置顶 + 透明。
    /// 注意这里故意<strong>不</strong>设置 WS_EX_NOACTIVATE，
    /// 以免影响第一次显示（显示之后由 <see cref="ApplyPostShowPolicy"/> 补上）。
    /// </summary>
    public void ApplyChrome()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            // 去掉标题栏和边框；顺便关掉缩放/最大化/最小化。
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;

            // 始终置顶：置于所有普通窗口之上。
            presenter.IsAlwaysOnTop = true;
        }

        // 不在任务栏显示，也不出现在 Alt+Tab 里。
        _appWindow.IsShownInSwitchers = false;

        // OverlappedPresenter 只去掉了标题栏和缩放框，窗口上还留着 WS_DLGFRAME / WS_SYSMENU
        // 这些「非客户区」，在浅色主题下就是一圈可见的白边，而且会把客户区缩小几个像素。
        // 这里把它们彻底清掉：客户区 = 窗口矩形，胶囊可以铺满整个窗口，圆角外不留白边。
        var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE).ToInt64();
        style &= ~(NativeMethods.WS_CAPTION
                   | NativeMethods.WS_BORDER
                   | NativeMethods.WS_DLGFRAME
                   | NativeMethods.WS_THICKFRAME
                   | NativeMethods.WS_SYSMENU
                   | NativeMethods.WS_MINIMIZEBOX
                   | NativeMethods.WS_MAXIMIZEBOX);
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE, new IntPtr(style));

        // 扩展样式：标记成工具窗口（任务栏 / Alt+Tab 都不出现），并清掉会画边框的那几个。
        var exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
        exStyle &= ~(NativeMethods.WS_EX_WINDOWEDGE
                     | NativeMethods.WS_EX_CLIENTEDGE
                     | NativeMethods.WS_EX_DLGMODALFRAME
                     | NativeMethods.WS_EX_APPWINDOW);
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

        // 让样式改动立即生效（不移动、不缩放、不改变 Z 序）。
        NativeMethods.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER
            | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);

        // 关掉 Win11 给顶层窗口的自动圆角（形状完全由 XAML 里的 Border 决定），也不画默认阴影/描边。
        var cornerPreference = NativeMethods.DWMWCP_DONOTROUND;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd,
            NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref cornerPreference,
            sizeof(int));

        var noBorder = NativeMethods.DWMWA_COLOR_NONE;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd,
            NativeMethods.DWMWA_BORDER_COLOR,
            ref noBorder,
            sizeof(int));

        // 让 DWM 把整个客户区当作「帧」：WinUI 合成结果的 per-pixel alpha 才会被尊重，
        // 圆角边上的抗锯齿像素才能正确地和桌面混合（胶囊形状本身由 ApplyPillShape 的区域裁剪保证）。
        var margins = new NativeMethods.MARGINS
        {
            cxLeftWidth = -1,
            cxRightWidth = -1,
            cyTopHeight = -1,
            cyBottomHeight = -1,
        };
        NativeMethods.DwmExtendFrameIntoClientArea(_hwnd, ref margins);

        _subclassInstalled = NativeMethods.SetWindowSubclass(_hwnd, _subclassProc, SubclassId, IntPtr.Zero);

        // TODO(扩展): 需要「鼠标穿透」时可在此加 WS_EX_TRANSPARENT | WS_EX_LAYERED，
        //             并提供一个运行时开关（点击穿透 / 取消穿透）。
    }

    /// <summary>
    /// 按主屏 + 当前 DPI 算一次尺寸和位置（主屏顶部居中），并应用。
    /// 只在启动时调用一次。
    /// </summary>
    public void ApplyPillLayout()
    {
        var scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        if (scale <= 0)
        {
            scale = 1.0;
        }

        var display = DisplayArea.Primary;
        if (display is null)
        {
            return;
        }

        // OuterBounds = 整个屏幕（含任务栏区域），保证「贴着屏幕顶边」而不是贴着工作区。
        var bounds = display.OuterBounds;

        _width = (int)Math.Round(_options.Width * scale);
        _height = (int)Math.Round(_options.Height * scale);
        _x = bounds.X + ((bounds.Width - _width) / 2);
        _y = bounds.Y + (int)Math.Round(_options.TopOffset * scale);

        _appWindow.MoveAndResize(new RectInt32(_x, _y, _width, _height));
    }

    /// <summary>
    /// 把窗口本身裁成胶囊形状（圆角半径 = 高度 / 2）。
    ///
    /// 为什么需要它：WinUI 3 的窗口客户区是不透明的（框架会用主题色把客户区填满），
    /// 所以即使 XAML 里圆角外什么都不画，窗口矩形本身仍然是「方的」。
    /// 用 Win32 窗口区域把矩形裁掉，圆角以外就不再属于这个窗口，
    /// 于是无论系统是深色还是浅色主题，都不会出现白角/黑角。
    /// 必须在 <see cref="ApplyPillLayout"/> 之后调用（尺寸已知）。
    /// </summary>
    public void ApplyPillShape()
    {
        if (_width <= 0 || _height <= 0)
        {
            return;
        }

        // 椭圆宽高都取窗口高度 → 圆角半径 = 高度 / 2 → 胶囊。
        var region = NativeMethods.CreateRoundRectRgn(0, 0, _width + 1, _height + 1, _height, _height);
        if (region == IntPtr.Zero)
        {
            return;
        }

        // SetWindowRgn 成功时由系统接管这块区域；失败时自己要记得释放。
        if (NativeMethods.SetWindowRgn(_hwnd, region, true) == 0)
        {
            NativeMethods.DeleteObject(region);
        }
    }

    /// <summary>显示之后的策略：不抢焦点、重新压到最顶层、锁死位置。</summary>
    public void ApplyPostShowPolicy()
    {
        // 点击胶囊不激活窗口、不抢焦点、不触发任何默认行为。
        var exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

        ReassertTopMost();

        // 从这一刻起，任何试图移动/缩放窗口的动作都会被拦回钉好的位置。
        _positionLocked = true;
    }

    /// <summary>重新把窗口压到最顶层（不移动、不缩放、不激活）。</summary>
    public void ReassertTopMost()
        => NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

    /// <summary>窗口关闭时摘掉子类化，避免回调打到已销毁的窗口。</summary>
    public void Detach()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_subclassInstalled)
        {
            NativeMethods.RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);
            _subclassInstalled = false;
        }
    }

    /// <summary>
    /// 窗口消息钩子：
    ///   * WM_WINDOWPOSCHANGING —— 位置锁定后强制保留启动时算好的矩形（用户拖不动，外部也改不了）。
    ///   * WM_MOUSEACTIVATE     —— 点击胶囊不激活窗口（双保险，配合 WS_EX_NOACTIVATE）。
    /// </summary>
    private IntPtr OnSubclassMessage(
        IntPtr hWnd,
        uint uMsg,
        IntPtr wParam,
        IntPtr lParam,
        IntPtr uIdSubclass,
        IntPtr dwRefData)
    {
        switch (uMsg)
        {
            case NativeMethods.WM_WINDOWPOSCHANGING when _positionLocked:
            {
                var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
                pos.x = _x;
                pos.y = _y;
                pos.cx = _width;
                pos.cy = _height;
                pos.flags |= NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE;
                Marshal.StructureToPtr(pos, lParam, false);
                break;
            }

            case NativeMethods.WM_MOUSEACTIVATE when _positionLocked:
                // 不激活，也不把点击交给窗口。
                return NativeMethods.MA_NOACTIVATE;
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // TODO(扩展): 处理 WM_DPICHANGED（被拖到别的缩放比例显示器时重新算尺寸），
    //             以及多显示器场景下「跟随当前主屏」的策略。
    // TODO(扩展): 处理 WM_DISPLAYCHANGE（分辨率/主屏变化后重新居中）。
    // TODO(扩展): 如果出现别的置顶窗口压在胶囊上面，可以在这里加一个低频定时器重新 ReassertTopMost()。
}
