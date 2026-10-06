using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Capsyn.Services;

/// <summary>
/// 内容层的淡入淡出（Composition）。
///
/// 按需求「保留弹簧动画、删除生长动画」：
///   * 保留：岛的外轮廓由 <see cref="SpringScalar"/> 弹簧驱动（IslandShell 里逐帧喂给窗口区域），
///     展开有回弹、收起平滑 —— 这是看得见的弹簧动画。
///   * 删除：内容整体从 0.94 放大到 1、以及卡片/按钮逐个错帧「上浮 + 淡入」的生长动画。
///     现在内容只做透明度过渡：不缩放、不位移、不逐条入场。
/// </summary>
internal sealed class IslandAnimator
{
    private readonly Compositor _compositor;
    private readonly Visual _content;
    private readonly TimeSpan _fadeInDuration;
    private readonly TimeSpan _fadeOutDuration;

    public IslandAnimator(FrameworkElement content, TimeSpan fadeInDuration, TimeSpan fadeOutDuration)
    {
        _content = ElementCompositionPreview.GetElementVisual(content);
        _compositor = _content.Compositor;
        _fadeInDuration = fadeInDuration;
        _fadeOutDuration = fadeOutDuration;

        _content.Opacity = 0f;
    }

    /// <summary>入场：内容淡入（不做生长动画）。</summary>
    public void Expand() => FadeTo(1f, _fadeInDuration);

    /// <summary>出场：内容淡出（不做生长动画）。</summary>
    public void Collapse() => FadeTo(0f, _fadeOutDuration);

    private void FadeTo(float target, TimeSpan duration)
    {
        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, target);
        fade.Duration = duration;
        _content.StartAnimation(nameof(Visual.Opacity), fade);
    }
}
