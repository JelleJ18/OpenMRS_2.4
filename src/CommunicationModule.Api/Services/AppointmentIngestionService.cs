using CommunicationModule.Core.Enums;
using CommunicationModule.Core.Models;
using CommunicationModule.Infrastructure.Data;
using CommunicationModule.Infrastructure.Services;
using Hangfire;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.EntityFrameworkCore;

namespace CommunicationModule.Api.Services;

public class AppointmentIngestionService
{
    private readonly CommunicationDbContext _db;
    private readonly AesEncryptionService _encryption;
    private readonly IBackgroundJobClient _jobs;
    private readonly CommunicationModule.Core.Interfaces.IEventPublisher _events;

    public AppointmentIngestionService(
        CommunicationDbContext db,
        AesEncryptionService encryption,
        IBackgroundJobClient jobs,
        CommunicationModule.Core.Interfaces.IEventPublisher events)
    {
        _db = db;
        _encryption = encryption;
        _jobs = jobs;
        _events = events;
    }

    public async Task<IngestionResult> IngestAsync(
        string fhirJson,
        Guid organisationId,
        CancellationToken ct)
    {
        Hl7.Fhir.Model.Appointment fhir;
        try
        {
            var deserializer = new FhirJsonDeserializer(new DeserializerSettings { AcceptUnknownMembers = false });
            fhir = deserializer.Deserialize<Hl7.Fhir.Model.Appointment>(fhirJson);
        }
        catch (Exception ex)
        {
            return IngestionResult.Fail($"Invalid FHIR Appointment: {ex.Message}");
        }

        var validationError = ValidateAppointment(fhir);
        if (validationError is not null)
            return IngestionResult.Fail(validationError);

        var start = fhir.Start;
        if (start is null)
            return IngestionResult.Fail("Appointment.start is required.");

        var appointmentTime = start.Value.UtcDateTime;
        var status = MapStatus(fhir.Status);
        var location = ExtractLocation(fhir);
        var patientPhone = ExtractPatientPhone(fhir);
        var appointmentId = fhir.Id ?? string.Empty;

        if (string.IsNullOrEmpty(patientPhone))
            return IngestionResult.Fail("Patient.telecom must contain a phone number.");

        var encryptedPhone = _encryption.Encrypt(patientPhone);

        var appointment = await _db.Appointments
            .Include(a => a.NotificationJobs)
            .FirstOrDefaultAsync(
                a => a.FhirAppointmentId == fhir.Id && a.OrganisationId == organisationId, ct);

        var isNew = appointment is null;

        if (isNew)
        {
            appointment = new Core.Models.Appointment
            {
                Id = Guid.NewGuid(),
                FhirAppointmentId = appointmentId,
                OrganisationId = organisationId
            };
            _db.Appointments.Add(appointment);
        }

        appointment!.EncryptedPatientPhone = encryptedPhone;
        appointment.AppointmentDateTime = appointmentTime;
        appointment.Location = location;
        appointment.Instructions = fhir.Description;
        appointment.Status = status;

        if (status != AppointmentStatus.Scheduled)
        {
            await _db.SaveChangesAsync(ct);
            await _events.PublishAsync(new CommunicationModule.Core.Events.AppointmentReceivedEvent(appointment.Id, organisationId, appointmentTime, location), ct);
            return IngestionResult.Ok(appointment.Id, scheduled: false);
        }

        if (isNew)
        {
            var now = DateTime.UtcNow;
            var t24 = appointmentTime.AddHours(-24);
            var t1 = appointmentTime.AddHours(-1);

            if (t24 > now)
            {
                var job24 = new NotificationJob
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = appointment.Id,
                    Type = NotificationJobType.TwentyFourHour,
                    ScheduledFor = t24
                };
                _db.NotificationJobs.Add(job24);
                _jobs.Schedule<NotificationDispatchService>(
                    s => s.DispatchAsync(job24.Id, CancellationToken.None),
                    t24 - now);
            }

            if (t1 > now)
            {
                var job1 = new NotificationJob
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = appointment.Id,
                    Type = NotificationJobType.OneHour,
                    ScheduledFor = t1
                };
                _db.NotificationJobs.Add(job1);
                _jobs.Schedule<NotificationDispatchService>(
                    s => s.DispatchAsync(job1.Id, CancellationToken.None),
                    t1 - now);
            }
        }

        await _db.SaveChangesAsync(ct);
        await _events.PublishAsync(new CommunicationModule.Core.Events.AppointmentReceivedEvent(appointment.Id, organisationId, appointmentTime, location), ct);
        return IngestionResult.Ok(appointment.Id, scheduled: isNew);
    }

    private static string? ValidateAppointment(Hl7.Fhir.Model.Appointment fhir)
    {
        if (string.IsNullOrWhiteSpace(fhir.Id))
            return "Appointment.id is required.";

        if (fhir.Start is null)
            return "Appointment.start is required.";

        if (fhir.Status is null)
            return "Appointment.status is required.";

        var patientParticipant = fhir.Participant.FirstOrDefault(p => p.Actor?.Reference?.StartsWith("Patient/") == true);
        var patientActor = patientParticipant?.Actor;
        if (patientActor?.Reference is null)
            return "Appointment must contain a Patient reference.";

        var patientRef = patientActor.Reference;
        if (!patientRef.StartsWith("Patient/"))
            return "Patient reference must use the format Patient/{id}.";

        var patientId = patientRef.Replace("Patient/", string.Empty).TrimStart('#');
        if (string.IsNullOrWhiteSpace(patientId))
            return "Patient reference is invalid.";

        var patient = fhir.Contained.OfType<Patient>().FirstOrDefault(p => p.Id == patientId);
        if (patient is null)
            return "Contained Patient resource is missing.";

        if (patient.Telecom is null || !patient.Telecom.Any(t => t.System == ContactPoint.ContactPointSystem.Phone && !string.IsNullOrWhiteSpace(t.Value)))
            return "Contained Patient must include a phone number.";

        var locationParticipant = fhir.Participant.FirstOrDefault(p => p.Actor?.Reference?.StartsWith("Location/") == true);
        var locationActor = locationParticipant?.Actor;
        if (locationActor?.Reference is not null)
        {
            var locationRef = locationActor.Reference;
            if (!locationRef.StartsWith("Location/"))
                return "Location reference must use the format Location/{id}.";

            var locationId = locationRef.Replace("Location/", string.Empty).TrimStart('#');
            if (string.IsNullOrWhiteSpace(locationId))
                return "Location reference is invalid.";
        }

        return null;
    }

    private static string ExtractLocation(Hl7.Fhir.Model.Appointment fhir)
    {
        return fhir.Participant
            .FirstOrDefault(p => p.Actor?.Reference?.StartsWith("Location/") == true)
            ?.Actor?.Display ?? "Unknown location";
    }

    private static string? ExtractPatientPhone(Hl7.Fhir.Model.Appointment fhir)
    {
        var patientRef = fhir.Participant
            .FirstOrDefault(p => p.Actor?.Reference?.StartsWith("Patient/") == true)
            ?.Actor?.Reference;

        if (patientRef is null) return null;

        var patientId = patientRef.Replace("Patient/", "").TrimStart('#');
        var patient = fhir.Contained
            .OfType<Patient>()
            .FirstOrDefault(p => p.Id == patientId);

        return patient?.Telecom
            .FirstOrDefault(t => t.System == ContactPoint.ContactPointSystem.Phone)
            ?.Value;
    }

    private static AppointmentStatus MapStatus(Hl7.Fhir.Model.Appointment.AppointmentStatus? status) =>
        status switch
        {
            Hl7.Fhir.Model.Appointment.AppointmentStatus.Booked    => AppointmentStatus.Scheduled,
            Hl7.Fhir.Model.Appointment.AppointmentStatus.Cancelled => AppointmentStatus.Cancelled,
            Hl7.Fhir.Model.Appointment.AppointmentStatus.Fulfilled => AppointmentStatus.Completed,
            _                                                       => AppointmentStatus.Scheduled
        };
}

public record IngestionResult(bool Success, Guid? AppointmentId, bool Scheduled, string? Error)
{
    public static IngestionResult Ok(Guid id, bool scheduled) => new(true, id, scheduled, null);
    public static IngestionResult Fail(string error) => new(false, null, false, error);
}
