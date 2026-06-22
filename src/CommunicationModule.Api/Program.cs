using CommunicationModule.Api.Endpoints;
using CommunicationModule.Api.Services;
using CommunicationModule.Infrastructure.Data;
using CommunicationModule.Infrastructure.Services;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<DataRetentionService>();

builder.Services.AddHostedService<DataRetentionBackgroundService>();

builder.Configuration.AddUserSecrets<Program>(optional: true);

var encryptionKey = builder.Configuration["Crypto:Key"];
if (string.IsNullOrWhiteSpace(encryptionKey))
{
    throw new InvalidOperationException("Missing required user secret 'Crypto:Key'.");
}

builder.Services.AddOpenApi();
builder.Services.AddSingleton(new AesEncryptionService(encryptionKey));
builder.Services.AddSingleton<TenantAccessService>();

builder.Services.AddHangfire(config => config.UseInMemoryStorage());
builder.Services.AddHangfireServer();
builder.Services.AddScoped<AppointmentIngestionService>();
builder.Services.AddScoped<NotificationDispatchService>();

builder.Services.AddScoped<CommunicationModule.Core.Interfaces.IEventPublisher, CommunicationModule.Infrastructure.Services.EventPublisher>();

var conn = DatabaseConnectionResolver.ResolveConnectionString(builder.Configuration);
var serverVersion = DatabaseConnectionResolver.GetServerVersion();
builder.Services.AddDbContext<CommunicationDbContext>(opts =>
    opts.UseMySql(conn, serverVersion, mySqlOptions =>
        mySqlOptions.MigrationsHistoryTable("__efmigrationshistory")));

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/db-check", async (CommunicationDbContext db, CancellationToken cancellationToken) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync(cancellationToken);
        var pendingMigrations = canConnect
            ? await db.Database.GetPendingMigrationsAsync(cancellationToken)
            : Array.Empty<string>();

        return Results.Ok(new { canConnect, pendingMigrations = pendingMigrations.ToArray() });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Database check failed",
            detail: ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapDashboardEndpoints();
app.MapOrganisationEndpoints();
app.MapOpenMRSInstanceEndpoints();
app.MapFhirEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire");
}

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
