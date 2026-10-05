using System.Diagnostics;
using System.Globalization;
using Capsyn.Models;
using Capsyn.Services;

namespace Capsyn.ViewModels;

/// <summary>
/// 「看板岛」的视图模型：CPU / 内存 / 网络上下行。
/// 采样本身跑在线程池上（<see cref="RefreshAsync"/>），不阻塞 UI 线程，
/// 否则 1 秒一次的采集会把动画的定时器挤住（读网卡统计是系统调用，可能不便宜）。
///
/// TODO(扩展): 通知列表、音乐卡片、计时器都往这里加可绑定属性即可。
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    /// <summary>
    /// 进度条最大宽度（DIP）。
    /// 看板 550 宽、左右各 16 内边距、三列各 12 间隔 → 每张卡约 164.7 宽，扣掉卡片左右各 14 内边距 ≈ 136。
    /// </summary>
    private const double BarMaxWidth = 136.0;

    private const double BytesPerGigabyte = 1024.0 * 1024.0 * 1024.0;

    private readonly SystemMetricsProvider _provider = new();

    private string _cpuText = "--";
    private double _cpuBarWidth;
    private string _memoryText = "--";
    private string _memoryPercentText = "--";
    private double _memoryBarWidth;
    private string _networkDownText = "↓ --";
    private string _networkUpText = "↑ --";

    /// <summary>CPU 占用率，形如 "23%"。</summary>
    public string CpuText
    {
        get => _cpuText;
        private set => SetProperty(ref _cpuText, value);
    }

    /// <summary>CPU 进度条填充宽度（DIP）。</summary>
    public double CpuBarWidth
    {
        get => _cpuBarWidth;
        private set => SetProperty(ref _cpuBarWidth, value);
    }

    /// <summary>内存用量，形如 "9.1 / 31.7 GB"。</summary>
    public string MemoryText
    {
        get => _memoryText;
        private set => SetProperty(ref _memoryText, value);
    }

    /// <summary>内存占用率，形如 "29%"。</summary>
    public string MemoryPercentText
    {
        get => _memoryPercentText;
        private set => SetProperty(ref _memoryPercentText, value);
    }

    /// <summary>内存进度条填充宽度（DIP）。</summary>
    public double MemoryBarWidth
    {
        get => _memoryBarWidth;
        private set => SetProperty(ref _memoryBarWidth, value);
    }

    /// <summary>下行速率，形如 "↓ 1.24 MB/s"。</summary>
    public string NetworkDownText
    {
        get => _networkDownText;
        private set => SetProperty(ref _networkDownText, value);
    }

    /// <summary>上行速率，形如 "↑ 320.5 KB/s"。</summary>
    public string NetworkUpText
    {
        get => _networkUpText;
        private set => SetProperty(ref _networkUpText, value);
    }

    /// <summary>在线程池上采一次样，然后回到 UI 线程刷新绑定。</summary>
    public async Task RefreshAsync()
    {
        var started = Stopwatch.GetTimestamp();
        var metrics = await Task.Run(_provider.Sample);
        var elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

        Diagnostics.Log(string.Format(CultureInfo.InvariantCulture, "sample {0:0.0} ms", elapsedMs));

        Apply(metrics);
    }

    private void Apply(SystemMetrics metrics)
    {
        CpuText = string.Format(CultureInfo.InvariantCulture, "{0:0}%", metrics.CpuUsagePercent);
        CpuBarWidth = BarMaxWidth * metrics.CpuUsagePercent / 100.0;

        MemoryText = string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.0} / {1:0.0} GB",
            metrics.MemoryUsedBytes / BytesPerGigabyte,
            metrics.MemoryTotalBytes / BytesPerGigabyte);
        var memoryPercent = metrics.MemoryUsagePercent;
        MemoryPercentText = string.Format(CultureInfo.InvariantCulture, "{0:0}%", memoryPercent);
        MemoryBarWidth = BarMaxWidth * memoryPercent / 100.0;

        NetworkDownText = "↓ " + FormatRate(metrics.NetworkDownBytesPerSecond);
        NetworkUpText = "↑ " + FormatRate(metrics.NetworkUpBytesPerSecond);
    }

    private static string FormatRate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024 * 1024 => string.Format(CultureInfo.InvariantCulture, "{0:0.00} MB/s", bytesPerSecond / (1024 * 1024)),
        >= 1024 => string.Format(CultureInfo.InvariantCulture, "{0:0.0} KB/s", bytesPerSecond / 1024),
        _ => string.Format(CultureInfo.InvariantCulture, "{0:0} B/s", bytesPerSecond),
    };
}
