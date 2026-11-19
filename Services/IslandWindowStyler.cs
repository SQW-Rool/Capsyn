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
///   * 始终置顶（OverlappedPresenter.IsAlwaysOnTop = true → WS_EX_TOPMOST），
///     并且有一个低频「置顶看门狗」：别的置顶窗口后出现压住岛时，把它重新顶回最上层
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

    /// <summary>窗口 presenter（保留引用，显示之后要再应用一次「置顶」）。</summary>
    private OverlappedPresenter? _presenter;

    /// <summary>置顶看门狗：低频检查、必要时把窗口重新压回置顶层。</summary>
    private DispatcherTimer? _topMostWatchdog;

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
    private int _lastPillLeft = -1;
    private int _lastPillWidth = -1;
    private int _lastDotKey = -1;
    private int _lastPanelLeft = -1;
    private int _lastPanelWidth = -1;
    private int _lastPanelHeight = -1;
    private int _lastSideKey = -1;

    // 最近一次提交的形状：DPI / 分辨率变化后要按它把窗口区域重算一遍。
    private IslandShapeSnapshot? _lastShape;

    // 当前可见轮廓的包围盒（窗口坐标，物理像素）：置顶看门狗用它判断「岛是不是真被压住了」。
    // 注意是「看得见的那几块」的并集，不是整块窗口画布 —— 画布下半部分大面积是空的，
    // 拿它去判重叠会误伤（比如别的置顶窗口压在空白区域，岛其实一点没被挡）。
    private int _visibleLeft;
    private int _visibleTop;
    private int _visibleRight;
    private int _visibleBottom;

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
            _presenter = presenter;

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
    /// 按主屏 + 当前 DPI 算窗口矩形（整块看板画布，主屏顶部居中）。
    /// 启动时调用一次；之后显示器缩放 / 分辨率变化时（WM_DPICHANGED / WM_DISPLAYCHANGE）也会再调。
    /// </summary>
    public void ApplyIslandLayout()
    {
        ApplyLayoutPass();

        // DPI 要「移完再取」才算准：窗口刚创建时可能还停在别的显示器上，
        // 这时 GetDpiForWindow 给的是那块屏的缩放比例。取到不一样的就按新比例再算一遍。
        if (Math.Abs(GetWindowScale() - _scale) > 0.001)
        {
            ApplyLayoutPass();
        }

        // 位置 / 缩放变了，上一次提交的区域不再有效：清掉去重键，并按最近的形状重算一次。
        _lastPillLeft = -1;
        _lastPillWidth = -1;
        _lastDotKey = -1;
        _lastPanelWidth = -1;
        _lastPanelHeight = -1;

        if (_lastShape is { } shape)
        {
            UpdateIslandShape(shape);
        }
    }

    /// <summary>窗口所在显示器的缩放比例（拿不到就按 100%）。</summary>
    private double GetWindowScale()
    {
        var scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        return scale > 0 ? scale : 1.0;
    }

    private void ApplyLayoutPass()
    {
        _scale = GetWindowScale();

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
    /// 按当前动画姿态更新窗口区域：胶囊（宽度会变）∪ 看板（弹簧中的宽 × 高）。
    /// 这是「看得见的形状」唯一来源，所以动画期间会被逐帧调用。
    ///
    /// 可见轮廓现在是**三块**的并集：
    ///   * 胶囊：时钟 320 / 监测区 172，左边界由外壳算好（有第二计时时会往右让半个圆）；
    ///   * **独立小圆岛**：第二个计时缩成的那颗圆，出现进度 &gt; 0 才参与区域；
    ///   * 看板：展开时从胶囊宽长到 550 宽、从 0 长到当前视图高度。
    /// </summary>
    public void UpdateIslandShape(IslandShapeSnapshot shape)
    {
        if (_width <= 0 || _height <= 0)
        {
            return;
        }

        _lastShape = shape;

        var progress = Math.Clamp(shape.CornerProgress, 0, 1);

        var pillLeft = (int)Math.Round(shape.PillLeft * _scale);
        var pillWidth = (int)Math.Round(Math.Max(2, shape.PillWidth) * _scale);
        var pillHeight = (int)Math.Round(_options.Height * _scale);
        var pillRadius = (int)Math.Round(_options.CornerRadius * _scale);

        var showDot = shape.DotOpacity > 0.02;
        var dotSize = (int)Math.Round(Math.Max(2, shape.DotSize) * _scale);
        var dotLeft = (int)Math.Round(shape.DotLeft * _scale);

        var panelLeftDip = shape.PanelLeft > 0.5 ? shape.PanelLeft : ((_width / _scale) - shape.PanelWidth) / 2.0;
        var panelLeft = (int)Math.Round(panelLeftDip * _scale);
        var panelWidth = (int)Math.Round(Math.Max(0, shape.PanelWidth) * _scale);
        var panelHeight = (int)Math.Round(Math.Max(0, shape.PanelHeight) * _scale);

        // 音乐副岛：独立的一块，出现在看板右侧。
        var showSide = shape.SideOpacity > 0.02 && shape.SideWidth >= 2 && shape.SideHeight >= 2;
        var sideLeft = (int)Math.Round(shape.SideLeft * _scale);
        var sideWidth = (int)Math.Round(Math.Max(0, shape.SideWidth) * _scale);
        var sideHeight = (int)Math.Round(Math.Max(0, shape.SideHeight) * _scale);

        // 形状没变就不要再提交（动画期间同一像素尺寸会重复出现很多帧）。
        var dotKey = showDot ? HashCode.Combine(dotLeft, dotSize) : 0;
        var sideKey = showSide ? HashCode.Combine(sideLeft, sideWidth, sideHeight) : 0;
        if (pillLeft == _lastPillLeft
            && pillWidth == _lastPillWidth
            && dotKey == _lastDotKey
            && panelLeft == _lastPanelLeft
            && panelWidth == _lastPanelWidth
            && panelHeight == _lastPanelHeight
            && sideKey == _lastSideKey)
        {
            return;
        }

        _lastPillLeft = pillLeft;
        _lastPillWidth = pillWidth;
        _lastDotKey = dotKey;
        _lastPanelLeft = panelLeft;
        _lastPanelWidth = panelWidth;
        _lastPanelHeight = panelHeight;
        _lastSideKey = sideKey;

        // 记下「看得见的那几块」的包围盒（窗口坐标，物理像素）：置顶看门狗拿它判断是否真被压住。
        var panelTop = (int)Math.Round(_options.PanelTop * _scale);

        _visibleLeft = pillLeft;
        _visibleTop = 0;
        _visibleRight = pillLeft + pillWidth;
        _visibleBottom = pillHeight;

        if (showDot && dotSize >= 2)
        {
            _visibleLeft = Math.Min(_visibleLeft, dotLeft);
            _visibleRight = Math.Max(_visibleRight, dotLeft + dotSize);
            _visibleBottom = Math.Max(_visibleBottom, dotSize);
        }

        if (panelWidth >= 2 && panelHeight >= 2)
        {
            _visibleLeft = Math.Min(_visibleLeft, panelLeft);
            _visibleTop = Math.Min(_visibleTop, panelTop);
            _visibleRight = Math.Max(_visibleRight, panelLeft + panelWidth);
            _visibleBottom = Math.Max(_visibleBottom, panelTop + panelHeight);
        }

        if (showSide)
        {
            _visibleLeft = Math.Min(_visibleLeft, sideLeft);
            _visibleTop = Math.Min(_visibleTop, panelTop);
            _visibleRight = Math.Max(_visibleRight, sideLeft + sideWidth);
            _visibleBottom = Math.Max(_visibleBottom, panelTop + sideHeight);
        }

        // 这行在动画期间是**逐帧**走的：字符串插值发生在调用之前，所以先看开关，别白造字符串。
        if (Diagnostics.IsEnabled)
        {
            Diagnostics.Log(
                $"region apply: pill {pillWidth}x{pillHeight} + dot {(showDot ? dotSize : 0)} " +
                $"+ panel {panelWidth}x{panelHeight}@({panelLeft}) " +
                $"+ side {(showSide ? $"{sideWidth}x{sideHeight}@{sideLeft}" : "off")}");
        }

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

        // 独立小圆岛：正方形 + 圆角 = 圆
        if (showDot && dotSize >= 2)
        {
            var dotRegion = NativeMethods.CreateRoundRectRgn(
                dotLeft,
                0,
                dotLeft + dotSize + 1,
                dotSize + 1,
                dotSize,
                dotSize);

            if (dotRegion != IntPtr.Zero)
            {
                NativeMethods.CombineRgn(region, region, dotRegion, NativeMethods.RGN_OR);
                NativeMethods.DeleteObject(dotRegion);
            }
        }

        if (panelWidth >= 2 && panelHeight >= 2)
        {
            // 看板圆角随生长从一个小圆角过渡到最终圆角；半径不能超过短边的一半。
            var panelRadius = (int)Math.Round((10 + ((_options.ExpandedCornerRadius - 10) * progress)) * _scale);
            panelRadius = Math.Max(1, Math.Min(panelRadius, Math.Min(panelWidth, panelHeight) / 2));

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

        // 音乐副岛：看板右侧的独立一块（圆角跟看板一致，顶边对齐）。
        if (showSide)
        {
            var sideRadius = (int)Math.Round(_options.ExpandedCornerRadius * _scale);
            sideRadius = Math.Max(1, Math.Min(sideRadius, Math.Min(sideWidth, sideHeight) / 2));

            var sideTop = panelTop;

            var sideRegion = NativeMethods.CreateRoundRectRgn(
                sideLeft,
                sideTop,
                sideLeft + sideWidth + 1,
                sideTop + sideHeight + 1,
                sideRadius * 2,
                sideRadius * 2);

            if (sideRegion != IntPtr.Zero)
            {
                NativeMethods.CombineRgn(region, region, sideRegion, NativeMethods.RGN_OR);
                NativeMethods.DeleteObject(sideRegion);
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

        // 置顶要在窗口**真正显示之后**再应用一次：
        // 构造阶段设的 IsAlwaysOnTop 在窗口还没显示时可能不生效（或随 WinUI 首次显示时的
        // 默认窗口状态一起被冲掉）。显示之后重新设置，presenter 的内部状态才和窗口实际样式一致。
        if (_presenter is not null)
        {
            _presenter.IsAlwaysOnTop = true;
        }

        ReassertTopMost();
        StartTopMostWatchdog();

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

    /// <summary>
    /// 「始终置顶」的看门狗：低频检查、被压住就把窗口重新顶回置顶层。
    ///
    /// 为什么需要它：WS_EX_TOPMOST 只保证「在所有**普通**窗口之上」而已。
    /// 同属置顶层的窗口（桌面歌词、各种悬浮工具、别的「置顶」窗口）后出现、或被激活时，
    /// 会插到岛的前面，把岛压住 —— 这时岛看起来就像「没能置顶」。
    /// 每 2 秒检查一次：一旦发现岛被可见窗口盖住（或 TOPMOST 位丢了），就 SetWindowPos(HWND_TOPMOST) 顶回来。
    ///
    /// 为什么是轮询而不是订阅事件：Windows 没有「有窗口插到我前面」的通知；
    /// 又不能用 <c>SetWinEventHook</c> 那种全局钩子（要注入别的进程、还容易被杀软当成外挂）。
    /// 轮询只读 Z 序 + 几条窗口矩形，2 秒一次可以忽略不计。
    /// </summary>
    private void StartTopMostWatchdog()
    {
        if (_topMostWatchdog is not null)
        {
            return;
        }

        // 调试钩子（截图 / 录屏用）：置顶看门狗每 2 秒 SetWindowPos 一次，
        // 会让 PrintWindow 偶发抓到黑帧。CAPSYN_NO_TOPMOST_WATCHDOG=1 时不启动。
        if (Environment.GetEnvironmentVariable("CAPSYN_NO_TOPMOST_WATCHDOG") == "1")
        {
            Diagnostics.Log("topmost[watchdog]: 已禁用（CAPSYN_NO_TOPMOST_WATCHDOG=1）");
            return;
        }

        _topMostWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topMostWatchdog.Tick += OnTopMostWatchdogTick;
        _topMostWatchdog.Start();
    }

    private void OnTopMostWatchdogTick(object? sender, object e)
    {
        if (_disposed)
        {
            return;
        }

        var topMostMissing = (NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64()
                              & NativeMethods.WS_EX_TOPMOST) == 0;

        // 已经在最上层、也没被挡住，就什么都不做（避免无意义的 Z 序变更）。
        if (!topMostMissing && !IsCoveredByVisibleWindow())
        {
            return;
        }

        // 被压住了：presenter 状态可能还被别的操作改过，先同步再 SetWindowPos。
        if (_presenter is not null && (_presenter.IsAlwaysOnTop is false))
        {
            _presenter.IsAlwaysOnTop = true;
        }

        ReassertTopMost();

        Diagnostics.Log($"topmost[watchdog]: 被压住（topmost 位丢失={topMostMissing}）→ 重新压回置顶层");
    }

    /// <summary>
    /// 岛（看得见的那几块）是不是被 Z 序里**排在它上面**的可见窗口真的压住了。
    /// 只看「有没有窗口在上面」不行：任务栏、系统 tooltip 这些置顶窗口常年挂在最上面，
    /// 虽然排在岛前面但根本不和岛重叠，那样看门狗会每 2 秒白顶一次。
    /// 所以这里做矩形相交判断，只有真的盖到岛身上才重新置顶。
    /// </summary>
    private bool IsCoveredByVisibleWindow()
    {
        if (_visibleRight <= _visibleLeft || _visibleBottom <= _visibleTop)
        {
            return false;
        }

        // 岛可见轮廓的屏幕矩形（窗口坐标 + 窗口位置）。
        var left = _x + _visibleLeft;
        var top = _y + _visibleTop;
        var right = _x + _visibleRight;
        var bottom = _y + _visibleBottom;

        for (var above = NativeMethods.GetWindow(_hwnd, NativeMethods.GW_HWNDPREV);
             above != IntPtr.Zero;
             above = NativeMethods.GetWindow(above, NativeMethods.GW_HWNDPREV))
        {
            // 不可见窗口（输入法 / MSCTF 的隐藏窗口常年挂在最上面一堆）不参与判断。
            if (!NativeMethods.IsWindowVisible(above))
            {
                continue;
            }

            if (!NativeMethods.GetWindowRect(above, out var rect))
            {
                continue;
            }

            var overlaps = rect.Right > left && rect.Left < right
                           && rect.Bottom > top && rect.Top < bottom;
            if (overlaps)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>窗口关闭时摘掉子类化、停掉看门狗，避免回调打到已销毁的窗口。</summary>
    public void Detach()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_topMostWatchdog is not null)
        {
            _topMostWatchdog.Stop();
            _topMostWatchdog.Tick -= OnTopMostWatchdogTick;
            _topMostWatchdog = null;
        }

        if (_subclassInstalled)
        {
            NativeMethods.RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);
            _subclassInstalled = false;
        }
    }

    /// <summary>
    /// 窗口消息钩子：
    ///   * WM_WINDOWPOSCHANGING —— 位置锁定后强制保留算好的矩形（用户拖不动，外部也改不了）。
    ///   * WM_MOUSEACTIVATE     —— 鼠标扫过岛不激活窗口（双保险，配合 WS_EX_NOACTIVATE）。
    ///   * WM_DPICHANGED / WM_DISPLAYCHANGE —— 缩放比例 / 分辨率 / 主屏变了，重算窗口矩形与形状。
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

            case NativeMethods.WM_DPICHANGED:
            case NativeMethods.WM_DISPLAYCHANGE:
            {
                // 位置锁会把 MoveAndResize 拦回去，所以先临时解锁，算完再锁上（锁的是新矩形）。
                var wasLocked = _positionLocked;
                _positionLocked = false;
                ApplyIslandLayout();
                _positionLocked = wasLocked;
                break;
            }
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // TODO(扩展): 多显示器场景下「跟随当前主屏」的策略（现在固定钉在主屏顶部居中）。
}
