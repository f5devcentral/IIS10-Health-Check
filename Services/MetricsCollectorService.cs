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
    private PerformanceCounter? _cpuCounter;
    private DateTime _lastCpuTime = DateTime.UtcNow;
    private TimeSpan _lastTotalProcessorTime;

    public double CurrentCpuPercentage { get; private set; }
    public double CurrentMemoryPercentage { get; private set; }

    public MetricsCollectorService(ILogger<MetricsCollectorService> logger, IOptionsMonitor<HealthOptions> options)
    {
        _logger = logger;
        _options = options;
        _lastTotalProcessorTime = Process.GetCurrentProcess().TotalProcessorTime;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue(); // First call initializes the counter
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize Windows PerformanceCounter for Processor CPU Time.");
            }
        }
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
        // 1. Collect CPU Percentage
        if (OperatingSystem.IsWindows() && _cpuCounter != null)
        {
            try
            {
                CurrentCpuPercentage = Math.Round(_cpuCounter.NextValue(), 1);
            }
            catch
            {
                CurrentCpuPercentage = CalculateProcessCpuUsage();
            }
        }
        else
        {
            CurrentCpuPercentage = CalculateProcessCpuUsage();
        }

        // 2. Collect Memory Percentage
        CurrentMemoryPercentage = CalculateMemoryUsage();
    }

    private double CalculateProcessCpuUsage()
    {
        try
        {
            var now = DateTime.UtcNow;
            var proc = Process.GetCurrentProcess();
            var totalTime = proc.TotalProcessorTime;

            var timeWindow = (now - _lastCpuTime).TotalMilliseconds;
            var cpuWindow = (totalTime - _lastTotalProcessorTime).TotalMilliseconds;

            _lastCpuTime = now;
            _lastTotalProcessorTime = totalTime;

            if (timeWindow > 0)
            {
                double usage = (cpuWindow / (timeWindow * Environment.ProcessorCount)) * 100.0;
                return Math.Round(Math.Min(100.0, Math.Max(0.0, usage)), 1);
            }
        }
        catch
        {
            // Ignore fallback errors
        }

        return 0.0;
    }

    private double CalculateMemoryUsage()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0 && info.MemoryLoadBytes > 0)
            {
                double percentage = ((double)info.MemoryLoadBytes / info.TotalAvailableMemoryBytes) * 100.0;
                return Math.Round(percentage, 1);
            }

            // Fallback using Process WorkingSet vs Configured RAM
            var workingSet = Process.GetCurrentProcess().WorkingSet64;
            double configuredRamBytes = _options.CurrentValue.TotalSystemRamGb * 1024 * 1024 * 1024;
            if (configuredRamBytes > 0)
            {
                return Math.Round((workingSet / configuredRamBytes) * 100.0, 1);
            }
        }
        catch
        {
            // Ignore fallback errors
        }

        return 0.0;
    }

    public override void Dispose()
    {
        _cpuCounter?.Dispose();
        base.Dispose();
    }
}
