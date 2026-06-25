using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunicationModule.Core.DTOs;
using CommunicationModule.Core.Enums;
using CommunicationModule.Core.Events;
using CommunicationModule.Core.Interfaces;
using CommunicationModule.Core.Models;
using CommunicationModule.Infrastructure.Data;
using CommunicationModule.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace CommunicationModule.Api.Services;

public class NotificationDispatchService
{
    private readonly CommunicationDbContext _db;
    private readonly AesEncryptionService _encryption;
    private readonly IEnumerable<IMessagingProvider> _providers;
    private readonly IEventPublisher _events;
    private readonly ILogger<NotificationDispatchService> _logger;

    public NotificationDispatchService(
        CommunicationDbContext db,
        AesEncryptionService encryption,
        IEnumerable<IMessagingProvider> providers,
        IEventPublisher events,
        ILogger<NotificationDispatchService> logger)
    {
        _db = db;
        _encryption = encryption;
        _providers = providers;
        _events = events;
        _logger = logger;
    }

    public async Task DispatchAsync(Guid notificationJobId, CancellationToken ct)
    {
        var dispatchStart = Stopwatch.GetTimestamp();

        _logger.LogInformation("Starting dispatch for job {JobId}", notificationJobId);

        var job = await _db.NotificationJobs
            .Include(j => j.Appointment)
            .FirstOrDefaultAsync(j => j.Id == notificationJobId, ct);

        if (job is null)
        {
            _logger.LogWarning("NotificationJob {Id} not found.", notificationJobId);
            return;
        }

        _logger.LogInformation("Job loaded. Status={Status}", job.Status);

        // skip check
        if (job.Appointment.AppointmentDateTime <= DateTime.UtcNow)
        {
            var old = job.Status;
            job.Status = NotificationJobStatus.Skipped;

            await WriteLogAsync(job, "system", false, null, "Appointment already started", ct);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Job skipped (appointment already started)");

            return;
        }

        var providerSubscription = await _db.ProviderSubscriptions
            .Where(p => p.OrganisationId == job.Appointment.OrganisationId)
            .OrderByDescending(p => p.IsActive)
            .FirstOrDefaultAsync(ct);

        if (providerSubscription is null)
        {
            job.Status = NotificationJobStatus.Failed;
            await WriteLogAsync(job, "unconfigured", false, null, "No provider configured", ct);
            await _db.SaveChangesAsync(ct);
            return;
        }

        if (!providerSubscription.IsActive)
        {
            job.Status = NotificationJobStatus.Failed;
            await WriteLogAsync(job, providerSubscription.ProviderName, false, null, "Provider inactive", ct);
            await _db.SaveChangesAsync(ct);
            return;
        }

        var provider = _providers.FirstOrDefault(p =>
            p.ProviderName.Equals(providerSubscription.ProviderName, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            job.Status = NotificationJobStatus.Failed;
            await WriteLogAsync(job, providerSubscription.ProviderName, false, null, "Provider not registered", ct);
            await _db.SaveChangesAsync(ct);
            return;
        }

        string phoneNumber;
        try
        {
            phoneNumber = _encryption.Decrypt(job.Appointment.EncryptedPatientPhone);
        }
        catch (Exception ex)
        {
            job.Status = NotificationJobStatus.Failed;
            await WriteLogAsync(job, providerSubscription.ProviderName, false, null, ex.Message, ct);
            await _db.SaveChangesAsync(ct);
            return;
        }

        var message = new NotificationMessage
        {
            NotificationJobId = job.Id,
            PhoneNumber = phoneNumber,
            MessageBody = BuildMessage(job)
        };

        var result = await provider.SendAsync(message, ct);

        if (!result.Success)
        {
            job.Status = NotificationJobStatus.Failed;
            job.RetryCount++;

            await WriteLogAsync(job, providerSubscription.ProviderName, false, result.ProviderMessageId, result.ErrorMessage, ct);
            await _db.SaveChangesAsync(ct);

            return;
        }

        job.Status = NotificationJobStatus.Sent;
        job.SentAt = DateTime.UtcNow;

        await WriteLogAsync(job, providerSubscription.ProviderName, true, result.ProviderMessageId, null, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Job successfully sent");
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
        var log = new MessageLog
        {
            Id = Guid.NewGuid(),
            NotificationJobId = job.Id,
            OrganisationId = job.Appointment.OrganisationId,
            ProviderName = providerName,
            Success = success,
            ProviderMessageId = providerMessageId,
            ErrorMessage = errorMessage,
            LoggedAt = DateTime.UtcNow
        };

        _db.MessageLogs.Add(log);

        // IMPORTANT: één SaveChanges per flow (niet per log)
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "MessageLog written: Job={JobId}, Success={Success}",
            job.Id, success);
    }
}