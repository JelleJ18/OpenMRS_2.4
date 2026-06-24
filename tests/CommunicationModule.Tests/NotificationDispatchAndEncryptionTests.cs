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
    // Test: controleert dat AesEncryptionService een plaintext kan versleutelen
    // en daarna correct kan ontsleutelen (roundtrip).
    [Fact]
    public void AesEncryptionService_RoundTripsPlainText()
    {
        var encryption = new AesEncryptionService(CreateBase64Key());

        var cipherText = encryption.Encrypt("+31612345678");

        cipherText.Should().NotBe("+31612345678");
        encryption.Decrypt(cipherText).Should().Be("+31612345678");
    }

    // Test: volledige succesvolle verzendflow.
    // -zet in-memory DB op met organisatie, afspraak (met versleuteld telefoonnummer), job en providerconfig
    // -gebruikt een fake provider die succes retourneert
    // -roept DispatchAsync aan en controleert dat de job op Sent staat,
    // -dat er een MessageLog is en dat provider het correcte, gedecrypte nummer ontving.
    [Fact]
    public async Task DispatchAsync_WhenProviderSucceeds_DecryptsPhoneAndMarksJobSent()
    {
        var encryption = new AesEncryptionService(CreateBase64Key());
        await using var db = CreateDbContext();
        var organisation = new Organisation { Id = Guid.NewGuid(), Name = "Clinic" };
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

        provider.SentMessages.Should().ContainSingle();
        provider.SentMessages[0].NotificationJobId.Should().Be(job.Id);
        provider.SentMessages[0].PhoneNumber.Should().Be("+31612345678");
        provider.SentMessages[0].MessageBody.Should().Contain("Main clinic");

        db.MessageLogs.Should().ContainSingle(log =>
            log.NotificationJobId == job.Id &&
            log.Success &&
            log.ProviderName == "SwiftSend" &&
            log.ProviderMessageId == "msg-123");
    }

    // Test: controleert dat de dispatchlaag naast versturen ook een MessageLoggedEvent publiceert.
    [Fact]
    public async Task DispatchAsync_WhenProviderSucceeds_PublishesMessageLoggedEventAndWritesLog()
    {
        var encryption = new AesEncryptionService(CreateBase64Key());
        await using var db = CreateDbContext();
        var organisation = new Organisation { Id = Guid.NewGuid(), Name = "Clinic" };
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            OrganisationId = organisation.Id,
            Organisation = organisation,
            FhirAppointmentId = "fhir-logged",
            EncryptedPatientPhone = encryption.Encrypt("+31655555555"),
            AppointmentDateTime = DateTime.UtcNow.AddHours(2),
            Location = "Room 2"
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
            ProviderName = "FastText",
            IsActive = true
        };

        db.AddRange(organisation, appointment, job, providerSubscription);
        await db.SaveChangesAsync();

        var provider = new SequencedProvider("FastText", new SendResult
        {
            Success = true,
            ProviderMessageId = "fast-777"
        });
        var eventsMock = new Mock<IEventPublisher>();
        eventsMock
            .Setup(x => x.PublishAsync(It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var dispatcher = CreateDispatcher(db, encryption, provider, eventsMock.Object);

        await dispatcher.DispatchAsync(job.Id, CancellationToken.None);

        db.MessageLogs.Should().ContainSingle(log =>
            log.NotificationJobId == job.Id &&
            log.Success &&
            log.ProviderName == "FastText" &&
            log.ProviderMessageId == "fast-777");

        eventsMock.Verify(x => x.PublishAsync(
                It.Is<MessageLoggedEvent>(evt =>
                    evt.NotificationJobId == job.Id &&
                    evt.OrganisationId == organisation.Id &&
                    evt.ProviderName == "FastText" &&
                    evt.Success &&
                    evt.ErrorMessage == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Test: simulatie van een tijdelijke provideruitval gevolgd door een succesvolle retry.
    // - eerste oproep: provider retourneert failure → job wordt Failed en RetryCount verhoogd
    // - tweede oproep: provider retourneert succes → job wordt Sent en SentAt gezet
    [Fact]
    public async Task DispatchAsync_WhenProviderFailsThenSucceeds_IncrementsRetryCountAndCanRetry()
    {
        var encryption = new AesEncryptionService(CreateBase64Key());
        await using var db = CreateDbContext();
        var organisation = new Organisation { Id = Guid.NewGuid(), Name = "Clinic" };
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
        afterFirstAttempt.SentAt.Should().BeNull();

        await dispatcher.DispatchAsync(job.Id, CancellationToken.None);

        var afterSecondAttempt = await db.NotificationJobs.SingleAsync(x => x.Id == job.Id);
        afterSecondAttempt.Status.Should().Be(NotificationJobStatus.Sent);
        afterSecondAttempt.RetryCount.Should().Be(1);
        afterSecondAttempt.SentAt.Should().NotBeNull();

        provider.SentMessages.Should().HaveCount(2);
        provider.SentMessages.All(message => message.PhoneNumber == "+31698765432").Should().BeTrue();

        db.MessageLogs.Should().HaveCount(2);
        db.MessageLogs.Count(log => !log.Success).Should().Be(1);
        db.MessageLogs.Count(log => log.Success).Should().Be(1);
    }

    // Helper: maakt een dispatcher met een enkele test-provider en een noop-eventpublisher.
    private static NotificationDispatchService CreateDispatcher(
        CommunicationDbContext db,
        AesEncryptionService encryption,
        IMessagingProvider provider)
    {
        var eventPublisher = new NoopEventPublisher();
        var logger = Mock.Of<ILogger<NotificationDispatchService>>();

        return new NotificationDispatchService(db, encryption, [provider], eventPublisher, logger);
    }

    // Helper: maakt een dispatcher met een test-provider en een expliciete eventpublisher.
    private static NotificationDispatchService CreateDispatcher(
        CommunicationDbContext db,
        AesEncryptionService encryption,
        IMessagingProvider provider,
        IEventPublisher eventPublisher)
    {
        var logger = Mock.Of<ILogger<NotificationDispatchService>>();

        return new NotificationDispatchService(db, encryption, [provider], eventPublisher, logger);
    }

    // Helper: creëert een nieuwe in-memory CommunicationDbContext per test.
    private static CommunicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CommunicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CommunicationDbContext(options);
    }

    // Helper: genereert een geldige 32-byte base64 AES sleutel voor tests.
    private static string CreateBase64Key()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    // Test-provider die in volgorde vooraf gedefinieerde SendResult waarden teruggeeft
    // en alle verstuurde berichten opslaat voor inspectie.
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
            {
                throw new InvalidOperationException("No more test results configured.");
            }

            return Task.FromResult(_results.Dequeue());
        }
    }

    // Eenvoudige event publisher stub die niets doet — voorkomt externe dependencies tijdens tests.
    private sealed class NoopEventPublisher : IEventPublisher
    {
        public Task PublishAsync(IIntegrationEvent evt, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}