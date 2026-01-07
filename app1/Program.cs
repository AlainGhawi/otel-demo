using System.Diagnostics;
using System.Diagnostics.Metrics;
using CameraGateway.Models;
using Microsoft.OpenApi;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Get service name from environment variable or default
var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "camera-gateway";

// Configure OpenTelemetry Resource
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName: serviceName, serviceVersion: "1.0.0")
    .AddAttributes(new Dictionary<string, object>
    {
        ["environment"] = "development",
        ["service.team"] = "platform",
        ["service.domain"] = "physical-security",
    });

// Custom metrics for camera operations
var meter = new Meter("Genetec.CameraGateway", "1.0.0");
var cameraEventsReceived = meter.CreateCounter<long>("camera_events_received_total", description: "Total camera events received");
var motionDetections = meter.CreateCounter<long>("motion_detections_total", description: "Total motion detection events");
var camerasOnline = meter.CreateUpDownCounter<int>("cameras_online", description: "Number of cameras currently online");
var eventProcessingTime = meter.CreateHistogram<double>("camera_event_processing_ms", unit: "ms", description: "Time to process camera events");

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
        tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddRuntimeInstrumentation();
        metrics.AddProcessInstrumentation();
        metrics.AddMeter(meter.Name);
        metrics.AddOtlpExporter();
    });

builder.Services.AddHttpClient();

// Add Swagger services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Camera Gateway API",
        Version = "v1",
        Description = "Genetec Camera Gateway API with OpenTelemetry integration"
    });
});

var app = builder.Build();

// Enable Swagger middleware
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Camera Gateway API v1");
    options.RoutePrefix = "swagger";
});

// Simulated camera data
var cameras = new Dictionary<string, CameraInfo>
{
    ["CAM-001"] = new("CAM-001", "Main Entrance", "Building A", true),
    ["CAM-002"] = new("CAM-002", "Parking Lot", "External", true),
    ["CAM-003"] = new("CAM-003", "Server Room", "Building B", true),
    ["CAM-004"] = new("CAM-004", "Loading Dock", "Warehouse", false),
    ["CAM-005"] = new("CAM-005", "Reception", "Building A", true)
};


// Health check endpoint
app.MapGet("/", () => "Camera Gateway service is running!");

// Get all cameras
app.MapGet("/cameras", (ILogger<Program> logger) =>
{
    logger.LogInformation("Retrieving all camera status. Total cameras: {Count}", cameras.Count);
    return cameras.Values;
});

// Get camera by ID
app.MapGet("/cameras/{cameraId}", (string cameraId, ILogger<Program> logger) =>
{
    if (cameras.TryGetValue(cameraId, out var camera))
    {
        logger.LogInformation("Camera status retrieved: {CameraId} - {Location}", cameraId, camera.Location);
        return Results.Ok(camera);
    }

    logger.LogWarning("Camera not found: {CameraId}", cameraId);
    return Results.NotFound($"Camera {cameraId} not found");
});

