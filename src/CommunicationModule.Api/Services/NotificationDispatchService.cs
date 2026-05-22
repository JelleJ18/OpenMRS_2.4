using CommunicationModule.Core.Enums;
using CommunicationModule.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CommunicationModule.Api.Services;

// Stub — real implementation wired up once messaging providers branch is merged.
public class NotificationDispatchService
{
    private readonly CommunicationDbContext _db;
    private readonly ILogger<NotificationDispatchService> _logger;

    public NotificationDispatchService(CommunicationDbContext db, ILogger<NotificationDispatchService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task DispatchAsync(Guid notificationJobId, CancellationToken ct)
    {
        var job = await _db.NotificationJobs
            .Include(j => j.Appointment)
            .FirstOrDefaultAsync(j => j.Id == notificationJobId, ct);

        if (job is null)
        {
            _logger.LogWarning("NotificationJob {Id} not found.", notificationJobId);
            return;
        }

        // Skip if the appointment has already started
        if (job.Appointment.AppointmentDateTime <= DateTime.UtcNow)
        {
            job.Status = NotificationJobStatus.Skipped;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Job {Id} skipped — appointment already started.", notificationJobId);
            return;
        }

        // TODO: call IMessagingProvider.SendAsync() once providers are implemented
        _logger.LogInformation(
            "Job {Id} fired for appointment {AppointmentId} — no provider connected yet.",
            notificationJobId, job.AppointmentId);
    }
}
