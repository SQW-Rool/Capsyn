using System.Runtime.InteropServices;
using Capsyn.Configuration;
using Capsyn.Interop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Capsyn.Services;

/// <summary>
/// 悬浮岛窗口的「系统行为」都收在这里：
///   * 无边框、无标题栏、不可缩放
///   * 不出现在任务栏、不出现在 Alt+Tab（IsShownInSwitchers = false + WS_EX_TOOLWINDOW）
///   * 始终置顶（OverlappedPresenter.IsAlwaysOnTop = true → WS_EX_TOPMOST）
///   * 窗口大小固定为「看板尺寸」这块画布，可见轮廓完全由窗口区域裁出来：
///       - 收起时区域 = 顶部居中的胶囊
///       - 展开时区域 = 胶囊 ∪ 下方看板（两块圆角矩形并集）
///       - 动画过程中由 <see cref="UpdateIslandShape"/> 逐帧跟着弹簧变形
///     （WinUI 3 的客户区是不透明的，圆角以外必须靠区域裁掉，否则会露白）
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

    /// <summary>窗口所在显示器的缩放比例（DIP → 物理像素）。</summary>
    private double _scale = 1.0;

    // 上一次提交给系统的形状，避免每帧都做重复的 SetWindowRgn。
    private int _lastPillKey = -1;
    private int _lastPanelWidth = -1;
    private int _lastPanelHeight = -1;
    private int _lastPowerWidth = -1;
    private int _lastPowerHeight = -1;

    public IslandWindowStyler(Window window, IntPtr hwnd, IslandOptions options)
    {
        _hwnd = hwnd;
        _options = options;
        _appWindow = window.AppWindow;
        _subclassProc = OnSubclassMessage;
    }

    /// <summary>窗口矩形（物理像素）。岛的悬停判定需要它。</summary>
    public RectInt32 WindowRect => new(_x, _y, _width, _height);

    /// <summary>
    /// 显示之前的窗口装饰：无边框 + 不进切换器 + 置顶 + 不画系统边框。
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
        // 这里把它们彻底清掉：客户区 = 窗口矩形。
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

        // 关掉 Win11 给顶层窗口的自动圆角（形状完全由窗口区域决定），也不画默认阴影/描边。
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

        // 让 DWM 把整个客户区当作「帧」：形状边缘的抗锯齿像素才能和桌面正确混合。
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
    /// 按主屏 + 当前 DPI 算一次窗口矩形（整块看板画布，主屏顶部居中），只在启动时调用一次。
    /// 胶囊在这块画布的顶部居中，看板从胶囊下方往下生长。
    /// </summary>
    public void ApplyIslandLayout()
    {
        _scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        if (_scale <= 0)
        {
            _scale = 1.0;
        }

        var display = DisplayArea.Primary;
        if (display is null)
        {
            return;
        }

        // OuterBounds = 整个屏幕（含任务栏区域），保证「贴着屏幕顶边」而不是贴着工作区。
        var bounds = display.OuterBounds;

        _width = (int)Math.Round(_options.WindowWidth * _scale);
        _height = (int)Math.Round(_options.WindowHeight * _scale);
        _x = bounds.X + ((bounds.Width - _width) / 2);
        _y = bounds.Y + (int)Math.Round(_options.TopOffset * _scale);

        _appWindow.MoveAndResize(new RectInt32(_x, _y, _width, _height));
    }

    /// <summary>
    /// 按动画进度更新窗口区域：胶囊（固定）∪ 看板（弹簧缩放中的尺寸）。
    /// 这是「看得见的形状」唯一来源，所以动画期间会被逐帧调用。
    /// </summary>
    /// <param name="panelScaleX">看板当前 ScaleX（收起 = 胶囊宽 / 看板宽）。</param>
    /// <param name="panelScaleY">看板当前 ScaleY（收起 = 0，展开 = 1）。</param>
    /// <param name="powerScale">电源岛当前生长进度（收起 = 0，展开 = 1）。</param>
    public void UpdateIslandShape(double panelScaleX, double panelScaleY, double powerScale)
    {
        if (_width <= 0 || _height <= 0)
        {
            return;
        }

        var scaleX = Math.Clamp(panelScaleX, 0, 1.2);
        var scaleY = Math.Clamp(panelScaleY, 0, 1.2);
        var power = Math.Clamp(powerScale, 0, 1.2);

        var pillWidth = (int)Math.Round(_options.Width * _scale);
        var pillHeight = (int)Math.Round(_options.Height * _scale);
        var pillLeft = (int)Math.Round(_options.PillLeft * _scale);
        var pillRadius = (int)Math.Round(_options.CornerRadius * _scale);

        var panelWidth = (int)Math.Round(_options.ExpandedWidth * _scale * scaleX);
        var panelHeight = (int)Math.Round(_options.ExpandedHeight * _scale * scaleY);

        // 电源岛：横向同样从胶囊宽度长起，纵向从 0 长到电源岛高度。
        var powerWidthScale = _options.CollapsedScaleX + ((1.0 - _options.CollapsedScaleX) * power);
        var powerWidth = (int)Math.Round(_options.PowerIslandWidth * _scale * powerWidthScale);
        var powerHeight = (int)Math.Round(_options.PowerIslandHeight * _scale * power);

        // 形状没变就不要再提交（动画期间同一像素尺寸会重复出现很多帧）。
        var pillKey = HashCode.Combine(pillWidth, pillHeight, pillLeft, pillRadius);
        if (pillKey == _lastPillKey
            && panelWidth == _lastPanelWidth
            && panelHeight == _lastPanelHeight
            && powerWidth == _lastPowerWidth
            && powerHeight == _lastPowerHeight)
        {
            return;
        }

        _lastPillKey = pillKey;
        _lastPanelWidth = panelWidth;
        _lastPanelHeight = panelHeight;
        _lastPowerWidth = powerWidth;
        _lastPowerHeight = powerHeight;

        Diagnostics.Log(
            $"region apply: pill {pillWidth}x{pillHeight} + panel {panelWidth}x{panelHeight} + power {powerWidth}x{powerHeight}");

        var region = NativeMethods.CreateRoundRectRgn(
            pillLeft,
            0,
            pillLeft + pillWidth + 1,
            pillHeight + 1,
            pillRadius * 2,
            pillRadius * 2);

        if (region == IntPtr.Zero)
        {
            return;
        }

        if (panelWidth >= 2 && panelHeight >= 2)
        {
            // 看板圆角随生长从一个小圆角过渡到最终圆角；半径不能超过短边的一半。
            var progress = Math.Clamp(scaleY, 0, 1);
            var panelRadius = (int)Math.Round((10 + ((_options.ExpandedCornerRadius - 10) * progress)) * _scale);
            panelRadius = Math.Max(1, Math.Min(panelRadius, Math.Min(panelWidth, panelHeight) / 2));

            var panelLeft = (_width - panelWidth) / 2;
            var panelTop = (int)Math.Round(_options.PanelTop * _scale);

            var panelRegion = NativeMethods.CreateRoundRectRgn(
                panelLeft,
                panelTop,
                panelLeft + panelWidth + 1,
                panelTop + panelHeight + 1,
                panelRadius * 2,
                panelRadius * 2);

            if (panelRegion != IntPtr.Zero)
            {
                NativeMethods.CombineRgn(region, region, panelRegion, NativeMethods.RGN_OR);
                NativeMethods.DeleteObject(panelRegion);
            }
        }

        // 电源岛：同样并进同一个窗口区域（挂着看板岛下方往下长）。
        if (powerWidth >= 2 && powerHeight >= 2)
        {
            var progress = Math.Clamp(power, 0, 1);
            var powerRadius = (int)Math.Round((8 + ((_options.PowerIslandCornerRadius - 8) * progress)) * _scale);
            powerRadius = Math.Max(1, Math.Min(powerRadius, Math.Min(powerWidth, powerHeight) / 2));

            var powerLeft = (_width - powerWidth) / 2;
            var powerTop = (int)Math.Round(_options.PowerIslandTop * _scale);

            var powerRegion = NativeMethods.CreateRoundRectRgn(
                powerLeft,
                powerTop,
                powerLeft + powerWidth + 1,
                powerTop + powerHeight + 1,
                powerRadius * 2,
                powerRadius * 2);

            if (powerRegion != IntPtr.Zero)
            {
                NativeMethods.CombineRgn(region, region, powerRegion, NativeMethods.RGN_OR);
                NativeMethods.DeleteObject(powerRegion);
            }
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
        // 鼠标扫过岛不激活窗口、不抢焦点、不触发任何默认行为。
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
    ///   * WM_MOUSEACTIVATE     —— 鼠标扫过岛不激活窗口（双保险，配合 WS_EX_NOACTIVATE）。
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
    // TODO(扩展): 如果出现别的置顶窗口压在岛上，可以在这里加一个低频定时器重新 ReassertTopMost()。
}
