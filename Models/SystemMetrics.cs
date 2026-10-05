namespace Capsyn.Models;

/// <summary>
/// 一次系统资源采样结果。
/// </summary>
public readonly record struct SystemMetrics(
    double CpuUsagePercent,
    ulong MemoryTotalBytes,
    ulong MemoryUsedBytes,
    double NetworkDownBytesPerSecond,
    double NetworkUpBytesPerSecond)
{
    /// <summary>内存占用百分比。</summary>
    public double MemoryUsagePercent
        => MemoryTotalBytes == 0 ? 0 : (double)MemoryUsedBytes / MemoryTotalBytes * 100.0;
}
