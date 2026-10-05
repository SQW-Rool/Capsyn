using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Capsyn.Models;

namespace Capsyn.Services;

/// <summary>
/// 系统指标采集：全部用系统自带 API，不依赖任何第三方库。
///   * CPU：kernel32!GetSystemTimes —— 两次采样的差值算占用率
///   * 内存：kernel32!GlobalMemoryStatusEx
///   * 网络：System.Net.NetworkInformation —— 各网卡累计收发字节数差值 / 时间
/// </summary>
internal sealed class SystemMetricsProvider
{
    private readonly object _gate = new();

    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private ulong _prevBytesReceived;
    private ulong _prevBytesSent;
    private long _prevTimestamp;
    private bool _primed;

    /// <summary>
    /// 采一次样。CPU 占用率和网络速率都是「相对上一次采样」的差值，
    /// 所以第一次调用只建立基线（速率返回 0）。
    /// </summary>
    public SystemMetrics Sample()
    {
        lock (_gate)
        {
            GetSystemTimes(out var idleTime, out var kernelTime, out var userTime);
            var idle = ToUInt64(idleTime);
            var kernel = ToUInt64(kernelTime);
            var user = ToUInt64(userTime);

            var (bytesReceived, bytesSent) = ReadNetworkCounters();

            var totalBytes = ReadTotalPhysicalMemory(out var availableBytes);

            var now = Stopwatch.GetTimestamp();
            double cpuPercent = 0;
            double downPerSecond = 0;
            double upPerSecond = 0;

            if (_primed)
            {
                var elapsedSeconds = (now - _prevTimestamp) / (double)Stopwatch.Frequency;
                if (elapsedSeconds > 0.001)
                {
                    // kernel 时间里包含 idle，所以总时间 = kernel + user。
                    var idleDelta = idle - _prevIdle;
                    var totalDelta = (kernel - _prevKernel) + (user - _prevUser);
                    if (totalDelta > 0)
                    {
                        cpuPercent = Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100.0, 0.0, 100.0);
                    }

                    downPerSecond = Math.Max(0, (bytesReceived - _prevBytesReceived) / elapsedSeconds);
                    upPerSecond = Math.Max(0, (bytesSent - _prevBytesSent) / elapsedSeconds);
                }
            }

            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _prevBytesReceived = bytesReceived;
            _prevBytesSent = bytesSent;
            _prevTimestamp = now;
            _primed = true;

            var usedBytes = totalBytes > availableBytes ? totalBytes - availableBytes : 0;
            return new SystemMetrics(cpuPercent, totalBytes, usedBytes, downPerSecond, upPerSecond);
        }
    }

    /// <summary>汇总所有「已启用、非环回、非隧道」网卡的累计收发字节数。</summary>
    private static (ulong Received, ulong Sent) ReadNetworkCounters()
    {
        ulong received = 0;
        ulong sent = 0;

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                    || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var statistics = nic.GetIPStatistics();
                received += (ulong)Math.Max(0, statistics.BytesReceived);
                sent += (ulong)Math.Max(0, statistics.BytesSent);
            }
        }
        catch (Exception)
        {
            // 个别虚拟网卡会在读取统计时抛异常：忽略它，保留已累计的值。
        }

        return (received, sent);
    }

    private static ulong ToUInt64(FILETIME value)
        => ((ulong)(uint)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;

    // ---------------- kernel32 ----------------

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out FILETIME lpIdleTime,
        out FILETIME lpKernelTime,
        out FILETIME lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static ulong ReadTotalPhysicalMemory(out ulong availableBytes)
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            availableBytes = 0;
            return 0;
        }

        availableBytes = status.ullAvailPhys;
        return status.ullTotalPhys;
    }
}
