using CommunicationModule.Core.Enums;
using CommunicationModule.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CommunicationModule.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard");

        group.MapGet("/stats", async (HttpRequest request, CommunicationDbContext db, CancellationToken ct) =>
        {
            TryGetOrganisationId(request, out var organisationId);

            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            var todayLogs = await db.MessageLogs
                .Where(l => l.OrganisationId == organisationId)
                .Where(l => l.LoggedAt >= today && l.LoggedAt < tomorrow)
                .ToListAsync(ct);

            var pendingJobs = await db.NotificationJobs
                .Where(j => j.Appointment.OrganisationId == organisationId)
                .CountAsync(j => j.Status == NotificationJobStatus.Pending, ct);

            var recentErrors = await db.MessageLogs
                .Where(l => l.OrganisationId == organisationId)
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

        group.MapGet("/metrics", async (HttpRequest request, CommunicationDbContext db, int windowMinutes = 60, CancellationToken ct = default) =>
        {
            if (!TryGetOrganisationId(request, out var organisationId))
            {
                return Results.BadRequest("X-Organisation-Id header is required and must be a valid GUID.");
            }

            if (windowMinutes <= 0) windowMinutes = 60;

            var since = DateTime.UtcNow.AddMinutes(-windowMinutes);

            var entries = await db.MessageLogs
                .Where(l => l.OrganisationId == organisationId && l.LoggedAt >= since)
                .ToListAsync(ct);

            var totalSent = entries.Count(e => e.Success);
            var totalFailed = entries.Count(e => !e.Success);
            var throughputPerMinute = windowMinutes > 0 ? (double)totalSent / windowMinutes : 0.0;
            var errorRate = (totalSent + totalFailed) == 0 ? 0.0 : (double)totalFailed / (totalSent + totalFailed) * 100.0;

            return Results.Ok(new MetricsResult(
                ThroughputPerMinute: Math.Round(throughputPerMinute, 2),
                ErrorRatePercent: Math.Round(errorRate, 2),
                WindowMinutes: windowMinutes,
                TotalSent: totalSent,
                TotalFailed: totalFailed
            ));
        });

        group.MapGet("/live", async (HttpRequest request, CommunicationDbContext db, int jobLimit = 100, int errorLimit = 50, CancellationToken ct = default) =>
        {
            if (!TryGetOrganisationId(request, out var organisationId))
            {
                return Results.BadRequest("X-Organisation-Id header is required and must be a valid GUID.");
            }

            if (jobLimit <= 0 || jobLimit > 1000) jobLimit = 100;
            if (errorLimit <= 0 || errorLimit > 500) errorLimit = 50;

            var jobs = await db.NotificationJobs
                .Where(j => j.Appointment.OrganisationId == organisationId)
                .Include(j => j.Appointment)
                .OrderBy(j => j.ScheduledFor)
                .Take(jobLimit)
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

            var recentErrors = await db.MessageLogs
                .Where(l => l.OrganisationId == organisationId && !l.Success)
                .OrderByDescending(l => l.LoggedAt)
                .Take(errorLimit)
                .Select(l => new ErrorSummary(l.Id, l.ProviderName, l.ErrorMessage, l.LoggedAt))
                .ToListAsync(ct);

            return Results.Ok(new LiveResult(jobs, recentErrors));
        });

        group.MapGet("/logs", async (
            HttpRequest request,
            CommunicationDbContext db,
            int page = 1,
            int pageSize = 20,
            bool? success = null,
            string? provider = null,
            CancellationToken ct = default) =>
        {
            if (!TryGetOrganisationId(request, out var organisationId))
            {
                return Results.BadRequest("X-Organisation-Id header is required and must be a valid GUID.");
            }

            if (pageSize > 100) pageSize = 100;

            var query = db.MessageLogs.Where(l => l.OrganisationId == organisationId);

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
            HttpRequest request,
            CommunicationDbContext db,
            NotificationJobStatus? status = null,
            CancellationToken ct = default) =>
        {
            if (!TryGetOrganisationId(request, out var organisationId))
            {
                return Results.BadRequest("X-Organisation-Id header is required and must be a valid GUID.");
            }

            var query = db.NotificationJobs
                .Where(j => j.Appointment.OrganisationId == organisationId);

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

    private static bool TryGetOrganisationId(HttpRequest request, out Guid organisationId)
    {
        
        // 1. Probeer header (zoals nu)
        if (request.Headers.TryGetValue("X-Organisation-Id", out var orgHeader)
            && Guid.TryParse(orgHeader, out organisationId))
        {
            return true;
        }

        // 2. Fallback (DEV ONLY)
        organisationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        return true;

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

record MetricsResult(double ThroughputPerMinute, double ErrorRatePercent, int WindowMinutes, int TotalSent, int TotalFailed);

record LiveResult(List<NotificationJobItem> Jobs, List<ErrorSummary> RecentErrors);

record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);
