using CommunicationModule.Api.Services;
using FluentAssertions;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace CommunicationModule.Tests;

public class Hl7PipelineTests
{
    [Fact]
    public void Parse_And_MapToAppointment_ProducesValidFHIRAppointment()
    {
        var hl7 = string.Join("\r", new[]
        {
            "MSH|^~\\&|OPENMRS|OPENMRS|COMM|COMM|20260623120000||SIU^S12|MSG0001|P|2.5",
            "PID|1||12345||Doe^John||19800101|M|||123 Main St^^City^ST^12345||+31612345678",
            "SCH|1|1|APT1|||||Ward A|20260624103000"
        });

        var parsed = Hl7Parser.Parse(hl7);

        parsed.MessageId.Should().Be("MSG0001");
        parsed.PatientId.Should().Be("12345");
        parsed.FirstName.Should().Be("John");
        parsed.LastName.Should().Be("Doe");
        parsed.PhoneNumber.Should().Be("+31612345678");
        parsed.OrganisationId.Should().NotBe(Guid.Empty);

        var fhirJson = Hl7ToFhirMapper.MapToAppointment(parsed);
        var deserializer = new FhirJsonDeserializer(new DeserializerSettings { AcceptUnknownMembers = false });
        var appointment = deserializer.Deserialize<Appointment>(fhirJson);

        appointment.Id.Should().Be("msg0001");
        appointment.Status.Should().Be(Appointment.AppointmentStatus.Booked);
        appointment.Start.Should().NotBeNull();
        appointment.Participant.Where(p => p.Actor is not null && p.Actor.Reference == "Patient/12345").Should().ContainSingle();
        appointment.Participant.Where(p => p.Actor is not null && p.Actor.Reference == "Location/warda").Should().ContainSingle();
        appointment.Contained.OfType<Patient>().Should().ContainSingle(p => p.Id == "12345");
    }
}