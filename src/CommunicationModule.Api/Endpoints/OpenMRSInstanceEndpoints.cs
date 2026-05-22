using CommunicationModule.Core.Models;
using CommunicationModule.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using CommunicationModule.Api.Services;

namespace CommunicationModule.Api.Endpoints;

public static class OpenMRSInstanceEndpoints
{
    public static IEndpointRouteBuilder MapOpenMRSInstanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organisations/{organisationId:guid}/openmrs-instances");

        group.MapGet("/", async (Guid organisationId, CommunicationDbContext db, CancellationToken ct) =>
        {
            var organisationExists = await db.Organisations.AnyAsync(o => o.Id == organisationId, ct);
            if (!organisationExists)
            {
                return Results.NotFound();
            }

            var instances = await db.OpenMRSInstances
                .Where(i => i.OrganisationId == organisationId)
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => new OpenMRSInstanceResponse(
                    i.Id,
                    i.DisplayName,
                    i.BaseUrl,
                    i.ApiVersion,
                    i.IsActive,
                    i.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(instances);
        });

        group.MapPost("/", async (
            Guid organisationId,
            OpenMRSInstanceCreateRequest request,
            CommunicationDbContext db,
            TenantAccessService accessService,
            CancellationToken ct) =>
        {
            var organisationExists = await db.Organisations.AnyAsync(o => o.Id == organisationId, ct);
            if (!organisationExists)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return Results.BadRequest("DisplayName is required.");
            }

            if (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                return Results.BadRequest("BaseUrl must be a valid absolute URL.");
            }

            var normalizedBaseUrl = baseUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            var accessKey = accessService.CreateAccessKey();

            var existingInstance = await db.OpenMRSInstances.AnyAsync(
                i => i.OrganisationId == organisationId && i.BaseUrl == normalizedBaseUrl,
                ct);

            if (existingInstance)
            {
                return Results.Conflict("An OpenMRS instance with the same BaseUrl already exists for this organisation.");
            }

            var instance = new OpenMRSInstance
            {
                Id = Guid.NewGuid(),
                OrganisationId = organisationId,
                DisplayName = request.DisplayName.Trim(),
                BaseUrl = normalizedBaseUrl,
                ApiVersion = string.IsNullOrWhiteSpace(request.ApiVersion) ? "2.7" : request.ApiVersion.Trim(),
                AccessKeyHash = accessKey.KeyHash,
                IsActive = request.IsActive
            };

            db.OpenMRSInstances.Add(instance);
            await db.SaveChangesAsync(ct);

            var createResponse = new OpenMRSInstanceCreateResponse(
                instance.Id,
                instance.DisplayName,
                instance.BaseUrl,
                instance.ApiVersion,
                instance.IsActive,
                accessKey.PlainTextKey,
                instance.CreatedAt);

            return Results.Created($"/api/organisations/{organisationId}/openmrs-instances/{instance.Id}", createResponse);
        });

        group.MapPost("/{instanceId:guid}/rotate-key", async (
            Guid organisationId,
            Guid instanceId,
            CommunicationDbContext db,
            TenantAccessService accessService,
            CancellationToken ct) =>
        {
            var instance = await db.OpenMRSInstances
                .FirstOrDefaultAsync(i => i.Id == instanceId && i.OrganisationId == organisationId, ct);

            if (instance is null)
            {
                return Results.NotFound();
            }

            if (!instance.IsActive)
            {
                return Results.BadRequest("This OpenMRS instance is revoked and cannot rotate its key.");
            }

            var accessKey = accessService.CreateAccessKey();
            instance.AccessKeyHash = accessKey.KeyHash;

            await db.SaveChangesAsync(ct);

            return Results.Ok(new OpenMRSInstanceRotateKeyResponse(
                instance.Id,
                accessKey.PlainTextKey,
                instance.CreatedAt));
        });

        group.MapPost("/{instanceId:guid}/revoke", async (
            Guid organisationId,
            Guid instanceId,
            CommunicationDbContext db,
            CancellationToken ct) =>
        {
            var instance = await db.OpenMRSInstances
                .FirstOrDefaultAsync(i => i.Id == instanceId && i.OrganisationId == organisationId, ct);

            if (instance is null)
            {
                return Results.NotFound();
            }

            instance.IsActive = false;
            instance.AccessKeyHash = string.Empty;

            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        return app;
    }
}

public sealed record OpenMRSInstanceCreateRequest(string DisplayName, string BaseUrl, string? ApiVersion, bool IsActive = true);

public sealed record OpenMRSInstanceResponse(Guid Id, string DisplayName, string BaseUrl, string ApiVersion, bool IsActive, DateTime CreatedAt);

public sealed record OpenMRSInstanceCreateResponse(Guid Id, string DisplayName, string BaseUrl, string ApiVersion, bool IsActive, string AccessKey, DateTime CreatedAt);

public sealed record OpenMRSInstanceRotateKeyResponse(Guid Id, string AccessKey, DateTime CreatedAt);