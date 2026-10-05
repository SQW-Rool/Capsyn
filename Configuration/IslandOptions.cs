namespace Capsyn.Configuration;

/// <summary>
/// 「岛」的外观与行为配置。
/// 目前是代码内默认值；后续要调宽高/位置/字体，只要改这里（或后续接入外部配置文件）。
///
/// TODO(扩展): 从 exe 同目录的 island.config.json 读取并覆盖下面的默认值，
///             这样用户不改代码就能调整尺寸、位置、字体、配色。
/// </summary>
public sealed class IslandOptions
{
    /// <summary>全局默认配置。</summary>
    public static IslandOptions Default { get; } = new();

    /// <summary>胶囊宽度（DIP / 逻辑像素）。</summary>
    public double Width { get; init; } = 320;

    /// <summary>胶囊高度（DIP / 逻辑像素）。</summary>
    public double Height { get; init; } = 40;

    /// <summary>距主屏顶边的距离（DIP / 逻辑像素）。</summary>
    public double TopOffset { get; init; } = 10;

    /// <summary>时间格式，24 小时制。</summary>
    public string TimeFormat { get; init; } = "HH:mm:ss";

    /// <summary>时间字号（DIP）。</summary>
    public double FontSize { get; init; } = 17;

    /// <summary>时间字体，等宽。</summary>
    public string FontFamily { get; init; } = "Cascadia Code";

    /// <summary>胶囊底色。</summary>
    public string PillColor { get; init; } = "#000000";

    /// <summary>时间文字颜色。</summary>
    public string ForegroundColor { get; init; } = "#FFFFFF";

    /// <summary>超圆角：半径 = 高度的一半，即 iOS 灵动岛的胶囊形状。</summary>
    public double CornerRadius => Height / 2.0;
}
