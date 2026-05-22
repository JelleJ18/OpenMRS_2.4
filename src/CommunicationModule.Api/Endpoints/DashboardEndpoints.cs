using CommunicationModule.Core.Enums;
using CommunicationModule.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CommunicationModule.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard");

        group.MapGet("/stats", async (CommunicationDbContext db, CancellationToken ct) =>
        {
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            var todayLogs = await db.MessageLogs
                .Where(l => l.LoggedAt >= today && l.LoggedAt < tomorrow)
                .ToListAsync(ct);

            var pendingJobs = await db.NotificationJobs
                .CountAsync(j => j.Status == NotificationJobStatus.Pending, ct);

            var recentErrors = await db.MessageLogs
                .Where(l => !l.Success)
                .OrderByDescending(l => l.LoggedAt)
                .Take(10)
                .Select(l => new ErrorSummary(l.Id, l.ProviderName, l.ErrorMessage, l.LoggedAt))
                .ToListAsync(ct);

            return Results.Ok(new DashboardStats(
                TotalSentToday: todayLogs.Count(l => l.Success),
                TotalFailedToday: todayLogs.Count(l => !l.Success),
                TotalPendingJobs: pendingJobs,
                RecentErrors: recentErrors
            ));
        });

        group.MapGet("/logs", async (
            CommunicationDbContext db,
            int page = 1,
            int pageSize = 20,
            bool? success = null,
            string? provider = null,
            CancellationToken ct = default) =>
        {
            if (pageSize > 100) pageSize = 100;

            var query = db.MessageLogs.AsQueryable();

            if (success.HasValue)
                query = query.Where(l => l.Success == success.Value);

            if (!string.IsNullOrEmpty(provider))
                query = query.Where(l => l.ProviderName == provider);

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(l => l.LoggedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(l => new MessageLogItem(
                    l.Id,
                    l.NotificationJobId,
                    l.OrganisationId,
                    l.ProviderName,
                    l.Success,
                    l.ErrorMessage,
                    l.LoggedAt
                ))
                .ToListAsync(ct);

            return Results.Ok(new PagedResult<MessageLogItem>(items, total, page, pageSize));
        });

        group.MapGet("/jobs", async (
            CommunicationDbContext db,
            NotificationJobStatus? status = null,
            CancellationToken ct = default) =>
        {
            var query = db.NotificationJobs.AsQueryable();

            if (status.HasValue)
                query = query.Where(j => j.Status == status.Value);

            var jobs = await query
                .OrderBy(j => j.ScheduledFor)
                .Take(100)
                .Select(j => new NotificationJobItem(
                    j.Id,
                    j.AppointmentId,
                    j.Type,
                    j.Status,
                    j.ScheduledFor,
                    j.RetryCount,
                    j.SentAt
                ))
                .ToListAsync(ct);

            return Results.Ok(jobs);
        });

        return app;
    }
}

record DashboardStats(
    int TotalSentToday,
    int TotalFailedToday,
    int TotalPendingJobs,
    List<ErrorSummary> RecentErrors);

record ErrorSummary(Guid LogId, string ProviderName, string? ErrorMessage, DateTime LoggedAt);

record MessageLogItem(
    Guid Id,
    Guid NotificationJobId,
    Guid OrganisationId,
    string ProviderName,
    bool Success,
    string? ErrorMessage,
    DateTime LoggedAt);

record NotificationJobItem(
    Guid Id,
    Guid AppointmentId,
    NotificationJobType Type,
    NotificationJobStatus Status,
    DateTime ScheduledFor,
    int RetryCount,
    DateTime? SentAt);

record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);
