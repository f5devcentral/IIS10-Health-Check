using System.Diagnostics;
using System.Runtime.InteropServices;
using HealthCheckSidecar.Models;
using Microsoft.Extensions.Options;

namespace HealthCheckSidecar.Services;

public interface IMetricsService
{
    double CurrentCpuPercentage { get; }
    double CurrentMemoryPercentage { get; }
    bool IsHealthy(out string statusReason);
}

public class MetricsCollectorService : BackgroundService, IMetricsService
{
    private readonly ILogger<MetricsCollectorService> _logger;
    private readonly IOptionsMonitor<HealthOptions> _options;

    // Windows P/Invoke CPU tracking fields
    private long _prevIdleTicks;
    private long _prevKernelTicks;
    private long _prevUserTicks;
    private bool _hasPrevCpuSample;

    public double CurrentCpuPercentage { get; private set; }
    public double CurrentMemoryPercentage { get; private set; }

    public MetricsCollectorService(ILogger<MetricsCollectorService> logger, IOptionsMonitor<HealthOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public bool IsHealthy(out string statusReason)
    {
        var options = _options.CurrentValue;
        bool cpuBreached = CurrentCpuPercentage > options.MaxCpuPercentage;
        bool memBreached = CurrentMemoryPercentage > options.MaxMemoryPercentage;

        if (cpuBreached || memBreached)
        {
            statusReason = $"Threshold breached (Max CPU: {options.MaxCpuPercentage}%, Max Mem: {options.MaxMemoryPercentage}%)";
            return false;
        }

        statusReason = "Healthy";
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CollectMetrics();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting performance metrics.");
            }

            await Task.Delay(1000, stoppingToken);
        }
    }

    private void CollectMetrics()
    {
        if (OperatingSystem.IsWindows())
        {
            CurrentCpuPercentage = GetWindowsSystemCpuUsage();
            CurrentMemoryPercentage = GetWindowsSystemMemoryUsage();
        }
        else
        {
            CurrentCpuPercentage = GetFallbackCpuUsage();
            CurrentMemoryPercentage = GetFallbackMemoryUsage();
        }
    }

    private double GetWindowsSystemCpuUsage()
    {
        try
        {
            if (NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            {
                long idleTicks = ToTicks(idleTime);
                long kernelTicks = ToTicks(kernelTime);
                long userTicks = ToTicks(userTime);

                if (_hasPrevCpuSample)
                {
                    long idleDiff = idleTicks - _prevIdleTicks;
                    long kernelDiff = kernelTicks - _prevKernelTicks;
                    long userDiff = userTicks - _prevUserTicks;

                    long totalDiff = kernelDiff + userDiff;
                    long busyDiff = totalDiff - idleDiff;

                    if (totalDiff > 0)
                    {
                        double cpu = ((double)busyDiff / totalDiff) * 100.0;
                        _prevIdleTicks = idleTicks;
                        _prevKernelTicks = kernelTicks;
                        _prevUserTicks = userTicks;
                        return Math.Round(Math.Min(100.0, Math.Max(0.0, cpu)), 1);
                    }
                }

                _prevIdleTicks = idleTicks;
                _prevKernelTicks = kernelTicks;
                _prevUserTicks = userTicks;
                _hasPrevCpuSample = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading Windows GetSystemTimes.");
        }

        return GetFallbackCpuUsage();
    }

    private double GetWindowsSystemMemoryUsage()
    {
        try
        {
            var memStatus = new NativeMethods.MEMORYSTATUSEX();
            if (NativeMethods.GlobalMemoryStatusEx(ref memStatus))
            {
                if (memStatus.ullTotalPhys > 0)
                {
                    double used = memStatus.ullTotalPhys - memStatus.ullAvailPhys;
                    double percent = (used / memStatus.ullTotalPhys) * 100.0;
                    return Math.Round(percent, 1);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading Windows GlobalMemoryStatusEx.");
        }

        return GetFallbackMemoryUsage();
    }

    private double GetFallbackCpuUsage()
    {
        return 0.0;
    }

    private double GetFallbackMemoryUsage()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0 && info.MemoryLoadBytes > 0)
            {
                double percentage = ((double)info.MemoryLoadBytes / info.TotalAvailableMemoryBytes) * 100.0;
                return Math.Round(percentage, 1);
            }
        }
        catch
        {
            // Ignore
        }

        return 0.0;
    }

    private static long ToTicks(System.Runtime.InteropServices.ComTypes.FILETIME fileTime)
    {
        return ((long)fileTime.dwHighDateTime << 32) + (uint)fileTime.dwLowDateTime;
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MEMORYSTATUSEX
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

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemTimes(
            out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);
    }
}
