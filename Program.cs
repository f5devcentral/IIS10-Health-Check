using HealthCheckSidecar.Models;
using HealthCheckSidecar.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure HealthThresholds options
builder.Services.Configure<HealthOptions>(
    builder.Configuration.GetSection(HealthOptions.SectionName));

// Register background metrics collector service
builder.Services.AddSingleton<MetricsCollectorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MetricsCollectorService>());
builder.Services.AddSingleton<IMetricsService>(sp => sp.GetRequiredService<MetricsCollectorService>());

var app = builder.Build();

// Health check endpoint handler
var healthHandler = (IMetricsService metricsService, Microsoft.Extensions.Options.IOptions<HealthOptions> optionsAccessor, HttpResponse response) =>
{
    bool isHealthy = metricsService.IsHealthy(out string statusReason);
    var options = optionsAccessor.Value;

    response.StatusCode = isHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
    response.ContentType = "application/json";

    var payload = new Dictionary<string, object>
    {
        ["status"] = isHealthy ? "Healthy" : "Unhealthy"
    };

    if (options.EnableCpuCheck) payload["cpu"] = $"{metricsService.CurrentCpuPercentage:F1}%";
    if (options.EnableMemoryCheck) payload["memory"] = $"{metricsService.CurrentMemoryPercentage:F1}%";
    if (options.EnableDiskCheck) payload["diskFree"] = $"{metricsService.CurrentDiskSpacePercentage:F1}%";
    if (options.EnableQueueCheck) payload["queueLength"] = (int)metricsService.CurrentQueueLength;

    payload["reason"] = statusReason;

    return Results.Json(payload, statusCode: response.StatusCode);
};

// Map endpoints matching BIG-IP probe patterns
app.MapGet("/api/health", healthHandler);
app.MapGet("/health", healthHandler);
app.MapGet("/", healthHandler);

app.Run();
