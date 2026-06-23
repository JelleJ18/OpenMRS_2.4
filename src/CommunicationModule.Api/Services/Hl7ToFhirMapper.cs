using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace CommunicationModule.Api.Services;

public static class Hl7ToFhirMapper
{
    public static string MapToAppointment(ParsedHl7Message parsed)
    {
        var appointmentId = NormalizeId(parsed.MessageId, "appointment");
        var patientId = NormalizeId(string.IsNullOrWhiteSpace(parsed.PatientId) ? parsed.MessageId : parsed.PatientId, "patient");
        var locationId = NormalizeId(string.IsNullOrWhiteSpace(parsed.Location) ? "location" : parsed.Location, "location");
        var start = parsed.AppointmentDateTime ?? DateTimeOffset.UtcNow.AddDays(1);
        var patientDisplay = BuildDisplayName(parsed.FirstName, parsed.LastName, $"Patient {patientId}");
        var locationDisplay = string.IsNullOrWhiteSpace(parsed.Location) ? "Unknown location" : parsed.Location;

        var patientName = new HumanName();
        if (!string.IsNullOrWhiteSpace(parsed.LastName))
        {
            patientName.Family = parsed.LastName;
        }

        if (!string.IsNullOrWhiteSpace(parsed.FirstName))
        {
            patientName.Given = [parsed.FirstName];
        }

        var patient = new Patient
        {
            Id = patientId,
            Name = string.IsNullOrWhiteSpace(patientName.Family) && (patientName.Given is null || !patientName.Given.Any())
                ? []
                : [patientName],
            Telecom = [new ContactPoint
            {
                System = ContactPoint.ContactPointSystem.Phone,
                Value = string.IsNullOrWhiteSpace(parsed.PhoneNumber) ? "0000000000" : parsed.PhoneNumber,
                Use = ContactPoint.ContactPointUse.Mobile
            }]
        };

        var location = new Location
        {
            Id = locationId,
            Name = locationDisplay
        };

        var appointment = new Appointment
        {
            Id = appointmentId,
            Status = Appointment.AppointmentStatus.Booked,
            Start = start.UtcDateTime,
            End = start.AddMinutes(30).UtcDateTime,
            Description = $"HL7 import for {parsed.FirstName} {parsed.LastName}".Trim(),
            Participant =
            [
                new Appointment.ParticipantComponent
                {
                    Actor = new ResourceReference($"Patient/{patient.Id}")
                    {
                        Display = patientDisplay
                    },
                    Status = ParticipationStatus.Accepted
                },
                new Appointment.ParticipantComponent
                {
                    Actor = new ResourceReference($"Location/{location.Id}")
                    {
                        Display = locationDisplay
                    },
                    Status = ParticipationStatus.Accepted
                }
            ],
            Contained = [patient, location]
        };

        var serializer = new FhirJsonSerializer();
        return serializer.SerializeToString(appointment);
    }

    private static string NormalizeId(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value;
        var normalized = new string(source.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string BuildDisplayName(string? firstName, string? lastName, string fallback)
    {
        var parts = new[] { firstName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return parts.Length == 0 ? fallback : string.Join(" ", parts);
    }
}