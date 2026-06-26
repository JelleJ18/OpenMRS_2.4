using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunicationModule.Core.DTOs;
using CommunicationModule.Core.Enums;
using CommunicationModule.Core.Interfaces;
using CommunicationModule.Core.Models;
using CommunicationModule.Infrastructure.Data;
using CommunicationModule.Infrastructure.Services;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CommunicationModule.Api.Services;

public class NotificationDispatchService
{
    private readonly CommunicationDbContext _db;
    private readonly AesEncryptionService _encryption;
    private readonly IEnumerable<IMessagingProvider> _providers;
    private readonly ILogger<NotificationDispatchService> _logger;
    private readonly IBackgroundJobClient _backgroundJobs;

    public NotificationDispatchService(
        CommunicationDbContext db,
        AesEncryptionService encryption,
        IEnumerable<IMessagingProvider> providers,
        IBackgroundJobClient backgroundJobs,
        ILogger<NotificationDispatchService> logger)
    {
        _db = db;
        _encryption = encryption;
        _providers = providers;
        _backgroundJobs = backgroundJobs;
        _logger = logger;
    }

    public async Task DispatchAsync(Guid notificationJobId, CancellationToken ct)
    {
        _logger.LogInformation("Dispatch started for Job {JobId}", notificationJobId);

        var job = await _db.NotificationJobs
            .Include(j => j.Appointment)
            .FirstOrDefaultAsync(j => j.Id == notificationJobId, ct);

        if (job is null)
        {
            _logger.LogWarning("Job not found {JobId}", notificationJobId);
            return;
        }

        if (job.Status == NotificationJobStatus.Sent ||
            job.Status == NotificationJobStatus.Cancelled)
            return;

        try
        {
            job.Status = NotificationJobStatus.Pending;
            await _db.SaveChangesAsync(ct);

            var provider = _providers.FirstOrDefault(p =>
                string.Equals(p.ProviderName, "SwiftSend", StringComparison.OrdinalIgnoreCase));

            var phoneNumber = _encryption.Decrypt(job.Appointment.EncryptedPatientPhone);

            var message = new NotificationMessage
            {
                NotificationJobId = job.Id,
                PhoneNumber = phoneNumber,
                MessageBody = BuildMessage(job)
            };

            var result = await provider.SendAsync(message, ct);

            // =========================
            // SUCCESS
            // =========================
            if (result.Success)
            {
                job.Status = NotificationJobStatus.Sent;
                job.SentAt = DateTime.UtcNow;

                await WriteLogAsync(job, provider.ProviderName, true,
                    result.ProviderMessageId, null, ct);

                await _db.SaveChangesAsync(ct);

                _logger.LogInformation("Job sent {JobId}", job.Id);
                return;
            }

            // =========================
            // FAILURE → RETRY FLOW
            // =========================
            await HandleRetry(job, result.ErrorMessage ?? "Send failed", ct);
        }
        catch (Exception ex)
        {
            await HandleRetry(job, ex.Message, ct);
            _logger.LogError(ex, "Unexpected error {JobId}", job.Id);
        }
    }

    private async Task HandleRetry(NotificationJob job, string error, CancellationToken ct)
    {
        job.RetryCount++;

        await WriteLogAsync(job, "system", false, null, error, ct);

        if (job.RetryCount >= 3)
        {
            job.Status = NotificationJobStatus.Failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogWarning("Job failed permanently {JobId}", job.Id);
            return;
        }

        var delayMinutes = Math.Pow(2, job.RetryCount);

        job.Status = NotificationJobStatus.Pending;
        job.ScheduledFor = DateTime.UtcNow.AddMinutes(delayMinutes);

        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Retry scheduled in {Minutes} min (attempt {Retry}) Job {JobId}",
            delayMinutes,
            job.RetryCount,
            job.Id);

        // 🔥 THIS IS THE MISSING PIECE
        _backgroundJobs.Schedule(
            () => DispatchAsync(job.Id, CancellationToken.None),
            TimeSpan.FromMinutes(delayMinutes));
    }

    private static string BuildMessage(NotificationJob job)
        => $"Herinnering: afspraak op {job.Appointment.AppointmentDateTime:yyyy-MM-dd HH:mm} bij {job.Appointment.Location}.";

    private async Task WriteLogAsync(
        NotificationJob job,
        string providerName,
        bool success,
        string? providerMessageId,
        string? errorMessage,
        CancellationToken ct)
    {
        _db.MessageLogs.Add(new MessageLog
        {
            Id = Guid.NewGuid(),
            NotificationJobId = job.Id,
            OrganisationId = job.Appointment.OrganisationId,
            ProviderName = providerName,
            Success = success,
            ProviderMessageId = providerMessageId,
            ErrorMessage = errorMessage,
            LoggedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }
}