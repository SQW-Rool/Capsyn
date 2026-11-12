namespace Capsyn.Configuration;

/// <summary>
/// 某一帧「岛」的外轮廓参数（全部 DIP）。由 <see cref="Controls.IslandShell"/> 逐帧算好，
/// 交给 <see cref="Services.IslandWindowStyler"/> 换算成物理像素、拼成窗口区域。
///
/// 为什么单独一个类型：现在可见轮廓不止一块 ——
///   胶囊（时钟 / 监测区，宽度会变）+ 旁边可能有的**独立小圆岛**（第二个计时）+（展开时的）看板。
/// 参数一多，位置参数列表就看不懂了，所以打包成一个快照。
/// </summary>
/// <param name="PillLeft">胶囊左边界（DIP，窗口坐标系）。</param>
/// <param name="PillWidth">胶囊宽度（DIP）。</param>
/// <param name="DotLeft">小圆岛左边界（DIP）；<paramref name="DotOpacity"/> 为 0 时忽略。</param>
/// <param name="DotSize">小圆岛直径（DIP）。</param>
/// <param name="DotOpacity">小圆岛的出现进度 0~1（0 = 完全不存在，不参与窗口区域）。</param>
/// <param name="PanelWidth">看板宽度（DIP）。</param>
/// <param name="PanelHeight">看板高度（DIP）。</param>
/// <param name="CornerProgress">看板展开进度 0~1（只用来过渡看板圆角）。</param>
public readonly record struct IslandShapeSnapshot(
    double PillLeft,
    double PillWidth,
    double DotLeft,
    double DotSize,
    double DotOpacity,
    double PanelWidth,
    double PanelHeight,
    double CornerProgress);
