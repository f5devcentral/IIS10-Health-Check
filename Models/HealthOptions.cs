namespace HealthCheckSidecar.Models;

public class HealthOptions
{
    public const string SectionName = "HealthThresholds";

    // Enable/disable individual metric checks
    public bool EnableCpuCheck { get; set; } = true;
    public bool EnableMemoryCheck { get; set; } = true;
    public bool EnableDiskCheck { get; set; } = true;
    public bool EnableQueueCheck { get; set; } = true;

    // Thresholds
    public double MaxCpuPercentage { get; set; } = 85.0;
    public double MaxMemoryPercentage { get; set; } = 90.0;
    public double TotalSystemRamGb { get; set; } = 16.0;
    public double MinDiskSpacePercentage { get; set; } = 10.0;
    public double MaxQueueLength { get; set; } = 50.0;
    public string AppPoolName { get; set; } = "HealthCheckPool";
}


