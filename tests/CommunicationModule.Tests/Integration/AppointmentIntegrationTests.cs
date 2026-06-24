using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using CommunicationModule.Infrastructure.Data;
using CommunicationModule.Api.Services;
using CommunicationModule.Infrastructure.Services;
using Hangfire;
using CommunicationModule.Core.Enums;
using CommunicationModule.Core.Models;
using FluentAssertions;
using Moq;

public class Hl7IntegrationTests
{
    [Fact]
    public async Task Hl7_Message_Should_Create_NotificationJob_And_Save_Data()
    {
        //ARRANGE
        var options = new DbContextOptionsBuilder<CommunicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new CommunicationDbContext(options);

        var organisationId = Guid.NewGuid();

        //Seed organisation
        db.Organisations.Add(new Organisation
        {
            Id = organisationId,
            Name = "Test Organisation",
            ApiKeyHash = "TEST_HASH"
        });

        await db.SaveChangesAsync();

        var encryption = new AesEncryptionService(Convert.ToBase64String(new byte[32]));
        var jobsMock = new Mock<IBackgroundJobClient>();
        var eventsMock = new Mock<CommunicationModule.Core.Interfaces.IEventPublisher>();
        eventsMock.Setup(e => e.PublishAsync(It.IsAny<CommunicationModule.Core.Events.IIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var ingestion = new AppointmentIngestionService(db, encryption, jobsMock.Object, eventsMock.Object);

        //Simuleer HL7 bericht
        var appointmentTime = DateTimeOffset.UtcNow.AddHours(2).ToString("yyyyMMddHHmm");

        var hl7 = $@"MSH|^~\&|HospitalA|System|App|App|{appointmentTime}||SIU^S12|12345|P|2.3
PID|1||P123||Jansen^Jan";

        //ACT

        //1. Parse HL7
        var parsed = Hl7Parser.Parse(hl7);

        //2. Override organisation (zoals echte flow later doet via lookup)
        parsed.OrganisationId = organisationId;

        //3. Map naar FHIR
        var fhirJson = Hl7ToFhirMapper.MapToAppointment(parsed);

        //4. Verwerk via ingestion
        var result = await ingestion.IngestAsync(
            fhirJson,
            organisationId,
            CancellationToken.None);

        //ASSERT

        //Result bestaat
        result.Should().NotBeNull();

        //Appointment opgeslagen
        db.Appointments.Should().HaveCount(1);

        var appointment = db.Appointments.First();
        appointment.OrganisationId.Should().Be(organisationId);

        //Notification job aangemaakt voor verdere verzending
        db.NotificationJobs.Should().HaveCount(1);

        var job = db.NotificationJobs.First();
        job.Status.Should().Be(NotificationJobStatus.Pending);
        job.AppointmentId.Should().Be(appointment.Id);

        //Logging pipeline werkt
        db.MessageLogs.Should().NotBeNull();
    }
}