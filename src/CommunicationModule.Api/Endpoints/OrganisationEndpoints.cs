using CommunicationModule.Infrastructure.Data;
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
                .Include(o => o.ProviderSubscriptions)
                .Select(o => new OrganisationItem(
                    o.Id,
                    o.Name,
                    o.ProviderSubscriptions
                        .Select(p => new ProviderItem(p.Id, p.ProviderName, p.IsActive, p.CreatedAt))
                        .ToList()
                ))
                .ToListAsync(ct);

            return Results.Ok(organisations);
        });

        return app;
    }
}

record OrganisationItem(Guid Id, string Name, List<ProviderItem> Providers);

record ProviderItem(Guid Id, string ProviderName, bool IsActive, DateTime CreatedAt);