// Simulate receiving a motion detection event
app.MapPost("/events/motion", async (MotionEvent motionEvent, HttpClient httpClient, ILogger<Program> logger) =>
{
    var stopwatch = Stopwatch.StartNew();

    using (logger.BeginScope(new Dictionary<string, object>
    {
        ["CameraId"] = motionEvent.CameraId,
        ["Zone"] = motionEvent.Zone
    }))
    {
        logger.LogInformation(
            "Motion detected by camera {CameraId} in zone {Zone}. Confidence: {Confidence}%",
            motionEvent.CameraId, motionEvent.Zone, motionEvent.Confidence);

        cameraEventsReceived.Add(1, new KeyValuePair<string, object?>("event_type", "motion"));
        motionDetections.Add(1,
            new KeyValuePair<string, object?>("camera_id", motionEvent.CameraId),
            new KeyValuePair<string, object?>("zone", motionEvent.Zone));

        // If high confidence, forward to Alert Service
        if (motionEvent.Confidence >= 80)
        {
            logger.LogInformation("High confidence motion event, forwarding to Alert Service");

            try
            {
                var alert = new
                {
                    Type = "MotionDetection",
                    Source = motionEvent.CameraId,
                    Severity = motionEvent.Confidence >= 95 ? "High" : "Medium",
                    Message = $"Motion detected in {motionEvent.Zone}",
                    Timestamp = DateTime.UtcNow,
                    Metadata = new { motionEvent.Zone, motionEvent.Confidence }
                };

                var response = await httpClient.PostAsJsonAsync("http://alert-service:8080/alerts", alert);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Alert created successfully for camera {CameraId}", motionEvent.CameraId);
                }
                else
                {
                    logger.LogError("Failed to create alert. Status: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to communicate with Alert Service");
            }
        }

        stopwatch.Stop();
        eventProcessingTime.Record(stopwatch.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("event_type", "motion"));

        return Results.Ok(new { Status = "Processed", EventId = Guid.NewGuid() });
    }
});

// Simulate camera health check event
app.MapPost("/events/health", (CameraHealthEvent healthEvent, ILogger<Program> logger) =>
{
    cameraEventsReceived.Add(1, new KeyValuePair<string, object?>("event_type", "health"));

    if (cameras.TryGetValue(healthEvent.CameraId, out var camera))
    {
        var previousStatus = camera.IsOnline;
        cameras[healthEvent.CameraId] = camera with { IsOnline = healthEvent.IsOnline };

        if (previousStatus != healthEvent.IsOnline)
        {
            camerasOnline.Add(healthEvent.IsOnline ? 1 : -1);

            if (healthEvent.IsOnline)
            {
                logger.LogInformation("Camera {CameraId} is now ONLINE at {Location}",
                    healthEvent.CameraId, camera.Location);
            }
            else
            {
                logger.LogWarning("Camera {CameraId} went OFFLINE at {Location}. Last error: {Error}",
                    healthEvent.CameraId, camera.Location, healthEvent.ErrorMessage ?? "Unknown");
            }
        }
    }

    return Results.Ok(new { Status = "Updated" });
});

// Simulate video analytics event (person/vehicle detection)
app.MapPost("/events/analytics", async (AnalyticsEvent analyticsEvent, HttpClient httpClient, ILogger<Program> logger) =>
{
    var stopwatch = Stopwatch.StartNew();

    cameraEventsReceived.Add(1, new KeyValuePair<string, object?>("event_type", "analytics"));

    logger.LogInformation(
        "Video analytics event from {CameraId}: {ObjectType} detected. Confidence: {Confidence}%",
        analyticsEvent.CameraId, analyticsEvent.ObjectType, analyticsEvent.Confidence);

    // High priority: Person detected in restricted area
    if (analyticsEvent.ObjectType == "Person" && analyticsEvent.IsRestrictedArea)
    {
        logger.LogWarning(
            "SECURITY: Person detected in RESTRICTED AREA by camera {CameraId}",
            analyticsEvent.CameraId);

        try
        {
            var alert = new
            {
                Type = "RestrictedAreaIntrusion",
                Source = analyticsEvent.CameraId,
                Severity = "Critical",
                Message = $"Unauthorized person detected in restricted area",
                Timestamp = DateTime.UtcNow,
                Metadata = new { analyticsEvent.ObjectType, analyticsEvent.Confidence, analyticsEvent.BoundingBox }
            };

            await httpClient.PostAsJsonAsync("http://alert-service:8080/alerts", alert);
            logger.LogInformation("Critical alert dispatched for restricted area intrusion");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to dispatch critical alert");
        }
    }

    stopwatch.Stop();
    eventProcessingTime.Record(stopwatch.ElapsedMilliseconds,
        new KeyValuePair<string, object?>("event_type", "analytics"));

    return Results.Ok(new { Status = "Processed", EventId = Guid.NewGuid() });
});

app.Run();