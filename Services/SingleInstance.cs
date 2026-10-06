namespace Capsyn.Services;

/// <summary>
/// 单实例保护：双击两次 exe、或者「开机自启 + 手动打开」时，不会出现两个岛叠在一起。
///
/// 用命名 Mutex（默认就是 <c>Local\</c> 作用域 = 当前登录会话，不会干扰别的用户）。
/// 拿不到互斥体时按「允许多实例」处理 —— 一个保护措施不该把程序挡在门外。
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = "Capsyn.SingleInstance";

    /// <summary>必须一直持有：被 GC 回收的话保护就失效了。</summary>
    private static Mutex? _mutex;

    /// <summary>本进程是不是第一个实例。false 时调用方应该直接退出。</summary>
    public static bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

            if (createdNew)
            {
                return true;
            }

            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"single instance: mutex 创建失败，按允许多实例处理 —— {ex.GetType().Name}: {ex.Message}");
            return true;
        }
    }
}
