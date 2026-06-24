using CommunicationModule.Api.Services;
using CommunicationModule.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CommunicationModule.Api.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        CommunicationDbContext db,
        TenantAccessService accessService)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";
        var method = context.Request.Method;

        if (path == "/api/organisations" && method == HttpMethods.Post)
        {
            await _next(context);
            return;
        }

        if (path.StartsWith("/swagger"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-API-KEY", out var apiKey))
        {

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("API key missing");
            return;
        }

        var key = apiKey.ToString().Trim();

        var hash = accessService.HashKey(key);

        var organisation = await db.Organisations
            .FirstOrDefaultAsync(o => o.ApiKeyHash == hash);

        if (organisation is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid API key");
            return;
        }

        context.Items["Organisation"] = organisation;

        await _next(context);
    }
}