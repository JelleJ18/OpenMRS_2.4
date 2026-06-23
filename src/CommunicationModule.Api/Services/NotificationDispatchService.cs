using System;
using System.Collections.Generic;
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
        var job = await _db.NotificationJobs
            .Include(j => j.Appointment)
            .FirstOrDefaultAsync(j => j.Id == notificationJobId, ct);

        if (job is null)
        {
            _logger.LogWarning("NotificationJob {Id} not found.", notificationJobId);
            Telemetry.NotificationJobsFailed.Add(1);
            BusinessMetrics.IncrementNotificationJobsFailed();
            var missingJobDispatchDuration = Stopwatch.GetElapsedTime(dispatchStart).TotalSeconds;
            Telemetry.NotificationDispatchDuration.Record(missingJobDispatchDuration);
            BusinessMetrics.RecordNotificationDispatchDuration(missingJobDispatchDuration);
            return;
        }

        // Skip if the appointment has already started
        if (job.Appointment.AppointmentDateTime <= DateTime.UtcNow)
        {
            var old = job.Status;
            job.Status = NotificationJobStatus.Skipped;
            await WriteLogAsync(job, "system", false, null, "Appointment already started.", ct);
            await _db.SaveChangesAsync(ct);
            await _events.PublishAsync(new NotificationJobStatusChangedEvent(job.Id, job.AppointmentId, job.Appointment.OrganisationId, old, job.Status, DateTime.UtcNow, "Appointment already started"), ct);
            _logger.LogInformation("Job {Id} skipped — appointment already started.", notificationJobId);
            Telemetry.NotificationJobsSkipped.Add(1);
            BusinessMetrics.IncrementNotificationJobsSkipped();
            var skippedDispatchDuration = Stopwatch.GetElapsedTime(dispatchStart).TotalSeconds;
            Telemetry.NotificationDispatchDuration.Record(skippedDispatchDuration);
            BusinessMetrics.RecordNotificationDispatchDuration(skippedDispatchDuration);
            return;
        }

        var providerSubscription = await _db.ProviderSubscriptions
            .Where(p => p.OrganisationId == job.Appointment.OrganisationId)
            .OrderByDescending(p => p.IsActive)
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (providerSubscription is null)
        {
            var old = job.Status;
            job.Status = NotificationJobStatus.Failed;
            await WriteLogAsync(job, "unconfigured", false, null, "No messaging provider is configured for this organisation.", ct);
            await _db.SaveChangesAsync(ct);
            await _events.PublishAsync(new NotificationJobStatusChangedEvent(job.Id, job.AppointmentId, job.Appointment.OrganisationId, old, job.Status, DateTime.UtcNow, "No provider configured"), ct);
            _logger.LogWarning("Job {Id} failed — organisation {OrganisationId} has no provider configured.", notificationJobId, job.Appointment.OrganisationId);
            Telemetry.NotificationJobsFailed.Add(1);
            BusinessMetrics.IncrementNotificationJobsFailed();
            var unconfiguredDispatchDuration = Stopwatch.GetElapsedTime(dispatchStart).TotalSeconds;
            Telemetry.NotificationDispatchDuration.Record(unconfiguredDispatchDuration);
            BusinessMetrics.RecordNotificationDispatchDuration(unconfiguredDispatchDuration);
            return;
        }

        if (!providerSubscription.IsActive)
        {
            var old = job.Status;
            job.Status = NotificationJobStatus.Failed;
            await WriteLogAsync(job, providerSubscription.ProviderName, false, null, "The configured messaging provider is inactive.", ct);
            await _db.SaveChangesAsync(ct);
            await _events.PublishAsync(new NotificationJobStatusChangedEvent(job.Id, job.AppointmentId, job.Appointment.OrganisationId, old, job.Status, DateTime.UtcNow, "Provider inactive"), ct);
            _logger.LogWarning("Job {Id} failed — provider {ProviderName} is inactive for organisation {OrganisationId}.", notificationJobId, providerSubscription.ProviderName, job.Appointment.OrganisationId);
            Telemetry.NotificationJobsFailed.Add(1);
            BusinessMetrics.IncrementNotificationJobsFailed();
            var inactiveProviderDispatchDuration = Stopwatch.GetElapsedTime(dispatchStart).TotalSeconds;
            Telemetry.NotificationDispatchDuration.Record(inactiveProviderDispatchDuration);
            BusinessMetrics.RecordNotificationDispatchDuration(inactiveProviderDispatchDuration);
            return;
        }

        var provider = _providers.FirstOrDefault(p => string.Equals(p.ProviderName, providerSubscription.ProviderName, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            var old = job.Status;
            job.Status = NotificationJobStatus.Failed;
            await WriteLogAsync(job, providerSubscription.ProviderName, false, null, "No provider implementation registered.", ct);
            await _db.SaveChangesAsync(ct);
            await _events.PublishAsync(new NotificationJobStatusChangedEvent(job.Id, job.AppointmentId, job.Appointment.OrganisationId, old, job.Status, DateTime.UtcNow, "No provider implementation"), ct);
            _logger.LogError("Job {Id} failed — provider implementation {ProviderName} is not registered.", notificationJobId, providerSubscription.ProviderName);
            Telemetry.NotificationJobsFailed.Add(1);
            BusinessMetrics.IncrementNotificationJobsFailed();
            var missingProviderDispatchDuration = Stopwatch.GetElapsedTime(dispatchStart).TotalSeconds;
            Telemetry.NotificationDispatchDuration.Record(missingProviderDispatchDuration);
            BusinessMetrics.RecordNotificationDispatchDuration(missingProviderDispatchDuration);
            return;
        }

        string phoneNumber;
        try
        {
            phoneNumber = _encryption.Decrypt(job.Appointment.EncryptedPatientPhone);
        }
        catch (Exception ex)
        {
            var old = job.Status;
            job.Status = NotificationJobStatus.Failed;
            await WriteLogAsync(job, providerSubscription.ProviderName, false, null, $"Could not decrypt patient phone: {ex.Message}", ct);
            await _db.SaveChangesAsync(ct);
            await _events.PublishAsync(new NotificationJobStatusChangedEvent(job.Id, job.AppointmentId, job.Appointment.OrganisationId, old, job.Status, DateTime.UtcNow, "Decrypt failure"), ct);
            _logger.LogError(ex, "Job {Id} failed — could not decrypt patient phone.", notificationJobId);
            Telemetry.NotificationJobsFailed.Add(1);
            BusinessMetrics.IncrementNotificationJobsFailed();
            var decryptFailureDispatchDuration = Stopwatch.GetElapsedTime(dispatchStart).TotalSeconds;
            Telemetry.NotificationDispatchDuration.Record(decryptFailureDispatchDuration);
            BusinessMetrics.RecordNotificationDispatchDuration(decryptFailureDispatchDuration);
            return;
        }

        var message = new NotificationMessage
        {
            NotificationJobId = job.Id,
            PhoneNumber = phoneNumber,
            MessageBody = BuildMessage(job)
        };

        SendResult result;
        try
        {
            result = await provider.SendAsync(message, ct);
        }
        catch (Exception ex)
        {
            var old = job.Status;
            job.Status = NotificationJobStatus.Failed;
            job.RetryCount += 1;
            await WriteLogAsync(job, providerSubscription.ProviderName, false, null, ex.Message, ct);
            await _db.SaveChangesAsync(ct);
            await _events.PublishAsync(new NotificationJobStatusChangedEvent(job.Id, job.AppointmentId, job.Appointment.OrganisationId, old, job.Status, DateTime.UtcNow, ex.Message), ct);
            _logger.LogError(ex, "Job {Id} failed while sending through provider {ProviderName}.", notificationJobId, providerSubscription.ProviderName);
            Telemetry.NotificationJobsFailed.Add(1);
            BusinessMetrics.IncrementNotificationJobsFailed();
            var sendExceptionDispatchDuration = Stopwatch.GetElapsedTime(dispatchStart).TotalSeconds;
            Telemetry.NotificationDispatchDuration.Record(sendExceptionDispatchDuration);
            BusinessMetrics.RecordNotificationDispatchDuration(sendExceptionDispatchDuration);
            return;
        }

        if (!result.Success)
        {
            var old = job.Status;
            job.Status = NotificationJobStatus.Failed;
            job.RetryCount += 1;
            await WriteLogAsync(job, providerSubscription.ProviderName, false, result.ProviderMessageId, result.ErrorMessage ?? "Provider returned a failure.", ct);
            await _db.SaveChangesAsync(ct);
            await _events.PublishAsync(new NotificationJobStatusChangedEvent(job.Id, job.AppointmentId, job.Appointment.OrganisationId, old, job.Status, DateTime.UtcNow, result.ErrorMessage), ct);
            _logger.LogWarning("Job {Id} failed through provider {ProviderName}: {ErrorMessage}", notificationJobId, providerSubscription.ProviderName, result.ErrorMessage);
            Telemetry.NotificationJobsFailed.Add(1);
            BusinessMetrics.IncrementNotificationJobsFailed();
            var providerFailureDispatchDuration = Stopwatch.GetElapsedTime(dispatchStart).TotalSeconds;
            Telemetry.NotificationDispatchDuration.Record(providerFailureDispatchDuration);
            BusinessMetrics.RecordNotificationDispatchDuration(providerFailureDispatchDuration);
            return;
        }

        var previous = job.Status;
        job.Status = NotificationJobStatus.Sent;
        job.SentAt = DateTime.UtcNow;
        await WriteLogAsync(job, providerSubscription.ProviderName, true, result.ProviderMessageId, null, ct);
        await _db.SaveChangesAsync(ct);
        await _events.PublishAsync(new NotificationJobStatusChangedEvent(job.Id, job.AppointmentId, job.Appointment.OrganisationId, previous, job.Status, DateTime.UtcNow, null), ct);

        _logger.LogInformation("Job {Id} sent for appointment {AppointmentId} using provider {ProviderName}.", notificationJobId, job.AppointmentId, providerSubscription.ProviderName);
        Telemetry.NotificationJobsSent.Add(1);
        BusinessMetrics.IncrementNotificationJobsSent();
        var sentDispatchDuration = Stopwatch.GetElapsedTime(dispatchStart).TotalSeconds;
        Telemetry.NotificationDispatchDuration.Record(sentDispatchDuration);
        BusinessMetrics.RecordNotificationDispatchDuration(sentDispatchDuration);
    }

    private static string BuildMessage(NotificationJob job)
    {
        return $"Herinnering: uw afspraak staat gepland op {job.Appointment.AppointmentDateTime:yyyy-MM-dd HH:mm} UTC bij {job.Appointment.Location}.";
    }

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
        await _db.SaveChangesAsync(ct);

        // publish message logged event for other modules
        try
        {
            var msgEvent = new MessageLoggedEvent(log.Id, job.Id, job.Appointment.OrganisationId, providerName, success, errorMessage, log.LoggedAt);
            await _events.PublishAsync(msgEvent, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish MessageLoggedEvent");
        }
    }
}
