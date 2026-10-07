using System.Numerics;
using Capsyn.Configuration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Capsyn.Services;

/// <summary>
/// 统一的指针反馈动效（Phase 2/3 第一块）：**hover 轻微放大、按下轻微缩小**，
/// 用 Composition 的 **隐式动画**（ImplicitAnimationCollection）实现 ——
/// 只写目标值，动画由合成线程跑，UI 线程不出帧、不占用逐帧 timer。
///
/// 依据（可追溯）：
///   * [XAML 与 Composition 互操作](https://learn.microsoft.com/en-us/windows/apps/develop/composition/xaml-comp-interop)
///     —— `ElementCompositionPreview.GetElementVisual` + `ImplicitAnimations` 的标准挂法；
///   * [Motion in practice](https://learn.microsoft.com/en-us/windows/apps/develop/motion/motion-in-practice)
///     —— 轻量反馈用 Fast 档（150ms）+ 减速缓动（本文件用 cubic-bezier 减速曲线，
///     与 Tokens.xaml 的 EasingDecelerate 同一意图）。
///
/// 只动画 `Scale`，**不碰 `Opacity`** —— 看板的过场动画（淡入淡出）用的也是这些元素的
/// Opacity，若这里也挂隐式动画会把过场时长改掉（踩过的坑预警）。
/// </summary>
internal static class InteractiveAnimations
{
    private const float HoverScale = 1.045f;
    private const float PressScale = 0.955f;

    /// <summary>给一批元素挂上指针反馈（hover 放大 / 按下缩小）。</summary>
    public static void AttachToButtons(params FrameworkElement[] elements)
    {
        foreach (var element in elements)
        {
            Attach(element);
        }
    }

    /// <summary>给单个元素挂指针反馈。</summary>
    public static void Attach(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        // 缩放中心保持在元素中心（尺寸变化时跟着更新，否则会围着左上角缩放）。
        void UpdateCenter()
        {
            visual.CenterPoint = new Vector3(
                (float)element.RenderSize.Width / 2f,
                (float)element.RenderSize.Height / 2f,
                0f);
        }

        UpdateCenter();
        element.SizeChanged += (_, _) => UpdateCenter();

        // 隐式动画：之后任何对 visual.Scale 的赋值都会被自动补间（Fast 档 + 减速缓动）。
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f),
            new Vector2(0.2f, 1.0f));

        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.Duration = MotionTokens.Fast;
        scaleAnimation.InsertExpressionKeyFrame(1.0f, "this.FinalValue", easing);
        scaleAnimation.Target = "Scale";

        var implicitAnimations = compositor.CreateImplicitAnimationCollection();
        implicitAnimations["Scale"] = scaleAnimation;
        visual.ImplicitAnimations = implicitAnimations;

        var hover = new Vector3(HoverScale, HoverScale, 1f);
        var press = new Vector3(PressScale, PressScale, 1f);

        element.PointerEntered += (_, _) => visual.Scale = hover;
        element.PointerExited += (_, _) => visual.Scale = Vector3.One;
        element.PointerPressed += (_, _) => visual.Scale = press;
        element.PointerReleased += (_, _) => visual.Scale = hover;
        element.PointerCanceled += (_, _) => visual.Scale = hover;
        element.PointerCaptureLost += (_, _) => visual.Scale = Vector3.One;
    }
}
