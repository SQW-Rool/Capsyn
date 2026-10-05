using System.IO;
using System.Text;

namespace Capsyn.Services;

/// <summary>
/// 轻量诊断日志：只有设置环境变量 <c>CAPSYN_DIAG=1</c> 时才写文件（exe 同目录的 capsyn-diag.log）。
/// 用来排查「动画/定时器/窗口区域」的时序问题，正常运行时完全不产生开销。
/// 写 UTF-8 with BOM，Windows PowerShell 5.1 / 记事本打开都不会乱码。
/// </summary>
internal static class Diagnostics
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("CAPSYN_DIAG") == "1";

    private static readonly object Gate = new();
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "capsyn-diag.log");

    public static bool IsEnabled => Enabled;

    public static void Log(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                // 文件不存在时 AppendAllText 会先写 BOM，之后一直追加。
                File.AppendAllText(
                    LogPath,
                    $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}",
                    Utf8WithBom);
            }
        }
        catch (Exception)
        {
            // 诊断日志失败不能影响主流程。
        }
    }
}
