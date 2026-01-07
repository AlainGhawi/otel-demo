using Microsoft.OpenApi;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using AlertService.Models;

var builder = WebApplication.CreateBuilder(args);

// Get service name from environment variable or default
var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "alert-service";

// Configure OpenTelemetry Resource
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName: serviceName, serviceVersion: "1.0.0")
    .AddAttributes(new Dictionary<string, object>
    {
        ["environment"] = "development",
        ["service.team"] = "platform",
        ["service.domain"] = "physical-security"
    });

// Custom metrics for alert operations
var meter = new Meter("Genetec.AlertService", "1.0.0");
var alertsReceived = meter.CreateCounter<long>("alerts_received_total", description: "Total alerts received");
var alertsDispatched = meter.CreateCounter<long>("alerts_dispatched_total", description: "Total alerts dispatched to operators");
var alertsByServerity = meter.CreateCounter<long>("alerts_by_severity_total", description: "Alerts by severity level");
var activeAlerts = meter.CreateUpDownCounter<int>("active_alerts", description: "Currently active (unacknowledged) alerts");
var alertProcessingTime = meter.CreateHistogram<double>("alert_processing_ms", unit: "ms", description: "Alert processing time");

// Setup logging to be exported via OpenTelemetry
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.SetResourceBuilder(resourceBuilder);
    logging.AddOtlpExporter();
});

// Configure OpenTelemetry for Traces and Metrics
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: serviceName, serviceVersion: "1.0.0"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        tracing.AddOtlpExporter(); // This will use the OTEL_EXPORTER_OTLP_ENDPOINT environment variable in docker-compose
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddRuntimeInstrumentation();
        metrics.AddProcessInstrumentation();
        metrics.AddMeter(meter.Name);
        metrics.AddOtlpExporter();
    });

// Add Swagger services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Alert Service API",
        Version = "v1",
        Description = "Alert Service API with OpenTelemetry integration"
    });
});

var app = builder.Build();

// Enable Swagger middleware (consider using only in Development for production)
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Alert Service API v1");
    options.RoutePrefix = "swagger";
});


// In-memory alert storage
var alerts = new ConcurrentDictionary<Guid, SecurityAlert>();
// Health check
app.MapGet("/", () => "Alert Service is running!");

// Get all alerts
app.MapGet("/alerts", (ILogger<Program> logger, bool? activeOnly) =>
{
    var result = activeOnly == true
        ? alerts.Values.Where(a => a.Status == "Active").ToList()
        : alerts.Values.ToList();

    logger.LogInformation("Retrieved {Count} alerts (activeOnly: {ActiveOnly})", result.Count, activeOnly);
    return result.OrderByDescending(a => a.Timestamp);
});

// Get alert by ID
app.MapGet("/alerts/{alertId}", (Guid alertId, ILogger<Program> logger) =>
{
    if (alerts.TryGetValue(alertId, out var alert))
    {
        return Results.Ok(alert);
    }

    logger.LogWarning("Alert not found: {AlertId}", alertId);
    return Results.NotFound();
});

// Create new alert
app.MapPost("/alerts", (CreateAlertRequest request, ILogger<Program> logger) =>
{
    var stopwatch = Stopwatch.StartNew();

    var alertId = Guid.NewGuid();

    using (logger.BeginScope(new Dictionary<string, object>
    {
        ["AlertId"] = alertId,
        ["AlertType"] = request.Type,
        ["Severity"] = request.Severity
    }))
    {
        logger.LogInformation(
            "New {Severity} alert received: {Type} from {Source}",
            request.Severity, request.Type, request.Source);

        alertsReceived.Add(1);
        alertsByServerity.Add(1, new KeyValuePair<string, object?>("severity", request.Severity));
        activeAlerts.Add(1);

        var alert = new SecurityAlert(
            Id: alertId,
            Type: request.Type,
            Source: request.Source,
            Severity: request.Severity,
            Message: request.Message,
            Timestamp: request.Timestamp ?? DateTime.UtcNow,
            Status: "Active",
            Metadata: request.Metadata
        );

        alerts[alertId] = alert;

        // Simulate dispatching to operators for high severity
        if (request.Severity is "High" or "Critical")
        {
            logger.LogWarning(
                "DISPATCHING {Severity} ALERT to on-duty operators: {Message}",
                request.Severity, request.Message);

            alertsDispatched.Add(1,
                new KeyValuePair<string, object?>("severity", request.Severity),
                new KeyValuePair<string, object?>("type", request.Type));

            // Simulate notification delay
            Task.Delay(Random.Shared.Next(10, 50));

            logger.LogInformation("Alert {AlertId} dispatched to Security Operations Center", alertId);
        }

        stopwatch.Stop();
        alertProcessingTime.Record(stopwatch.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("severity", request.Severity));

        logger.LogInformation("Alert {AlertId} created and stored. Processing time: {ProcessingTime}ms",
            alertId, stopwatch.ElapsedMilliseconds);

        return Results.Created($"/alerts/{alertId}", alert);
    }
});

// Acknowledge alert
app.MapPost("/alerts/{alertId}/acknowledge", (Guid alertId, AcknowledgeRequest request, ILogger<Program> logger) =>
{
    if (!alerts.TryGetValue(alertId, out var alert))
    {
        logger.LogWarning("Cannot acknowledge - Alert not found: {AlertId}", alertId);
        return Results.NotFound();
    }

    var updatedAlert = alert with
    {
        Status = "Acknowledged",
        AcknowledgedBy = request.OperatorId,
        AcknowledgedAt = DateTime.UtcNow
    };

    alerts[alertId] = updatedAlert;
    activeAlerts.Add(-1);

    logger.LogInformation(
        "Alert {AlertId} acknowledged by operator {OperatorId}. Response time: {ResponseTime}s",
        alertId, request.OperatorId, (DateTime.UtcNow - alert.Timestamp).TotalSeconds);

    return Results.Ok(updatedAlert);
});

// Resolve alert
app.MapPost("/alerts/{alertId}/resolve", (Guid alertId, ResolveRequest request, ILogger<Program> logger) =>
{
    if (!alerts.TryGetValue(alertId, out var alert))
    {
        return Results.NotFound();
    }

    var updatedAlert = alert with
    {
        Status = "Resolved",
        Resolution = request.Resolution,
        ResolvedAt = DateTime.UtcNow
    };

    alerts[alertId] = updatedAlert;

    if (alert.Status == "Active")
    {
        activeAlerts.Add(-1);
    }

    logger.LogInformation(
        "Alert {AlertId} resolved. Resolution: {Resolution}. Total time: {TotalTime}s",
        alertId, request.Resolution, (DateTime.UtcNow - alert.Timestamp).TotalSeconds);

    return Results.Ok(updatedAlert);
});

// Get alert statistics
app.MapGet("/alerts/stats", (ILogger<Program> logger) =>
{
    var stats = new
    {
        Total = alerts.Count,
        Active = alerts.Values.Count(a => a.Status == "Active"),
        Acknowledged = alerts.Values.Count(a => a.Status == "Acknowledged"),
        Resolved = alerts.Values.Count(a => a.Status == "Resolved"),
        BySeverity = alerts.Values
            .GroupBy(a => a.Severity)
            .ToDictionary(g => g.Key, g => g.Count()),
        ByType = alerts.Values
            .GroupBy(a => a.Type)
            .ToDictionary(g => g.Key, g => g.Count())
    };

    logger.LogInformation("Alert statistics retrieved. Active: {Active}, Total: {Total}",
        stats.Active, stats.Total);

    return stats;
});

app.Run();