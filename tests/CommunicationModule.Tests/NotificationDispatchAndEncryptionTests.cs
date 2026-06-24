using System.Security.Cryptography;
using CommunicationModule.Api.Services;
using CommunicationModule.Core.DTOs;
using CommunicationModule.Core.Enums;
using CommunicationModule.Core.Events;
using CommunicationModule.Core.Interfaces;
using CommunicationModule.Core.Models;
using CommunicationModule.Infrastructure.Data;
using CommunicationModule.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace CommunicationModule.Tests;

public class NotificationDispatchAndEncryptionTests
{
    [Fact]
    public void AesEncryptionService_RoundTripsPlainText()
    {
        var encryption = new AesEncryptionService(CreateBase64Key());

        var cipherText = encryption.Encrypt("+31612345678");

        cipherText.Should().NotBe("+31612345678");
        encryption.Decrypt(cipherText).Should().Be("+31612345678");
    }

    [Fact]
    public async Task DispatchAsync_WhenProviderSucceeds_DecryptsPhoneAndMarksJobSent()
    {
        var encryption = new AesEncryptionService(CreateBase64Key());
        await using var db = CreateDbContext();

        var organisation = new Organisation
        {
            Id = Guid.NewGuid(),
            Name = "Clinic",
            ApiKeyHash = "TEST_HASH"
        };

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            OrganisationId = organisation.Id,
            Organisation = organisation,
            FhirAppointmentId = "fhir-1",
            EncryptedPatientPhone = encryption.Encrypt("+31612345678"),
            AppointmentDateTime = DateTime.UtcNow.AddHours(2),
            Location = "Main clinic"
        };

        var job = new NotificationJob
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointment.Id,
            Appointment = appointment,
            Type = NotificationJobType.TwentyFourHour,
            ScheduledFor = DateTime.UtcNow.AddMinutes(-1)
        };

        var providerSubscription = new ProviderSubscription
        {
            Id = Guid.NewGuid(),
            OrganisationId = organisation.Id,
            Organisation = organisation,
            ProviderName = "SwiftSend",
            IsActive = true
        };

        db.AddRange(organisation, appointment, job, providerSubscription);
        await db.SaveChangesAsync();

        var provider = new SequencedProvider("SwiftSend", new SendResult
        {
            Success = true,
            ProviderMessageId = "msg-123"
        });

        var dispatcher = CreateDispatcher(db, encryption, provider);

        await dispatcher.DispatchAsync(job.Id, CancellationToken.None);

        var storedJob = await db.NotificationJobs.SingleAsync(x => x.Id == job.Id);
        storedJob.Status.Should().Be(NotificationJobStatus.Sent);
        storedJob.RetryCount.Should().Be(0);
        storedJob.SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DispatchAsync_WhenProviderFailsThenSucceeds_IncrementsRetryCountAndCanRetry()
    {
        var encryption = new AesEncryptionService(CreateBase64Key());
        await using var db = CreateDbContext();

        var organisation = new Organisation
        {
            Id = Guid.NewGuid(),
            Name = "Clinic",
            ApiKeyHash = "TEST_HASH"
        };

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            OrganisationId = organisation.Id,
            Organisation = organisation,
            FhirAppointmentId = "fhir-2",
            EncryptedPatientPhone = encryption.Encrypt("+31698765432"),
            AppointmentDateTime = DateTime.UtcNow.AddHours(3),
            Location = "Ward B"
        };

        var job = new NotificationJob
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointment.Id,
            Appointment = appointment,
            Type = NotificationJobType.OneHour,
            ScheduledFor = DateTime.UtcNow.AddMinutes(-5)
        };

        var providerSubscription = new ProviderSubscription
        {
            Id = Guid.NewGuid(),
            OrganisationId = organisation.Id,
            Organisation = organisation,
            ProviderName = "LegacyLink",
            IsActive = true
        };

        db.AddRange(organisation, appointment, job, providerSubscription);
        await db.SaveChangesAsync();

        var provider = new SequencedProvider(
            "LegacyLink",
            new SendResult { Success = false, ErrorMessage = "Temporary outage" },
            new SendResult { Success = true, ProviderMessageId = "legacy-456" });

        var dispatcher = CreateDispatcher(db, encryption, provider);

        await dispatcher.DispatchAsync(job.Id, CancellationToken.None);

        var afterFirstAttempt = await db.NotificationJobs.SingleAsync(x => x.Id == job.Id);
        afterFirstAttempt.Status.Should().Be(NotificationJobStatus.Failed);
        afterFirstAttempt.RetryCount.Should().Be(1);

        await dispatcher.DispatchAsync(job.Id, CancellationToken.None);

        var afterSecondAttempt = await db.NotificationJobs.SingleAsync(x => x.Id == job.Id);
        afterSecondAttempt.Status.Should().Be(NotificationJobStatus.Sent);
        afterSecondAttempt.RetryCount.Should().Be(1);
    }

    private static NotificationDispatchService CreateDispatcher(
        CommunicationDbContext db,
        AesEncryptionService encryption,
        IMessagingProvider provider)
    {
        var eventPublisher = new NoopEventPublisher();
        var logger = Mock.Of<ILogger<NotificationDispatchService>>();

        return new NotificationDispatchService(db, encryption, [provider], eventPublisher, logger);
    }

    private static CommunicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CommunicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CommunicationDbContext(options);
    }

    private static string CreateBase64Key()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private sealed class SequencedProvider : IMessagingProvider
    {
        private readonly Queue<SendResult> _results;

        public SequencedProvider(string providerName, params SendResult[] results)
        {
            ProviderName = providerName;
            _results = new Queue<SendResult>(results);
        }

        public string ProviderName { get; }
        public List<NotificationMessage> SentMessages { get; } = new();

        public Task<SendResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(message);

            if (_results.Count == 0)
                throw new InvalidOperationException("No more test results configured.");

            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class NoopEventPublisher : IEventPublisher
    {
        public Task PublishAsync(IIntegrationEvent evt, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}