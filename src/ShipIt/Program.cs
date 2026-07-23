using ShipIt.Models;
using ShipIt.Services;

var builder = WebApplication.CreateBuilder(args);

// Default to port 8080 (matches the container image and the Kubernetes probes
// used in Modules 3 and 6) unless the host sets ASPNETCORE_URLS.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://0.0.0.0:8080");
}

builder.Services.AddSingleton<ShipmentStore>();

var app = builder.Build();

// Configuration comes from the environment. On Kubernetes these are set by a
// ConfigMap (Module 6); locally they fall back to sensible defaults.
static string Region() => Environment.GetEnvironmentVariable("SHIPIT_REGION") ?? "local";
static string BannerColor() => Environment.GetEnvironmentVariable("SHIPIT_BANNER_COLOR") ?? "green";
static string AppVersion() => Environment.GetEnvironmentVariable("SHIPIT_VERSION") ?? "0.1.0-dev";
// Readiness can be forced off with SHIPIT_READY=false to simulate a bad deploy
// (used to trigger rollback in Labs 5 and 7).
static bool IsReady() =>
    !string.Equals(Environment.GetEnvironmentVariable("SHIPIT_READY"), "false", StringComparison.OrdinalIgnoreCase);
//static bool IsReady() => false;   
// Liveness: the process is up. Never gated, so a live pod is not killed by config.
app.MapGet("/healthz", () => Results.Text("OK", "text/plain"));

// Readiness: is the app ready to serve real traffic? Kubernetes holds traffic
// back (and the CD pipeline rolls back) when this returns 503.
app.MapGet("/readyz", () => IsReady()
    ? Results.Text("READY", "text/plain")
    : Results.Text("NOT READY", "text/plain", statusCode: 503));

// Human-friendly status page: shows version, region, and the banner color so a
// config change (per environment) is visible without a rebuild.
app.MapGet("/", () =>
{
    var ready = IsReady() ? "yes" : "no";
    var html = $$"""
    <!doctype html>
    <html lang="en">
    <head>
      <meta charset="utf-8">
      <title>ShipIt</title>
      <style>
        body { font-family: system-ui, -apple-system, Segoe UI, sans-serif; margin: 0; color: #1a1a1a; }
        .banner { background: {{BannerColor()}}; color: #fff; padding: 24px 32px; font-size: 26px; font-weight: 600; }
        .body { padding: 24px 32px; line-height: 1.7; }
        code { background: #f2f2f4; padding: 2px 8px; border-radius: 4px; }
      </style>
    </head>
    <body>
      <div class="banner">ShipIt &middot; {{Region()}}</div>
      <div class="body">
        <p>Version: <code>{{AppVersion()}}</code></p>
        <p>Region: <code>{{Region()}}</code></p>
        <p>Banner color: <code>{{BannerColor()}}</code></p>
        <p>Ready: <code>{{ready}}</code></p>
        <p>API: <code>GET /api/shipments</code></p>
      </div>
    </body>
    </html>
    """;
    return Results.Content(html, "text/html");
});

// Minimal shipment API backed by an in-memory store (no database).
app.MapGet("/api/shipments", (ShipmentStore store) => Results.Ok(store.All()));

app.MapGet("/api/shipments/{id}", (string id, ShipmentStore store) =>
    store.Get(id) is { } shipment ? Results.Ok(shipment) : Results.NotFound());

app.MapPost("/api/shipments", (ShipmentInput input, ShipmentStore store) =>
{
    if (string.IsNullOrWhiteSpace(input.Destination))
        return Results.BadRequest(new { error = "destination is required" });

    var created = store.Add(input.Destination.Trim());
    return Results.Created($"/api/shipments/{created.Id}", created);
});

app.Run();

// Exposed so the test project can host the app with WebApplicationFactory<Program>.
public partial class Program { }
