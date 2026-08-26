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
var healthHandler = (IMetricsService metricsService, HttpResponse response) =>
{
    bool isHealthy = metricsService.IsHealthy(out string _);

    response.StatusCode = isHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
    response.ContentType = "application/json";

    var payload = new
    {
        status = isHealthy ? "Healthy" : "Unhealthy",
        cpu = $"{metricsService.CurrentCpuPercentage:F1}%",
        memory = $"{metricsService.CurrentMemoryPercentage:F1}%"
    };

    return Results.Json(payload, statusCode: response.StatusCode);
};

// Map endpoints matching BIG-IP probe patterns
app.MapGet("/api/health", healthHandler);
app.MapGet("/health", healthHandler);
app.MapGet("/", healthHandler);

app.Run();
