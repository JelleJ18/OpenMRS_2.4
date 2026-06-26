using CommunicationModule.Api.Services;
using CommunicationModule.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace CommunicationModule.Api.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _environment;

    public ApiKeyMiddleware(RequestDelegate next, IHostEnvironment environment)
    {
        _next = next;
        _environment = environment;
    }

    public async Task InvokeAsync(
        HttpContext context,
        CommunicationDbContext db,
        TenantAccessService accessService)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        var method = context.Request.Method;

        if (path == "/api/organisations" && method == HttpMethods.Post)
        {
            await _next(context);
            return;
        }

        if (_environment.IsDevelopment() && (path.StartsWith("/swagger") || path.StartsWith("/openapi")))
        {
            await _next(context);
            return;
        }

        if (IsMetricsEndpoint(path) && IsInternalAddress(context.Connection.RemoteIpAddress))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-API-KEY", out var apiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        var key = apiKey.ToString().Trim();

        var hash = accessService.HashKey(key);

        var organisation = await db.Organisations
            .FirstOrDefaultAsync(o => o.ApiKeyHash == hash);

        if (organisation is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        context.Items["Organisation"] = organisation;

        await _next(context);
    }

    private static bool IsMetricsEndpoint(string path)
        => path == "/metrics" || path == "/metrics/business";

    private static bool IsInternalAddress(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        return address.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => IsPrivateIPv4(address),
            System.Net.Sockets.AddressFamily.InterNetworkV6 => IsPrivateIPv6(address),
            _ => false
        };
    }

    private static bool IsPrivateIPv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();

        return bytes[0] switch
        {
            10 => true,
            172 => bytes[1] >= 16 && bytes[1] <= 31,
            192 => bytes[1] == 168,
            _ => false
        };
    }

    private static bool IsPrivateIPv6(IPAddress address)
    {
        var bytes = address.GetAddressBytes();

        return bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80;
    }
}