using CommunicationModule.Core.Models;
using CommunicationModule.Infrastructure.Data;
using CommunicationModule.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace CommunicationModule.Api.Endpoints;

public static class ProviderEndpoints
{
    private static readonly HashSet<string> AllowedProviders = new()
    {
        "SwiftSend",
        "LegacyLink",
        "AsyncFlow",
        "SecurePost"
    };

    public static IEndpointRouteBuilder MapProviderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/organisations/{orgId}/provider", async (
            Guid orgId,
            ProviderRequest request,
            CommunicationDbContext db,
            AesEncryptionService encryption,
            CancellationToken ct) =>
        {
            var orgExists = await db.Organisations.AnyAsync(o => o.Id == orgId, ct);
            if (!orgExists)
                return Results.NotFound("Organisation not found");

            if (!AllowedProviders.Contains(request.ProviderName))
            {
                return Results.BadRequest(new
                {
                    error = "Invalid provider name",
                    allowedProviders = AllowedProviders
                });
            }

            var existing = await db.ProviderSubscriptions
                .Where(p => p.OrganisationId == orgId)
                .ToListAsync(ct);

            db.ProviderSubscriptions.RemoveRange(existing);

            var subscription = new ProviderSubscription
            {
                Id = Guid.NewGuid(),
                OrganisationId = orgId,
                ProviderName = request.ProviderName,
                EncryptedApiKey = encryption.Encrypt(request.ApiKey),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            db.ProviderSubscriptions.Add(subscription);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                message = "Provider successfully linked",
                orgId,
                provider = request.ProviderName
            });
        });

        app.MapGet("/organisations/{orgId}/provider", async (
            Guid orgId,
            CommunicationModule.Infrastructure.Data.CommunicationDbContext db,
            CancellationToken ct) =>
        {
            var provider = await db.ProviderSubscriptions
                .Where(p => p.OrganisationId == orgId && p.IsActive)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (provider is null)
                return Results.NotFound("No provider configured");

            return Results.Ok(new
            {
                provider.ProviderName,
                provider.IsActive,
                provider.CreatedAt
            });
        });

        return app;
    }

    public record ProviderRequest(string ProviderName, string ApiKey);
}