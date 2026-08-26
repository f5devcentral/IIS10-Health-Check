namespace HealthCheckSidecar.Models;

public class HealthOptions
{
    public const string SectionName = "HealthThresholds";

    public double MaxCpuPercentage { get; set; } = 85.0;
    public double MaxMemoryPercentage { get; set; } = 90.0;
    public double TotalSystemRamGb { get; set; } = 16.0;
}
