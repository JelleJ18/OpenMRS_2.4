using CommunicationModule.Infrastructure.Data;
using CommunicationModule.Infrastructure.Services;
using CommunicationModule.Core.Models;
using Microsoft.EntityFrameworkCore;
using CommunicationModule.Api.Services;

namespace CommunicationModule.Api.Endpoints;

public static class OrganisationEndpoints
{
    public static IEndpointRouteBuilder MapOrganisationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organisations");

        // 🔐 GET: only current organisation (NOT all orgs anymore)
        group.MapGet("/", async (HttpContext ctx, CommunicationDbContext db, CancellationToken ct) =>
        {
            var organisation = ctx.Items["Organisation"] as Organisation;

            if (organisation is null)
                return Results.Unauthorized();

            var org = await db.Organisations
                .AsNoTracking()
                .Include(o => o.ProviderSubscriptions)
                .Include(o => o.OpenMRSInstances)
                .FirstAsync(o => o.Id == organisation.Id, ct);

            var response = new OrganisationItem(
                org.Id,
                org.Name,
                org.ProviderSubscriptions
                    .Select(p => new ProviderItem(p.Id, p.ProviderName, p.IsActive, p.CreatedAt))
                    .ToList(),
                org.OpenMRSInstances
                    .Select(i => new OpenMRSInstanceItem(i.Id, i.DisplayName, i.BaseUrl, i.ApiVersion, i.IsActive, i.CreatedAt))
                    .ToList()
            );

            return Results.Ok(response);
        });

        // 🔧 Providers (unchanged, already correct multi-tenant style)
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
                return Results.NotFound();

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

        // 🔐 CREATE organisation (ONLY endpoint without API key)
        group.MapPost("/", async (
            CreateOrganisationRequest request,
            CommunicationDbContext db,
            TenantAccessService accessService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest("Name is required.");

            var apiKey = accessService.CreateAccessKey();

            var organisation = new Organisation
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                ApiKeyHash = apiKey.KeyHash
            };

            db.Organisations.Add(organisation);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/organisations/{organisation.Id}", new
            {
                organisation.Id,
                organisation.Name,
                ApiKey = apiKey.PlainTextKey
            });
        });

        return app;
    }
}

record OrganisationItem(Guid Id, string Name, List<ProviderItem> Providers, List<OpenMRSInstanceItem> OpenMRSInstances);

record ProviderItem(Guid Id, string ProviderName, bool IsActive, DateTime CreatedAt);

record ProviderSubscriptionRequest(string ProviderName, string ApiKey);

record OpenMRSInstanceItem(Guid Id, string DisplayName, string BaseUrl, string ApiVersion, bool IsActive, DateTime CreatedAt);

record CreateOrganisationRequest(string Name);