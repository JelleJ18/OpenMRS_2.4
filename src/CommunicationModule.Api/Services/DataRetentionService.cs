using CommunicationModule.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using CommunicationModule.Core.Enums;

namespace CommunicationModule.Api.Services;

public class DataRetentionService
{
    private readonly CommunicationDbContext _db;

    public DataRetentionService(CommunicationDbContext db)
    {
        _db = db;
    }

    public async Task CleanupAsync()
    {
        await RemovePatientDataAsync();
        await RemoveOldLogsAsync();
    }

    private async Task RemovePatientDataAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-14);

        var appointments = await _db.Appointments
            .Where(a =>
                a.NotificationJobs.Any(j =>
                    j.Status == NotificationJobStatus.Sent &&
                    j.SentAt <= cutoff))
            .ToListAsync();

        foreach (var appointment in appointments)
        {
            appointment.EncryptedPatientPhone = null;
            appointment.FhirAppointmentId = null;
        }

        await _db.SaveChangesAsync();
    }

    private async Task RemoveOldLogsAsync()
    {
        var cutoff = DateTime.UtcNow.AddYears(-1);

        var logs = await _db.MessageLogs
            .Where(x => x.LoggedAt <= cutoff)
            .ToListAsync();

        _db.MessageLogs.RemoveRange(logs);

        await _db.SaveChangesAsync();
    }
}