using System.Globalization;
using Windows.UI;

namespace Capsyn.Helpers;

/// <summary>
/// 把 "#RRGGBB" / "#AARRGGBB" 解析成 Color，失败返回兜底色。
/// </summary>
internal static class ColorHelper
{
    public static Color Parse(string value, Color fallback)
    {
        var text = value.Trim().TrimStart('#');

        if (text.Length == 6)
        {
            text = "FF" + text;
        }

        if (text.Length != 8
            || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            return fallback;
        }

        return Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);
    }
}
