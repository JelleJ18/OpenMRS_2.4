using CommunicationModule.Infrastructure.Data;
using CommunicationModule.Infrastructure.Services;
using CommunicationModule.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CommunicationModule.Api.Endpoints;

public static class OrganisationEndpoints
{
    public static IEndpointRouteBuilder MapOrganisationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organisations");

        group.MapGet("/", async (CommunicationDbContext db, CancellationToken ct) =>
        {
            var organisations = await db.Organisations
                .AsNoTracking()
                .Include(o => o.ProviderSubscriptions)
                .Include(o => o.OpenMRSInstances)
                .ToListAsync(ct);

            var response = organisations
                .Select(o => new OrganisationItem(
                    o.Id,
                    o.Name,
                    o.ProviderSubscriptions
                        .Select(p => new ProviderItem(p.Id, p.ProviderName, p.IsActive, p.CreatedAt))
                        .ToList(),
                    o.OpenMRSInstances
                        .Select(i => new OpenMRSInstanceItem(i.Id, i.DisplayName, i.BaseUrl, i.ApiVersion, i.IsActive, i.CreatedAt))
                        .ToList()))
                .ToList();

            return Results.Ok(response);
        });

        group.MapPost("/{organisationId:guid}/providers", async (
            Guid organisationId,
            ProviderSubscriptionRequest request,
            CommunicationDbContext db,
            AesEncryptionService encryption,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ProviderName))
                return Results.BadRequest("ProviderName is required.");

            if (string.IsNullOrWhiteSpace(request.ApiKey))
                return Results.BadRequest("ApiKey is required.");

            var organisationExists = await db.Organisations.AnyAsync(o => o.Id == organisationId, ct);
            if (!organisationExists)
                return Results.NotFound($"Organisation {organisationId} was not found.");

            var existing = await db.ProviderSubscriptions
                .FirstOrDefaultAsync(p => p.OrganisationId == organisationId && p.ProviderName == request.ProviderName, ct);

            if (existing is null)
            {
                existing = new ProviderSubscription
                {
                    Id = Guid.NewGuid(),
                    OrganisationId = organisationId,
                    ProviderName = request.ProviderName.Trim(),
                    EncryptedApiKey = encryption.Encrypt(request.ApiKey.Trim()),
                    IsActive = true
                };
                db.ProviderSubscriptions.Add(existing);
            }
            else
            {
                existing.EncryptedApiKey = encryption.Encrypt(request.ApiKey.Trim());
                existing.IsActive = true;
            }

            await db.SaveChangesAsync(ct);

            return Results.Ok(new ProviderItem(existing.Id, existing.ProviderName, existing.IsActive, existing.CreatedAt));
        });

        return app;
    }
}

record OrganisationItem(Guid Id, string Name, List<ProviderItem> Providers, List<OpenMRSInstanceItem> OpenMRSInstances);

record ProviderItem(Guid Id, string ProviderName, bool IsActive, DateTime CreatedAt);

record ProviderSubscriptionRequest(string ProviderName, string ApiKey);

record OpenMRSInstanceItem(Guid Id, string DisplayName, string BaseUrl, string ApiVersion, bool IsActive, DateTime CreatedAt);
