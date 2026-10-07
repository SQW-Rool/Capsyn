using System.Numerics;
using Capsyn.Configuration;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Capsyn.Services;

/// <summary>
/// 窗口（岛）出现时的入场动效：**淡入 + 轻微上浮**，走 Composition、时长取三档 token。
///
/// 为什么不做缩放：本项目的可见形状是由 **Win32 窗口区域（SetWindowRgn）逐帧裁出来**的 ✗，
/// 缩放内容会和窗口形状对不上（胶囊位置/圆角会错位）——所以入场只用透明度 + Translation。
///
/// 依据：
///   * [Motion in practice](https://learn.microsoft.com/en-us/windows/apps/develop/motion/motion-in-practice)
///     —— 入场用 Normal 档（300ms）+ 减速缓动；
///   * [XAML 与 Composition 互操作](https://learn.microsoft.com/en-us/windows/apps/develop/composition/xaml-comp-interop)
///     —— `ElementCompositionPreview` + `StartAnimation`，以及 Translation 需要先
///     `SetIsTranslationEnabled`。
/// </summary>
internal static class WindowAnimations
{
    /// <summary>入场：透明度 0 → 1，同时从上方 8px 处落到原位。</summary>
    public static void PlayEnter(UIElement root)
    {
        var visual = ElementCompositionPreview.GetElementVisual(root);
        var compositor = visual.Compositor;

        // Translation 动画必须先打开，否则 StartAnimation("Translation") 不生效。
        ElementCompositionPreview.SetIsTranslationEnabled(root, true);

        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f),
            new Vector2(0.2f, 1.0f));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = MotionTokens.Normal;
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, easing);

        var rise = compositor.CreateVector3KeyFrameAnimation();
        rise.Duration = MotionTokens.Normal;
        rise.InsertKeyFrame(0f, new Vector3(0f, -8f, 0f));
        rise.InsertKeyFrame(1f, Vector3.Zero, easing);

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", rise);
    }
}
