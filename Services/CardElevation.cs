using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Capsyn.Services;

/// <summary>
/// 卡片层级（Phase 2 阴影）：给"卡片样式"的 Border 挂上 <see cref="ThemeShadow"/> + Z 抬升。
///
/// 为什么在代码里挂、而不是写进 `Style`：`ThemeShadow` 是共享对象，**不能在 `Style` 的 Setter 里
/// 走 ThemeResource**（会让 XamlCompiler 直接失败 —— 本项目的踩坑记录之一 ✗），必须逐元素内联。
/// 这里用"样式匹配 + 视觉树遍历"达到同样效果，且不碰 XAML 布局。
///
/// 依据：[Fluent 层级 / Materials](https://learn.microsoft.com/en-us/windows/apps/develop/ui/materials)
/// —— Fluent 用阴影表达层级；`ThemeShadow` 需要元素有 Z 方向位移（Translation）才看得出来。
/// </summary>
internal static class CardElevation
{
    private const float ElevationZ = 8f;

    /// <summary>把视觉树里所有使用 <paramref name="cardStyle"/> 的 Border 抬起来。</summary>
    public static void ApplyCardElevation(FrameworkElement root, Style? cardStyle)
    {
        if (cardStyle is null || root is null)
        {
            return;
        }

        var count = 0;

        foreach (var border in FindBorders(root))
        {
            if (!ReferenceEquals(border.Style, cardStyle))
            {
                continue;
            }

            border.Shadow = new ThemeShadow();
            border.Translation = new Vector3(0f, 0f, ElevationZ);
            count++;
        }

        Diagnostics.Log($"card elevation: 抬升 {count} 张卡片（ThemeShadow + Z {ElevationZ}）");
    }

    private static IEnumerable<Border> FindBorders(DependencyObject node)
    {
        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < children; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);

            if (child is Border border)
            {
                yield return border;
            }

            foreach (var nested in FindBorders(child))
            {
                yield return nested;
            }
        }
    }
}
