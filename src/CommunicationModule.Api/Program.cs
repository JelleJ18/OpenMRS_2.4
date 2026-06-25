using CommunicationModule.Api.Endpoints;
using CommunicationModule.Api.Middleware;
using CommunicationModule.Api.Services;
using CommunicationModule.Core.Interfaces;
using CommunicationModule.Infrastructure.Data;
using CommunicationModule.Infrastructure.Services;
using CommunicationModule.Infrastructure.Providers.SwiftSend;
using CommunicationModule.Infrastructure.Providers.LegacyLink;
using CommunicationModule.Infrastructure.Providers.AsyncFlow;
using CommunicationModule.Infrastructure.Providers.SecurePost;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using System.Security.Authentication;

var builder = WebApplication.CreateBuilder(args);

// ======================
// CORE
// ======================
builder.Services.AddScoped<DataRetentionService>();
builder.Services.AddHostedService<DataRetentionBackgroundService>();

builder.Configuration.AddUserSecrets<Program>(optional: true);

var encryptionKey = builder.Configuration["Crypto:Key"];
if (string.IsNullOrWhiteSpace(encryptionKey))
    throw new InvalidOperationException("Missing Crypto:Key");

builder.Services.AddSingleton(new AesEncryptionService(encryptionKey));
builder.Services.AddSingleton<TenantAccessService>();

builder.Services.AddScoped<IEventPublisher, EventPublisher>();

// ======================
// METRICS
// ======================
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(Telemetry.MeterName);
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddPrometheusExporter();
    });

// ======================
// HANGFIRE
// ======================
builder.Services.AddHangfire(x => x.UseInMemoryStorage());
builder.Services.AddHangfireServer();

// ======================
// APPLICATION SERVICES
// ======================
builder.Services.AddScoped<AppointmentIngestionService>();
builder.Services.AddScoped<NotificationDispatchService>();

// ======================
// 🔥 PROVIDERS (CORRECT FIX)
// ======================

// HttpClients (BELANGRIJK: fakecomworld docker base)
builder.Services.AddHttpClient<SwiftSendProvider>(c =>
{
    c.BaseAddress = new Uri("http://localhost:1337");
});

builder.Services.AddHttpClient<LegacyLinkProvider>(c =>
{
    c.BaseAddress = new Uri("http://localhost:1337");
});

builder.Services.AddHttpClient<AsyncFlowProvider>(c =>
{
    c.BaseAddress = new Uri("http://localhost:1337");
});

builder.Services.AddHttpClient<SecurePostProvider>(c =>
{
    c.BaseAddress = new Uri("http://localhost:1337");
});

// 👉 DIT IS DE CRUCIALE FIX (IMessagingProvider REGISTRY)
builder.Services.AddScoped<IMessagingProvider>(sp =>
    sp.GetRequiredService<SwiftSendProvider>());

builder.Services.AddScoped<IMessagingProvider>(sp =>
    sp.GetRequiredService<LegacyLinkProvider>());

builder.Services.AddScoped<IMessagingProvider>(sp =>
    sp.GetRequiredService<AsyncFlowProvider>());

builder.Services.AddScoped<IMessagingProvider>(sp =>
    sp.GetRequiredService<SecurePostProvider>());

// ======================
// DB
// ======================
var conn = DatabaseConnectionResolver.ResolveConnectionString(builder.Configuration);
var serverVersion = DatabaseConnectionResolver.GetServerVersion();

builder.Services.AddDbContext<CommunicationDbContext>(opts =>
    opts.UseMySql(conn, serverVersion, o =>
        o.MigrationsHistoryTable("__efmigrationshistory")));

// ======================
// KESTREL
// ======================
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureHttpsDefaults(h =>
    {
        h.SslProtocols = SslProtocols.Tls13;
    });
});

var app = builder.Build();

// ======================
// PIPELINE
// ======================
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHangfireDashboard("/hangfire");
}

app.UseHttpsRedirection();
app.UseMiddleware<ApiKeyMiddleware>();

// ======================
// ENDPOINTS
// ======================
app.MapDashboardEndpoints();
app.MapOrganisationEndpoints();
app.MapOpenMRSInstanceEndpoints();
app.MapFhirEndpoints();
app.MapHl7Endpoints();
app.MapProviderEndpoints();

// test endpoint
app.MapPost("/test/dispatch/{jobId}", async (
    Guid jobId,
    NotificationDispatchService service,
    CancellationToken ct) =>
{
    await service.DispatchAsync(jobId, ct);
    return Results.Ok(new { jobId });
});

// db check
app.MapGet("/db-check", async (CommunicationDbContext db, CancellationToken ct) =>
{
    return Results.Ok(new { canConnect = await db.Database.CanConnectAsync(ct) });
});

// metrics
app.MapGet("/metrics/business",
    () => Results.Text(BusinessMetrics.GetPrometheusText(),
    "text/plain; version=0.0.4; charset=utf-8"));

app.MapPrometheusScrapingEndpoint();

app.Run();