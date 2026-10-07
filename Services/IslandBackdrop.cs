using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Capsyn.Services;

/// <summary>
/// 【Phase 0 实验钩子】窗口背景材质（Mica / Acrylic）开关。
///
/// **只在设置了环境变量 <c>CAPSYN_BACKDROP</c> 时才生效**，默认（未设置或 =off）完全不改变现有外观：
///   * <c>CAPSYN_BACKDROP=mica</c>    → 尝试 `MicaBackdrop`（不支持则回退 Acrylic → 再回退纯黑）
///   * <c>CAPSYN_BACKDROP=acrylic</c> → 尝试 `DesktopAcrylicBackdrop`
///
/// 依据（可追溯）：
///   * [System backdrops (Mica/Acrylic)](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops)
///     —— Mica 是不透明材质、用作窗口基础层；Acrylic 半透明、用于 transient 浮层；
///     用 `Window.SystemBackdrop` + `MicaBackdrop`/`DesktopAcrylicBackdrop`（WinAppSDK 1.3+），
///     并以 `IsSupported()` 做运行时判断。
///   * [WinUI Gallery `SampleSystemBackdropsWindow`](https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/SampleSupport/SamplePages/SampleSystemBackdropsWindow.xaml.cs)
///     —— 官方接线写法参考。
///
/// 用途：实测两个只能跑起来才知道的问题 ——
///   D1：Mica/Acrylic 能否与本项目用 `SetWindowRgn` 裁出来的**异形窗口**共存；
///   D5：透明窗口 + Composition 动画 + 逐帧窗口区域 有没有冲突/掉帧。
/// 实测手段：为了让材质可见，钩子生效时会把岛体底色改成半透明（`IslandShell.SetBodyOpacity`）。
///
/// 日志走 `Diagnostics.Log`（需要 `CAPSYN_DIAG=1`，写到 exe 同目录的 `capsyn-diag.log`）。
/// </summary>
internal static class IslandBackdrop
{
    private const double ExperimentBodyOpacity = 0.72;

    /// <summary>环境变量里请求的材质模式（小写；空/off 表示不改）。</summary>
    public static string Requested =>
        (Environment.GetEnvironmentVariable("CAPSYN_BACKDROP") ?? "off").Trim().ToLowerInvariant();

    /// <summary>按环境变量应用背景材质。默认什么都不做。</summary>
    public static void Apply(Window window, Controls.IslandShell shell)
    {
        var mode = Requested;

        if (mode is "" or "off" or "none")
        {
            Diagnostics.Log(
                $"backdrop: CAPSYN_BACKDROP={mode}（默认）→ 不改窗口背景材质（纯黑岛体）。"
                + "想实测：设 CAPSYN_BACKDROP=mica 或 acrylic（建议同时设 CAPSYN_DIAG=1 看日志）后再启动。");
            return;
        }

        try
        {
            if (mode == "mica")
            {
                if (MicaController.IsSupported())
                {
                    window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
                    shell.SetBodyOpacity(ExperimentBodyOpacity);
                    Diagnostics.Log($"backdrop: 已应用 MicaBackdrop(Base)，岛体透明度 {ExperimentBodyOpacity}（实验态）");
                    return;
                }

                Diagnostics.Log("backdrop: MicaController.IsSupported()=false → 回退 Acrylic");
                mode = "acrylic";
            }

            if (mode == "acrylic")
            {
                if (DesktopAcrylicController.IsSupported())
                {
                    window.SystemBackdrop = new DesktopAcrylicBackdrop();
                    shell.SetBodyOpacity(ExperimentBodyOpacity);
                    Diagnostics.Log($"backdrop: 已应用 DesktopAcrylicBackdrop，岛体透明度 {ExperimentBodyOpacity}（实验态）");
                    return;
                }

                Diagnostics.Log("backdrop: DesktopAcrylicController.IsSupported()=false → 保持纯黑岛体");
                return;
            }

            Diagnostics.Log($"backdrop: 未知模式「{mode}」→ 保持纯黑岛体（可用值：mica / acrylic / off）");
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"backdrop: 应用背景材质失败（保持纯黑岛体）—— {ex.GetType().Name}: {ex.Message}");
        }
    }
}
