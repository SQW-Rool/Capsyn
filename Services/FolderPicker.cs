using System.Runtime.InteropServices;

namespace Capsyn.Services;

/// <summary>
/// 系统原生的「选择文件夹」对话框（IFileOpenDialog + FOS_PICKFOLDERS）。
///
/// 为什么不用 <c>Windows.Storage.Pickers.FolderPicker</c>：它只能给一个
/// <c>PickerLocationId</c> 枚举，**没法把「默认打开到这个路径」传进去** ——
/// 而这个功能的明确要求是「默认是该压缩包所在的目录」。投资回报上，
/// 直接用 shell 的 IFileOpenDialog 才能 <c>SetFolder(压缩包所在目录)</c>，
/// 顺便还能拿到和资源管理器完全一致的观感。
/// </summary>
internal static class FolderPicker
{
    private const uint FosPickFolders = 0x00000020;
    private const uint FosForceFileSystem = 0x00000040;
    private const uint FosNoChangeDir = 0x00000008;
    private const uint FosPathMustExist = 0x00000800;
    private const uint SigdnFileSysPath = 0x80058000;

    /// <summary>HRESULT：用户在对话框里点了取消。</summary>
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    /// <summary>
    /// 弹出选择文件夹对话框。<paramref name="initialPath"/> 存在的话会默认定位到它。
    /// 返回 null 表示用户取消。
    /// </summary>
    public static string? PickFolder(IntPtr owner, string? initialPath, string title)
    {
        IFileOpenDialog? dialog = null;

        try
        {
            dialog = (IFileOpenDialog)new FileOpenDialogRcw();

            dialog.GetOptions(out var options);
            options |= FosPickFolders | FosForceFileSystem | FosNoChangeDir | FosPathMustExist;
            dialog.SetOptions(options);
            dialog.SetTitle(title);

            if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
            {
                var iid = typeof(IShellItem).GUID;
                if (SHCreateItemFromParsingName(initialPath, IntPtr.Zero, ref iid, out var folder) == 0
                    && folder is not null)
                {
                    try
                    {
                        dialog.SetFolder(folder);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(folder);
                    }
                }
            }

            var hr = dialog.Show(owner);

            if (hr == ErrorCancelled || hr < 0)
            {
                return null;
            }

            var item = dialog.GetResult();

            try
            {
                item.GetDisplayName(SigdnFileSysPath, out var path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"folder picker: failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (dialog is not null)
            {
                Marshal.ReleaseComObject(dialog);
            }
        }
    }

    /// <summary>
    /// 冒烟测试（调试用）：把对话框创建出来、设好选项和默认目录再释放，**不 Show** ——
    /// 用来在没有人工点选的情况下验证 COM 互操作没写错（vtable / 参数类型）。
    /// </summary>
    public static void SmokeTest(string initialPath)
    {
        IFileOpenDialog? dialog = null;

        try
        {
            dialog = (IFileOpenDialog)new FileOpenDialogRcw();

            dialog.GetOptions(out var options);
            options |= FosPickFolders | FosForceFileSystem | FosNoChangeDir | FosPathMustExist;
            dialog.SetOptions(options);
            dialog.SetTitle("冒烟测试");

            var iid = typeof(IShellItem).GUID;
            if (SHCreateItemFromParsingName(initialPath, IntPtr.Zero, ref iid, out var folder) == 0
                && folder is not null)
            {
                try
                {
                    dialog.SetFolder(folder);
                }
                finally
                {
                    Marshal.ReleaseComObject(folder);
                }
            }

            Diagnostics.Log("folder picker: smoke test OK（对话框已创建、选项与默认目录已应用、未 Show）");
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"folder picker: smoke test FAILED: {ex}");
        }
        finally
        {
            if (dialog is not null)
            {
                Marshal.ReleaseComObject(dialog);
            }
        }
    }

    // ---------------- COM 声明 ----------------
    [ComImport]
    [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRcw
    {
    }

    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(uint count, IntPtr filterSpec);

        void SetFileTypeIndex(uint index);

        uint GetFileTypeIndex();

        uint Advise(IntPtr events);

        void Unadvise(uint cookie);

        void SetOptions(uint options);

        void GetOptions(out uint options);

        void SetDefaultFolder(IShellItem item);

        void SetFolder(IShellItem item);

        IShellItem GetFolder();

        IShellItem GetCurrentSelection();

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        IShellItem GetResult();

        void AddPlace(IShellItem item, int fileDialogAddPlace);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);

        void Close(int hr);

        void SetClientGuid(ref Guid guid);

        void ClearClientData();

        void SetFilter(IntPtr filter);
    }

    [ComImport]
    [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog : IFileDialog
    {
        // 只用到新增的这两个；顺序不能乱（vtable 接着 IFileDialog 往下排）。
        void GetResults(out IntPtr enumShellItems);

        void GetSelectedItems(out IntPtr shellItemArray);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindCtx, ref Guid handler, ref Guid iid, out IntPtr result);

        void GetParent(out IShellItem parent);

        void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);

        void GetAttributes(uint mask, out uint attributes);

        void Compare(IShellItem other, uint hint, out int order);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindCtx,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);
}
