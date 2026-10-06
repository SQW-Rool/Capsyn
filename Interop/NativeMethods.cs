using System.Runtime.InteropServices;

namespace Capsyn.Interop;

/// <summary>
/// 悬浮窗口需要的 Win32 互操作。
/// 这里只保留「无边框 / 置顶 / 不进任务栏 / 不抢焦点 / 焊死位置」这几个用途所需的最小集合。
/// </summary>
internal static class NativeMethods
{
    // ---- Get/SetWindowLongPtr 的索引 ----
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    // ---- 普通窗口样式（要把系统非客户区清理干净） ----
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_BORDER = 0x00800000;
    public const int WS_DLGFRAME = 0x00400000;   // “有边框但没标题栏”，浅色主题下就是那条白边
    public const int WS_THICKFRAME = 0x00040000;
    public const int WS_SYSMENU = 0x00080000;
    public const int WS_MINIMIZEBOX = 0x00020000;
    public const int WS_MAXIMIZEBOX = 0x00010000;

    // ---- 扩展窗口样式 ----
    public const int WS_EX_DLGMODALFRAME = 0x00000001;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TOOLWINDOW = 0x00000080;   // 不出现在任务栏 / Alt+Tab
    public const int WS_EX_WINDOWEDGE = 0x00000100;   // 立体边框
    public const int WS_EX_CLIENTEDGE = 0x00000200;   // 凹陷客户区边框
    public const int WS_EX_APPWINDOW = 0x00040000;    // 强制出现在任务栏
    public const int WS_EX_NOACTIVATE = 0x08000000;   // 被点击也不激活、不抢焦点

    // ---- 窗口消息 ----
    public const uint WM_WINDOWPOSCHANGING = 0x0046;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_DISPLAYCHANGE = 0x007E;   // 分辨率 / 主屏变化
    public const uint WM_DPICHANGED = 0x02E0;      // 窗口所在显示器的缩放比例变化
    public const int MA_NOACTIVATE = 3;

    // ---- SetWindowPos / ShowWindow ----
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;

    // ---- DWM ----
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    public delegate IntPtr SubclassProc(
        IntPtr hWnd,
        uint uMsg,
        IntPtr wParam,
        IntPtr lParam,
        IntPtr uIdSubclass,
        IntPtr dwRefData);

    // ---------------- user32 ----------------

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLong64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    /// <summary>读取窗口扩展样式（自动适配 32/64 位）。</summary>
    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 8 ? GetWindowLong64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    /// <summary>写入窗口扩展样式（自动适配 32/64 位）。</summary>
    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        => IntPtr.Size == 8
            ? SetWindowLong64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>窗口所在显示器的缩放比例（96 DPI = 1.0）。</summary>
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>当前光标位置（屏幕坐标，物理像素）。用来判断鼠标是否悬停在岛上。</summary>
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    // ---------------- comctl32（窗口子类化，用于锁死位置） ----------------

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool SetWindowSubclass(
        IntPtr hWnd,
        SubclassProc pfnSubclass,
        IntPtr uIdSubclass,
        IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool RemoveWindowSubclass(
        IntPtr hWnd,
        SubclassProc pfnSubclass,
        IntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    // ---------------- dwmapi ----------------
    /// <summary>
    /// 把 DWM 的「边框」扩展到整个客户区（-1）。
    /// 这是让 WinUI 合成结果的 per-pixel alpha 被 DWM 尊重、从而真正透明的关键：
    /// 胶囊圆角以外的像素不画任何东西，直接透出桌面。
    /// </summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    /// <summary>
    /// Win11 会给顶层窗口加系统圆角/投影，胶囊要自己控制形状，所以显式关掉。
    /// TODO(扩展): 需要自定义阴影时，可以在这里改成给窗口加 DWM 投影。
    /// </summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(
        IntPtr hWnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    // ---------------- gdi32 / user32：把窗口裁成胶囊形状 ----------------

    /// <summary>
    /// 生成一个圆角矩形区域（角是四分之一椭圆，宽 = 椭圆宽，高 = 椭圆高）。
    /// 胶囊形状 = 椭圆宽高都取窗口高度（半径 = 高度 / 2）。
    /// </summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateRoundRectRgn(
        int nLeftRect,
        int nTopRect,
        int nRightRect,
        int nBottomRect,
        int nWidthEllipse,
        int nHeightEllipse);

    /// <summary>把窗口裁剪成指定区域（成功时区域所有权交给系统）。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    /// <summary>合并两个区域（用来把「胶囊」和「看板」拼成一个窗口形状）。</summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int iMode);

    /// <summary>CombineRgn 的模式：并集。</summary>
    public const int RGN_OR = 2;

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);
}
